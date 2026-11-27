#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Decoration scatter brush: rub over the floor to sprinkle a chosen set of props.
// Props land only on walkable floor, outside exit passages, and keep a minimum gap from every placed object.
// One rub is one undo step, because the stroke snapshot is taken when the button goes down.
public sealed partial class RoomWorkshopWindow
{
    private bool scatter;
    // Chosen props in the order they were added, so removing one can fall back to the latest remaining choice.
    private readonly List<int> scatterSet = new();
    // Density and spacing are step indexes into ScatterSteps (0.5, 1, 2, 3, 4, 5); index 2 is the old default "2".
    private int scatterDensity = 2, scatterSpacing = 2;
    private float scatterReady, scatterBudget;
    private readonly System.Random scatterRandom = new();
    private const float ScatterRadiusCells = 2.5f;

    // Steps 1~3 keep their old meaning; 0.5 tries once every other tick.
    private static readonly string[] ScatterSteps = { "0.5", "1", "2", "3", "4", "5" };
    private static readonly float[] SpacingCells = { 1f, 1.5f, 2.5f, 4f, 5.5f, 7f };
    private static readonly float[] DensityTries = { .5f, 1, 2, 4, 6, 9 };
    private float ScatterGapCells => SpacingCells[Mathf.Clamp(scatterSpacing, 0, ScatterSteps.Length - 1)];
    private static int ScatterStep(string value) => Mathf.Max(0, Array.IndexOf(ScatterSteps, value));
    private string ScatterSummary => scatterSet.Count == 0 ? "흩뿌릴 소품 고르기" : "흩뿌릴 소품 " + scatterSet.Count + "종";

    // Only the props chosen for scattering; an empty set scatters nothing.
    private WorkshopCatalog.Piece[] ScatterChoices => scatterSet.Where(i => i >= 0 && i < props.Length).Select(i => props[i]).ToArray();
    private const string ScatterEmptyNote = "흩뿌릴 소품을 먼저 고르세요  ·  '흩뿌릴 소품 고르기' 버튼에서 선택";

    private void ToggleScatter()
    {
        scatter = !scatter;
        if (scatter && scatterSet.Count == 0) scatterSet.Add(propIndex);
        ghostKey = null; Draw(page);
        Say(scatter ? "흩뿌리기  ·  바닥을 문질러 소품 뿌리기 · 재료 버튼으로 여러 소품 선택" : "소품 배치  ·  한 개씩 놓기");
    }

    // Adding a prop shows it on the material button; removing the shown one switches to the latest remaining choice.
    private void ToggleScatterMember(int index)
    {
        if (scatterSet.Remove(index)) { if (propIndex == index && scatterSet.Count > 0) propIndex = scatterSet[^1]; }
        else { scatterSet.Add(index); propIndex = index; }
        ghostKey = null;
    }
    private bool ScatterEmpty => scatter && scatterSet.Count == 0;

    private void ScatterPointer(Vector3 world, bool first)
    {
        if (props.Length == 0) return;
        var choices = ScatterChoices;
        if (choices.Length == 0) { if (first) Say(ScatterEmptyNote); return; }
        if (!first && Time.unscaledTime < scatterReady) return;
        scatterReady = Time.unscaledTime + .05f;
        float cell = CellLocal;
        var center = preview.Local(world);
        var blocked = new HashSet<Vector3Int>(preview.ExitPassages().SelectMany(e => e.cells));
        var placed = preview.Pieces.Where(p => p.Value != null && p.Value.gameObject.activeInHierarchy)
            .Select(p => preview.Local(p.Value.position)).ToList();
        if (first) scatterBudget = 0;
        scatterBudget += DensityTries[Mathf.Clamp(scatterDensity, 0, ScatterSteps.Length - 1)] * (first ? 2 : 1);
        int tries = (int)scatterBudget;
        scatterBudget -= tries;
        float gap = ScatterGapCells * cell;
        for (int i = 0; i < tries; i++)
        {
            double angle = scatterRandom.NextDouble() * Math.PI * 2;
            float distance = Mathf.Sqrt((float)scatterRandom.NextDouble()) * ScatterRadiusCells * cell;
            var local = SnapLocal(center + new Vector3(Mathf.Cos((float)angle), Mathf.Sin((float)angle)) * distance);
            var point = preview.World(local);
            if (!preview.Walkable(point) || blocked.Contains(preview.Ground.WorldToCell(point))) continue;
            if (placed.Any(p => ((Vector2)(p - local)).sqrMagnitude < gap * gap)) continue;
            var entry = choices[scatterRandom.Next(choices.Length)];
            string id = Guid.NewGuid().ToString("N");
            Doc.pieces.Add(new WorkshopRoom.Piece { id = id, catalog = entry.id, local = local });
            preview.AddPiece(id, entry, local);
            preview.Bounce(id);
            placed.Add(local);
        }
    }

    private void ScatterCursor(Vector3 world)
    {
        preview.HideGhost();
        var ground = preview.Ground;
        var cell = ground.WorldToCell(world);
        int r = Mathf.CeilToInt(ScatterRadiusCells) - 1;
        // Red when nothing is chosen, so the empty brush is visible before rubbing.
        var color = scatterSet.Count == 0 ? new Color(.96f, .38f, .33f, .22f) : new Color(.57f, .92f, .79f, .18f);
        preview.HoverCells(ground, cell - new Vector3Int(r, r, 0), cell + new Vector3Int(r, r, 0), color);
    }

    private void DrawScatterRow()
    {
        if (!scatter)
        {
            Btn("이전", 497, 225, 30, 18, () => Cycle(-1)); Btn("다음", 529, 225, 30, 18, () => Cycle(1));
            Btn("흩뿌리기", 563, 225, 62, 18, ToggleScatter).name = "흩뿌리기";
            return;
        }
        // "밀도 0.5" needs the room, so while scattering the on/off button reads "끄기"; the material button already says 흩뿌릴 소품.
        Stepper("밀도 " + ScatterSteps[scatterDensity], 497, 225, 48, 18,
            d => { scatterDensity = StepWrap(scatterDensity + d, ScatterSteps.Length); Draw(page); }).name = "흩뿌리기 밀도";
        Stepper("간격 " + ScatterSteps[scatterSpacing], 547, 225, 48, 18,
            d => { scatterSpacing = StepWrap(scatterSpacing + d, ScatterSteps.Length); Draw(page); }).name = "흩뿌리기 간격";
        Btn("끄기", 597, 225, 28, 18, ToggleScatter, true).name = "흩뿌리기";
    }
}
#endif
