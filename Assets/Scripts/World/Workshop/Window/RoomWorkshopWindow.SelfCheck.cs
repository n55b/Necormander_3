#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Editor-only self check for the room workshop ("자동 점검").
// Started from Tools/Level Workshop/자동 점검. It builds its own throwaway room, works only inside a temporary
// folder (drafts and stamps), restores every option it touches, and shows a pass/fail list at the end.
// The user's rooms, drafts and stamps are never read for writing: the folders are swapped before the first step.
public sealed partial class RoomWorkshopWindow
{
    internal WorkshopPreview CheckPreview => preview;
    internal WorkshopRoom CheckDoc => Doc;
    internal WorkshopCatalog CheckCatalog => catalog;
    internal WorkshopCatalog.Piece[] CheckProps => props;
    internal float CheckCell => CellLocal;
    internal float CheckGapCells => ScatterGapCells;
    internal int CheckStampCount => stamp.Count;
    internal Vector2Int CheckPivot => StampPivot;

    internal void CheckOpen(WorkshopRoom room) => Begin(room, true);
    internal void CheckRebuild() => Rebuild();
    internal void CheckBeginStroke() => before = Doc.Serialize();
    internal void CheckEndStroke() => EndStroke();
    internal void CheckUndo() => History(false);
    internal string CheckWarnings() => RoomWarnings();
    internal void CheckTool(string name) { SetTool((Tool)Enum.Parse(typeof(Tool), name)); Draw(page); }
    internal void CheckLayer(int value) => SetLayer(value);
    internal void CheckTurn(int mode) => TransformStamp(mode);
    internal void CheckPlay() { playWarned = true; Play(); }

    internal void CheckScatter(bool on, IEnumerable<int> chosen, int density, int spacing)
    {
        scatter = on; scatterSet.Clear();
        foreach (int i in chosen) scatterSet.Add(i);
        scatterDensity = ScatterStep(density.ToString()); scatterSpacing = ScatterStep(spacing.ToString()); Draw(page);
    }

    internal void CheckRub(Vector3 world, bool first) { scatterReady = 0; ScatterPointer(world, first); }

    internal void CheckSample(Vector2Int from, Vector2Int to)
    {
        stamp.Clear(); sampling = true; sampleStart = from;
        FinishArea(true, preview.Map(layer).GetCellCenterWorld((Vector3Int)to));
    }

    internal List<(Vector2Int offset, Matrix4x4 matrix, TileBase tile)> CheckStampCells =>
        stamp.Select(s => (s.Offset, s.Tile.transform, s.Tile.tile)).ToList();

    internal void CheckGhost(Vector3Int cell)
    {
        ghostKey = null;
        var map = preview.Map(layer);
        UpdateCursorPreview(map, map.GetCellCenterWorld(cell), true);
    }

    internal void CheckStampAt(Vector3Int cell)
    {
        before = Doc.Serialize();
        StampPointer(preview.Map(layer), (Vector2Int)cell, true);
        EndStroke();
    }

    internal string CheckSaveStamp(string title)
    {
        var doc = new WorkshopStamp { title = title, layer = layer };
        foreach (var s in stamp)
            doc.cells.Add(new WorkshopStamp.Sample { offset = s.Offset, tile = s.Id, matrix = s.Tile.transform, color = s.Tile.color });
        Directory.CreateDirectory(WorkshopStamp.Folder);
        doc.Save(catalog);
        return Directory.GetFiles(WorkshopStamp.Folder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc)
            .First(f => File.ReadAllText(f).Contains(title));
    }

    internal void CheckOpenLibrary() { tool = Tool.Stamp; drawer = true; palettePage = 0; Draw(page); }
    internal bool CheckDeletePending => pendingStampDelete != null;

    internal void CheckFinish()
    {
        stamp.Clear(); scatter = false; scatterSet.Clear(); drawer = false; pendingStampDelete = null;
        CloseMap();
        WorkshopTrial.Draft = null;
        WorkshopTrial.Undo.Clear(); WorkshopTrial.Redo.Clear();
    }

    internal void CheckShowList() => Draw(Page.Desk);

    internal RawImage CheckViewport => viewport != null ? viewport.GetComponent<RawImage>() : null;
    internal TMPro.TMP_InputField CheckNameField => nameField;
    internal RectTransform CheckPanels => mapPanels;
    internal void CheckRedraw() => Draw(page);

    internal string[] CheckStrip => QuickStrip().Select(s => (s.favorite ? "*" : "") + s.material.Key).ToArray();
    internal void CheckRemember(char kind, string id, int layerIndex) => RememberQuick(new QuickMaterial(kind, id, layerIndex));
    internal string CheckCurrentMaterial => CurrentMaterial?.Key;
    internal string CheckToolName => tool.ToString();
    internal void CheckPinCurrent() { if (CurrentMaterial.HasValue) ToggleFavorite(CurrentMaterial.Value); Draw(page); }
    internal RectTransform CheckMapRect => viewport;
    internal int CheckMapX => MapLeft;
    internal int CheckMapWidth => MapWidth;
    internal void CheckToggleStrip() => ToggleStrip();
    internal void CheckToggleList() => ToggleMaterialList();
    internal bool CheckDrawer => drawer;
    internal int CheckListKind => paletteKind;
    internal void CheckPickListEntry(int kind, int n) { paletteKind = kind; UseQuick(PaletteEntries(kind)[n].material); }
    internal Button CheckButton(string name) => workspace.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == name);
    internal int CheckEmptySlots => workspace.GetComponentsInChildren<RectTransform>(true).Count(r => r.name.StartsWith("빈 재료 칸"));
    internal void CheckClickSlot(int number)
    {
        var slot = workspace.GetComponentsInChildren<Button>(true).First(b => b.name == "재료 칸 " + number);
        slot.onClick.Invoke();
    }
}

internal sealed class WorkshopSelfCheck : MonoBehaviour
{
    internal const string PendingKey = "WorkshopSelfCheck.Pending";
    private const string Title = "자동 점검 방";

    private readonly List<(bool ok, string name, string detail)> results = new();
    private string root, roomId, tileId;
    private bool juice, walk, background, cleaned;
    private TileBase floorTile;
    private string userSnapshot, favorites, recent;
    private int stripOpen = 1;

