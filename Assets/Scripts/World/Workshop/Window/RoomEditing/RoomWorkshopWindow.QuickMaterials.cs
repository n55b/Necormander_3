#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The material strip left of the map: six one-click slots for tiles, enemies and props.
// Starred materials stay at the top in the order they were starred; the remaining slots show what was used last.
// Both lists are kept in PlayerPrefs, so the strip looks the same on the next run.
public sealed partial class RoomWorkshopWindow
{
    // The map sits right of the strip; drawers and panels still cover both, as before.
    // Folded (L), only a thin tab stays and the map takes the room back.
    private const int StripX = 7, StripWidth = 40, StripTab = 12, MapAreaWidth = 478;
    internal const int QuickSlots = 6;
    internal const string FavoritesKey = "Workshop.QuickFavorites", RecentKey = "Workshop.QuickRecent", StripOpenKey = "Workshop.StripOpen";
    private static bool StripOpen
    {
        get => PlayerPrefs.GetInt(StripOpenKey, 1) != 0;
        set => PlayerPrefs.SetInt(StripOpenKey, value ? 1 : 0);
    }
    private int MapLeft => StripX + (StripOpen ? StripWidth : StripTab) + 4;
    private int MapWidth => StripX + MapAreaWidth - MapLeft;

    // The map picture changes width, so the whole page is rebuilt once; zoom and centre are kept.
    private void ToggleStrip()
    {
        StripOpen = !StripOpen;
        drawnPreview = null;
        preview?.ResizeFrame(MapWidth, WorkshopPreview.Height);
        Draw(page);
        Say(StripOpen ? "재료 줄을 폈습니다 · L로 접기" : "재료 줄을 접었습니다 · L로 펴기");
    }

    // The list drawer. Opened from a tool without a material (select, exit, start...), it shows the last kind viewed.
    private static int paletteKind = 1;
    private static readonly string[] PaletteKinds = { "자동", "타일", "적", "소품" };
    private int KindOfTool => tool switch { Tool.Auto => 0, Tool.Paint or Tool.Rect or Tool.Fill => 1, Tool.Enemy => 2, Tool.Prop => 3, _ => -1 };

    private void ToggleMaterialList()
    {
        if (!drawer && KindOfTool >= 0) paletteKind = KindOfTool;
        paletteQuery = ""; palettePage = 0; drawer = !drawer; encounterOpen = false;
        Draw(page);
    }

    // "t|tile id|layer", "e|piece id", "p|piece id". The layer tells which layer a plain tile was painted on.
    private readonly struct QuickMaterial
    {
        public readonly char kind; public readonly string id; public readonly int layer;
        public QuickMaterial(char kind, string id, int layer = -1) { this.kind = kind; this.id = id; this.layer = layer; }
        public string Key => kind + "|" + id + (kind == 't' && layer >= 0 ? "|" + layer : "");
        public bool Same(QuickMaterial other) => kind == other.kind && id == other.id;
        public static bool TryParse(string text, out QuickMaterial value)
        {
            value = default;
            var parts = text.Split('|');
            if (parts.Length < 2 || parts[0].Length != 1 || "tep".IndexOf(parts[0][0]) < 0 || parts[1].Length == 0) return false;
            int layer = parts.Length > 2 && int.TryParse(parts[2], out var l) && l >= 0 && l < 4 ? l : -1;
            value = new QuickMaterial(parts[0][0], parts[1], layer);
            return true;
        }
    }

