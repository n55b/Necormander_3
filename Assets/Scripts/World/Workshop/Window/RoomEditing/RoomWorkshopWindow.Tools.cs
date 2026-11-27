#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// The toolbox beside the map, number boxes, the magnet, the cursor preview, view settings
// and the checks before testing. Painting and shortcuts live in their own files.
public sealed partial class RoomWorkshopWindow
{
    private static int brushSize = 1, newRoomSize = 1;
    private const int MaxBrush = 5;
    private static bool showGrid;
    private static readonly bool[] hiddenLayers = new bool[4];
    private static readonly List<string> recentTiles = new();
    private static readonly List<(string catalog, Vector3 local)> clipboard = new();
    private Vector2Int? shapeStart;
    private bool lineStroke, helpOpen, playWarned;
    private float zoomReady;
    private static int snapMode; // 0 off (pixel), 1 one cell, 2 half a cell
    private string ghostKey;
    // The region the fill tool would cover from the hovered cell; reused while the cursor stays inside it.
    private HashSet<Vector2Int> fillRegion;
    private int fillLayer = -1;
    private string fillMaterial;

    // ---------- Number boxes: Enter computes a formula, the next Enter applies ----------

    private void SubmitNumber(TMPro.TMP_InputField field, Func<double, string> format, Action apply)
    {
        if (WorkshopMath.IsFormula(field.text))
        {
            if (WorkshopMath.TryEvaluate(field.text, out double value)) field.text = format(value);
            else Say("계산식을 확인하세요 · " + field.text);
            // Submitting ends editing; the box is reopened next frame with the caret after the result.
            StartCoroutine(Refocus(field));
            return;
        }
        Safe(apply);
    }

    private static System.Collections.IEnumerator Refocus(TMPro.TMP_InputField field)
    {
        yield return null;
        if (field == null) yield break;
        field.onFocusSelectAll = false;
        field.Select(); field.ActivateInputField();
        yield return null;
        if (field != null) field.MoveTextEnd(false);
    }

    // ---------- Magnet ----------

    private string SnapLabel => snapMode switch { 1 => "자석 칸", 2 => "자석 반칸", _ => "자석 끔" };

    private void CycleSnap()
    {
        snapMode = (snapMode + 1) % 3;
        ghostKey = null;
        Say(snapMode == 0 ? "자석 끔 · 픽셀 단위로 배치합니다." : SnapLabel + " · 배치와 이동이 타일 격자에 붙습니다. Alt를 누르는 동안은 잠시 꺼집니다.");
        Draw(page);
    }

    private Vector3 GridOriginLocal => preview.Local(preview.Ground.CellToWorld(Vector3Int.zero));

    // Rounds a room-local point to the tile grid (magnet on) or to whole texels (magnet off or Alt held).
    private Vector3 SnapLocal(Vector3 local)
    {
        local.z = 0;
        float cell = CellLocal;
        if (cell <= 0) return local;
        int mode = Keyboard.current?.altKey.isPressed == true ? 0 : snapMode;
        float step = mode == 1 ? cell : mode == 2 ? cell * .5f : cell / 16f;
        var origin = GridOriginLocal;
        local.x = origin.x + Mathf.Round((local.x - origin.x) / step) * step;
        local.y = origin.y + Mathf.Round((local.y - origin.y) / step) * step;
        return local;
    }

    // ---------- Cursor preview ----------

    private static readonly Color32 EraseColor = new(255, 104, 111, 144), CursorColor = new(163, 244, 195, 136), GhostTint = new(255, 255, 255, 150);

