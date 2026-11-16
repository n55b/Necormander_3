using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기획자용 장비 편집기 (Tools > 기획 > 장비 편집기).
///
///  · 하나씩 편집 — 왼쪽 목록에서 장비를 고르고 오른쪽에서 표시/패시브/강화/기본 공격을 한글 화면으로 고친다.
///  · 표로 보기   — 모든 장비의 이름·등급·가격·강화·등록 상태·패시브 요약을 한 화면에서 본다.
///  · 새 장비 / 복제 / 삭제 / 에셋 이름 바꾸기 — 게임 목록(GrowthRegistry)·상점 후보(ShopRegistry) 등록까지 함께 처리.
///  · 확인 필요만 — 이름·아이콘 누락, 패시브 없음, 목록 미등록 등 문제가 있는 장비만 본다.
///
/// 모든 수정은 Undo(Ctrl+Z) 가 된다. 세이브는 장비를 '에셋 파일 이름'으로 찾으므로(EquipmentInventoryManager.LoadFromData)
/// 이름 중복을 막고, 이름을 바꿀 때는 경고한다.
/// </summary>
[Serializable]
public class EquipmentEditorPanel
{
    private enum View { Detail, Table }

    [SerializeField] private View view = View.Detail;
    [SerializeField] private string search = "";
    [SerializeField] private bool onlyWarnings;
    [SerializeField] private EquipmentSO selected;
    [SerializeField] private int previewLevel;
    [SerializeField] private bool showWeakness;
    [SerializeField] private bool showHand;

    private List<EquipmentSO> all = new List<EquipmentSO>();
    private Dictionary<EquipmentSO, SerializedObject> serialized = new Dictionary<EquipmentSO, SerializedObject>();
    private Vector2 listScroll, detailScroll, tableScroll;
    private string renameText;
    private EquipmentSO renameTarget;
    private string lastContext;
    private int version;

    // ── 창 연결 ──────────────────────────────────────────────────────
    // 이 화면은 '장비 편집기' 창(EquipmentEditorWindow) 안에서 그려진다.
    [NonSerialized] private EditorWindow host;
    [NonSerialized] private bool enabled;

    public static EquipmentEditorPanel Open(EquipmentSO select) => EquipmentEditorWindow.Open(select);

    public void Enable(EditorWindow owner)
    {
        host = owner;
        if (enabled) return;
        enabled = true;
        if (all == null) all = new List<EquipmentSO>();
        if (serialized == null) serialized = new Dictionary<EquipmentSO, SerializedObject>();
        Reload();
        EditorApplication.projectChanged += OnProjectChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    public void Disable()
    {
        if (!enabled) return;
        enabled = false;
        EditorApplication.projectChanged -= OnProjectChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        ClearSerialized();
    }

    /// <summary>특정 장비를 골라 '하나씩 편집' 화면으로 연다.</summary>
    public void Select(EquipmentSO e)
    {
        Reload();
        if (e != null) { selected = e; view = View.Detail; }
    }

    /// <summary>새 장비 창을 띄운다.</summary>
    public void ShowNewPopup() => NewEquipmentPopup.Open(null, OnCreated);

    public int Count => all?.Count ?? 0;

    private void Repaint() { if (host != null) host.Repaint(); }
    private void Focus() { if (host != null) host.Focus(); }

    private void OnProjectChanged() { Reload(); Repaint(); }
    private void OnUndoRedo() { version++; Repaint(); }

    private void ClearSerialized()
    {
        foreach (var so in serialized.Values) so?.Dispose();
        serialized.Clear();
    }

    private void Reload()
    {
        ClearSerialized();
        all = EquipmentAssets.All()
            .OrderBy(e => e.upgradeOnly ? 1 : 0)
            .ThenBy(e => Convert.ToInt32(e.rarity))
            .ThenBy(e => EquipmentAssets.DisplayName(e), StringComparer.Ordinal)
            .ToList();
        if (selected != null && !all.Contains(selected)) selected = null;
        version++;
    }

    private SerializedObject SO(EquipmentSO e)
    {
        if (!serialized.TryGetValue(e, out var so) || so == null || so.targetObject == null)
        {
            so = new SerializedObject(e);
            serialized[e] = so;
        }
        return so;
    }

    /// <summary>화면 구성이 바뀌는 동작은 다음 프레임으로 미룬다(IMGUI 레이아웃 불일치 방지).</summary>
    private void Defer(Action action)
    {
        EditorApplication.delayCall += () => { if (host == null) return; action(); Repaint(); };
    }

    private static void EndTextEditing()
    {
        GUIUtility.keyboardControl = 0;
        EditorGUIUtility.editingTextField = false;
    }

    // ── 필터 ──────────────────────────────────────────────────────────
    private List<EquipmentSO> Filtered()
    {
        var g = EquipmentAssets.Growth;
        var s = EquipmentAssets.Shop;
        return all.Where(e => e != null
                              && (!onlyWarnings || EquipmentAssets.Warnings(e, g, s).Count > 0)
                              && Matches(e)).ToList();
    }

    private bool Matches(EquipmentSO e)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        string q = search.Trim();
        return Has(e.name, q) || Has(e.equipmentName, q) || Has(e.description, q)
               || (e.passives != null && e.passives.Any(p => p != null && (Has(p.label, q) || Has(EquipmentAssets.PassiveSummary(p, 0), q))));
    }

    private static bool Has(string text, string q) => !string.IsNullOrEmpty(text) && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

