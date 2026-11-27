#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Copy stamp extras: rotate and flip before stamping (the cursor preview follows), thumbnails in the library,
// and the most recently used stamp first.
public sealed partial class RoomWorkshopWindow
{
    private readonly Dictionary<string, (DateTime time, RenderTexture texture)> stampThumbnails = new();

    // The cell of the stamp that sits under the cursor: its middle (lower-left of the middle for even sizes).
    // The cursor preview and the actual stamping both use this one value, so they always agree.
    // Measured on the tiles themselves: a sampled box with empty rows or columns at its edge stays centred too.
    private Vector2Int StampPivot => stamp.Count == 0 ? Vector2Int.zero :
        new Vector2Int((stamp.Min(s => s.Offset.x) + stamp.Max(s => s.Offset.x)) / 2,
            (stamp.Min(s => s.Offset.y) + stamp.Max(s => s.Offset.y)) / 2);

    // 0 = rotate 90° clockwise, 1 = flip left/right, 2 = flip up/down.
    private void TransformStamp(int mode)
    {
        if (stamp.Count == 0) return;
        int w = stamp.Max(s => s.Offset.x) + 1, h = stamp.Max(s => s.Offset.y) + 1;
        var turn = mode == 0 ? Matrix4x4.Rotate(Quaternion.Euler(0, 0, -90)) :
            mode == 1 ? Matrix4x4.Scale(new Vector3(-1, 1, 1)) : Matrix4x4.Scale(new Vector3(1, -1, 1));
        var next = stamp.Select(s =>
        {
            var o = s.Offset;
            var at = mode == 0 ? new Vector2Int(o.y, w - 1 - o.x) : mode == 1 ? new Vector2Int(w - 1 - o.x, o.y) : new Vector2Int(o.x, h - 1 - o.y);
            var t = s.Tile;
            // Auto tiles pick their own edge art from neighbours, so turning their matrix only distorts the preview.
            var matrix = t.tile is Tile ? Clean(turn * t.transform) : Matrix4x4.identity;
            return new StampCell(at, new TileChangeData((Vector3Int)at, t.tile, t.color, matrix), s.Id);
        }).ToList();
        stamp.Clear(); stamp.AddRange(next);
        ghostKey = null; Draw(page);
        Say((mode == 0 ? "도장 회전" : mode == 1 ? "도장 좌우 뒤집기" : "도장 상하 뒤집기") + "  ·  Q 회전 · X 좌우 · Shift+X 상하");
    }

    // Rotations by quarter turns leave tiny float errors; snapping keeps saved tile styles identical.
    private static Matrix4x4 Clean(Matrix4x4 m)
    {
        for (int i = 0; i < 16; i++)
        {
            float v = m[i], r = Mathf.Round(v);
            if (Mathf.Abs(v - r) < 1e-4f) m[i] = r;
        }
        return m;
    }

    private void DrawStampTools()
    {
        Btn("회전 Q", 497, 203, 41, 20, () => TransformStamp(0)).name = "도장 회전";
        Btn("좌우 X", 540, 203, 41, 20, () => TransformStamp(1)).name = "도장 좌우";
        Btn("상하", 583, 203, 42, 20, () => TransformStamp(2)).name = "도장 상하";
    }

    private static Sprite SpriteOf(TileBase tile) => tile switch
    {
        WorkshopTerrainTile terrain => terrain.PaletteSprite,
        Tile plain => plain.sprite,
        AnimatedTile animated when animated.m_AnimatedSprites is { Length: > 0 } => animated.m_AnimatedSprites[0],
        _ => null
    };