    private void UpdateCursorPreview(Tilemap map, Vector3 world, bool over)
    {
        bool dragging = before != null && (tool == Tool.Rect || lineStroke || selectingArea || sampling);
        if (!over || map == null || tool == Tool.Select || dragging)
        {
            preview.Hover(null, Vector3.zero, false, Color.clear);
            preview.HideGhost(); ghostKey = null; fillRegion = null;
            return;
        }
        var cell = map.WorldToCell(world);
        string key = tool + "|" + layer + "|" + materialIndex + "|" + brushSize + "|" + stamp.Count + "|" + enemyIndex + "|" + propIndex + "|" + snapMode + "|" + scatter + "|" +
            (tool is Tool.Enemy or Tool.Prop ? SnapLocal(preview.Local(world)).ToString("F4") : cell.ToString());
        if (key == ghostKey) return;
        ghostKey = key;
        switch (tool)
        {
            case Tool.Fill:
                PreviewFill(map, cell);
                break;
            case Tool.Paint: case Tool.Auto: case Tool.Rect:
            {
                preview.Hover(null, Vector3.zero, false, Color.clear);
                if (palette.Length == 0) { preview.HideGhost(); return; }
                var tile = palette[materialIndex].tile;
                var cells = tool is Tool.Paint or Tool.Auto ? Brush((Vector2Int)cell) : new[] { (Vector2Int)cell };
                preview.GhostTiles(map, cells.Select(at => new TileChangeData((Vector3Int)at, tile, GhostTint, Matrix4x4.identity)).ToArray());
                break;
            }
            case Tool.Stamp when stamp.Count > 0:
                preview.Hover(null, Vector3.zero, false, Color.clear);
                var pivot = (Vector3Int)StampPivot;
                preview.GhostTiles(map, stamp.Select(s => new TileChangeData(cell + (Vector3Int)s.Offset - pivot, s.Tile.tile, GhostTint,
                    s.Tile.tile is Tile ? s.Tile.transform : Matrix4x4.identity)).ToArray());
                break;
            case Tool.Erase:
            {
                preview.HideGhost();
                int low = -(brushSize - 1) / 2;
                preview.HoverCells(map, cell + new Vector3Int(low, low, 0), cell + new Vector3Int(low + brushSize - 1, low + brushSize - 1, 0), EraseColor);
                break;
            }
            case Tool.Prop when scatter:
                ScatterCursor(world);
                break;
            case Tool.Enemy: case Tool.Prop:
                preview.Hover(null, Vector3.zero, false, Color.clear);
                var piece = tool == Tool.Enemy ? enemies[enemyIndex] : props[propIndex];
                preview.GhostPiece(piece.prefab, SnapLocal(preview.Local(world)));
                break;
            default:
                preview.HideGhost();
                preview.Hover(map, world, true, CursorColor);
                break;
        }
    }

    // ---------- View ----------

    private void ApplyViewSettings(bool home)
    {
        preview.Guide = GuideRect();
        for (int i = 0; i < hiddenLayers.Length; i++) preview.SetLayerVisible(i, !hiddenLayers[i]);
        preview.ShowGrid(showGrid);
        ghostKey = null; fillRegion = null;
        if (home && preview.Zoom == 0) preview.Overview(); // refit now that the guide is known
        preview.PositionOutlines();
    }