    // ── 그리기 ────────────────────────────────────────────────────────
    public void OnGUI()
    {
        if (all.Any(e => e == null)) Reload();

        // 보여주는 대상이 바뀐 첫 프레임에 입력 중이던 글 칸을 닫는다(다른 장비로 글이 옮겨 붙는 것 방지).
        string context = $"{view}|{(selected != null ? selected.GetInstanceID() : 0)}|{onlyWarnings}|{version}";
        if (context != lastContext) { lastContext = context; EndTextEditing(); }

        DrawToolbar();
        var list = Filtered();
        var g = EquipmentAssets.Growth;
        var s = EquipmentAssets.Shop;
        int warn = all.Count(e => EquipmentAssets.Warnings(e, g, s).Count > 0);
        EditorGUILayout.LabelField(
            $"장비 {all.Count}개 · 표시 중 {list.Count}개 · 확인 필요 {warn}개 · 모든 수정은 Ctrl+Z 로 되돌릴 수 있습니다"
            + (Application.isPlaying ? " · 플레이 중 수정도 에셋에 그대로 남습니다" : ""), EditorStyles.miniLabel);

        if (g == null) EditorGUILayout.HelpBox("GrowthRegistrySO 에셋을 찾지 못했습니다. 장비 목록 등록 기능이 꺼집니다.", MessageType.Warning);

        if (all.Count == 0)
        {
            EditorGUILayout.HelpBox("EquipmentSO 에셋이 없습니다. 위의 '새 장비'로 만드세요.", MessageType.Info);
            return;
        }
        if (view == View.Table) DrawTable(list); else DrawDetail(list);
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var nv = (View)GUILayout.Toolbar((int)view, new[] { "하나씩 편집", "표로 보기" }, EditorStyles.toolbarButton, GUILayout.Width(170));
            if (nv != view) { var v = nv; Defer(() => view = v); }
            GUILayout.Space(6);
            search = GUILayout.TextField(search ?? "", EditorStyles.toolbarSearchField, GUILayout.MinWidth(120), GUILayout.MaxWidth(260));
            onlyWarnings = GUILayout.Toggle(onlyWarnings, new GUIContent("확인 필요만", "이름·설명·아이콘 누락, 패시브 없음, 목록 미등록 등 확인이 필요한 장비만 본다."), EditorStyles.toolbarButton, GUILayout.Width(78));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("새 장비", "이름·등급·첫 패시브·목록 등록을 정해 새 장비를 만든다. 기존 장비 복사로 시작할 수도 있다."), EditorStyles.toolbarButton))
                Defer(() => NewEquipmentPopup.Open(null, OnCreated));
            if (GUILayout.Button(new GUIContent("목록 정리", "프로젝트의 모든 장비를 게임 목록/상점 후보와 맞춘다(무엇이 바뀌는지 먼저 보여준다)."), EditorStyles.toolbarButton))
                Defer(SyncRegistries);
            if (GUILayout.Button(new GUIContent("↻", "목록 새로고침"), EditorStyles.toolbarButton, GUILayout.Width(24))) Defer(Reload);
        }
    }

    // ── 하나씩 편집 ───────────────────────────────────────────────────
    private void DrawDetail(List<EquipmentSO> list)
    {
        if (selected == null && list.Count > 0) selected = list[0];

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
            {
                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                bool upgradeHeader = false;
                foreach (var e in list)
                {
                    if (e.upgradeOnly && !upgradeHeader)
                    {
                        upgradeHeader = true;
                        GUILayout.Label("강화 전용 장비", EditorStyles.miniBoldLabel);
                    }
                    DrawListRow(e);
                }
                EditorGUILayout.EndScrollView();
            }

            var div = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(div, new Color(0.5f, 0.5f, 0.5f, 0.4f));

            using (new EditorGUILayout.VerticalScope())
            {
                if (selected == null) { EditorGUILayout.HelpBox("왼쪽에서 장비를 고르세요.", MessageType.Info); return; }
                DrawDetailHeader(selected);
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                DrawEquipment(selected);
                EditorGUILayout.Space(12);
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawListRow(EquipmentSO e)
    {
        var r = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
        if (e == selected) EditorGUI.DrawRect(r, new Color(0.24f, 0.48f, 0.90f, 0.35f));
        EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 4, 4, r.height - 8), EquipmentAssets.RarityColor(e));
        DrawSprite(new Rect(r.x + 10, r.y + 3, 20, 20), e.icon);
        GUI.Label(new Rect(r.x + 34, r.y + 4, r.width - 58, 18), new GUIContent(EquipmentAssets.DisplayName(e), e.name));
        if (EquipmentAssets.Warnings(e, EquipmentAssets.Growth, EquipmentAssets.Shop).Count > 0)
        {
            var icon = EditorGUIUtility.IconContent("console.warnicon.sml");
            icon.tooltip = "확인 필요";
            GUI.Label(new Rect(r.xMax - 22, r.y + 4, 20, 18), icon);
        }
        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            var target = e;
            Defer(() => selected = target);
            Event.current.Use();
        }
    }

    private void DrawDetailHeader(EquipmentSO e)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(EquipmentAssets.DisplayName(e), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("프로젝트에서 찾기", "Project 창에서 에셋을 가리킨다"), EditorStyles.miniButtonLeft))
                EditorGUIUtility.PingObject(e);
            if (GUILayout.Button(new GUIContent("인스펙터", "원본 인스펙터에서 연다(고급)"), EditorStyles.miniButtonMid))
                Selection.activeObject = e;
            if (GUILayout.Button(new GUIContent("복제", "이 장비를 복사해 새 장비를 만든다"), EditorStyles.miniButtonMid))
            { var t = e; Defer(() => NewEquipmentPopup.Open(t, OnCreated)); }
            if (GUILayout.Button(new GUIContent("삭제", "참조하는 곳을 확인한 뒤 목록에서 빼고 OS 휴지통으로 옮긴다"), EditorStyles.miniButtonRight))
            { var t = e; Defer(() => { if (EquipmentAssets.Delete(t)) { selected = null; renameTarget = null; Reload(); } }); }
        }

        if (renameTarget != e) { renameTarget = e; renameText = e.name; }
        using (new EditorGUILayout.HorizontalScope())
        {
            renameText = EditorGUILayout.TextField(new GUIContent("에셋 파일 이름",
                "세이브 데이터가 이 이름으로 장비를 찾는다. 이미 세이브에 들어간 장비는 바꾸지 마세요."), renameText);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(renameText) || renameText == e.name))
                if (GUILayout.Button("바꾸기", EditorStyles.miniButton, GUILayout.Width(50)))
                { var t = e; string to = renameText.Trim(); Defer(() => Rename(t, to)); }
        }

        foreach (var w in EquipmentAssets.Warnings(e, EquipmentAssets.Growth, EquipmentAssets.Shop))
            EditorGUILayout.HelpBox(w, MessageType.Warning);
    }

    private void DrawEquipment(EquipmentSO e)
    {
        var so = SO(e);
        so.Update();

        // 표시
        Header("표시");
        Prop(so, "equipmentName", "게임 표시 이름", "장비 이름. 비우면 에셋 파일 이름이 보인다.");
        var desc = so.FindProperty("description");
        EditorGUILayout.LabelField(new GUIContent("설명", "툴팁·상점·보상 카드에 표시"));
        EditorGUI.BeginChangeCheck();
        string dv = EditorGUILayout.TextArea(desc.stringValue ?? "", WrapArea, GUILayout.MinHeight(48));
        if (EditorGUI.EndChangeCheck()) desc.stringValue = dv;
        using (new EditorGUILayout.HorizontalScope())
        {
            var icon = so.FindProperty("icon");
            EditorGUILayout.PropertyField(icon, new GUIContent("아이콘"));
            var r = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
            DrawSprite(r, icon.objectReferenceValue as Sprite);
        }
        Prop(so, "rarity", "등급", "장비 등급(표시 색).");
        Prop(so, "shopCost", "상점 가격(골드)", "상점 판매가.");

        // 등록
        Header("게임 등록");
        DrawRegistry(e);

        // 패시브
        Header("패시브 (공격 관련 · 유틸은 주머니 아이템 담당)");
        DrawPassives(so, e);

        // 강화
        Header("강화");
        Prop(so, "maxEnhanceLevel", "최대 강화 레벨", "강화 상한 레벨(+N).");
        Prop(so, "upgradeOnly", "강화 전용 장비", "켜면 무작위 보상·상점 진열에 나오지 않는다. 다른 장비의 '강화 시 선택지'로만 얻는다.");
        Prop(so, "upgradeStage", "강화 단계", "강화 전용 장비가 몇 단계 장비인지(불러올 때 강화 레벨로 쓰인다).");
        EditorGUILayout.PropertyField(so.FindProperty("upgradeOptions"),
            new GUIContent("강화 시 선택지 (다음 장비)", "비어 있지 않으면 +N 수치 강화 대신 이 장비들 중 하나를 고른다. 비어 있으면 최종 단계."), true);

        // 기본 공격
        Header("기본 공격");
        var combo = so.FindProperty("comboMultipliers");
        var cv = combo.vector3Value;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(new GUIContent("콤보 배율 (1·2·3타)", "평타 1·2·3타의 데미지 배율."));
            float lw = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 28;
            EditorGUI.BeginChangeCheck();
            cv.x = EditorGUILayout.FloatField("1타", cv.x);
            cv.y = EditorGUILayout.FloatField("2타", cv.y);
            cv.z = EditorGUILayout.FloatField("3타", cv.z);
            if (EditorGUI.EndChangeCheck()) combo.vector3Value = cv;
            EditorGUIUtility.labelWidth = lw;
        }
        Prop(so, "basicHitsPerContact", "접촉당 타격 횟수", "평타가 한 번 닿을 때 몇 번 때리는지.");
        Prop(so, "basicRepeatHitInterval", "추가 타격 간격(초)", "0 이면 여러 타격이 동시에 들어간다.");
        Prop(so, "basicDamagePerHit", "타격당 데미지 배율", "타격 한 번의 데미지 배율.");
        Prop(so, "minionDashDamageMultiplier", "소환수 돌진 데미지 배율", "소환수 돌진 공격의 데미지 배율.");

        // 세렌디피티
        Header("세렌디피티");
        var ser = so.FindProperty("serendipity");
        EditorGUILayout.PropertyField(ser, new GUIContent("세렌디피티 사용", "켜면 확률로 쿨타임 초기화 + 이동 속도 버프."));
        using (new EditorGUI.DisabledScope(!ser.boolValue))
        using (new EditorGUI.IndentLevelScope())
        {
            Prop(so, "resetChance", "초기화 확률", "0.13 = 13%. 행운 수치만큼 더해진다.");
            Prop(so, "guaranteedProcSeconds", "확정 발동 시간(초)", "이 시간 동안 안 터지면 확정으로 발동.");
            Prop(so, "moveBuffSeconds", "이동 버프 시간(초)", "");
            Prop(so, "moveBuffPercent", "이동 버프 수치", "0.03 = +3%.");
        }

        // 손 애니메이션
        EditorGUILayout.Space(6);
        showHand = EditorGUILayout.Foldout(showHand, "손 애니메이션 (아트 담당)", true);
        if (showHand)
            using (new EditorGUI.IndentLevelScope())
                Prop(so, "handAnimationOverride", "손 애니메이션 오버라이드", "선택 사항. 비어 있으면 기본 외형을 쓴다.");

        // 약점 공략(비활성)
        showWeakness = EditorGUILayout.Foldout(showWeakness, "약점 공략 (현재 비활성 · 데이터 보존용)", true);
        if (showWeakness)
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox("현재 게임에서 효과가 없습니다. 취약은 용사 계열 소환수 스킬만 부여합니다.", MessageType.None);
                Prop(so, "exploitsWeakness", "약점 공략", "");
                Prop(so, "weaknessDuration", "취약 지속(초)", "");
            }

        so.ApplyModifiedProperties();

        // 미리보기
        Header("강화 미리보기");
        int max = Mathf.Max(0, e.maxEnhanceLevel);
        previewLevel = EditorGUILayout.IntSlider(new GUIContent("강화 레벨", "이 레벨에서 패시브 수치가 얼마인지 본다."), Mathf.Clamp(previewLevel, 0, max), 0, max);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (e.passives == null || e.passives.Count == 0) EditorGUILayout.LabelField("패시브 없음", EditorStyles.miniLabel);
            else foreach (var p in e.passives) EditorGUILayout.LabelField("· " + EquipmentAssets.PassiveSummary(p, previewLevel), WrapLabel);
            var c = e.comboMultipliers;
            EditorGUILayout.LabelField($"· 평타 배율 {c.x:0.##} / {c.y:0.##} / {c.z:0.##}, 접촉당 {e.basicHitsPerContact}회", WrapLabel);
            if (e.upgradeOptions != null && e.upgradeOptions.Length > 0)
                EditorGUILayout.LabelField("· 강화 시 선택: " + string.Join(", ", e.upgradeOptions.Where(x => x != null).Select(EquipmentAssets.DisplayName)), WrapLabel);
        }
    }

    private void DrawRegistry(EquipmentSO e)
    {
        var g = EquipmentAssets.Growth;
        var s = EquipmentAssets.Shop;
        using (new EditorGUI.DisabledScope(g == null))
        {
            bool inG = g != null && g.equipments.Contains(e);
            bool nG = EditorGUILayout.ToggleLeft(new GUIContent("게임 장비 목록에 등록" + (g != null ? $" ({g.name})" : ""),
                "세이브 불러오기와 보상에 쓰는 전체 목록. 빠지면 세이브에서 이 장비를 찾지 못한다."), inG);
            if (nG != inG) EquipmentAssets.SetIn(g, g.equipments, e, nG);
        }
        using (new EditorGUI.DisabledScope(s == null))
        {
            bool inS = s != null && s.equipmentPool.Contains(e);
            bool nS = EditorGUILayout.ToggleLeft(new GUIContent("상점 진열 후보에 등록" + (s != null ? $" ({s.name})" : ""),
                "상점에 나올 수 있는 장비 목록. 강화 전용 장비는 넣지 않는다."), inS);
            if (nS != inS) EquipmentAssets.SetIn(s, s.equipmentPool, e, nS);
        }
        if (g != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool isStart = g.startingEquipment == e;
                EditorGUILayout.LabelField(isStart ? "★ 새 게임 시작 장비입니다" : "새 게임 시작 장비: " + (g.startingEquipment != null ? EquipmentAssets.DisplayName(g.startingEquipment) : "없음"), EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(isStart))
                    if (GUILayout.Button("시작 장비로 지정", EditorStyles.miniButton, GUILayout.Width(110)))
                    {
                        var t = e;
                        Defer(() =>
                        {
                            if (!EditorUtility.DisplayDialog("시작 장비 변경", $"새 게임 시작 장비를 '{EquipmentAssets.DisplayName(t)}' 로 바꿉니다.", "바꾸기", "취소")) return;
                            Undo.RecordObject(g, "시작 장비 변경");
                            g.startingEquipment = t;
                            if (!g.equipments.Contains(t)) g.equipments.Add(t);
                            EditorUtility.SetDirty(g);
                            AssetDatabase.SaveAssetIfDirty(g);
                        });
                    }
            }
        }
    }

    // ── 패시브 ────────────────────────────────────────────────────────
    private void DrawPassives(SerializedObject so, EquipmentSO e)
    {
        var arr = so.FindProperty("passives");
        if (arr.arraySize == 0)
            EditorGUILayout.HelpBox("패시브가 없습니다. 장비는 패시브가 1개 이상 있어야 합니다. 아래 '+ 패시브 추가'를 누르세요.", MessageType.Info);

        for (int i = 0; i < arr.arraySize; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            var obj = el.managedReferenceValue;
            Type t = obj?.GetType();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var info = EquipmentAssets.PassiveInfo(t);
                    GUILayout.Label(new GUIContent($"{i + 1}. {info.name}", info.help), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    int idx = i;
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button(new GUIContent("▲", "위로"), EditorStyles.miniButtonLeft, GUILayout.Width(24))) Defer(() => EditPassives(e, a => a.MoveArrayElement(idx, idx - 1), "패시브 이동"));
                    using (new EditorGUI.DisabledScope(i == arr.arraySize - 1))
                        if (GUILayout.Button(new GUIContent("▼", "아래로"), EditorStyles.miniButtonMid, GUILayout.Width(24))) Defer(() => EditPassives(e, a => a.MoveArrayElement(idx, idx + 1), "패시브 이동"));
                    using (new EditorGUI.DisabledScope(obj == null))
                        if (GUILayout.Button(new GUIContent("복사", "이 패시브를 복사해 아래에 붙인다"), EditorStyles.miniButtonMid, GUILayout.Width(36))) Defer(() => DuplicatePassive(e, idx));
                    if (GUILayout.Button(new GUIContent("✕", "이 패시브 삭제"), EditorStyles.miniButtonRight, GUILayout.Width(24))) Defer(() => EditPassives(e, a => a.DeleteArrayElementAtIndex(idx), "패시브 삭제"));
                }
                if (obj == null)
                {
                    EditorGUILayout.HelpBox("비어 있거나 코드에서 사라진 종류입니다. 삭제하세요.", MessageType.Warning);
                    continue;
                }
                var help = EquipmentAssets.PassiveInfo(t).help;
                if (!string.IsNullOrEmpty(help)) EditorGUILayout.LabelField(help, WrapMini);
                DrawChildren(el);
                EditorGUILayout.LabelField("→ 0강: " + EquipmentAssets.PassiveSummary(obj as EquipmentPassive, 0)
                    + (e.maxEnhanceLevel > 0 ? $"   /   {e.maxEnhanceLevel}강: " + EquipmentAssets.PassiveSummary(obj as EquipmentPassive, e.maxEnhanceLevel) : ""), WrapMini);
            }
        }

        if (GUILayout.Button("+ 패시브 추가", GUILayout.Height(22)))
        {
            var menu = new GenericMenu();
            foreach (var pt in EquipmentAssets.PassiveTypes)
            {
                var info = EquipmentAssets.PassiveInfo(pt);
                var type = pt;
                menu.AddItem(new GUIContent($"{info.name}  —  {info.help}"), false, () => AddPassive(e, type));
            }
            menu.ShowAsContext();
        }
    }

    private void DrawChildren(SerializedProperty prop)
    {
        var it = prop.Copy();
        var end = it.GetEndProperty();
        bool enter = true;
        while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
        {
            enter = false;
            DrawField(it);
        }
    }

    private void DrawField(SerializedProperty p)
    {
        if (p.propertyType == SerializedPropertyType.Generic && !p.isArray && p.type == nameof(StatEffect))
        {
            DrawStatEffect(p, EquipmentAssets.Label(p.name, p.displayName));
            return;
        }
        if (p.isArray && p.propertyType == SerializedPropertyType.Generic && p.arrayElementType == nameof(StatEffect))
        {
            DrawStatEffectList(p);
            return;
        }
        EditorGUILayout.PropertyField(p, EquipmentAssets.Label(p.name, p.displayName), true);
    }

    private static void DrawStatEffect(SerializedProperty p, GUIContent title)
    {
        using (new EditorGUILayout.VerticalScope())
        {
            if (title != null && !string.IsNullOrEmpty(title.text)) EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(p.FindPropertyRelative("statType"), new GUIContent("스탯", "올릴 스탯 종류"));
                var pct = p.FindPropertyRelative("isPercent");
                EditorGUILayout.PropertyField(pct, new GUIContent("비율(%)로 적용", "켜면 비율(0.1 = +10%), 끄면 고정치"));
                EditorGUILayout.PropertyField(p.FindPropertyRelative("value"), new GUIContent("수치", pct.boolValue ? "0.1 = +10%" : "고정치"));
                EditorGUILayout.PropertyField(p.FindPropertyRelative("valuePerLevel"), new GUIContent("강화당 증가", "강화 레벨당 더해지는 값(수치와 같은 단위). 0 이면 강화와 무관."));
            }
        }
    }

    private void DrawStatEffectList(SerializedProperty list)
    {
        EditorGUILayout.LabelField(EquipmentAssets.Label(list.name, list.displayName), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                        DrawStatEffect(list.GetArrayElementAtIndex(i), new GUIContent($"스탯 {i + 1}"));
                    if (GUILayout.Button(new GUIContent("✕", "이 스탯 삭제"), EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        var target = selected; string path = list.propertyPath; int idx = i;
                        Defer(() => EditProperty(target, path, a => a.DeleteArrayElementAtIndex(idx), "버프 스탯 삭제"));
                    }
                }
            }
            if (GUILayout.Button("+ 스탯 추가", EditorStyles.miniButton, GUILayout.Width(90)))
            {
                var target = selected; string path = list.propertyPath;
                Defer(() => EditProperty(target, path, a =>
                {
                    a.InsertArrayElementAtIndex(a.arraySize);
                    var n = a.GetArrayElementAtIndex(a.arraySize - 1);
                    n.FindPropertyRelative("value").floatValue = 0.1f;
                    n.FindPropertyRelative("isPercent").boolValue = true;
                    n.FindPropertyRelative("valuePerLevel").floatValue = 0f;
                }, "버프 스탯 추가"));
            }
        }
    }

    private void EditPassives(EquipmentSO e, Action<SerializedProperty> edit, string undoName)
        => EditProperty(e, "passives", edit, undoName);

    private void EditProperty(EquipmentSO e, string path, Action<SerializedProperty> edit, string undoName)
    {
        if (e == null) return;
        var so = SO(e);
        so.Update();
        var p = so.FindProperty(path);
        if (p == null) return;
        edit(p);
        so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName(undoName);
        EndTextEditing();
    }

    private void AddPassive(EquipmentSO e, Type t)
    {
        EditPassives(e, a =>
        {
            int i = a.arraySize;
            a.InsertArrayElementAtIndex(i);
            var inst = (EquipmentPassive)Activator.CreateInstance(t);
            inst.label = EquipmentAssets.PassiveInfo(t).name;
            if (inst is EquipmentConditionBuffPassive cb && cb.effects.Count == 0) cb.effects.Add(new StatEffect());
            a.GetArrayElementAtIndex(i).managedReferenceValue = inst;
        }, "패시브 추가");
    }

    private void DuplicatePassive(EquipmentSO e, int index)
    {
        EditPassives(e, a =>
        {
            var src = a.GetArrayElementAtIndex(index).managedReferenceValue;
            if (src == null) return;
            var clone = JsonUtility.FromJson(JsonUtility.ToJson(src), src.GetType());
            a.InsertArrayElementAtIndex(index + 1);
            a.GetArrayElementAtIndex(index + 1).managedReferenceValue = clone;
        }, "패시브 복사");
    }

    // ── 표 보기 ───────────────────────────────────────────────────────
    private static readonly (string title, float width, string tip)[] Columns =
    {
        ("", 52, ""),
        ("이름 / 에셋", 150, "게임 표시 이름 / 에셋 파일 이름(세이브 키)"),
        ("등급", 90, ""),
        ("가격", 60, "상점 판매가(골드)"),
        ("최대 강화", 60, ""),
        ("강화 전용", 56, "켜면 보상·상점에 안 나옴"),
        ("목록 · 상점", 70, "게임 장비 목록 / 상점 진열 후보 등록"),
        ("패시브 (0강 기준)", 320, ""),
        ("확인", 220, "확인이 필요한 항목"),
    };

    private void DrawTable(List<EquipmentSO> list)
    {
        var g = EquipmentAssets.Growth;
        var s = EquipmentAssets.Shop;
        tableScroll = EditorGUILayout.BeginScrollView(tableScroll);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(4);
            foreach (var c in Columns) GUILayout.Label(new GUIContent(c.title, c.tip), EditorStyles.miniBoldLabel, GUILayout.Width(c.width));
        }

        foreach (var e in list)
        {
            var so = SO(e);
            so.Update();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[0].width)))
                {
                    var r = GUILayoutUtility.GetRect(44, 40, GUILayout.Width(44), GUILayout.Height(40));
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), EquipmentAssets.RarityColor(e));
                    DrawSprite(new Rect(r.x + 6, r.y + 2, 36, 36), e.icon);
                    if (GUILayout.Button(new GUIContent("편집", "하나씩 편집 화면에서 연다"), EditorStyles.miniButton, GUILayout.Width(44)))
                    { var t = e; Defer(() => { selected = t; view = View.Detail; }); }
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[1].width)))
                {
                    EditorGUILayout.PropertyField(so.FindProperty("equipmentName"), GUIContent.none, GUILayout.Width(Columns[1].width));
                    EditorGUILayout.LabelField(new GUIContent(e.name, "에셋 파일 이름(세이브 키)"), EditorStyles.miniLabel, GUILayout.Width(Columns[1].width));
                }
                EditorGUILayout.PropertyField(so.FindProperty("rarity"), GUIContent.none, GUILayout.Width(Columns[2].width));
                EditorGUILayout.PropertyField(so.FindProperty("shopCost"), GUIContent.none, GUILayout.Width(Columns[3].width));
                EditorGUILayout.PropertyField(so.FindProperty("maxEnhanceLevel"), GUIContent.none, GUILayout.Width(Columns[4].width));
                using (new EditorGUILayout.HorizontalScope(GUILayout.Width(Columns[5].width)))
                {
                    GUILayout.Space(16);
                    EditorGUILayout.PropertyField(so.FindProperty("upgradeOnly"), GUIContent.none, GUILayout.Width(20));
                }
                so.ApplyModifiedProperties();

                using (new EditorGUILayout.HorizontalScope(GUILayout.Width(Columns[6].width)))
                {
                    using (new EditorGUI.DisabledScope(g == null))
                    {
                        bool a = g != null && g.equipments.Contains(e);
                        bool na = GUILayout.Toggle(a, new GUIContent("", "게임 장비 목록"), GUILayout.Width(18));
                        if (na != a) EquipmentAssets.SetIn(g, g.equipments, e, na);
                    }
                    using (new EditorGUI.DisabledScope(s == null))
                    {
                        bool b = s != null && s.equipmentPool.Contains(e);
                        bool nb = GUILayout.Toggle(b, new GUIContent("", "상점 진열 후보"), GUILayout.Width(18));
                        if (nb != b) EquipmentAssets.SetIn(s, s.equipmentPool, e, nb);
                    }
                }

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[7].width)))
                {
                    if (e.passives == null || e.passives.Count == 0) EditorGUILayout.LabelField("(없음)", EditorStyles.miniLabel, GUILayout.Width(Columns[7].width));
                    else foreach (var p in e.passives)
                            EditorGUILayout.LabelField("· " + EquipmentAssets.PassiveSummary(p, 0), WrapMini, GUILayout.Width(Columns[7].width));
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[8].width)))
                {
                    var ws = EquipmentAssets.Warnings(e, g, s);
                    if (ws.Count == 0) EditorGUILayout.LabelField("✓", EditorStyles.miniLabel, GUILayout.Width(Columns[8].width));
                    foreach (var w in ws) EditorGUILayout.LabelField(new GUIContent("⚠ " + w, w), WrapMini, GUILayout.Width(Columns[8].width));
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    // ── 에셋 작업 ─────────────────────────────────────────────────────
    public void OnCreated(EquipmentSO e)
    {
        Reload();
        search = ""; onlyWarnings = false;
        selected = e; view = View.Detail;
        EditorGUIUtility.PingObject(e);
        Focus();
        Repaint();
    }

    private void Rename(EquipmentSO e, string newName)
    {
        string err = EquipmentAssets.ValidateName(newName, e);
        if (err != null) { EditorUtility.DisplayDialog("이름 변경 실패", err, "확인"); return; }
        if (!EditorUtility.DisplayDialog("에셋 이름 바꾸기",
                $"'{e.name}' → '{newName}'\n\n세이브 데이터가 에셋 이름으로 장비를 찾습니다. 기존 세이브에 착용 중인 장비면 불러올 때 사라집니다.\n" +
                "코드나 검증 스크립트에 이름이 적혀 있으면 함께 고쳐야 합니다.", "바꾸기", "취소")) return;
        string r = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(e), newName);
        if (!string.IsNullOrEmpty(r)) { EditorUtility.DisplayDialog("이름 변경 실패", r, "확인"); return; }
        AssetDatabase.SaveAssets();
        renameTarget = null;
    }

    private void SyncRegistries()
    {
        var g = EquipmentAssets.Growth;
        var s = EquipmentAssets.Shop;
        var valid = EquipmentAssets.All().Where(e => !AssetDatabase.GetAssetPath(e).Contains("/Deprecated/")).ToList();
        var addG = g != null ? valid.Where(e => !g.equipments.Contains(e)).ToList() : new List<EquipmentSO>();
        var addS = s != null ? valid.Where(e => !e.upgradeOnly && !s.equipmentPool.Contains(e)).ToList() : new List<EquipmentSO>();
        var removeS = s != null ? s.equipmentPool.Where(e => e == null || e.upgradeOnly).ToList() : new List<EquipmentSO>();
        int nullG = g != null ? g.equipments.Count(x => x == null) : 0;

        if (addG.Count + addS.Count + removeS.Count + nullG == 0)
        {
            EditorUtility.DisplayDialog("목록 정리", "바꿀 것이 없습니다. 모든 장비가 목록·상점 후보와 맞습니다.", "확인");
            return;
        }
        var msg = new StringBuilder();
        if (addG.Count > 0) msg.AppendLine($"· 게임 장비 목록에 추가 {addG.Count}개: {string.Join(", ", addG.Select(x => x.name))}");
        if (nullG > 0) msg.AppendLine($"· 게임 장비 목록의 빈 칸 {nullG}개 제거");
        if (addS.Count > 0) msg.AppendLine($"· 상점 진열 후보에 추가 {addS.Count}개: {string.Join(", ", addS.Select(x => x.name))}");
        if (removeS.Count > 0) msg.AppendLine($"· 상점 후보에서 빼기 {removeS.Count}개 (빈 칸 · 강화 전용)");
        if (!EditorUtility.DisplayDialog("목록 정리", msg.ToString(), "적용", "취소")) return;

        if (g != null)
        {
            Undo.RecordObject(g, "장비 목록 정리");
            g.equipments.RemoveAll(x => x == null);
            g.equipments.AddRange(addG);
            EditorUtility.SetDirty(g);
        }
        if (s != null)
        {
            Undo.RecordObject(s, "장비 목록 정리");
            s.equipmentPool.RemoveAll(x => x == null || x.upgradeOnly);
            s.equipmentPool.AddRange(addS);
            EditorUtility.SetDirty(s);
        }
        AssetDatabase.SaveAssets();
    }

    // ── 도우미 ────────────────────────────────────────────────────────
    private static GUIStyle wrapMini, wrapLabel, wrapArea;
    private static GUIStyle WrapMini => wrapMini ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = false };
    private static GUIStyle WrapLabel => wrapLabel ??= new GUIStyle(EditorStyles.label) { wordWrap = true };
    private static GUIStyle WrapArea => wrapArea ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

    internal static void Header(string text)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.35f));
        EditorGUILayout.Space(2);
    }

    private static void Prop(SerializedObject so, string name, string label, string tip)
    {
        var p = so.FindProperty(name);
        if (p == null) { EditorGUILayout.LabelField(label, $"(필드 '{name}' 없음)"); return; }
        EditorGUILayout.PropertyField(p, new GUIContent(label, tip), true);
    }

    internal static void DrawSprite(Rect r, Sprite s)
    {
        if (s == null || s.texture == null) return;
        try
        {
            var t = s.texture;
            var tr = s.textureRect;
            var uv = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height);
            float aspect = tr.height > 0 ? tr.width / tr.height : 1f;
            var fit = r;
            if (aspect > 1f) { fit.height = r.width / aspect; fit.y += (r.height - fit.height) * 0.5f; }
            else { fit.width = r.height * aspect; fit.x += (r.width - fit.width) * 0.5f; }
            GUI.DrawTextureWithTexCoords(fit, t, uv);
        }
        catch (Exception)
        {
            var preview = AssetPreview.GetAssetPreview(s);
            if (preview != null) GUI.DrawTexture(r, preview, ScaleMode.ScaleToFit);
        }
    }
}

