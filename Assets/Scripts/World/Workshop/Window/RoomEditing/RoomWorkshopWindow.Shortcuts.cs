#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Keyboard shortcuts: copy and paste, nudging, tool keys, the effects toggle and the help card.
public sealed partial class RoomWorkshopWindow
{
    private void CopySelection()
    {
        clipboard.Clear();
        var root = catalog.FindRoom(Doc.template).room.transform;
        foreach (string key in selection)
        {
            var entry = Doc.pieces.Find(p => p.id == key);
            string id = !string.IsNullOrEmpty(entry?.catalog) ? entry.catalog : null;
#if UNITY_EDITOR
            if (id == null)
            {
                var node = WorkshopCatalog.Resolve(root, key);
                var source = node != null ? UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(node.gameObject) : null;
                id = source != null ? Array.Find(catalog.pieces, p => p.prefab == source)?.id : null;
            }
#endif
            var placed = preview.Pieces.FirstOrDefault(p => p.Key == key).Value;
            if (id != null && placed != null) clipboard.Add((id, preview.Local(placed.position)));
        }
        Say(clipboard.Count > 0 ? clipboard.Count + "개 복사 · Ctrl+V 붙여넣기" : "복사할 수 있는 배치물이 없습니다.");
    }

    private void PasteClipboard()
    {
        if (clipboard.Count == 0) { Say("먼저 Ctrl+C로 배치물을 복사하세요."); return; }
        EndStroke();
        before = Doc.Serialize();
        selection.Clear();
        float step = CellLocal;
        foreach (var (id, local) in clipboard)
        {
            string key = Guid.NewGuid().ToString("N");
            Doc.pieces.Add(new WorkshopRoom.Piece { id = key, catalog = id, local = local + new Vector3(step, -step, 0) });
            selection.Add(key);
        }
        for (int i = 0; i < clipboard.Count; i++) clipboard[i] = (clipboard[i].catalog, clipboard[i].local + new Vector3(step, -step, 0));
        EndStroke(); Rebuild();
    }

    private void Nudge(Vector2 direction, bool fine)
    {
        if (selection.Count == 0) return;
        EndStroke();
        before = Doc.Serialize();
        float step = snapMode == 2 ? CellLocal * .5f : CellLocal;
        var delta = (Vector3)(direction * (fine ? step * .25f : step));
        foreach (var piece in preview.Pieces)
        {
            if (!selection.Contains(piece.Key)) continue;
            var next = preview.Local(piece.Value.position) + delta;
            if (snapMode > 0 && !fine) next = SnapLocal(next);
            next.z = 0;
            piece.Value.position = preview.World(next);
            EditPiece(piece.Key).local = next;
            preview.Land(piece.Key, fine ? .35f : .6f);
        }
        preview.PositionOutlines();
        EndStroke();
    }