    // A small picture of a saved stamp, drawn once per file version.
    private Texture StampThumbnail(string path, WorkshopStamp saved)
    {
        var time = File.GetLastWriteTimeUtc(path);
        if (stampThumbnails.TryGetValue(path, out var cached) && cached.texture != null)
        {
            if (cached.time == time) return cached.texture;
            cached.texture.Release(); Destroy(cached.texture);
        }
        const int px = 16;
        int w = saved.cells.Max(s => s.offset.x) + 1, h = saved.cells.Max(s => s.offset.y) + 1;
        var texture = new RenderTexture(w * px, h * px, 0) { filterMode = FilterMode.Point, name = "Stamp thumbnail" };
        texture.Create();
        var previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(true, true, Color.clear);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, w * px, h * px, 0);
        foreach (var cell in saved.cells)
        {
            var tile = catalog.Tile(cell.tile);
            var sprite = SpriteOf(tile);
            if (sprite == null) continue;
            var r = sprite.textureRect; var source = sprite.texture;
            var uv = new Rect(r.x / source.width, r.y / source.height, r.width / source.width, r.height / source.height);
            // Quarter turns and flips of the stored matrix are shown by turning the sampled rectangle.
            bool plain = tile is Tile;
            float sx = plain ? cell.matrix.m00 : 1, sy = plain ? cell.matrix.m11 : 1;
            if (sx < 0) uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
            if (sy < 0) uv = new Rect(uv.x, uv.yMax, uv.width, -uv.height);
            var target = new Rect(cell.offset.x * px, (h - 1 - cell.offset.y) * px, px, px);
            Graphics.DrawTexture(target, source, uv, 0, 0, 0, 0, cell.color * .5f);
        }
        GL.PopMatrix();
        RenderTexture.active = previous;
        stampThumbnails[path] = (time, texture);
        return texture;
    }

    private void ShowStampThumbnail(Button button, Texture texture, int size, int labelWidth, int rowHeight)
    {
        if (texture == null) return;
        var label = button.GetComponentInChildren<TMP_Text>();
        label.rectTransform.anchoredPosition = new Vector2(size + 7, 0);
        label.rectTransform.sizeDelta = new Vector2(labelWidth, rowHeight);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        float aspect = texture.width / (float)Mathf.Max(1, texture.height);
        int iw = aspect >= 1 ? size : Mathf.Max(2, Mathf.RoundToInt(size * aspect));
        int ih = aspect >= 1 ? Mathf.Max(2, Mathf.RoundToInt(size / aspect)) : size;
        var icon = WorkshopUI.Rect(button.transform, "Stamp thumbnail", 3 + (size - iw) / 2, (rowHeight - ih) / 2, iw, ih).gameObject.AddComponent<RawImage>();
        icon.texture = texture; icon.raycastTarget = false;
    }

    private (string path, string title, int cells)? pendingStampDelete;

    private void DrawStampDeleteConfirm()
    {
        var item = pendingStampDelete.Value;
        Modal(150, 98, 340, 150);
        WorkshopUI.Label(content, "도장을 삭제할까요?", 166, 108, 300, 24, 15);
        WorkshopUI.Label(content, "이름  ·  " + item.title + "  (" + item.cells + "칸)", 166, 138, 310, 18, 12);
        WorkshopUI.Label(content, "보관함에서 지웁니다. 되돌릴 수 없습니다.", 166, 160, 310, 16);
        Btn("삭제", 166, 210, 150, 24, () =>
        {
            pendingStampDelete = null;
            if (stampThumbnails.TryGetValue(item.path, out var cached) && cached.texture != null) { cached.texture.Release(); Destroy(cached.texture); }
            stampThumbnails.Remove(item.path);
            try { File.Delete(item.path); Say("도장을 삭제했습니다 · " + item.title); }
            catch (IOException ex) { Say("도장을 지우지 못했습니다 · " + ex.Message); }
            Draw(page);
        }).name = "도장 삭제 확인";
        Btn("취소", 324, 210, 150, 24, () => { pendingStampDelete = null; Draw(page); }, true).Select();
    }

    private void ReleaseStampThumbnails()
    {
        foreach (var item in stampThumbnails.Values)
            if (item.texture != null) { item.texture.Release(); Destroy(item.texture); }
        stampThumbnails.Clear();
    }
}
#endif