/// <summary>장비 에셋 조회 · 검사 · 생성 · 삭제 · 표시 문구.</summary>
public static class EquipmentAssets
{
    public const string DefaultFolder = "Assets/SOData/Equipment/Weapons";
    public static string DefaultFolderOrAssets => AssetDatabase.IsValidFolder(DefaultFolder) ? DefaultFolder : "Assets";

    public static List<EquipmentSO> All() => AssetDatabase.FindAssets("t:EquipmentSO")
        .Select(g => AssetDatabase.LoadAssetAtPath<EquipmentSO>(AssetDatabase.GUIDToAssetPath(g)))
        .Where(e => e != null).ToList();

    public static string DisplayName(EquipmentSO e) => e == null ? "(없음)" : string.IsNullOrWhiteSpace(e.equipmentName) ? e.name : e.equipmentName;

    // ── 레지스트리 ────────────────────────────────────────────────────
    private static GrowthRegistrySO growth;
    private static ShopRegistrySO shop;

    public static GrowthRegistrySO Growth => growth != null ? growth : (growth = LoadFirst<GrowthRegistrySO>());
    public static ShopRegistrySO Shop => shop != null ? shop : (shop = LoadFirst<ShopRegistrySO>());

    private static T LoadFirst<T>() where T : UnityEngine.Object
    {
        var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    public static void SetIn(UnityEngine.Object registry, List<EquipmentSO> list, EquipmentSO e, bool include)
    {
        if (registry == null || list == null || e == null || list.Contains(e) == include) return;
        Undo.RecordObject(registry, include ? "장비 등록" : "장비 등록 해제");
        if (include) list.Add(e); else list.RemoveAll(x => x == e);
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssetIfDirty(registry);
    }

    // ── 표시 문구 ─────────────────────────────────────────────────────
    private static readonly Color[] RarityColors =
    {
        new Color(0.70f, 0.70f, 0.70f), new Color(0.35f, 0.80f, 0.40f), new Color(0.30f, 0.60f, 1.00f),
        new Color(0.70f, 0.40f, 1.00f), new Color(1.00f, 0.60f, 0.20f), new Color(1.00f, 0.30f, 0.30f),
    };

    public static Color RarityColor(EquipmentSO e)
    {
        int i;
        try { i = Convert.ToInt32(e.rarity); } catch { i = 0; }
        return RarityColors[Mathf.Clamp(i, 0, RarityColors.Length - 1)];
    }

    public static string EnumName(Enum value)
    {
        if (value == null) return "";
        var f = value.GetType().GetField(value.ToString());
        var attr = f?.GetCustomAttribute<InspectorNameAttribute>();
        return attr != null ? attr.displayName : ObjectNames.NicifyVariableName(value.ToString());
    }

    private static readonly Dictionary<Type, (string name, string help)> passiveInfo = new Dictionary<Type, (string, string)>
    {
        { typeof(EquipmentStatPassive), ("스탯형", "착용 중 항상 플레이어 스탯을 올린다.") },
        { typeof(EquipmentStatusPassive), ("상태이상형", "Q/E 스킬이 특정 상태이상을 거는 확률·강도를 올린다.") },
        { typeof(EquipmentConditionBuffPassive), ("조건형 버프", "조건이 맞는 동안 스탯 버프를 유지한다(예: 전투 중 일정 시간 무피격).") },
        { typeof(EquipmentTriggerBuffPassive), ("발동형 버프", "특정 행동 뒤 다음 1타의 데미지를 올린다(예: 밀치기·끌어당기기 후).") },
    };

    public static IReadOnlyList<Type> PassiveTypes => TypeCache.GetTypesDerivedFrom<EquipmentPassive>()
        .Where(t => !t.IsAbstract && !t.IsGenericType && t.GetConstructor(Type.EmptyTypes) != null)
        .OrderBy(t => passiveInfo.ContainsKey(t) ? 0 : 1).ThenBy(t => t.Name).ToList();

    public static (string name, string help) PassiveInfo(Type t)
    {
        if (t == null) return ("비어 있음", "");
        return passiveInfo.TryGetValue(t, out var info) ? info : (ObjectNames.NicifyVariableName(t.Name), "");
    }

    private static readonly Dictionary<string, (string label, string tip)> fieldLabels = new Dictionary<string, (string, string)>
    {
        { "label", ("메모 라벨", "편집기 식별용. 게임에는 나오지 않는다.") },
        { "effect", ("스탯 효과", "") },
        { "status", ("상태이상", "어떤 상태이상을 강화할지") },
        { "applyChanceBonus", ("부여 확률 보너스", "0.2 = +20%p") },
        { "applyChancePerLevel", ("  └ 강화당 증가", "확률 보너스의 강화 레벨당 증가분") },
        { "strengthBonus", ("강도 보너스", "지속·피해 등. 0.3 = +30%") },
        { "strengthPerLevel", ("  └ 강화당 증가", "강도 보너스의 강화 레벨당 증가분") },
        { "condition", ("발동 조건", "") },
        { "reapplyDelay", ("필요한 무피격 시간(초)", "조건이 다시 켜지기까지 맞지 않아야 하는 시간") },
        { "reapplyDelayPerLevel", ("  └ 강화당 증가(초)", "음수면 강화할수록 짧아진다. 예: -0.1") },
        { "effects", ("버프 스탯", "조건이 맞는 동안 주는 스탯들") },
        { "trigger", ("발동 계기", "") },
        { "buffDuration", ("유지 시간(초)", "이 안에 타격하지 않으면 사라진다") },
        { "damageBonus", ("다음 타격 데미지 증가", "0.2 = +20%") },
        { "damageBonusPerLevel", ("  └ 강화당 증가", "0.02 = 레벨당 +2%") },
    };

    public static GUIContent Label(string field, string fallback)
        => fieldLabels.TryGetValue(field, out var l) ? new GUIContent(l.label, l.tip) : new GUIContent(fallback);

    private static string Pct(float v) => $"{v * 100f:0.##}%";
    private static string Stat(StatEffect s, int lv)
    {
        if (s == null) return "(빈 스탯)";
        float v = s.Effective(lv);
        string sign = v >= 0 ? "+" : "";
        return $"{EnumName(s.statType)} {sign}{(s.isPercent ? Pct(v) : v.ToString("0.##"))}";
    }

    public static string PassiveSummary(EquipmentPassive p, int lv)
    {
        switch (p)
        {
            case null: return "(비어 있는 패시브)";
            case EquipmentStatPassive sp: return $"[스탯] {Stat(sp.effect, lv)}";
            case EquipmentStatusPassive st:
            {
                var parts = new List<string>();
                if (st.EffectiveChance(lv) != 0) parts.Add($"확률 +{Pct(st.EffectiveChance(lv))}p");
                if (st.EffectiveStrength(lv) != 0) parts.Add($"강도 +{Pct(st.EffectiveStrength(lv))}");
                return $"[상태이상] {EnumName(st.status)} " + (parts.Count > 0 ? string.Join(", ", parts) : "(수치 없음)");
            }
            case EquipmentConditionBuffPassive cb:
                return $"[조건 버프] {EnumName(cb.condition)} ({cb.EffectiveDelay(lv):0.##}초): "
                       + (cb.effects != null && cb.effects.Count > 0 ? string.Join(", ", cb.effects.Select(x => Stat(x, lv))) : "(스탯 없음)");
            case EquipmentTriggerBuffPassive tb:
                return $"[발동 버프] {EnumName(tb.trigger)} → 다음 타격 +{Pct(tb.EffectiveBonus(lv))} ({tb.buffDuration:0.##}초 유지)";
            default: return $"[{PassiveInfo(p.GetType()).name}] {p.label}";
        }
    }

    // ── 검사 ──────────────────────────────────────────────────────────
    public static List<string> Warnings(EquipmentSO e, GrowthRegistrySO g, ShopRegistrySO s)
    {
        var w = new List<string>();
        if (e == null) return w;
        if (string.IsNullOrWhiteSpace(e.equipmentName)) w.Add("게임 표시 이름이 비어 있습니다.");
        if (string.IsNullOrWhiteSpace(e.description)) w.Add("설명이 비어 있습니다.");
        if (e.icon == null) w.Add("아이콘이 없습니다.");
        if (e.passives == null || e.passives.Count == 0) w.Add("패시브가 없습니다(1개 이상 필요).");
        else
        {
            if (e.passives.Any(p => p == null)) w.Add("비어 있는 패시브 칸이 있습니다.");
            foreach (var p in e.passives.OfType<EquipmentStatusPassive>())
                if (p.status == StatusType.None) w.Add("상태이상형 패시브의 상태이상이 '없음'입니다.");
            foreach (var p in e.passives.OfType<EquipmentStatPassive>())
                if (p.effect != null && p.effect.isPercent && Mathf.Abs(p.effect.value) >= 3f) w.Add($"비율 스탯 수치가 {p.effect.value} 입니다(= {Pct(p.effect.value)}). 0.1 = 10% 입니다.");
            foreach (var p in e.passives.OfType<EquipmentConditionBuffPassive>())
                if (p.effects == null || p.effects.Count == 0) w.Add("조건형 버프에 버프 스탯이 없습니다.");
        }
        bool deprecated = AssetDatabase.GetAssetPath(e).Contains("/Deprecated/");
        if (g != null && !deprecated && !g.equipments.Contains(e)) w.Add("게임 장비 목록에 없습니다(세이브에서 찾지 못함).");
        if (s != null && e.upgradeOnly && s.equipmentPool.Contains(e)) w.Add("강화 전용 장비인데 상점 진열 후보에 들어 있습니다.");
        if (e.upgradeOptions != null)
        {
            if (e.upgradeOptions.Any(x => x == null)) w.Add("강화 선택지에 빈 칸이 있습니다.");
            if (e.upgradeOptions.Contains(e)) w.Add("강화 선택지에 자기 자신이 들어 있습니다.");
        }
        var c = e.comboMultipliers;
        if (c.x <= 0 || c.y <= 0 || c.z <= 0) w.Add("콤보 배율에 0 이하 값이 있습니다.");
        return w;
    }

    public static string ValidateName(string name, EquipmentSO self = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return "에셋 파일 이름을 입력하세요.";
        name = name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            return "파일 이름에 쓸 수 없는 문자가 있습니다.";
        if (name.StartsWith(".") || name.EndsWith(".")) return "점(.)으로 시작하거나 끝날 수 없습니다.";
        if (All().Any(e => e != self && string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase)))
            return $"'{name}' 장비가 이미 있습니다. 세이브가 에셋 이름으로 장비를 찾으므로 겹치면 안 됩니다.";
        return null;
    }