    // The rectangle spanned by the exits of a room that started empty: where the floor is expected to go.
    private Rect? GuideRect()
    {
        if (!Doc.clearTiles || Doc.exits.Count < 2) return null;
        float minX = Doc.exits.Min(e => e.local.x), maxX = Doc.exits.Max(e => e.local.x);
        float minY = Doc.exits.Min(e => e.local.y), maxY = Doc.exits.Max(e => e.local.y);
        if (maxX - minX < .01f || maxY - minY < .01f) return null;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private float CellLocal => Mathf.Abs((preview.Local(Vector3.right * preview.CellWidth) - preview.Local(Vector3.zero)).x);

    private void StepZoom(Vector2 uv, int delta)
    {
        preview.ZoomStep(uv, delta);
        if (zoomIn != null) zoomIn.interactable = preview.CanZoomIn;
        if (zoomOut != null) zoomOut.interactable = preview.CanZoomOut;
        UpdateDetail();
    }

    // ---------- Toolbox ----------

    private void DrawToolbox()
    {
        WorkshopUI.Frame(content, "Toolbox", 491, 36, 143, 291);
        WorkshopUI.Label(content, "지형 그리기", 497, 36, 130, 16, 12).color = WorkshopUI.Mint;
        Choice("자동 타일", Tool.Auto, 497, 53); Choice("칠하기", Tool.Paint, 563, 53);
        Choice("사각형", Tool.Rect, 497, 71); Choice("채우기", Tool.Fill, 563, 71);
        Choice("지우개", Tool.Erase, 497, 89); Choice("스포이드", Tool.Sample, 563, 89);
        WorkshopUI.Label(content, "배치와 선택", 497, 108, 130, 16, 12).color = WorkshopUI.Mint;
        Choice("선택", Tool.Select, 497, 125); Choice("소품", Tool.Prop, 563, 125);
        Choice("적", Tool.Enemy, 497, 143); Choice("출구", Tool.Door, 563, 143);
        Choice("시작점", Tool.Start, 497, 161); Choice("복사 도장", Tool.Stamp, 563, 161);

        // The working layer is always on show, whichever tool is active.
        Btn("레이어 · " + layerNames[layer] + (tool == Tool.Auto ? " 자동" : ""), 497, 182, 95, 19, () => SetLayer((layer + 1) % 4)).name = "레이어";
        Btn(hiddenLayers[layer] ? "숨김" : "보임", 596, 182, 29, 19, () =>
        {
            hiddenLayers[layer] = !hiddenLayers[layer];
            preview.SetLayerVisible(layer, !hiddenLayers[layer]);
            Draw(page);
        }, hiddenLayers[layer]).name = "레이어 보이기";

        switch (tool)
        {
            case Tool.Auto: case Tool.Paint: case Tool.Rect: case Tool.Fill: case Tool.Enemy: case Tool.Prop:
                string current = tool == Tool.Enemy ? enemies[enemyIndex].prefab.name :
                    tool == Tool.Prop ? (scatter ? ScatterSummary : props[propIndex].prefab.name) : palette.Length > 0 ? palette[materialIndex].Title : "재료 없음";
                var materialButton = Btn(current, 497, 203, 128, 20, ToggleMaterialList);
                materialButton.interactable = tool is Tool.Enemy or Tool.Prop || palette.Length > 0;
                var piece = tool == Tool.Enemy ? enemies[enemyIndex] : tool == Tool.Prop ? props[propIndex] : null;
                // An empty scatter set shows no picture, only "흩뿌릴 소품 고르기".
                var sample = tool == Tool.Prop && ScatterEmpty ? null :
                    piece != null ? piece.prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite :
                    palette.Length > 0 ? palette[materialIndex].Sample : null;
                ShowMaterial(materialButton, sample, 16, 16, 104, 20);
                if (tool is Tool.Auto or Tool.Paint)
                {
                    Btn("이전", 497, 225, 30, 18, () => Cycle(-1)); Btn("다음", 529, 225, 30, 18, () => Cycle(1));
                    BrushButton(563, 62);
                }
                else if (tool == Tool.Prop) DrawScatterRow();
                else { Btn("이전", 497, 225, 62, 18, () => Cycle(-1)); Btn("다음", 563, 225, 62, 18, () => Cycle(1)); }
                break;
            case Tool.Erase:
                WorkshopUI.Label(content, "끌어서 지우기 · Shift 직선", 497, 203, 129, 20);
                BrushButton(497, 128);
                break;
            case Tool.Stamp:
                if (stamp.Count > 0) DrawStampTools();
                else WorkshopUI.Label(content, "끌어서 복사할 범위 선택", 497, 203, 128, 20);
                Btn("다시 고르기", 497, 225, 62, 18, () => { stamp.Clear(); drawer = false; Draw(page); });
                Btn("보관함", 563, 225, 62, 18, () => { drawer = !drawer; palettePage = 0; Draw(page); });
                break;
            case Tool.Door:
                for (int i = 0; i < compass.Length; i++)
                {
                    int direction = i;
                    Btn(compass[i], 497 + i * 33, 203, 30, 20, () =>
                    {
                        dir = direction;
                        var spot = preview.DoorLocal(directions[direction]);
                        if (spot.HasValue) preview.Focus(spot.Value);
                        Draw(page);
                    }, dir == i).name = "출구 " + compass[i];
                }
                Btn("연결 미리보기", 497, 225, 128, 18, OpenPassage).interactable = preview.HasDoor(directions[dir]);
                break;
            case Tool.Start:
                WorkshopUI.Label(content, "시작할 바닥을 선택", 497, 203, 129, 20);
                Btn("입구부터 플레이", 497, 225, 128, 18, () => { start = null; preview.MarkStart(null); Play(); });
                break;
            case Tool.Sample:
                WorkshopUI.Label(content, "지도에서 타일을 선택", 497, 203, 129, 20);
                break;
            default:
                WorkshopUI.Label(content, "빈 곳을 끌어 묶음 선택", 497, 203, 129, 20);
                Btn("선택한 배치물 수치", 497, 225, 128, 18, SelectedObject);
                break;
        }
        undoButton = Btn("실행 취소", 497, 247, 62, 20, () => History(false));
        redoButton = Btn("다시 실행", 563, 247, 62, 20, () => History(true));
        removeButton = Btn("선택 삭제", 497, 269, 128, 20, Remove);
        detail = WorkshopUI.Label(content, "", 497, 292, 130, 32);

        void Choice(string label, Tool choice, int x, int y) =>
            Btn(label, x, y, 62, 17, () => SetTool(choice), tool == choice).name = label;
        void BrushButton(int x, int w) =>
            Stepper("붓 " + brushSize + "칸", x, 225, w, 18, d => { brushSize = StepWrap(brushSize - 1 + d, MaxBrush) + 1; Draw(page); }).name = "붓 크기";
    }

    private void SetTool(Tool choice)
    {
        var previous = tool;
        tool = choice; drawer = false; encounterOpen = false; notice = ""; shapeStart = null;
        if (tool == Tool.Auto) { stamp.Clear(); SyncPalette(); }
        else if (previous == Tool.Auto) SyncPalette();
        Draw(page);
    }

    private void SetLayer(int next)
    {
        layer = next; stamp.Clear(); drawer = false;
        SyncPalette();
        Draw(page);
    }

    private void RememberTile(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        recentTiles.Remove(id); recentTiles.Insert(0, id);
        if (recentTiles.Count > 8) recentTiles.RemoveAt(8);
    }

    private int RecentRank(string id)
    {
        int index = recentTiles.IndexOf(id);
        return index < 0 ? int.MaxValue : index;
    }

    // ---------- Checks before testing and saving ----------

    internal string RoomWarnings()
    {
        var notes = new List<string>();
        var ground = preview.Ground;
        if (ground.GetUsedTilesCount() == 0) notes.Add("바닥이 없습니다");
        else
            foreach (var exit in preview.ExitPassages())
            {
                if (exit.missing.Length == 0) continue;
                int i = Array.IndexOf(directions, exit.direction);
                notes.Add((i >= 0 ? compass[i] + "쪽 " : "") + "출구 통로 바닥 " + exit.missing.Length + "칸 비어 있음");
            }
        if (start.HasValue && !preview.Walkable(preview.World(start.Value))) notes.Add("시작점이 바닥 밖에 있습니다");
        return notes.Count == 0 ? null : string.Join(" · ", notes.Take(2)) + (notes.Count > 2 ? " 외 " + (notes.Count - 2) + "건" : "");
    }
}
#endif