    // Returns true when a shortcut consumed the frame.
    private bool HandleShortcuts(Keyboard kb)
    {
        if (kb == null) return false;
        bool ctrl = kb.ctrlKey.isPressed, shift = kb.shiftKey.isPressed;
        if (kb.slashKey.wasPressedThisFrame) { helpOpen = !helpOpen; Draw(page); return true; }
        if (ctrl)
        {
            if (kb.cKey.wasPressedThisFrame) { CopySelection(); return true; }
            if (kb.vKey.wasPressedThisFrame) { Safe(PasteClipboard); return true; }
            if (kb.dKey.wasPressedThisFrame) { CopySelection(); Safe(PasteClipboard); return true; }
            if (kb.gKey.wasPressedThisFrame) { showGrid = !showGrid; preview.ShowGrid(showGrid); Draw(page); return true; }
            return false;
        }
        if (before != null) return false;
        if (tool == Tool.Stamp && stamp.Count > 0)
        {
            if (kb.qKey.wasPressedThisFrame) { TransformStamp(0); return true; }
            if (kb.xKey.wasPressedThisFrame) { TransformStamp(shift ? 2 : 1); return true; }
        }
        if (kb.f6Key.wasPressedThisFrame && shift) { start = null; preview.MarkStart(null); Safe(Play); return true; }
        (Key key, Tool tool)[] tools = { (Key.V, Tool.Select), (Key.B, Tool.Paint), (Key.A, Tool.Auto), (Key.R, Tool.Rect),
            (Key.G, Tool.Fill), (Key.E, Tool.Erase), (Key.I, Tool.Sample), (Key.T, Tool.Stamp) };
        foreach (var (key, choice) in tools)
            if (kb[key].wasPressedThisFrame) { SetTool(choice); return true; }
        Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
        for (int i = 0; i < digits.Length; i++)
            if (kb[digits[i]].wasPressedThisFrame) { SetLayer(i); return true; }
        if (kb.mKey.wasPressedThisFrame) { CycleSnap(); return true; }
        if (kb.jKey.wasPressedThisFrame) { ToggleJuice(); return true; }
        if (kb.hKey.wasPressedThisFrame) { preview.Home(); Draw(page); return true; }
        if (kb.fKey.wasPressedThisFrame) { preview.Overview(); Draw(page); return true; }
        if (kb.leftBracketKey.wasPressedThisFrame) { brushSize = Mathf.Max(1, brushSize - 1); Draw(page); return true; }
        if (kb.rightBracketKey.wasPressedThisFrame) { brushSize = Mathf.Min(MaxBrush, brushSize + 1); Draw(page); return true; }
        if (tool == Tool.Select && selection.Count > 0)
        {
            Vector2 move = kb.leftArrowKey.wasPressedThisFrame ? Vector2.left : kb.rightArrowKey.wasPressedThisFrame ? Vector2.right :
                kb.upArrowKey.wasPressedThisFrame ? Vector2.up : kb.downArrowKey.wasPressedThisFrame ? Vector2.down : Vector2.zero;
            if (move != Vector2.zero) { Safe(() => Nudge(move, shift)); return true; }
        }
        return false;
    }

    private void ToggleJuice()
    {
        WorkshopPreview.Juice = !WorkshopPreview.Juice;
        Draw(page);
        Say(WorkshopPreview.Juice ? "편집 효과 켬  ·  J로 끄기" : "편집 효과 끔  ·  J로 켜기");
    }

    private void DrawHelp()
    {
        Modal(70, 40, 500, 280);
        WorkshopUI.Label(content, "단축키", 86, 48, 200, 24, 15);
        string[] left =
        {
            "V 선택   B 칠하기   A 자동 타일", "R 사각형   G 채우기   E 지우개", "I 스포이드   T 도장 (Q 회전 · X 뒤집기)",
            "1 ~ 4  레이어 (바닥/벽/그림자/통행 불가)", "[ ]  붓 크기 1~5칸", "Shift+끌기  직선 칠하기",
            "휠  확대·축소 (커서 기준)", "우클릭 / 가운데 / 스페이스+끌기  화면 이동", "H 원위치   F 전체 보기   Ctrl+G 격자",
            "M  자석 (끔 → 칸 → 반 칸)   Alt  잠시 끔"
        };
        string[] right =
        {
            "Ctrl+C / Ctrl+V  배치물 복사·붙여넣기", "Ctrl+D  배치물 복제", "방향키  배치물 한 칸 이동 (Shift 미세)",
            "Delete  선택 삭제", "Ctrl+Z / Ctrl+Y  실행 취소·다시 실행", "Ctrl+S  저장",
            "F6  시작점에서 시험   Shift+F6 입구부터", "Esc  닫기 / 목록으로", "?  이 도움말", "J  편집 효과 켜기·끄기",
            "L  재료 줄 접기·펴기   Shift+L  재료 목록"
        };
        for (int i = 0; i < left.Length; i++) WorkshopUI.Label(content, left[i], 86, 78 + i * 22, 236, 20);
        for (int i = 0; i < right.Length; i++) WorkshopUI.Label(content, right[i], 328, 78 + i * 22, 236, 20);
        Btn("닫기", 250, 297, 140, 21, () => { helpOpen = false; Draw(page); }, true).name = "도움말 닫기";
    }
}
#endif