    public static string NameHint(string name)
        => !string.IsNullOrEmpty(name) && name.Any(c => c > 127)
            ? "한글 등 영문 외 문자가 있습니다. 동작은 하지만 버전 관리·코드에서 다루기 쉽게 영문(예: IronSword)을 권장합니다."
            : null;

    public static string UniqueName(string baseName)
    {
        var used = new HashSet<string>(All().Select(e => e.name), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName)) return baseName;
        for (int n = 2; ; n++) if (!used.Contains(baseName + n)) return baseName + n;
    }

    // ── 생성 ──────────────────────────────────────────────────────────
    public sealed class Request
    {
        public string assetName = "NewEquipment";
        public string folder = DefaultFolder;
        public EquipmentSO copyFrom;
        public string displayName = "새 장비";
        public ItemRarity rarity;
        public int shopCost = 150;
        public int maxEnhanceLevel = 5;
        public Type firstPassive;
        public bool addToGrowth = true;
        public bool addToShop = true;
    }

    public static EquipmentSO Create(Request r, out string error)
    {
        error = ValidateName(r.assetName);
        if (error != null) return null;
        string name = r.assetName.Trim();
        string folder = string.IsNullOrEmpty(r.folder) || !AssetDatabase.IsValidFolder(r.folder) ? DefaultFolderOrAssets : r.folder;
        string path = $"{folder}/{name}.asset";
        if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)) { error = $"{path} 에 이미 파일이 있습니다."; return null; }

        EquipmentSO e;
        if (r.copyFrom != null)
        {
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(r.copyFrom), path)) { error = "복사에 실패했습니다."; return null; }
            e = AssetDatabase.LoadAssetAtPath<EquipmentSO>(path);
        }
        else
        {
            e = ScriptableObject.CreateInstance<EquipmentSO>();
            AssetDatabase.CreateAsset(e, path);
            if (r.firstPassive != null)
            {
                var p = (EquipmentPassive)Activator.CreateInstance(r.firstPassive);
                p.label = PassiveInfo(r.firstPassive).name;
                if (p is EquipmentConditionBuffPassive cb && cb.effects.Count == 0) cb.effects.Add(new StatEffect());
                e.passives.Add(p);
            }
        }

        e.equipmentName = r.displayName ?? "";
        e.rarity = r.rarity;
        e.shopCost = Mathf.Max(0, r.shopCost);
        e.maxEnhanceLevel = Mathf.Max(0, r.maxEnhanceLevel);
        EditorUtility.SetDirty(e);

        if (r.addToGrowth && Growth != null) SetIn(Growth, Growth.equipments, e, true);
        if (r.addToShop && Shop != null && !e.upgradeOnly) SetIn(Shop, Shop.equipmentPool, e, true);
        AssetDatabase.SaveAssets();
        return e;
    }

    // ── 삭제 ──────────────────────────────────────────────────────────
    public static bool Delete(EquipmentSO e)
    {
        if (e == null) return false;
        var g = Growth;
        var s = Shop;
        if (g != null && g.startingEquipment == e)
        {
            EditorUtility.DisplayDialog("장비 삭제", $"'{DisplayName(e)}' 은(는) 새 게임 시작 장비라 지울 수 없습니다.\n먼저 다른 장비를 '시작 장비로 지정'하세요.", "확인");
            return false;
        }

        string path = AssetDatabase.GetAssetPath(e);
        var skip = new HashSet<string> { path };
        if (g != null) skip.Add(AssetDatabase.GetAssetPath(g));
        if (s != null) skip.Add(AssetDatabase.GetAssetPath(s));
        if (!FindReferences(e, skip, out var assetRefs, out var codeRefs)) return false;

        var msg = new StringBuilder();
        msg.AppendLine($"'{DisplayName(e)}' ({e.name}) 을(를) 삭제합니다.");
        msg.AppendLine("에셋은 OS 휴지통으로 이동합니다(휴지통에서 복구 가능).\n");
        if (g != null && g.equipments.Contains(e)) msg.AppendLine($"· {g.name} 장비 목록에서 자동으로 뺍니다.");
        if (s != null && s.equipmentPool.Contains(e)) msg.AppendLine($"· {s.name} 상점 후보에서 자동으로 뺍니다.");
        if (assetRefs.Count > 0)
        {
            msg.AppendLine("\n다른 에셋·씬·프리팹이 참조 중입니다(삭제 후 빈 칸이 됩니다):");
            foreach (var p in assetRefs.Take(8)) msg.AppendLine("  · " + p);
            if (assetRefs.Count > 8) msg.AppendLine($"  … 외 {assetRefs.Count - 8}곳");
        }
        if (codeRefs.Count > 0)
        {
            msg.AppendLine("\n코드에 이 장비 이름이 적혀 있습니다(함께 고쳐야 할 수 있음):");
            foreach (var p in codeRefs.Take(8)) msg.AppendLine("  · " + p);
        }
        msg.AppendLine("\n이미 세이브에 착용 중인 장비면 불러올 때 사라집니다.");
        if (!EditorUtility.DisplayDialog("장비 삭제", msg.ToString(), "삭제", "취소")) return false;

        if (g != null && g.equipments.Contains(e)) { Undo.RecordObject(g, "장비 삭제"); g.equipments.RemoveAll(x => x == e); EditorUtility.SetDirty(g); }
        if (s != null && s.equipmentPool.Contains(e)) { Undo.RecordObject(s, "장비 삭제"); s.equipmentPool.RemoveAll(x => x == e); EditorUtility.SetDirty(s); }
        foreach (var other in All().Where(x => x != e && x.upgradeOptions != null && x.upgradeOptions.Contains(e)))
        {
            Undo.RecordObject(other, "장비 삭제");
            other.upgradeOptions = other.upgradeOptions.Where(x => x != e).ToArray();
            EditorUtility.SetDirty(other);
        }
        AssetDatabase.SaveAssets();

        if (!AssetDatabase.MoveAssetToTrash(path))
        {
            EditorUtility.DisplayDialog("장비 삭제", "휴지통으로 옮기지 못했습니다. 목록에서는 이미 빠졌으니 Project 창에서 직접 지워 주세요.", "확인");
            return false;
        }
        return true;
    }

    public static bool FindReferences(UnityEngine.Object target, HashSet<string> skip, out List<string> assetRefs, out List<string> codeRefs)
    {
        assetRefs = new List<string>();
        codeRefs = new List<string>();
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target));
        string quoted = "\"" + target.name + "\"";
        string root = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/";

        var files = Directory.EnumerateFiles(Application.dataPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".prefab") || f.EndsWith(".unity") || f.EndsWith(".asset") || f.EndsWith(".cs"))
            .ToList();
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                string rel = files[i].Replace('\\', '/').Replace(root, "");
                if (i % 40 == 0 && EditorUtility.DisplayCancelableProgressBar("장비 참조 검사", rel, (float)i / files.Count)) return false;
                if (skip.Contains(rel)) continue;
                string text;
                try { text = File.ReadAllText(files[i]); } catch { continue; }
                if (rel.EndsWith(".cs")) { if (text.Contains(quoted)) codeRefs.Add(rel); }
                else if (text.Contains(guid)) assetRefs.Add(rel);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        return true;
    }
}

