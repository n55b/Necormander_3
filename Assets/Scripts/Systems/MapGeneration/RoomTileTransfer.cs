using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

internal static class RoomTileTransfer
{
    public static IEnumerator Stamp(Tilemap source, Tilemap target, double budget, Action<Tilemap, Vector3Int[]> register)
    {
        if (source == null || target == null) yield break;
        using (MapGenerator.MeasureGeneration("1.Stamp CompressBounds"))
            source.CompressBounds();
        BoundsInt bounds = source.cellBounds;
        if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0) yield break;

        // 타일마다 GetTile/SetTile을 호출하지 않고, 실제로 채워진 타일만 한 번에 읽고 쓴다.
        Vector3Int rangeEnd = bounds.max - Vector3Int.one;
        int tileCount;
        using (MapGenerator.MeasureGeneration($"1.Stamp GetTilesRangeCount bounds={bounds.size}"))
            tileCount = source.GetTilesRangeCount(bounds.min, rangeEnd);
        if (tileCount == 0) yield break;

        var sourcePositions = new Vector3Int[tileCount];
        var tiles = new TileBase[tileCount];
        using (MapGenerator.MeasureGeneration($"1.Stamp GetTilesRangeNonAlloc tiles={tileCount}"))
            tileCount = source.GetTilesRangeNonAlloc(bounds.min, rangeEnd, sourcePositions, tiles);
        var targetPositions = new Vector3Int[tileCount];
        List<TileChangeData> artOverrides = null;

        // 두 타일맵 사이의 셀 변환은 방 전체에서 동일하다. 매 타일마다 행렬 변환하지 않는다.
        Vector3Int targetOrigin = target.WorldToCell(source.CellToWorld(Vector3Int.zero));
        Vector3Int targetX = target.WorldToCell(source.CellToWorld(Vector3Int.right)) - targetOrigin;
        Vector3Int targetY = target.WorldToCell(source.CellToWorld(Vector3Int.up)) - targetOrigin;
        Vector3Int targetZ = target.WorldToCell(source.CellToWorld(new Vector3Int(0, 0, 1))) - targetOrigin;

        using (MapGenerator.MeasureGeneration($"1.Stamp convert tiles={tileCount}"))
        {
            for (int i = 0; i < tileCount; i++)
            {
                Vector3Int pos = sourcePositions[i];
                Vector3Int targetCellPos = targetOrigin + targetX * pos.x + targetY * pos.y + targetZ * pos.z;
                targetPositions[i] = targetCellPos;
            }
        }

        using (MapGenerator.MeasureGeneration($"1.Stamp SetTiles target={target.name} tiles={tileCount}"))
        {
            if (budget <= 0) target.SetTiles(targetPositions, tiles);
            else
            {
                const int batchSize = 192;
                var batchCells = new Vector3Int[Math.Min(batchSize, tileCount)];
                var batchTiles = new TileBase[batchCells.Length];
                double deadline = Time.realtimeSinceStartupAsDouble + budget;
                for (int start = 0; start < tileCount; start += batchSize)
                {
                    int length = Math.Min(batchSize, tileCount - start);
                    if (length != batchCells.Length)
                    {
                        batchCells = new Vector3Int[length];
                        batchTiles = new TileBase[length];
                    }
                    Array.Copy(targetPositions, start, batchCells, 0, length);
                    Array.Copy(tiles, start, batchTiles, 0, length);
                    target.SetTiles(batchCells, batchTiles);
                    if (Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        yield return null;
                        deadline = Time.realtimeSinceStartupAsDouble + budget;
                    }
                }
            }

            // Keep painted cell tints. Let rule tiles resolve rotation and mirroring in the destination.
            double detailDeadline = Time.realtimeSinceStartupAsDouble + budget;
            for (int i = 0; i < tileCount; i++)
            {
                if (budget > 0 && (i & 127) == 0 && Time.realtimeSinceStartupAsDouble >= detailDeadline)
                {
                    yield return null;
                    detailDeadline = Time.realtimeSinceStartupAsDouble + budget;
                }
                Vector3Int cell = targetPositions[i];
                // Later batches may add ordinary artist tiles which do not notify an earlier brush.
                if (tiles[i] is WorkshopTerrainTile) target.RefreshTile(cell);
                Color color = source.GetColor(sourcePositions[i]);
                bool plainTile = tiles[i] is Tile;
                bool hasTint = color != target.GetColor(cell);
                if (!plainTile && !hasTint) continue;
                Matrix4x4 targetMatrix = target.GetTransformMatrix(cell);
                Matrix4x4 matrix = plainTile ? source.GetTransformMatrix(sourcePositions[i]) : targetMatrix;
                if (!hasTint && matrix == targetMatrix) continue;

                artOverrides ??= new List<TileChangeData>();
                artOverrides.Add(new TileChangeData(cell, tiles[i], color, matrix));
            }
            if (artOverrides != null)
                target.SetTiles(artOverrides.ToArray(), ignoreLockFlags: true);
        }
        register(target, targetPositions);
    }

    public static void Erase(Tilemap source, Tilemap target)
    {
        if (source == null || target == null) return;
        source.CompressBounds();
        BoundsInt bounds = source.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            TileBase tile = source.GetTile(pos);
            if (tile != null)
            {
                Vector3 worldPos = source.CellToWorld(pos);
                Vector3Int targetCellPos = target.WorldToCell(worldPos);
                target.SetTile(targetCellPos, null);
            }
        }
    }
}