    private static List<QuickMaterial> ReadQuick(string key) =>
        PlayerPrefs.GetString(key, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(text => QuickMaterial.TryParse(text, out var m) ? (QuickMaterial?)m : null)
            .Where(m => m.HasValue).Select(m => m.Value).ToList();

    private static void WriteQuick(string key, IEnumerable<QuickMaterial> items) =>
        PlayerPrefs.SetString(key, string.Join(";", items.Select(m => m.Key)));

    // The material the current tool would place, or null for tools without one.
    private QuickMaterial? CurrentMaterial => tool switch
    {
        Tool.Auto or Tool.Paint or Tool.Rect or Tool.Fill when palette.Length > 0 =>
            new QuickMaterial('t', palette[materialIndex].id, tool == Tool.Auto ? -1 : layer),
        Tool.Enemy when enemies.Length > 0 => new QuickMaterial('e', enemies[enemyIndex].id),
        Tool.Prop when props.Length > 0 => new QuickMaterial('p', props[propIndex].id),
        _ => null
    };

    private bool Known(QuickMaterial m) => m.kind switch
    {
        't' => catalog.tiles.Any(t => t.id == m.id && t.tile != null),
        'e' => enemies.Any(p => p.id == m.id),
        _ => props.Any(p => p.id == m.id)
    };

    // Starred first, then the most recent ones not already shown, up to six; missing catalog entries are skipped.
    private List<(QuickMaterial material, bool favorite)> QuickStrip()
    {
        var strip = new List<(QuickMaterial, bool)>();
        foreach (var m in ReadQuick(FavoritesKey))
            if (strip.Count < QuickSlots && Known(m) && !strip.Any(s => s.Item1.Same(m))) strip.Add((m, true));
        foreach (var m in ReadQuick(RecentKey))
            if (strip.Count < QuickSlots && Known(m) && !strip.Any(s => s.Item1.Same(m))) strip.Add((m, false));
        return strip;
    }

    private void RememberQuick(QuickMaterial m)
    {
        var recent = ReadQuick(RecentKey);
        recent.RemoveAll(r => r.Same(m));
        recent.Insert(0, m);
        WriteQuick(RecentKey, recent.Take(12));
    }

    private bool IsFavorite(QuickMaterial m) => ReadQuick(FavoritesKey).Any(f => f.Same(m));

    private void ToggleFavorite(QuickMaterial m)
    {
        var favorites = ReadQuick(FavoritesKey);
        if (favorites.RemoveAll(f => f.Same(m)) > 0) { WriteQuick(FavoritesKey, favorites); Say("즐겨찾기에서 뺐습니다."); return; }
        if (favorites.Count(Known) >= QuickSlots) { Say("즐겨찾기는 " + QuickSlots + "개까지입니다. 먼저 하나를 빼 주세요."); return; }
        favorites.Add(m);
        WriteQuick(FavoritesKey, favorites);
        Say("즐겨찾기에 고정했습니다 · 재료 줄 위쪽에 남습니다.");
    }

    // One click: switch to the tool that places this material and pick it, as the list drawer would.
    private void UseQuick(QuickMaterial m)
    {
        var previous = tool;
        drawer = false; encounterOpen = false; notice = ""; shapeStart = null; ghostKey = null;
        if (m.kind == 't')
        {
            var entry = catalog.tiles.FirstOrDefault(t => t.id == m.id);
            if (entry == null) return;
            bool terrain = entry.tile is WorkshopTerrainTile;
            tool = terrain ? Tool.Auto : previous is Tool.Paint or Tool.Rect or Tool.Fill ? previous : Tool.Paint;
            if (tool == Tool.Auto && previous != Tool.Auto) stamp.Clear();
            if (!terrain && m.layer >= 0) layer = m.layer;
            SyncPalette();
            int index = Array.FindIndex(palette, t => t.id == m.id);
            if (index >= 0) SelectMaterial(index);
        }
        else if (m.kind == 'e')
        {
            int index = Array.FindIndex(enemies, p => p.id == m.id);
            if (index < 0) return;
            tool = Tool.Enemy; enemyIndex = index;
        }
        else
        {
            int index = Array.FindIndex(props, p => p.id == m.id);
            if (index < 0) return;
            tool = Tool.Prop;
            // While scattering, the strip adds or removes a prop from the set, like the list does.
            if (scatter) ToggleScatterMember(index); else propIndex = index;
        }
        if (previous == Tool.Auto && tool != Tool.Auto && m.kind != 't') SyncPalette();
        Draw(page);
    }

    private Sprite QuickSprite(QuickMaterial m) => m.kind switch
    {
        't' => catalog.tiles.FirstOrDefault(t => t.id == m.id)?.Sample,
        'e' => enemies.FirstOrDefault(p => p.id == m.id)?.prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite,
        _ => props.FirstOrDefault(p => p.id == m.id)?.prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite
    };

    private string QuickTitle(QuickMaterial m) => m.kind switch
    {
        't' => catalog.tiles.FirstOrDefault(t => t.id == m.id)?.Title ?? "",
        'e' => enemies.FirstOrDefault(p => p.id == m.id)?.prefab.name ?? "",
        _ => props.FirstOrDefault(p => p.id == m.id)?.prefab.name ?? ""
    };

    private void DrawQuickStrip()
    {
        if (!StripOpen)
        {
            // Same small size as the fold button beside "재료", at the top of the folded strip.
            Btn(">", StripX, 56, 11, 12, ToggleStrip).name = "재료 줄 펴기";
            return;
        }
        WorkshopUI.Frame(content, "Material strip", StripX, 54, StripWidth, WorkshopPreview.Height);
        WorkshopUI.Label(content, "재료", StripX + 4, 55, 24, 14).color = WorkshopUI.Mint;
        Btn("<", StripX + 27, 56, 11, 12, ToggleStrip).name = "재료 줄 접기";
        var strip = QuickStrip();
        var current = CurrentMaterial;
        for (int i = 0; i < QuickSlots; i++)
        {
            int y = 70 + i * 36;
            if (i >= strip.Count)
            {
                WorkshopUI.Frame(content, "빈 재료 칸 " + (i + 1), StripX + 3, y, 34, 34);
                continue;
            }
            var (material, favorite) = strip[i];
            bool chosen = current.HasValue && current.Value.Same(material) &&
                (material.kind != 'p' || !scatter) && (material.kind != 't' || material.layer < 0 || material.layer == layer || tool == Tool.Auto);
            if (material.kind == 'p' && scatter && tool == Tool.Prop)
                chosen = scatterSet.Contains(Array.FindIndex(props, p => p.id == material.id));
            var slot = Btn("", StripX + 3, y, 34, 34, () => UseQuick(material), chosen);
            slot.name = "재료 칸 " + (i + 1);
            var sprite = QuickSprite(material);
            if (sprite != null)
            {
                var icon = WorkshopUI.Rect(slot.transform, "Sample", 3, 3, 28, 28).gameObject.AddComponent<Image>();
                icon.sprite = sprite; icon.preserveAspect = true; icon.raycastTarget = false;
            }
            if (favorite)
            {
                var star = WorkshopUI.Label(slot.transform, "★", 22, 0, 12, 12);
                star.color = new Color32(252, 203, 103, 255); star.raycastTarget = false;
            }
        }
        // Pins the material in use; lit while it is pinned. Without one (select, exit...) it opens the list to star from.
        var pin = Btn("★", StripX + 3, 288, 34, 17, () =>
        {
            if (CurrentMaterial.HasValue) { ToggleFavorite(CurrentMaterial.Value); Draw(page); return; }
            if (!drawer) ToggleMaterialList();
            Say("목록에서 재료 칸의 ★를 눌러 고정하세요.");
        }, current.HasValue && IsFavorite(current.Value));
        pin.name = "즐겨찾기";
        Btn("목록", StripX + 3, 307, 34, 17, ToggleMaterialList, drawer).name = "재료 목록";
    }

    // The star in each drawer entry: pins or unpins that material without choosing it.
    private void FavoriteToggle(QuickMaterial m, int x, int y)
    {
        bool on = IsFavorite(m);
        var button = Btn("★", x, y, 16, 16, () => { ToggleFavorite(m); Draw(page); }, on);
        button.name = "즐겨찾기 " + QuickTitle(m);
    }
}
#endif