/// <summary>새 장비 / 장비 복제 창.</summary>
public class NewEquipmentPopup : EditorWindow
{
    private EquipmentAssets.Request req;
    private Action<EquipmentSO> onCreated;
    private List<EquipmentSO> sources;
    private int mode;          // 0 = 빈 장비, 1 = 기존 장비 복사
    private int sourceIndex;
    private int passiveIndex;  // 0 = 없음
    private string error;

    public static void Open(EquipmentSO copyFrom, Action<EquipmentSO> onCreated)
    {
        var w = CreateInstance<NewEquipmentPopup>();
        w.titleContent = new GUIContent(copyFrom != null ? "장비 복제" : "새 장비");
        w.onCreated = onCreated;
        w.sources = EquipmentAssets.All().OrderBy(e => EquipmentAssets.DisplayName(e), StringComparer.Ordinal).ToList();
        w.req = new EquipmentAssets.Request
        {
            folder = EquipmentAssets.DefaultFolderOrAssets,
            assetName = EquipmentAssets.UniqueName("NewEquipment"),
        };
        w.passiveIndex = 1; // 가장 흔한 '스탯형'
        if (copyFrom != null)
        {
            w.mode = 1;
            w.sourceIndex = Mathf.Max(0, w.sources.IndexOf(copyFrom));
            w.ApplySource();
        }
        w.minSize = w.maxSize = new Vector2(460, 450);
        w.ShowUtility();
    }

