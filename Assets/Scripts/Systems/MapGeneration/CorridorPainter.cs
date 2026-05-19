using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Linq;

public class CorridorPainter : MonoBehaviour
{
    private Tilemap _groundTilemap;
    private Tilemap _wallTilemap;
    private TileBase _floorTile;
    private TileBase _wallTile;

    public void Init(Tilemap ground, Tilemap wall, TileBase floor, TileBase wallT)
    {
        _groundTilemap = ground;
        _wallTilemap = wall;
        _floorTile = floor;
        _wallTile = wallT;
    }

    public void PaintCorridor(List<Vector2Int> path)
    {
        if (path == null || path.Count == 0) return;

        foreach (var pos in path)
        {
            _groundTilemap.SetTile((Vector3Int)pos, _floorTile);
            _wallTilemap.SetTile((Vector3Int)pos, null);
        }

        foreach (var pos in path)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    Vector3Int neighbor = new Vector3Int(pos.x + x, pos.y + y, 0);
                    if (_groundTilemap.GetTile(neighbor) == null && _wallTilemap.GetTile(neighbor) == null)
                    {
                        _wallTilemap.SetTile(neighbor, _wallTile);
                    }
                }
            }
        }
    }

    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int end, int margin)
    {
        PriorityQueueCustom<Vector2Int> openSet = new PriorityQueueCustom<Vector2Int>();
        openSet.Enqueue(start, 0);

        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float>();
        gScore[start] = 0;

        int iterations = 0;
        while (openSet.Count > 0 && iterations < 5000)
        {
            iterations++;
            Vector2Int current = openSet.Dequeue();
            if (current == end) return ReconstructPath(cameFrom, current);

            foreach (Vector2Int neighbor in GetNeighbors(current))
            {
                float moveCost = CalculateCost(neighbor, margin);
                float tentativeGScore = gScore[current] + moveCost;

                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    float fScore = tentativeGScore + Vector2Int.Distance(neighbor, end);
                    openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        Debug.LogWarning($"[MapGen] A* Path failed. Using Fallback L-path from {start} to {end}");
        return CreateSimpleLPath(start, end);
    }

    private List<Vector2Int> CreateSimpleLPath(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int current = start;
        while (current.x != end.x) { path.Add(current); current.x += (int)Mathf.Sign(end.x - current.x); }
        while (current.y != end.y) { path.Add(current); current.y += (int)Mathf.Sign(end.y - current.y); }
        path.Add(end);
        return path;
    }

    private float CalculateCost(Vector2Int pos, int margin)
    {
        if (_groundTilemap.HasTile((Vector3Int)pos)) return 50f;
        if (_wallTilemap.HasTile((Vector3Int)pos)) return 30f;

        for (int x = -margin; x <= margin; x++)
            for (int y = -margin; y <= margin; y++)
            {
                if (x == 0 && y == 0) continue;
                if (_groundTilemap.HasTile(new Vector3Int(pos.x + x, pos.y + y, 0))) return 15f;
            }
        return 1f;
    }

    private List<Vector2Int> GetNeighbors(Vector2Int n)
    {
        return new List<Vector2Int> {
            new Vector2Int(n.x+1, n.y), new Vector2Int(n.x-1, n.y),
            new Vector2Int(n.x, n.y+1), new Vector2Int(n.x, n.y-1)
        };
    }

    private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        List<Vector2Int> totalPath = new List<Vector2Int> { current };
        while (cameFrom.ContainsKey(current)) { current = cameFrom[current]; totalPath.Add(current); }
        totalPath.Reverse();
        return totalPath;
    }
}

public class PriorityQueueCustom<T>
{
    private List<KeyValuePair<T, float>> elements = new List<KeyValuePair<T, float>>();
    public int Count => elements.Count;
    public void Enqueue(T item, float priority) { elements.Add(new KeyValuePair<T, float>(item, priority)); }
    public T Dequeue()
    {
        int bestIndex = 0;
        for (int i = 0; i < elements.Count; i++) { if (elements[i].Value < elements[bestIndex].Value) bestIndex = i; }
        T item = elements[bestIndex].Key;
        elements.RemoveAt(bestIndex);
        return item;
    }
}