    // Names, sizes and times of the user's drafts and stamps, compared before and after the check.
    private static string UserData()
    {
        string List(string folder) => !Directory.Exists(folder) ? "-" : string.Join(";", Directory.GetFiles(folder)
            .OrderBy(f => f).Select(f => Path.GetFileName(f) + "@" + File.GetLastWriteTimeUtc(f).Ticks + "/" + new FileInfo(f).Length));
        return List(WorkshopDrafts.Folder) + "|" + List(WorkshopStamp.Folder) + "|" + WorkshopPreview.Juice + WorkshopTrial.WalkMode +
            "|" + PlayerPrefs.GetString(RoomWorkshopWindow.FavoritesKey, "") + "|" + PlayerPrefs.GetString(RoomWorkshopWindow.RecentKey, "") +
            "|" + PlayerPrefs.GetInt(RoomWorkshopWindow.StripOpenKey, 1);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!UnityEditor.SessionState.GetBool(PendingKey, false)) return;
        UnityEditor.SessionState.SetBool(PendingKey, false);
        var go = new GameObject("Workshop self check");
        DontDestroyOnLoad(go);
        go.AddComponent<WorkshopSelfCheck>();
    }

    private static RoomWorkshopWindow Window => FindObjectOfType<RoomWorkshopWindow>();

    private void Pass(string name, string detail) => results.Add((true, name, detail));
    private void Fail(string name, string detail) => results.Add((false, name, detail));
    private void Run(string name, Func<string> body)
    {
        try
        {
            string failure = body();
            if (failure == null) Pass(name, ""); else Fail(name, failure);
        }
        catch (Exception ex) { Fail(name, ex.GetType().Name + ": " + ex.Message); }
    }

    private static IEnumerator Until(Func<bool> condition, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            bool done;
            try { done = condition(); } catch { done = false; }
            if (done) yield break;
            yield return null;
        }
    }

    private static IEnumerator Pause(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    private IEnumerator Start()
    {
        background = Application.runInBackground;
        Application.runInBackground = true;
        juice = WorkshopPreview.Juice; walk = WorkshopTrial.WalkMode;
        favorites = PlayerPrefs.GetString(RoomWorkshopWindow.FavoritesKey, "");
        recent = PlayerPrefs.GetString(RoomWorkshopWindow.RecentKey, "");
        stripOpen = PlayerPrefs.GetInt(RoomWorkshopWindow.StripOpenKey, 1);
        PlayerPrefs.SetInt(RoomWorkshopWindow.StripOpenKey, 1);
        userSnapshot = UserData();
        root = Path.Combine(Application.temporaryCachePath, "WorkshopSelfCheck", Guid.NewGuid().ToString("N"));
        WorkshopDrafts.TestFolder = Path.Combine(root, "Drafts");
        WorkshopWorkspace.UseTestHome(Path.Combine(root, "Home"));
        Debug.Log("[자동 점검] 시작 · 임시 폴더 " + root);

        yield return Until(() => Window != null && Window.CheckCatalog != null, 30);
        yield return Pause(.5f);
        if (Window == null) { Fail("준비", "룸 워크숍 화면을 찾지 못했습니다."); Finish(); yield break; }

        bool ready = false;
        Run("준비 · 점검용 방 만들기", () => { string e = BuildRoom(); ready = e == null; return e; });
        if (!ready) { Finish(); yield break; }
        yield return null; yield return null;

        Run("출구 통로 · 빈 칸 표시와 경고", CheckPassages);
        yield return RedrawChecks();
        yield return CoverageChecks();
        yield return StripChecks();
        yield return EffectChecks();
        yield return ScatterChecks();
        yield return StampChecks();
        yield return LibraryChecks();
        yield return TrialChecks();
        Finish();
    }

    // ---------- room ----------

    private string BuildRoom()
    {
        var window = Window; var catalog = window.CheckCatalog;
        floorTile = Resources.Load<WorkshopTerrainTile>("Sprites/Tile/FieldPath");
        tileId = catalog.Id(floorTile);
        if (tileId == null) return "흙길(FieldPath) 타일을 재료 목록에서 찾지 못했습니다.";
        var doc = WorkshopPrefabs.Empty(catalog.DefaultRoom, Title, 0);
        roomId = doc.id;
        var cells = doc.exits.Select(e => new Vector2Int(Mathf.FloorToInt(e.local.x / .5f), Mathf.FloorToInt(e.local.y / .5f))).ToList();
        if (cells.Count < 2) return "빈 방의 출구가 " + cells.Count + "개뿐입니다.";
        int minX = cells.Min(c => c.x), maxX = cells.Max(c => c.x), minY = cells.Min(c => c.y), maxY = cells.Max(c => c.y);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            doc.Paint(0, new Vector2Int(x, y), tileId);
        window.CheckOpen(doc);
        if (window.CheckDoc?.id != roomId) return "점검용 방을 열지 못했습니다.";
        return null;
    }

    private string CheckPassages()
    {
        var window = Window; var preview = window.CheckPreview;
        var east = preview.ExitPassages().FirstOrDefault(e => e.direction == Vector2Int.right);
        if (east.cells == null) return "동쪽 출구가 없습니다.";
        if (preview.ExitPassages().Any(e => e.missing.Length > 0)) return "바닥을 다 칠했는데 빈 통로 칸이 있습니다.";
        // Take one passage cell away: it must show up as a gap and in the warning.
        var gap = east.cells[east.cells.Length / 2];
        window.CheckBeginStroke();
        window.CheckDoc.Paint(0, (Vector2Int)gap, null);
        window.CheckEndStroke(); window.CheckRebuild();
        preview = window.CheckPreview;
        var after = preview.ExitPassages().First(e => e.direction == Vector2Int.right);
        if (after.missing.Length != 1 || after.missing[0] != gap) return "빈 칸 1개를 지웠는데 표시된 빈 칸이 " + after.missing.Length + "개입니다.";
        string warning = window.CheckWarnings() ?? "";
        if (!warning.Contains("동쪽 출구 통로 바닥 1칸")) return "경고 문구에 빈 칸이 없습니다: " + warning;
        window.CheckBeginStroke();
        window.CheckDoc.Paint(0, (Vector2Int)gap, tileId);
        window.CheckEndStroke(); window.CheckRebuild();
        if (window.CheckPreview.ExitPassages().Any(e => e.missing.Length > 0)) return "다시 칠했는데 빈 칸 표시가 남았습니다.";
        if ((window.CheckWarnings() ?? "").Contains("출구 통로")) return "다시 칠했는데 경고가 남았습니다.";
        return null;
    }

    // ---------- redrawing ----------

    private IEnumerator RedrawChecks()
    {
        var window = Window;
        RawImage picture = null; TMPro.TMP_InputField name = null;
        Run("화면 갱신 · 버튼을 눌러도 지도와 이름 칸은 그대로", () =>
        {
            picture = window.CheckViewport; name = window.CheckNameField;
            var panels = window.CheckPanels;
            if (picture == null || name == null || panels == null) return "지도, 이름 칸 또는 패널을 찾지 못했습니다.";
            window.CheckRedraw();
            if (window.CheckViewport != picture) return "지도 그림이 다시 만들어졌습니다.";
            if (window.CheckNameField != name) return "이름 칸이 다시 만들어졌습니다.";
            if (window.CheckPanels == panels || panels != null && panels.gameObject.activeSelf) return "패널이 다시 그려지지 않았습니다.";
            return picture.texture == window.CheckPreview.Frame ? null : "지도 그림이 현재 방을 가리키지 않습니다.";
        });
        string original = window.CheckDoc.title;
        if (name != null) { name.Select(); name.ActivateInputField(); }
        yield return null; yield return null;
        Run("화면 갱신 · 입력 중 다시 그려도 커서와 글자 유지", () =>
        {
            if (name == null || !name.isFocused) return "이름 칸에 입력 상태로 들어가지 못했습니다.";
            name.SetTextWithoutNotify(original + " 입력중");
            name.caretPosition = name.selectionAnchorPosition = name.selectionFocusPosition = 2;
            window.CheckRedraw();
            if (!name.isFocused) return "다시 그린 뒤 입력 상태가 풀렸습니다.";
            if (name.text != original + " 입력중") return "입력하던 글자가 바뀌었습니다: " + name.text;
            return name.caretPosition == 2 ? null : "커서가 " + name.caretPosition + "번째로 옮겨졌습니다.";
        });
        if (name != null) { name.SetTextWithoutNotify(original); name.DeactivateInputField(); }
        yield return null;
        Run("화면 갱신 · 다른 방을 열면 지도도 새로 그림", () =>
        {
            var before = window.CheckViewport;
            window.CheckRebuild();
            var after = window.CheckViewport;
            if (after == null || after == before) return "방을 다시 열었는데 지도 그림이 그대로입니다.";
            if (window.CheckDoc.title != original) return "방 이름이 바뀌었습니다: " + window.CheckDoc.title;
            return after.texture == window.CheckPreview.Frame ? null : "새 지도 그림이 현재 방을 가리키지 않습니다.";
        });
        yield return null;
    }

    // ---------- layer coverage ----------

    private IEnumerator CoverageChecks()
    {
        var window = Window;
        int Filled(Tilemap map)
        {
            int n = 0;
            foreach (var c in map.cellBounds.allPositionsWithin) if (map.HasTile(c)) n++;
            return n;
        }
        window.CheckLayer(0);
        yield return null; yield return null;
        Run("레이어 표시 · 바닥은 주황 테두리만, 초록 칸 없음", () =>
        {
            var preview = window.CheckPreview;
            if (preview.CoverageLayer != 0 || !preview.CoverageVisible) return "바닥 레이어 표시가 켜지지 않았습니다.";
            if (preview.CoverageEdgeCount == 0) return "바닥 테두리가 없습니다.";
            if (preview.CoverageCellsVisible) return "바닥인데 초록 칸이 보입니다.";
            int floor = Filled(preview.Map(0));
            return preview.CoverageCount == floor ? null : "표시 " + preview.CoverageCount + "칸, 실제 바닥 " + floor + "칸입니다.";
        });

        // Shadow: an outline appears once something is placed on it, and goes away again.
        var map2 = window.CheckPreview.Map(2);
        window.CheckLayer(2);
        yield return null; yield return null;
        int shadows = map2 != null ? Filled(map2) : 0;
        var shade = ScriptableObject.CreateInstance<Tile>();
        shade.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * .5f, 4);
        var shadeCell = map2 != null ? map2.WorldToCell(window.CheckPreview.Ground.GetCellCenterWorld(new Vector3Int(-3, 3, 0))) : Vector3Int.zero;
        bool shaded = map2 != null && !map2.HasTile(shadeCell);
        if (shaded) map2.SetTile(shadeCell, shade);
        yield return null; yield return null;
        Run("레이어 표시 · 그림자를 놓으면 주황 테두리가 생김", () =>
        {
            var preview = window.CheckPreview;
            if (map2 == null) return "그림자 레이어가 없습니다.";
            if (!shaded) return "점검용 그림자를 놓을 칸이 없습니다.";
            if (preview.CoverageLayer != 2 || preview.CoverageCount != shadows + 1) return "그림자 표시가 " + preview.CoverageCount + "칸입니다 (기대 " + (shadows + 1) + ").";
            if (preview.CoverageEdgeCount == 0) return "그림자 테두리가 없습니다.";
            return preview.CoverageCellsVisible ? "그림자인데 초록 칸이 보입니다." : null;
        });
        if (shaded) map2.SetTile(shadeCell, null);
        UnityEngine.Object.Destroy(shade.sprite); UnityEngine.Object.Destroy(shade);

        window.CheckLayer(3);
        yield return null; yield return null;
        int baseline = 0;
        Run("레이어 표시 · 통행 불가를 고르면 채운 칸 수만큼 표시", () =>
        {
            var preview = window.CheckPreview;
            if (preview.CoverageLayer != 3 || !preview.CoverageVisible) return "통행 불가 레이어 표시가 켜지지 않았습니다.";
            baseline = Filled(preview.Map(3));
            return preview.CoverageCount == baseline ? null : "표시 " + preview.CoverageCount + "칸, 실제 " + baseline + "칸입니다.";
        });

        var map3 = window.CheckPreview.Map(3);
        var spot = map3.WorldToCell(window.CheckPreview.Ground.GetCellCenterWorld(new Vector3Int(3, -3, 0)));
        var extra = new[] { spot, spot + Vector3Int.right, spot + Vector3Int.right * 2 };
        var block = ScriptableObject.CreateInstance<Tile>();
        block.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * .5f, 4);
        foreach (var c in extra) if (!map3.HasTile(c)) map3.SetTile(c, block);
        int added = extra.Count(c => map3.GetTile(c) == block);
        yield return null; yield return null; yield return null;
        Run("레이어 표시 · 칸을 칠하면 바로 늘어나고 테두리가 생김", () =>
        {
            var preview = window.CheckPreview;
            if (added == 0) return "점검용 칸을 칠하지 못했습니다.";
            if (preview.CoverageCount != baseline + added) return "칠한 뒤 " + preview.CoverageCount + "칸 (기대 " + (baseline + added) + ").";
            if (!preview.CoverageCellsVisible) return "통행 불가인데 초록 칸이 보이지 않습니다.";
            return preview.CoverageEdgeCount > 0 ? null : "테두리 선이 없습니다.";
        });
        foreach (var c in extra) if (map3.GetTile(c) == block) map3.SetTile(c, null);
        yield return null; yield return null; yield return null;
        Run("레이어 표시 · 지우면 원래 칸 수로 돌아옴", () =>
            window.CheckPreview.CoverageCount == baseline ? null : "지운 뒤 " + window.CheckPreview.CoverageCount + "칸 (기대 " + baseline + ").");
        UnityEngine.Object.Destroy(block.sprite); UnityEngine.Object.Destroy(block);

        window.CheckLayer(1);
        yield return null; yield return null;
        Run("레이어 표시 · 벽은 주황 테두리만, 초록 칸 없음", () =>
        {
            var preview = window.CheckPreview;
            int walls = Filled(preview.Map(1));
            if (preview.CoverageLayer != 1 || preview.CoverageCount != walls)
                return "벽 레이어 표시가 맞지 않습니다 (표시 " + preview.CoverageCount + ", 실제 " + walls + ").";
            return preview.CoverageCellsVisible ? "벽인데 초록 칸이 보입니다." : null;
        });
        window.CheckLayer(0);
        yield return null;
    }

    // ---------- material strip ----------

    private IEnumerator StripChecks()
    {
        var window = Window;
        PlayerPrefs.SetString(RoomWorkshopWindow.FavoritesKey, "");
        PlayerPrefs.SetString(RoomWorkshopWindow.RecentKey, "");
        window.CheckTool("Select");
        yield return null;
        Run("재료 줄 · 지도 왼쪽에 빈 칸 6개, 지도는 그 오른쪽", () =>
        {
            if (window.CheckEmptySlots != RoomWorkshopWindow.QuickSlots) return "빈 칸이 " + window.CheckEmptySlots + "개입니다.";
            var map = window.CheckMapRect;
            if (map == null) return "지도를 찾지 못했습니다.";
            return Mathf.RoundToInt(map.anchoredPosition.x) == window.CheckMapX && Mathf.RoundToInt(map.rect.width) == window.CheckMapWidth ? null :
                "지도 위치 " + map.anchoredPosition.x + ", 너비 " + map.rect.width + "입니다.";
        });

        var prop = window.CheckProps[0].id;
        var enemy = window.CheckCatalog.pieces.First(p => p.enemy).id;
        window.CheckRemember('t', tileId, -1);
        window.CheckRemember('p', prop, -1);
        window.CheckRemember('e', enemy, -1);
        window.CheckRedraw();
        yield return null;
        Run("재료 줄 · 최근에 쓴 재료가 최근 순서로 나옴", () =>
        {
            var keys = window.CheckStrip;
            string expected = "e|" + enemy + ",p|" + prop + ",t|" + tileId;
            return string.Join(",", keys) == expected ? null : "순서가 " + string.Join(",", keys) + " 입니다.";
        });
        Run("재료 줄 · 칸을 누르면 그 도구와 재료로 바뀜", () =>
        {
            window.CheckClickSlot(2);
            if (window.CheckToolName != "Prop" || window.CheckCurrentMaterial != "p|" + prop) return "소품 칸을 눌렀는데 " + window.CheckToolName + " / " + window.CheckCurrentMaterial;
            window.CheckClickSlot(3);
            if (window.CheckToolName != "Auto" || !(window.CheckCurrentMaterial ?? "").StartsWith("t|" + tileId)) return "흙길 칸을 눌렀는데 " + window.CheckToolName + " / " + window.CheckCurrentMaterial;
            return null;
        });
        Run("재료 줄 · 별표로 고정하면 맨 위에 남고 다음 실행에도 기억", () =>
        {
            window.CheckPinCurrent();
            window.CheckRemember('p', window.CheckProps[1].id, -1);
            var keys = window.CheckStrip;
            if (keys.Length == 0 || keys[0] != "*t|" + tileId) return "맨 위가 " + (keys.Length > 0 ? keys[0] : "없음") + " 입니다.";
            return PlayerPrefs.GetString(RoomWorkshopWindow.FavoritesKey, "").Contains(tileId) ? null : "즐겨찾기가 저장되지 않았습니다.";
        });
        Run("재료 줄 · 별표를 다시 누르면 고정이 풀림", () =>
        {
            window.CheckPinCurrent();
            return window.CheckStrip.Any(k => k.StartsWith("*")) ? "고정이 풀리지 않았습니다." : null;
        });
        window.CheckTool("Select");
        yield return null;
        Run("재료 줄 · 선택 도구에서도 목록과 별표가 열림", () =>
        {
            var pin = window.CheckButton("즐겨찾기"); var list = window.CheckButton("재료 목록");
            if (pin == null || list == null || !pin.interactable || !list.interactable) return "선택 도구에서 목록이나 별표 버튼이 꺼져 있습니다.";
            pin.onClick.Invoke();
            if (!window.CheckDrawer) return "별표를 눌렀는데 목록이 열리지 않았습니다.";
            if (window.CheckButton("재료 종류 소품") == null) return "목록에 종류 탭이 없습니다.";
            window.CheckToggleList();
            if (window.CheckDrawer) return "목록 버튼으로 닫히지 않았습니다.";
            window.CheckButton("재료 목록").onClick.Invoke();
            return window.CheckDrawer ? null : "목록 버튼으로 열리지 않았습니다.";
        });
        Run("재료 줄 · 목록에서 고르면 그 도구로 바뀜", () =>
        {
            window.CheckPickListEntry(3, 0);
            if (window.CheckToolName != "Prop" || window.CheckCurrentMaterial != "p|" + window.CheckProps[0].id) return "소품을 골랐는데 " + window.CheckToolName + " / " + window.CheckCurrentMaterial;
            if (window.CheckDrawer) return "고른 뒤에도 목록이 열려 있습니다.";
            window.CheckPickListEntry(0, 0);
            return window.CheckToolName == "Auto" ? null : "자동 지형을 골랐는데 " + window.CheckToolName;
        });
        yield return null;
        Run("재료 줄 · 접으면 지도가 넓어지고 펴면 돌아옴", () =>
        {
            int open = window.CheckMapWidth;
            window.CheckToggleStrip();
            var map = window.CheckMapRect;
            if (window.CheckButton("재료 줄 펴기") == null) return "접은 뒤 펴기 탭이 없습니다.";
            if (map == null || Mathf.RoundToInt(map.rect.width) <= open || window.CheckPreview.Frame.width != Mathf.RoundToInt(map.rect.width))
                return "접었는데 지도 너비가 " + (map != null ? map.rect.width : 0) + " (그림 " + window.CheckPreview.Frame.width + ") 입니다.";
            if (PlayerPrefs.GetInt(RoomWorkshopWindow.StripOpenKey, 1) != 0) return "접은 상태가 저장되지 않았습니다.";
            window.CheckButton("재료 줄 펴기").onClick.Invoke();
            map = window.CheckMapRect;
            return Mathf.RoundToInt(map.rect.width) == open && window.CheckPreview.Frame.width == open && window.CheckButton("재료 줄 접기") != null ? null :
                "다시 폈는데 지도 너비가 " + map.rect.width + " 입니다.";
        });
        PlayerPrefs.SetString(RoomWorkshopWindow.FavoritesKey, favorites);
        PlayerPrefs.SetString(RoomWorkshopWindow.RecentKey, recent);
        window.CheckTool("Select");
        yield return null;
    }

    // ---------- editing feedback ----------

    private IEnumerator EffectChecks()
    {
        var window = Window;
        Run("편집 효과 · 꺼져 있으면 효과 없음", () =>
        {
            WorkshopPreview.Juice = false;
            window.CheckPreview.Ground.SetTile(new Vector3Int(200, 200, 0), floorTile);
            int n = window.CheckPreview.ActiveEffects;
            return n == 0 ? null : "꺼져 있는데 효과 " + n + "개가 생겼습니다.";
        });
        int started = 0;
        Run("편집 효과 · 켜면 효과가 나옴", () =>
        {
            WorkshopPreview.Juice = true;
            var cells = Enumerable.Range(0, 20).Select(i => new Vector3Int(210 + i % 5, 210 + i / 5, 0)).ToArray();
            window.CheckPreview.Ground.SetTiles(cells, cells.Select(_ => (TileBase)floorTile).ToArray());
            started = window.CheckPreview.ActiveEffects;
            return started > 0 ? null : "켰는데 효과가 생기지 않았습니다.";
        });
        yield return Pause(.5f);
        Run("편집 효과 · 0.5초 안에 모두 끝남", () =>
            window.CheckPreview.ActiveEffects == 0 ? null : "효과 " + window.CheckPreview.ActiveEffects + "개가 남아 있습니다.");

        string pieceId = null; Vector3 scale = default; Transform node = null;
        Run("편집 효과 · 배치물 튀어 오름 준비", () =>
        {
            pieceId = Guid.NewGuid().ToString("N");
            window.CheckBeginStroke();
            window.CheckDoc.pieces.Add(new WorkshopRoom.Piece { id = pieceId, catalog = window.CheckProps[0].id, local = Vector3.zero });
            window.CheckEndStroke(); window.CheckRebuild();
            node = window.CheckPreview.Pieces.First(p => p.Key == pieceId).Value;
            scale = node.localScale;
            window.CheckPreview.Bounce(pieceId);
            return null;
        });
        yield return Pause(.07f);
        bool moved = node != null && node.localScale != scale;
        yield return Pause(.35f);
        Run("편집 효과 · 튀어 오른 뒤 원래 크기로 복귀", () =>
            node == null ? "배치물이 없습니다." : !moved ? "튀어 오르지 않았습니다." :
            node.localScale == scale ? null : "크기가 " + node.localScale + "로 남았습니다 (원래 " + scale + ").");
        WorkshopPreview.Juice = false;
        window.CheckRebuild();
        yield return null;
    }

    // ---------- scatter ----------

    private IEnumerator ScatterChecks()
    {
        var window = Window;
        window.CheckTool("Prop");
        Vector3 Cell(int x, int y) => window.CheckPreview.Ground.GetCellCenterWorld(new Vector3Int(x, y, 0));

        Run("흩뿌리기 · 소품 0종이면 아무것도 놓지 않음", () =>
        {
            window.CheckScatter(true, Array.Empty<int>(), 3, 1);
            int before = window.CheckDoc.pieces.Count;
            window.CheckBeginStroke();
            for (int x = -6; x <= 6; x++) window.CheckRub(Cell(x, 0), x == -6);
            window.CheckEndStroke();
            int placed = window.CheckDoc.pieces.Count - before;
            return placed == 0 ? null : placed + "개가 놓였습니다.";
        });
        yield return null;

        int count = 0;
        Run("흩뿌리기 · 바닥 위, 통로 밖, 간격 유지, 고른 소품만", () =>
        {
            var props = window.CheckProps;
            window.CheckScatter(true, new[] { 0, 1 }, 3, 1);
            count = window.CheckDoc.pieces.Count;
            window.CheckBeginStroke();
            bool first = true;
            for (int y = -3; y <= 3; y += 1)
            for (int x = -12; x <= 12; x++) { window.CheckRub(Cell(x, y), first); first = false; }
            window.CheckEndStroke();
            var preview = window.CheckPreview;
            var added = window.CheckDoc.pieces.Skip(count).ToList();
            if (added.Count == 0) return "아무것도 놓이지 않았습니다.";
            var passages = new HashSet<Vector3Int>(preview.ExitPassages().SelectMany(e => e.cells));
            var allowed = new HashSet<string> { props[0].id, props[1].id };
            float gap = window.CheckGapCells * window.CheckCell * .999f;
            var others = window.CheckDoc.pieces.Where(p => !p.removed).Select(p => p.local).ToList();
            foreach (var p in added)
            {
                var world = preview.World(p.local);
                if (!preview.Walkable(world)) return "바닥 밖에 놓였습니다: " + p.local;
                if (passages.Contains(preview.Ground.WorldToCell(world))) return "출구 통로 안에 놓였습니다: " + p.local;
                if (!allowed.Contains(p.catalog)) return "고르지 않은 소품이 놓였습니다.";
                if (others.Any(o => o != p.local && ((Vector2)(o - p.local)).magnitude < gap)) return "간격보다 가깝게 놓였습니다: " + p.local;
            }
            return null;
        });
        yield return null;
        Run("흩뿌리기 · 한 번 문지른 것은 실행 취소 한 번에 사라짐", () =>
        {
            window.CheckUndo();
            return window.CheckDoc.pieces.Count == count ? null : "실행 취소 뒤 " + (window.CheckDoc.pieces.Count - count) + "개가 남았습니다.";
        });
        window.CheckScatter(false, Array.Empty<int>(), 2, 2);
        yield return null;
    }

    // ---------- stamp ----------

    private string StampLands(Vector3Int cursor)
    {
        var window = Window;
        window.CheckGhost(cursor);
        var ghost = GameObject.Find("Brush preview")?.GetComponent<Tilemap>();
        if (ghost == null) return "커서 미리보기가 없습니다.";
        ghost.CompressBounds();
        var cells = new List<Vector3Int>();
        foreach (var p in ghost.cellBounds.allPositionsWithin) if (ghost.HasTile(p)) cells.Add(p);
        if (cells.Count != window.CheckStampCount) return "미리보기 칸 " + cells.Count + " / 도장 칸 " + window.CheckStampCount;
        float dx = (cells.Min(c => c.x) + cells.Max(c => c.x)) / 2f - cursor.x;
        float dy = (cells.Min(c => c.y) + cells.Max(c => c.y)) / 2f - cursor.y;
        if (dx < 0 || dx > .5f || dy < 0 || dy > .5f) return $"도장 가운데가 마우스에서 ({dx}, {dy})칸 떨어져 있습니다.";
        var ground = window.CheckPreview.Ground;
        foreach (var c in cells) if (ground.HasTile(c)) return "점검 자리에 이미 타일이 있습니다.";
        window.CheckStampAt(cursor);
        ground = window.CheckPreview.Ground;
        int placed = cells.Count(c => ground.HasTile(c));
        if (placed != cells.Count) return "찍힌 칸 " + placed + " / 미리보기 칸 " + cells.Count;
        if (cells.Any(c => ground.GetTransformMatrix(c) != Matrix4x4.identity && !(ground.GetTile(c) is Tile)))
            return "자동 타일이 돌아간 모양으로 찍혔습니다.";
        return null;
    }

    private IEnumerator StampChecks()
    {
        var window = Window;
        window.CheckLayer(0);
        window.CheckTool("Stamp");
        Run("도장 · 홀수 크기, 가운데가 마우스 위치", () =>
        {
            window.CheckSample(new Vector2Int(-3, -1), new Vector2Int(1, 1));
            return window.CheckStampCount == 15 ? StampLands(new Vector3Int(80, 0, 0)) : "5×3 범위에서 " + window.CheckStampCount + "칸을 떴습니다.";
        });
        yield return null;
        Run("도장 · 짝수 크기, 가운데가 마우스 위치", () =>
        {
            window.CheckSample(new Vector2Int(-3, -2), new Vector2Int(0, 1));
            return window.CheckStampCount == 16 ? StampLands(new Vector3Int(100, 0, 0)) : "4×4 범위에서 " + window.CheckStampCount + "칸을 떴습니다.";
        });
        yield return null;
        Run("도장 · 회전 4번·뒤집기 2번이면 원래대로", () =>
        {
            string Key() => string.Join(";", window.CheckStampCells.OrderBy(c => c.offset.x).ThenBy(c => c.offset.y)
                .Select(c => c.offset + "|" + c.matrix.ToString("F3")));
            var start = Key();
            for (int i = 0; i < 4; i++) window.CheckTurn(0);
            if (Key() != start) return "회전 4번 뒤 모양이 다릅니다.";
            window.CheckTurn(1); window.CheckTurn(1);
            if (Key() != start) return "좌우 뒤집기 2번 뒤 모양이 다릅니다.";
            window.CheckTurn(2); window.CheckTurn(2);
            return Key() == start ? null : "상하 뒤집기 2번 뒤 모양이 다릅니다.";
        });
        Run("도장 · 회전한 자동 타일은 그림을 돌리지 않음", () =>
        {
            window.CheckTurn(0);
            if (window.CheckStampCells.Any(c => !(c.tile is Tile) && c.matrix != Matrix4x4.identity)) return "자동 타일에 회전값이 남았습니다.";
            return StampLands(new Vector3Int(120, 0, 0));
        });
        yield return null;
        Run("도장 · 가장자리에 빈 줄이 있어도 가운데 유지", () =>
        {
            // A box that hangs over the room edge: the empty columns must not shift the centre.
            int edge = Window.CheckDoc.cells.Where(c => c.layer == 0 && !string.IsNullOrEmpty(c.tile)).Max(c => c.at.x);
            window.CheckSample(new Vector2Int(edge - 2, -1), new Vector2Int(edge + 4, 1));
            return window.CheckStampCount == 9 ? StampLands(new Vector3Int(140, 0, 0)) : "가장자리 범위에서 " + window.CheckStampCount + "칸을 떴습니다 (9칸 예상).";
        });
        yield return null;
    }

    // ---------- stamp library ----------

    private Button FindButton(Func<Button, bool> match) => FindObjectsOfType<Button>().FirstOrDefault(match);
    private static string Caption(Button b) => b.GetComponentInChildren<TMPro.TMP_Text>()?.text ?? "";

    private Button DeleteButtonFor(string title)
    {
        var entry = FindButton(b => Caption(b).StartsWith(title + " ·"));
        if (entry == null) return null;
        var at = ((RectTransform)entry.transform).anchoredPosition + new Vector2(199, 0);
        return FindObjectsOfType<Button>().Where(b => b.name == "도장 삭제")
            .OrderBy(b => (((RectTransform)b.transform).anchoredPosition - at).sqrMagnitude).FirstOrDefault();
    }

    private IEnumerator LibraryChecks()
    {
        var window = Window;
        string first = null, second = null;
        bool safe = WorkshopStamp.Folder.StartsWith(root);
        if (!safe) { Fail("도장 보관함", "도장 폴더가 임시 폴더가 아니어서 건너뜁니다: " + WorkshopStamp.Folder); yield break; }
        Run("도장 보관함 · 저장", () =>
        {
            window.CheckSample(new Vector2Int(-3, -1), new Vector2Int(1, 1));
            first = window.CheckSaveStamp("점검 도장 A");
            return File.Exists(first) ? null : "파일이 생기지 않았습니다.";
        });
        yield return Pause(1.1f);
        Run("도장 보관함 · 두 번째 저장", () =>
        {
            second = window.CheckSaveStamp("점검 도장 B");
            return File.Exists(second) ? null : "파일이 생기지 않았습니다.";
        });
        yield return Pause(1.1f);
        Run("도장 보관함 · 썸네일 표시", () =>
        {
            window.CheckOpenLibrary();
            var entry = FindButton(b => Caption(b).StartsWith("점검 도장 A ·"));
            if (entry == null) return "목록에 도장이 없습니다.";
            var thumb = entry.GetComponentsInChildren<RawImage>(true).FirstOrDefault(r => r.name == "Stamp thumbnail");
            return thumb != null && thumb.texture != null ? null : "썸네일이 없습니다.";
        });
        Run("도장 보관함 · 쓴 도장이 맨 앞으로", () =>
        {
            var before = File.GetLastWriteTimeUtc(first);
            FindButton(b => Caption(b).StartsWith("점검 도장 A ·")).onClick.Invoke();
            return File.GetLastWriteTimeUtc(first) > File.GetLastWriteTimeUtc(second) && File.GetLastWriteTimeUtc(first) > before
                ? null : "고른 도장의 순서가 앞으로 오지 않았습니다.";
        });
        yield return null;
        Run("도장 보관함 · 삭제 확인에서 취소하면 남음", () =>
        {
            window.CheckOpenLibrary();
            var x = DeleteButtonFor("점검 도장 B");
            if (x == null) return "삭제 버튼이 없습니다.";
            x.onClick.Invoke();
            if (!window.CheckDeletePending) return "확인 창이 뜨지 않았습니다.";
            var title = FindObjectsOfType<TMPro.TMP_Text>().Any(t => t.text.StartsWith("이름  ·  점검 도장 B"));
            FindButton(b => Caption(b) == "취소").onClick.Invoke();
            return !title ? "확인 창에 도장 이름이 없습니다." : File.Exists(second) ? null : "취소했는데 파일이 지워졌습니다.";
        });
        yield return null;
        Run("도장 보관함 · 삭제 확인하면 지워짐", () =>
        {
            var x = DeleteButtonFor("점검 도장 B");
            if (x == null) return "삭제 버튼이 없습니다.";
            x.onClick.Invoke();
            var confirm = FindButton(b => b.name == "도장 삭제 확인");
            if (confirm == null) return "확인 버튼이 없습니다.";
            confirm.onClick.Invoke();
            if (File.Exists(second)) return "파일이 남아 있습니다.";
            return File.Exists(first) ? null : "다른 도장까지 지워졌습니다.";
        });
        yield return null;
    }

    // ---------- test play ----------

    private static RoomInstance TestRoom()
    {
        var generator = FindObjectOfType<MapGenerator>();
        return generator == null ? null : generator.AllRooms.FirstOrDefault(r => r != null && r.gridPosition == Vector2Int.zero);
    }
    private static int Enemies() =>
        FindObjectsOfType<CharacterHealth>().Count(h => !h.transform.root.CompareTag("Player") && !h.IsDead);
    private static bool InTrial() =>
        SceneManager.GetActiveScene().name != WorkshopTrial.SceneName && !UnityNote.SceneLoader.IsLoading &&
        TestRoom() != null && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null;

    private IEnumerator Reload(Action start)
    {
        var marker = new GameObject("Self check scene marker");
        start();
        yield return Until(() => marker == null, 30);
        yield return Until(InTrial, 40);
        yield return Pause(1.5f);
    }

    private IEnumerator TrialChecks()
    {
        WorkshopTrial.WalkMode = true;
        yield return Reload(() => Window.CheckPlay());
        Run("시험 · 걷기 모드에서 적이 나오지 않음", () =>
        {
            if (!InTrial()) return "시험 화면이 열리지 않았습니다.";
            var room = TestRoom();
            if (!room.isCleared) return "방이 전투 상태입니다.";
            int n = Enemies();
            return n == 0 ? null : "적 " + n + "명이 있습니다.";
        });
        Run("시험 · 출구 통로에 예전 잔디가 없음", () =>
        {
            var grounds = FindObjectsOfType<Tilemap>().Where(m => m.name == "Ground" && m.GetComponentInParent<RoomInstance>() == null);
            int grass = 0;
            foreach (var map in grounds)
                foreach (var p in map.cellBounds.allPositionsWithin)
                    if (map.GetTile(p)?.name == "Tilemap_16px_1") grass++;
            return grass == 0 ? null : "잔디 " + grass + "칸이 깔렸습니다.";
        });
        float max = 0;
        Run("시험 · R 다시 시작 준비 (피해 주기)", () =>
        {
            var hp = GameManager.Instance.PLAYERCONTROLLER.Stat.Health;
            max = hp.MaxHP;
            hp.TakeDamage(new DamageInfo(max * .3f, DamageType.Fixed, hp.gameObject));
            return hp.CurHP < max ? null : "피해가 들어가지 않았습니다.";
        });
        yield return Reload(() => WorkshopTrial.Restart());
        Run("시험 · R 다시 시작하면 처음 상태", () =>
        {
            if (!InTrial()) return "다시 시작한 화면이 열리지 않았습니다.";
            var hp = GameManager.Instance.PLAYERCONTROLLER.Stat.Health;
            if (hp.CurHP < hp.MaxHP) return "체력이 " + hp.CurHP + "/" + hp.MaxHP + "입니다.";
            return TestRoom().isCleared && Enemies() == 0 ? null : "걷기 모드가 유지되지 않았습니다.";
        });
        WorkshopTrial.WalkMode = false;
        yield return Reload(() => WorkshopTrial.Restart());
        yield return Until(() => Enemies() > 0, 10);
        Run("시험 · 걷기 모드를 끄면 전투 시작", () =>
            !InTrial() ? "시험 화면이 열리지 않았습니다." : Enemies() > 0 ? null : "적이 나오지 않았습니다.");
        var marker = new GameObject("Self check scene marker");
        WorkshopTrial.Open();
        yield return Until(() => marker == null, 30);
        yield return Until(() => Window != null && Window.CheckPreview != null, 30);
        yield return Pause(.5f);
        Run("시험 · 편집기로 돌아오면 같은 방", () =>
            Window?.CheckDoc?.id == roomId ? null : "편집기에 다른 방이 열렸습니다.");
    }

    // ---------- finish ----------

    private void Finish()
    {
        Cleanup();
        Run("안전 · 사용자 방·임시 저장·도장·설정 그대로", () => UserData() == userSnapshot ? null : "점검 전후로 사용자 데이터가 달라졌습니다.");
        int passed = results.Count(r => r.ok);
        string summary = $"[자동 점검] 통과 {passed} / 전체 {results.Count}";
        foreach (var r in results)
        {
            string line = (r.ok ? "통과 · " : "실패 · ") + r.name + (r.detail.Length > 0 ? " — " + r.detail : "");
            if (r.ok) Debug.Log("[자동 점검] " + line); else Debug.LogWarning("[자동 점검] " + line);
        }
        if (passed == results.Count) Debug.Log(summary); else Debug.LogWarning(summary);
        ShowReport(passed);
    }

    private void Cleanup()
    {
        if (cleaned) return;
        cleaned = true;
        try { Window?.CheckFinish(); } catch (Exception ex) { Debug.LogWarning("[자동 점검] 편집기 정리 중 오류: " + ex.Message); }
        WorkshopPreview.Juice = juice; WorkshopTrial.WalkMode = walk;
        if (favorites != null) { PlayerPrefs.SetString(RoomWorkshopWindow.FavoritesKey, favorites); PlayerPrefs.SetString(RoomWorkshopWindow.RecentKey, recent); }
        PlayerPrefs.SetInt(RoomWorkshopWindow.StripOpenKey, stripOpen);
        Application.runInBackground = background;
        if (roomId != null) PlayerPrefs.DeleteKey("Workshop.Opened." + roomId);
        WorkshopDrafts.TestFolder = null;
        WorkshopWorkspace.UseDefaultHome();
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (Exception ex) { Debug.LogWarning("[자동 점검] 임시 폴더를 지우지 못했습니다: " + ex.Message); }
        // The list is drawn only now, so it shows the user's own rooms again.
        try { Window?.CheckShowList(); } catch (Exception ex) { Debug.LogWarning("[자동 점검] 목록 표시 중 오류: " + ex.Message); }
    }

    private void OnDestroy() => Cleanup();

    private void ShowReport(int passed)
    {
        var canvas = WorkshopUI.Canvas("Workshop self check report", 26000);
        canvas.transform.SetParent(transform, false);
        var t = canvas.transform;
        WorkshopUI.Box(t, "Report blocker", 0, 0, 640, 360, new Color(0, 0, 0, .6f));
        WorkshopUI.Frame(t, "Report", 40, 18, 560, 324);
        bool all = passed == results.Count;
        var head = WorkshopUI.Label(t, $"자동 점검 결과 · 통과 {passed} / 전체 {results.Count}", 54, 24, 420, 22, 14);
        head.color = all ? WorkshopUI.Mint : new Color32(255, 140, 140, 255);
        // One line per area, then every failure with its reason, so the window alone tells the whole story.
        int y = 52;
        foreach (var group in results.GroupBy(r => r.name.Split(new[] { " · " }, StringSplitOptions.None)[0]))
        {
            int ok = group.Count(r => r.ok), total = group.Count();
            var line = WorkshopUI.Label(t, (ok == total ? "통과  " : "실패  ") + group.Key + "  " + ok + " / " + total, 60, y, 520, 15, 11);
            if (ok != total) line.color = new Color32(255, 140, 140, 255);
            y += 17;
        }
        var failed = results.Where(r => !r.ok).ToList();
        y += 6;
        WorkshopUI.Label(t, failed.Count == 0 ? "모든 항목을 통과했습니다." : "실패한 항목", 60, y, 520, 14, 10).color =
            failed.Count == 0 ? WorkshopUI.Mint : new Color32(255, 140, 140, 255);
        y += 16;
        foreach (var r in failed)
        {
            if (y > 296) { WorkshopUI.Label(t, "… 실패 " + (failed.Count - failed.IndexOf(r)) + "개 더", 60, y, 520, 13, 9); break; }
            WorkshopUI.Label(t, r.name + " — " + r.detail, 60, y, 528, 13, 9).color = new Color32(255, 140, 140, 255);
            y += 13;
        }
        WorkshopUI.Button(t, "닫기", 500, 316, 86, 20, () => Destroy(gameObject));
    }
}
#endif