    private void ApplySource()
    {
        if (sources.Count == 0) { mode = 0; return; }
        var src = sources[Mathf.Clamp(sourceIndex, 0, sources.Count - 1)];
        req.folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(src))?.Replace('\\', '/') ?? EquipmentAssets.DefaultFolderOrAssets;
        req.assetName = EquipmentAssets.UniqueName(src.name + "Copy");
        req.displayName = EquipmentAssets.DisplayName(src) + " (사본)";
        req.rarity = src.rarity;
        req.shopCost = src.shopCost;
        req.maxEnhanceLevel = src.maxEnhanceLevel;
        req.addToShop = !src.upgradeOnly;
    }

    private void OnGUI()
    {
        if (req == null) { Close(); return; }
        EditorGUIUtility.labelWidth = 130;
        EditorGUILayout.Space(6);

        int nm = GUILayout.Toolbar(mode, new[] { "빈 장비로 시작", "기존 장비 복사" });
        if (nm != mode) { mode = nm; if (mode == 1) ApplySource(); }
        EditorGUILayout.Space(4);

        if (mode == 1)
        {
            if (sources.Count == 0) { EditorGUILayout.HelpBox("복사할 장비가 없습니다.", MessageType.Info); mode = 0; }
            else
            {
                var labels = sources.Select(s => $"{EquipmentAssets.DisplayName(s)}  ({s.name})").ToArray();
                int idx = EditorGUILayout.Popup(new GUIContent("복사할 장비", "패시브·설명·아이콘·공격 수치를 그대로 가져온다."), sourceIndex, labels);
                if (idx != sourceIndex) { sourceIndex = idx; ApplySource(); }
            }
        }

        EquipmentEditorPanel.Header("이름");
        req.assetName = EditorGUILayout.TextField(new GUIContent("에셋 파일 이름", "세이브가 이 이름으로 장비를 찾는다. 나중에 바꾸기 어렵다."), req.assetName);
        string nameError = EquipmentAssets.ValidateName(req.assetName);
        if (nameError != null) EditorGUILayout.HelpBox(nameError, MessageType.Error);
        else if (EquipmentAssets.NameHint(req.assetName) is string hint) EditorGUILayout.HelpBox(hint, MessageType.Info);
        req.displayName = EditorGUILayout.TextField(new GUIContent("게임 표시 이름"), req.displayName);

        EquipmentEditorPanel.Header("기본 값");
        req.rarity = (ItemRarity)EditorGUILayout.EnumPopup(new GUIContent("등급"), req.rarity);
        req.shopCost = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("상점 가격(골드)"), req.shopCost));
        req.maxEnhanceLevel = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("최대 강화 레벨"), req.maxEnhanceLevel));
        if (mode == 0)
        {
            var types = EquipmentAssets.PassiveTypes;
            var names = new[] { "없음 (나중에 추가)" }.Concat(types.Select(t => EquipmentAssets.PassiveInfo(t).name)).ToArray();
            passiveIndex = EditorGUILayout.Popup(new GUIContent("첫 패시브", "만들면서 패시브 한 칸을 넣어 둔다. 수치는 만든 뒤 편집기에서 정한다."), Mathf.Clamp(passiveIndex, 0, names.Length - 1), names);
        }

        EquipmentEditorPanel.Header("저장 위치 · 등록");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent("폴더"), new GUIContent(req.folder));
            if (GUILayout.Button("변경…", GUILayout.Width(56)))
            {
                string abs = EditorUtility.OpenFolderPanel("장비 저장 폴더", req.folder, "");
                string data = Application.dataPath.Replace('\\', '/');
                if (!string.IsNullOrEmpty(abs))
                {
                    abs = abs.Replace('\\', '/');
                    if (abs == data || abs.StartsWith(data + "/")) req.folder = "Assets" + abs.Substring(data.Length);
                    else EditorUtility.DisplayDialog("폴더", "프로젝트의 Assets 폴더 안을 골라 주세요.", "확인");
                }
                GUIUtility.ExitGUI();
            }
        }
        var g = EquipmentAssets.Growth;
        var s = EquipmentAssets.Shop;
        using (new EditorGUI.DisabledScope(g == null))
            req.addToGrowth = EditorGUILayout.ToggleLeft(new GUIContent("게임 장비 목록에 등록" + (g != null ? $" ({g.name})" : " (목록 에셋 없음)"),
                "세이브 불러오기·보상에 쓰는 전체 목록. 빠지면 세이브에서 찾지 못한다."), req.addToGrowth && g != null);
        using (new EditorGUI.DisabledScope(s == null))
            req.addToShop = EditorGUILayout.ToggleLeft(new GUIContent("상점 진열 후보에 등록" + (s != null ? $" ({s.name})" : " (상점 에셋 없음)")), req.addToShop && s != null);

        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);

        GUILayout.FlexibleSpace();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("취소", GUILayout.Width(80))) { Close(); GUIUtility.ExitGUI(); }
            using (new EditorGUI.DisabledScope(nameError != null))
                if (GUILayout.Button(mode == 1 ? "복제" : "만들기", GUILayout.Width(100)))
                {
                    req.copyFrom = mode == 1 && sources.Count > 0 ? sources[sourceIndex] : null;
                    var types = EquipmentAssets.PassiveTypes;
                    req.firstPassive = mode == 0 && passiveIndex > 0 && passiveIndex - 1 < types.Count ? types[passiveIndex - 1] : null;
                    var e = EquipmentAssets.Create(req, out error);
                    if (e != null)
                    {
                        var cb = onCreated;
                        Close();
                        cb?.Invoke(e);
                    }
                    GUIUtility.ExitGUI();
                }
        }
        EditorGUILayout.Space(6);
    }
}
