using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기획자용 적 몬스터 편집기 (Tools > 기획자 툴 > 적 몬스터 편집기).
///
///  · 하나씩 편집 — 기본 정보 · 능력치 · AI 패턴(같은 화면에서 수치 편집) · 프리팹 · 등장 위치를 한글 화면으로 고친다.
///  · 능력치 표   — 모든 적의 체력/공격력/공속/방어/이동/사거리를 한 표에서 비교하고 바로 고친다(초당 피해·실질 체력 자동 계산).
///  · 군집 편집   — 일반 방 웨이브에 나오는 '군집'(어떤 적이 몇 마리)을 만들고 고친다.
///
/// 게임이 적을 고르는 경로(이 편집기의 '등장 위치'가 보여주는 것):
///  · 일반 적   — EnemyClusterRegistry 의 군집(또는 층 조절표의 군집 목록)에서 뽑힌다.
///  · 엘리트    — MinionRegistry 의 엘리트 목록(손으로 고른 목록)에서 뽑힌다.
///  · 보스      — MapGenerationData 의 '층수별 보스'에 배정된 층에서 나온다.
/// 모든 수정은 Undo(Ctrl+Z) 가 된다.
/// </summary>
public class EnemyEditorWindow : EditorWindow
{
    private enum View { Detail, Table, Cluster }
    private enum SortBy { Default, Hp, Attack, Dps, Move, Range }
    private static readonly string[] SortLabels = { "정렬: 등급순", "정렬: 체력", "정렬: 공격력", "정렬: 초당 피해", "정렬: 이동 속도", "정렬: 사거리" };
    private static readonly string[] TierFilterLabels = { "전체 등급", "일반", "엘리트", "보스" };

    [SerializeField] private View view = View.Detail;
    [SerializeField] private string search = "";
    [SerializeField] private int tierFilter;
    [SerializeField] private bool onlyWarnings;
    [SerializeField] private SortBy sortBy;
    [SerializeField] private EnemyMinionDataSO selected;
    [SerializeField] private EnemyClusterSO selectedCluster;
    [SerializeField] private bool showAi = true, showFx, showAdvanced;
    [SerializeField] private int newBossFloor = 1;

    private List<EnemyMinionDataSO> all = new List<EnemyMinionDataSO>();
    private List<EnemyClusterSO> clusters = new List<EnemyClusterSO>();
    private readonly Dictionary<UnityEngine.Object, SerializedObject> serialized = new Dictionary<UnityEngine.Object, SerializedObject>();
    private Editor patternEditor;
    private Vector2 listScroll, detailScroll, tableScroll, clusterListScroll, clusterScroll;
    private string renameText;
    private EnemyMinionDataSO renameTarget;
    private string lastContext;
    private int version;

    // ── 메뉴 ──────────────────────────────────────────────────────────
    [MenuItem("Tools/기획자 툴/적 몬스터 편집기", priority = 20)]
    public static void OpenMenu() => Open(null);

    [MenuItem("Tools/기획자 툴/새 적 몬스터 만들기", priority = 21)]
    public static void NewMenu()
    {
        var w = Open(null);
        NewEnemyPopup.Open(null, w.OnCreated);
    }

    [MenuItem("Tools/기획자 툴/적 군집 편집", priority = 22)]
    public static void ClusterMenu()
    {
        var w = Open(null);
        w.view = View.Cluster;
    }

    public static EnemyEditorWindow Open(EnemyMinionDataSO select)
    {
        var w = GetWindow<EnemyEditorWindow>();
        w.titleContent = new GUIContent("적 몬스터 편집기");
        w.minSize = new Vector2(820, 460);
        w.Reload();
        if (select != null) { w.selected = select; w.view = View.Detail; }
        w.Show();
        return w;
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("적 몬스터 편집기");
        Reload();
        EditorApplication.projectChanged += OnProjectChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= OnProjectChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        ClearSerialized();
        if (patternEditor != null) DestroyImmediate(patternEditor);
    }

    private void OnProjectChanged() { Reload(); Repaint(); }
    private void OnUndoRedo() { version++; EnemyAssets.InvalidateUsage(); Repaint(); }

    private void ClearSerialized()
    {
        foreach (var so in serialized.Values) so?.Dispose();
        serialized.Clear();
    }

    private void Reload()
    {
        ClearSerialized();
        EnemyAssets.InvalidateUsage();
        all = EnemyAssets.All();
        clusters = EnemyAssets.AllClusters();
        if (selected != null && !all.Contains(selected)) selected = null;
        if (selectedCluster != null && !clusters.Contains(selectedCluster)) selectedCluster = null;
        version++;
    }

    private SerializedObject SO(UnityEngine.Object o)
    {
        if (!serialized.TryGetValue(o, out var so) || so == null || so.targetObject == null)
        {
            so = new SerializedObject(o);
            serialized[o] = so;
        }
        return so;
    }

    private void Defer(Action action)
    {
        EditorApplication.delayCall += () => { if (this == null) return; action(); EnemyAssets.InvalidateUsage(); Repaint(); };
    }

    private static void EndTextEditing()
    {
        GUIUtility.keyboardControl = 0;
        EditorGUIUtility.editingTextField = false;
    }

    // ── 필터/정렬 ─────────────────────────────────────────────────────
    private List<EnemyMinionDataSO> Filtered()
    {
        var list = all.Where(e => e != null
                                  && (tierFilter == 0 || (int)e.tier == tierFilter - 1)
                                  && (!onlyWarnings || EnemyAssets.Warnings(e).Count > 0)
                                  && Matches(e));
        switch (sortBy)
        {
            case SortBy.Hp: return list.OrderByDescending(e => e.maxHP).ToList();
            case SortBy.Attack: return list.OrderByDescending(e => e.attack).ToList();
            case SortBy.Dps: return list.OrderByDescending(EnemyAssets.Dps).ToList();
            case SortBy.Move: return list.OrderByDescending(e => e.moveSpeed).ToList();
            case SortBy.Range: return list.OrderByDescending(e => e.attackRange).ToList();
            default:
                return list.OrderBy(e => (int)e.tier).ThenBy(e => e.isPhaseVariant ? 1 : 0)
                    .ThenBy(e => EnemyAssets.DisplayName(e), StringComparer.Ordinal).ToList();
        }
    }

    private bool Matches(EnemyMinionDataSO e)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        string q = search.Trim();
        return Has(e.name, q) || Has(e.minionName, q) || (e.aiPattern != null && Has(e.aiPattern.name, q));
    }

    private static bool Has(string t, string q) => !string.IsNullOrEmpty(t) && t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

    // ── 그리기 ────────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (all.Any(e => e == null) || clusters.Any(c => c == null)) Reload();

        string context = $"{view}|{(selected != null ? selected.GetInstanceID() : 0)}|{(selectedCluster != null ? selectedCluster.GetInstanceID() : 0)}|{version}";
        if (context != lastContext) { lastContext = context; EndTextEditing(); }

        DrawToolbar();

        int warn = all.Count(e => EnemyAssets.Warnings(e).Count > 0);
        EditorGUILayout.LabelField(
            $"적 {all.Count}종 (일반 {all.Count(e => e.tier == EnemyTier.Normal)} · 엘리트 {all.Count(e => e.tier == EnemyTier.Elite)} · 보스 {all.Count(e => e.tier == EnemyTier.Boss)}) · 군집 {clusters.Count}개 · 확인 필요 {warn}개 · Ctrl+Z 로 되돌리기"
            + (Application.isPlaying ? " · 플레이 중 수정도 에셋에 그대로 남습니다" : ""), EditorStyles.miniLabel);

        if (view == View.Cluster) { DrawClusters(); return; }
        if (all.Count == 0)
        {
            EditorGUILayout.HelpBox("EnemyMinionDataSO 에셋이 없습니다. '새 적'으로 만드세요.", MessageType.Info);
            return;
        }
        var list = Filtered();
        if (view == View.Table) DrawTable(list); else DrawDetail(list);
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var nv = (View)GUILayout.Toolbar((int)view, new[] { "하나씩 편집", "능력치 표", "군집 편집" }, EditorStyles.toolbarButton, GUILayout.Width(250));
            if (nv != view) { var v = nv; Defer(() => view = v); }
            GUILayout.Space(6);
            using (new EditorGUI.DisabledScope(view == View.Cluster))
            {
                search = GUILayout.TextField(search ?? "", EditorStyles.toolbarSearchField, GUILayout.MinWidth(100), GUILayout.MaxWidth(220));
                tierFilter = EditorGUILayout.Popup(tierFilter, TierFilterLabels, EditorStyles.toolbarPopup, GUILayout.Width(76));
                sortBy = (SortBy)EditorGUILayout.Popup((int)sortBy, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(110));
                onlyWarnings = GUILayout.Toggle(onlyWarnings, new GUIContent("확인 필요만", "능력치 이상, AI·프리팹 누락, 어디에도 등장하지 않는 적 등만 본다."), EditorStyles.toolbarButton, GUILayout.Width(74));
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("새 적", "등급·AI 패턴·프리팹·등장 위치를 정해 새 적을 만든다. 기존 적 복사로 시작할 수도 있다."), EditorStyles.toolbarButton))
                Defer(() => NewEnemyPopup.Open(null, OnCreated));
            if (GUILayout.Button(new GUIContent("↻", "목록 새로고침"), EditorStyles.toolbarButton, GUILayout.Width(24))) Defer(Reload);
        }
    }

    // ── 하나씩 편집 ───────────────────────────────────────────────────
    private void DrawDetail(List<EnemyMinionDataSO> list)
    {
        if (selected == null && list.Count > 0) selected = list[0];

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
            {
                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                int lastTier = -1;
                foreach (var e in list)
                {
                    if (sortBy == SortBy.Default && (int)e.tier != lastTier)
                    {
                        lastTier = (int)e.tier;
                        GUILayout.Label(EnemyAssets.TierName(e.tier), EditorStyles.miniBoldLabel);
                    }
                    DrawListRow(e);
                }
                EditorGUILayout.EndScrollView();
            }

            var div = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(div, new Color(0.5f, 0.5f, 0.5f, 0.4f));

            using (new EditorGUILayout.VerticalScope())
            {
                if (selected == null) { EditorGUILayout.HelpBox("왼쪽에서 적을 고르세요.", MessageType.Info); return; }
                DrawDetailHeader(selected);
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                DrawEnemy(selected);
                EditorGUILayout.Space(12);
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawListRow(EnemyMinionDataSO e)
    {
        var r = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
        if (e == selected) EditorGUI.DrawRect(r, new Color(0.24f, 0.48f, 0.90f, 0.35f));
        EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 4, 4, r.height - 8), EnemyAssets.TierColor(e.tier));
        EquipmentEditorWindow.DrawSprite(new Rect(r.x + 10, r.y + 3, 20, 20), e.minionIcon);
        string label = EnemyAssets.DisplayName(e) + (e.isPhaseVariant ? "  (페이즈)" : "");
        GUI.Label(new Rect(r.x + 34, r.y + 4, r.width - 58, 18), new GUIContent(label, e.name));
        if (EnemyAssets.Warnings(e).Count > 0)
        {
            var icon = EditorGUIUtility.IconContent("console.warnicon.sml");
            icon.tooltip = "확인 필요";
            GUI.Label(new Rect(r.xMax - 22, r.y + 4, 20, 18), icon);
        }
        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            var t = e;
            Defer(() => selected = t);
            Event.current.Use();
        }
    }

    private void DrawDetailHeader(EnemyMinionDataSO e)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(EnemyAssets.DisplayName(e), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            GUILayout.Label(EnemyAssets.TierName(e.tier) + (e.isPhaseVariant ? " · 페이즈 전환용" : ""), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("프로젝트에서 찾기"), EditorStyles.miniButtonLeft)) EditorGUIUtility.PingObject(e);
            if (GUILayout.Button(new GUIContent("인스펙터", "원본 인스펙터에서 연다(고급)"), EditorStyles.miniButtonMid)) Selection.activeObject = e;
            if (GUILayout.Button(new GUIContent("복제", "이 적을 복사해 새 적을 만든다"), EditorStyles.miniButtonMid))
            { var t = e; Defer(() => NewEnemyPopup.Open(t, OnCreated)); }
            if (GUILayout.Button(new GUIContent("삭제", "참조하는 곳을 확인한 뒤 목록·군집에서 빼고 OS 휴지통으로 옮긴다"), EditorStyles.miniButtonRight))
            { var t = e; Defer(() => { if (EnemyAssets.Delete(t)) { selected = null; renameTarget = null; Reload(); } }); }
        }

        if (renameTarget != e) { renameTarget = e; renameText = e.name; }
        using (new EditorGUILayout.HorizontalScope())
        {
            renameText = EditorGUILayout.TextField(new GUIContent("에셋 파일 이름", "Project 창에 보이는 파일 이름. 게임 화면에는 '게임 표시 이름'이 나온다."), renameText);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(renameText) || renameText == e.name))
                if (GUILayout.Button("바꾸기", EditorStyles.miniButton, GUILayout.Width(50)))
                { var t = e; string to = renameText.Trim(); Defer(() => Rename(t, to)); }
        }

        var ws = EnemyAssets.Warnings(e);
        foreach (var w in ws) EditorGUILayout.HelpBox(w, MessageType.Warning);
        if (EnemyAssets.RegistryMismatch(e) != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("등급에 맞게 목록 분류 고치기", EditorStyles.miniButton, GUILayout.Width(190)))
                { var t = e; Defer(() => EnemyAssets.FixRegistry(t)); }
            }
        }
    }

    private void DrawEnemy(EnemyMinionDataSO e)
    {
        var so = SO(e);
        so.Update();

        // 기본 정보
        EquipmentEditorWindow.Header("기본 정보");
        Prop(so, "minionName", "게임 표시 이름", "체력바·도감 등에 보이는 이름.");
        using (new EditorGUILayout.HorizontalScope())
        {
            var icon = so.FindProperty("minionIcon");
            EditorGUILayout.PropertyField(icon, new GUIContent("아이콘", "얼굴만 있는 작은 이미지"));
            var r = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
            EquipmentEditorWindow.DrawSprite(r, icon.objectReferenceValue as Sprite);
        }
        Prop(so, "tier", "등급", "일반/엘리트/보스. 피해 갈래·엘리트 방 추첨·미니맵 표시가 이 값 하나로 정해진다. 바꾸면 위의 '목록 분류 고치기'로 목록도 맞추세요.");
        if (e.tier != EnemyTier.Normal)
            Prop(so, "isPhaseVariant", "페이즈 전환용 데이터", "켜면 스폰 추첨에서 빠진다. 보스/엘리트가 체력이 바닥났을 때 갈아끼우는 2페이즈 몸 전용.");
        Prop(so, "attackType", "공격 속성", "물리/마법 태그. 데미지는 공격력으로 계산한다.");

        // 능력치
        EquipmentEditorWindow.Header("능력치");
        Prop(so, "maxHP", "최대 체력", "");
        Prop(so, "attack", "공격력", "한 번 공격의 기본 피해.");
        Prop(so, "attackSpeed", "공격 속도 (회/초)", "높을수록 빠르다. 1 = 1초에 1번.");
        Prop(so, "defense", "방어력 (%)", "그 자체가 피해 감소율. 20 = 20% 감소. 75%에서 잘린다.");
        Prop(so, "moveSpeed", "이동 속도", "");
        Prop(so, "attackRange", "공격 사거리 (유닛)", "이 거리 안이면 공격한다.");
        Prop(so, "detectRange", "감지 범위 (유닛)", "이 거리 안의 플레이어를 발견하고 쫓아온다.");
        Prop(so, "baseEvasion", "회피율 (0~1)", "0 = 회피 안 함, 0.1 = 10%.");
        Prop(so, "baseAccuracy", "명중률 (0~1)", "1 = 무조건 맞힘.");
        so.ApplyModifiedProperties();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            float def = Mathf.Clamp(e.defense, 0f, 75f);
            EditorGUILayout.LabelField($"초당 피해 ≈ {EnemyAssets.Dps(e):0.#}   ·   실질 체력 ≈ {EnemyAssets.EffectiveHp(e):0.#} (방어 {def:0.#}% 반영)   ·   공격 간격 {(e.attackSpeed > 0 ? 1f / e.attackSpeed : 0f):0.##}초", EditorStyles.miniLabel);
            var same = all.Where(x => x != e && x.tier == e.tier && !x.isPhaseVariant).ToList();
            if (same.Count > 0)
                EditorGUILayout.LabelField($"같은 등급 평균: 체력 {same.Average(x => x.maxHP):0.#} · 공격력 {same.Average(x => x.attack):0.#} · 초당 피해 {same.Average(EnemyAssets.Dps):0.#} · 이동 {same.Average(x => x.moveSpeed):0.#}", EditorStyles.miniLabel);
        }

        // 등장 위치
        EquipmentEditorWindow.Header("등장 위치");
        DrawSpawnInfo(e);

        // AI 패턴
        so.Update();
        EquipmentEditorWindow.Header("AI 행동 패턴");
        DrawAi(so, e);

        // 프리팹
        so.Update();
        EquipmentEditorWindow.Header("프리팹");
        Prop(so, "minionPrefab", "몬스터 프리팹", "실제로 소환되는 프리팹. 루트에 BaseEntity 계열 컴포넌트가 있어야 이 데이터가 들어간다.");
        if (e.tier != EnemyTier.Normal)
            Prop(so, "dedicatedRoomPrefab", "전용 방 프리팹", "이 엘리트/보스가 나오는 방. 비우면 일반 엘리트 방을 쓴다.");
        so.ApplyModifiedProperties();
        if (e.minionPrefab != null)
        {
            var entity = e.minionPrefab.GetComponent<BaseEntity>();
            if (entity == null) EditorGUILayout.HelpBox("프리팹 루트에 BaseEntity 가 없어 소환해도 능력치·AI 가 들어가지 않습니다.", MessageType.Error);
            else if (!e.minionPrefab.activeSelf) EditorGUILayout.HelpBox("프리팹 루트가 꺼져 있어 로딩 중 미리 준비하지 않습니다.", MessageType.Info);
            if (GUILayout.Button("프리팹 열기", EditorStyles.miniButton, GUILayout.Width(90))) AssetDatabase.OpenAsset(e.minionPrefab);
        }

        // 연출·사운드
        so.Update();
        showFx = EditorGUILayout.Foldout(showFx, "연출 · 사운드", true);
        if (showFx)
            using (new EditorGUI.IndentLevelScope())
            {
                Prop(so, "impactColor", "피격 파편 색", "금속=노랑, 뼈=흰색 등. 방어력과는 무관.");
                Prop(so, "AttackSound", "공격 효과음", "");
            }

        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "고급 (대부분 건드릴 필요 없음)", true);
        if (showAdvanced)
            using (new EditorGUI.IndentLevelScope())
            {
                Prop(so, "minionType", "명령 종류 (CommandData)", "데이터를 종류로 찾을 때 쓰는 키.");
                Prop(so, "baseEffectValue", "투척 효과 수치", "옛 투척 시스템 값. 현재 적에게는 거의 쓰이지 않는다.");
                Prop(so, "shopCost", "상점 가격", "적에게는 쓰이지 않는 공용 칸.");
                Prop(so, "rewardItemData", "보상 표시 데이터", "적에게는 쓰이지 않는 공용 칸.");
            }
        so.ApplyModifiedProperties();
    }

    private void DrawAi(SerializedObject so, EnemyMinionDataSO e)
    {
        Prop(so, "aiPattern", "AI 패턴 에셋", "대기·추격·공격을 정하는 행동 패턴. 패턴마다 수치가 다르다.");
        so.ApplyModifiedProperties();
        var pattern = e.aiPattern;
        if (pattern == null)
        {
            EditorGUILayout.HelpBox("AI 패턴이 없습니다. 게임 기본 패턴(DataManager)이 쓰이거나 움직이지 않을 수 있습니다.", MessageType.Warning);
            return;
        }

        var users = EnemyAssets.PatternUsers(pattern);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"패턴 종류: {ObjectNames.NicifyVariableName(pattern.GetType().Name.Replace("SO", ""))}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("이 적 전용으로 복사", "패턴 에셋을 복사해서 이 적에만 연결한다. 다른 적에 영향 없이 고칠 수 있다."), EditorStyles.miniButton, GUILayout.Width(130)))
            { var t = e; Defer(() => EnemyAssets.MakePatternUnique(t)); }
        }
        if (users.Count > 1)
            EditorGUILayout.HelpBox($"이 패턴을 {users.Count}종이 함께 씁니다: {string.Join(", ", users.Select(EnemyAssets.DisplayName))}\n아래 수치를 고치면 모두 같이 바뀝니다. 이 적만 바꾸려면 '이 적 전용으로 복사'를 누르세요.", MessageType.Info);

        showAi = EditorGUILayout.Foldout(showAi, "패턴 수치 편집", true);
        if (!showAi) return;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            Editor.CreateCachedEditor(pattern, null, ref patternEditor);
            if (patternEditor != null) patternEditor.OnInspectorGUI();
        }
    }

    private void DrawSpawnInfo(EnemyMinionDataSO e)
    {
        var reg = EnemyAssets.MinionRegistry;
        if (e.isPhaseVariant)
        {
            var owners = EnemyAssets.PhaseOwners(e);
            EditorGUILayout.HelpBox("페이즈 전환용 데이터라 스폰 추첨에 들어가지 않습니다."
                + (owners.Count > 0 ? "\n이 데이터를 2페이즈로 쓰는 곳: " + string.Join(", ", owners) : "\n이 데이터를 2페이즈로 연결한 AI 패턴·컨트롤러를 찾지 못했습니다."), MessageType.None);
            return;
        }

        switch (e.tier)
        {
            case EnemyTier.Normal:
                DrawClusterMembership(e);
                break;

            case EnemyTier.Elite:
                using (new EditorGUI.DisabledScope(reg == null))
                {
                    bool inList = reg != null && reg.eliteMinionData.Contains(e);
                    bool n = EditorGUILayout.ToggleLeft(new GUIContent("층 엘리트 후보에 등록" + (reg != null ? $" ({reg.name} 엘리트 목록)" : ""),
                        "엘리트 방이 이 목록에서 엘리트를 뽑는다. 빠지면 게임에 나오지 않는다."), inList);
                    if (n != inList) EnemyAssets.SetIn(reg, reg.eliteMinionData, e, n);
                }
                if (reg != null)
                {
                    var others = reg.eliteMinionData.Where(x => x != null && x != e).Select(EnemyAssets.DisplayName).ToList();
                    EditorGUILayout.LabelField("현재 엘리트 후보: " + (others.Count + (reg.eliteMinionData.Contains(e) ? 1 : 0)) + "종"
                        + (others.Count > 0 ? " (그 외: " + string.Join(", ", others) + ")" : ""), EditorStyles.miniLabel);
                }
                DrawClusterMembership(e, compact: true);
                break;

            case EnemyTier.Boss:
                DrawBossFloors(e);
                break;
        }
    }

    private void DrawClusterMembership(EnemyMinionDataSO e, bool compact = false)
    {
        var registry = EnemyAssets.ClusterRegistry;
        var inClusters = clusters.Where(c => c.enemies != null && c.enemies.Any(x => x.enemyData == e)).ToList();
        if (compact && inClusters.Count == 0) return;

        EditorGUILayout.LabelField(compact ? "일반 군집에도 들어 있음" : "이 적이 들어 있는 군집 (일반 방 웨이브)", EditorStyles.miniBoldLabel);
        if (inClusters.Count == 0)
            EditorGUILayout.HelpBox("어떤 군집에도 없어 일반 방 랜덤 웨이브에 나오지 않습니다. (튜토리얼 직접 배치용이면 괜찮습니다)", MessageType.Info);

        foreach (var c in inClusters)
        {
            var cso = SO(c);
            cso.Update();
            var arr = cso.FindProperty("enemies");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                if (el.FindPropertyRelative("enemyData").objectReferenceValue != e) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool registered = registry != null && registry.enemyClusters.Contains(c);
                    GUILayout.Label(new GUIContent((registered ? "" : "⚠ ") + EnemyAssets.ClusterName(c),
                        registered ? EnemyAssets.ClusterFloorsText(c) : "전체 군집 목록에 등록되지 않은 군집입니다(층 조절표에 직접 넣은 층에서만 나옴)."), GUILayout.Width(200));
                    EditorGUILayout.LabelField("마릿수", GUILayout.Width(40));
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("count"), GUIContent.none, GUILayout.Width(50));
                    GUILayout.Label($"/ 군집 전체 {c.enemies.Sum(x => Mathf.Max(0, x.count))}마리", EditorStyles.miniLabel, GUILayout.Width(110));
                    if (GUILayout.Button("군집 열기", EditorStyles.miniButtonLeft, GUILayout.Width(64)))
                    { var t = c; Defer(() => { selectedCluster = t; view = View.Cluster; }); }
                    int idx = i;
                    if (GUILayout.Button(new GUIContent("빼기", "이 군집에서 이 적을 뺀다"), EditorStyles.miniButtonRight, GUILayout.Width(40)))
                    { var t = c; Defer(() => EditArray(t, "enemies", a => a.DeleteArrayElementAtIndex(idx), "군집에서 적 빼기")); }
                }
            }
            cso.ApplyModifiedProperties();
        }

        if (GUILayout.Button("+ 군집에 넣기…", EditorStyles.miniButton, GUILayout.Width(110)))
        {
            var menu = new GenericMenu();
            foreach (var c in clusters)
            {
                var t = c;
                bool has = c.enemies != null && c.enemies.Any(x => x.enemyData == e);
                if (has) menu.AddDisabledItem(new GUIContent(EnemyAssets.ClusterName(c) + " (이미 있음)"));
                else menu.AddItem(new GUIContent(EnemyAssets.ClusterName(c)), false, () => Defer(() => EnemyAssets.AddToCluster(t, e, 1)));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("새 군집 만들어서 넣기"), false, () => Defer(() =>
            {
                var c = EnemyAssets.CreateCluster(EnemyAssets.DisplayName(e) + " 군집", true);
                if (c != null) { EnemyAssets.AddToCluster(c, e, 3); Reload(); }
            }));
            menu.ShowAsContext();
        }
    }

    private void DrawBossFloors(EnemyMinionDataSO e)
    {
        var map = EnemyAssets.MapData;
        if (map == null) { EditorGUILayout.HelpBox("MapGenerationDataSO 를 찾지 못해 층 배정을 볼 수 없습니다.", MessageType.Warning); return; }

        var mso = SO(map);
        mso.Update();
        var arr = mso.FindProperty("floorBosses");
        bool any = false;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            if (el.FindPropertyRelative("boss").objectReferenceValue != e) continue;
            any = true;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("보스로 나오는 층", GUILayout.Width(100));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("floor"), GUIContent.none, GUILayout.Width(50));
                GUILayout.Label("층", GUILayout.Width(20));
                int idx = i;
                if (GUILayout.Button("배정 해제", EditorStyles.miniButton, GUILayout.Width(70)))
                    Defer(() => EditArray(map, "floorBosses", a => a.DeleteArrayElementAtIndex(idx), "보스 층 배정 해제"));
            }
        }
        mso.ApplyModifiedProperties();
        if (!any) EditorGUILayout.HelpBox($"어느 층에도 배정되지 않았습니다. ({map.name} 의 층수별 보스)", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            newBossFloor = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("층 배정 추가"), newBossFloor, GUILayout.Width(220)));
            if (GUILayout.Button("이 층 보스로 지정", EditorStyles.miniButton, GUILayout.Width(120)))
            {
                int floor = newBossFloor; var t = e;
                Defer(() => EnemyAssets.AssignBossFloor(map, t, floor));
            }
        }
        var others = map.floorBosses.Where(f => f != null && f.boss != e).OrderBy(f => f.floor).Select(f => $"{f.floor}층 {EnemyAssets.DisplayName(f.boss)}").ToList();
        if (others.Count > 0) EditorGUILayout.LabelField("다른 층 보스: " + string.Join(", ", others), EditorStyles.miniLabel);
    }

    private void EditArray(UnityEngine.Object target, string path, Action<SerializedProperty> edit, string undoName)
    {
        if (target == null) return;
        var so = SO(target);
        so.Update();
        var p = so.FindProperty(path);
        if (p == null) return;
        edit(p);
        so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName(undoName);
        EndTextEditing();
    }

    // ── 능력치 표 ─────────────────────────────────────────────────────
    private static readonly (string title, float width, string tip)[] Columns =
    {
        ("", 52, ""),
        ("이름 / 에셋", 150, ""),
        ("등급", 70, ""),
        ("체력", 60, "최대 체력"),
        ("공격력", 56, ""),
        ("공속", 50, "공격 속도 (회/초)"),
        ("초당 피해", 60, "공격력 × 공격 속도 (자동 계산)"),
        ("방어%", 50, "피해 감소율(%)"),
        ("실질 체력", 62, "방어력을 반영한 체력 (자동 계산)"),
        ("이동", 50, "이동 속도"),
        ("사거리", 50, "공격 사거리"),
        ("감지", 50, "감지 범위"),
        ("AI 패턴", 150, ""),
        ("확인", 200, ""),
    };

    private void DrawTable(List<EnemyMinionDataSO> list)
    {
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
                    var r = GUILayoutUtility.GetRect(44, 36, GUILayout.Width(44), GUILayout.Height(36));
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), EnemyAssets.TierColor(e.tier));
                    EquipmentEditorWindow.DrawSprite(new Rect(r.x + 6, r.y + 2, 32, 32), e.minionIcon);
                    if (GUILayout.Button("편집", EditorStyles.miniButton, GUILayout.Width(44)))
                    { var t = e; Defer(() => { selected = t; view = View.Detail; }); }
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[1].width)))
                {
                    EditorGUILayout.PropertyField(so.FindProperty("minionName"), GUIContent.none, GUILayout.Width(Columns[1].width));
                    EditorGUILayout.LabelField(new GUIContent(e.name + (e.isPhaseVariant ? " (페이즈)" : ""), "에셋 파일 이름"), EditorStyles.miniLabel, GUILayout.Width(Columns[1].width));
                }
                EditorGUILayout.PropertyField(so.FindProperty("tier"), GUIContent.none, GUILayout.Width(Columns[2].width));
                EditorGUILayout.PropertyField(so.FindProperty("maxHP"), GUIContent.none, GUILayout.Width(Columns[3].width));
                EditorGUILayout.PropertyField(so.FindProperty("attack"), GUIContent.none, GUILayout.Width(Columns[4].width));
                EditorGUILayout.PropertyField(so.FindProperty("attackSpeed"), GUIContent.none, GUILayout.Width(Columns[5].width));
                GUILayout.Label(EnemyAssets.Dps(e).ToString("0.#"), GUILayout.Width(Columns[6].width));
                EditorGUILayout.PropertyField(so.FindProperty("defense"), GUIContent.none, GUILayout.Width(Columns[7].width));
                GUILayout.Label(EnemyAssets.EffectiveHp(e).ToString("0.#"), GUILayout.Width(Columns[8].width));
                EditorGUILayout.PropertyField(so.FindProperty("moveSpeed"), GUIContent.none, GUILayout.Width(Columns[9].width));
                EditorGUILayout.PropertyField(so.FindProperty("attackRange"), GUIContent.none, GUILayout.Width(Columns[10].width));
                EditorGUILayout.PropertyField(so.FindProperty("detectRange"), GUIContent.none, GUILayout.Width(Columns[11].width));
                EditorGUILayout.PropertyField(so.FindProperty("aiPattern"), GUIContent.none, GUILayout.Width(Columns[12].width));
                so.ApplyModifiedProperties();
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[13].width)))
                {
                    var ws = EnemyAssets.Warnings(e);
                    if (ws.Count == 0) EditorGUILayout.LabelField("✓", EditorStyles.miniLabel, GUILayout.Width(Columns[13].width));
                    foreach (var w in ws) EditorGUILayout.LabelField(new GUIContent("⚠ " + w.Split('\n')[0], w), EnemyAssets.WrapMini, GUILayout.Width(Columns[13].width));
                }
            }
        }

        // 등급별 평균
        EditorGUILayout.Space(6);
        foreach (EnemyTier t in Enum.GetValues(typeof(EnemyTier)))
        {
            var g = list.Where(x => x.tier == t && !x.isPhaseVariant).ToList();
            if (g.Count == 0) continue;
            EditorGUILayout.LabelField($"{EnemyAssets.TierName(t)} 평균 ({g.Count}종): 체력 {g.Average(x => x.maxHP):0.#} · 공격력 {g.Average(x => x.attack):0.#} · 초당 피해 {g.Average(EnemyAssets.Dps):0.#} · 방어 {g.Average(x => x.defense):0.#}% · 이동 {g.Average(x => x.moveSpeed):0.#}", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    // ── 군집 편집 ─────────────────────────────────────────────────────
    private void DrawClusters()
    {
        var registry = EnemyAssets.ClusterRegistry;
        if (registry == null) EditorGUILayout.HelpBox("EnemyClusterRegistrySO 를 찾지 못했습니다. 일반 방 웨이브가 군집을 찾지 못합니다.", MessageType.Warning);
        if (selectedCluster == null && clusters.Count > 0) selectedCluster = clusters[0];

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
            {
                if (GUILayout.Button("+ 새 군집"))
                    Defer(() => { var c = EnemyAssets.CreateCluster("새 군집", true); if (c != null) { Reload(); selectedCluster = c; } });
                clusterListScroll = EditorGUILayout.BeginScrollView(clusterListScroll);
                foreach (var c in clusters)
                {
                    var r = GUILayoutUtility.GetRect(10, 24, GUILayout.ExpandWidth(true));
                    if (c == selectedCluster) EditorGUI.DrawRect(r, new Color(0.24f, 0.48f, 0.90f, 0.35f));
                    bool reg = registry != null && registry.enemyClusters.Contains(c);
                    int total = c.enemies?.Sum(x => Mathf.Max(0, x.count)) ?? 0;
                    GUI.Label(new Rect(r.x + 6, r.y + 3, r.width - 12, 18), new GUIContent($"{(reg ? "●" : "○")} {EnemyAssets.ClusterName(c)}  ({total}마리)", reg ? "전체 군집 목록에 등록됨" : "전체 군집 목록에 없음"));
                    if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                    { var t = c; Defer(() => selectedCluster = t); Event.current.Use(); }
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.LabelField("● = 전체 군집 목록에 등록", EditorStyles.miniLabel);
            }

            var div = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(div, new Color(0.5f, 0.5f, 0.5f, 0.4f));

            using (new EditorGUILayout.VerticalScope())
            {
                if (selectedCluster == null) { EditorGUILayout.HelpBox("군집이 없습니다. '+ 새 군집'으로 만드세요.", MessageType.Info); return; }
                clusterScroll = EditorGUILayout.BeginScrollView(clusterScroll);
                DrawCluster(selectedCluster, registry);
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawCluster(EnemyClusterSO c, EnemyClusterRegistrySO registry)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(EnemyAssets.ClusterName(c), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("프로젝트에서 찾기", EditorStyles.miniButtonLeft)) EditorGUIUtility.PingObject(c);
            if (GUILayout.Button("복제", EditorStyles.miniButtonMid))
            { var t = c; Defer(() => { var n = EnemyAssets.DuplicateCluster(t); if (n != null) { Reload(); selectedCluster = n; } }); }
            if (GUILayout.Button("삭제", EditorStyles.miniButtonRight))
            { var t = c; Defer(() => { if (EnemyAssets.DeleteCluster(t)) { selectedCluster = null; Reload(); } }); }
        }

        var so = SO(c);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("clusterName"), new GUIContent("군집 이름", "편집기 표시용"));

        using (new EditorGUI.DisabledScope(registry == null))
        {
            bool reg = registry != null && registry.enemyClusters.Contains(c);
            bool n = EditorGUILayout.ToggleLeft(new GUIContent("전체 군집 목록에 등록" + (registry != null ? $" ({registry.name})" : ""),
                "층 조절표에 군집 목록이 없는 층은 이 전체 목록에서 무작위로 뽑는다."), reg);
            if (n != reg) EnemyAssets.SetIn(registry, registry.enemyClusters, c, n);
        }
        EditorGUILayout.LabelField(EnemyAssets.ClusterFloorsText(c), EnemyAssets.WrapMini);

        EquipmentEditorWindow.Header("구성 (웨이브 하나에 이만큼 나온다)");
        var arr = so.FindProperty("enemies");
        int total = 0;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            var dataProp = el.FindPropertyRelative("enemyData");
            var countProp = el.FindPropertyRelative("count");
            var data = dataProp.objectReferenceValue as EnemyMinionDataSO;
            total += Mathf.Max(0, countProp.intValue);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var r = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                if (data != null) EquipmentEditorWindow.DrawSprite(r, data.minionIcon);
                EditorGUILayout.PropertyField(dataProp, GUIContent.none, GUILayout.MinWidth(160));
                EditorGUILayout.LabelField("마릿수", GUILayout.Width(40));
                EditorGUILayout.PropertyField(countProp, GUIContent.none, GUILayout.Width(50));
                if (data != null)
                {
                    GUILayout.Label($"{EnemyAssets.TierName(data.tier)} · 체력 {data.maxHP:0} · 공격 {data.attack:0.#}", EditorStyles.miniLabel, GUILayout.Width(170));
                    if (GUILayout.Button("적 열기", EditorStyles.miniButtonLeft, GUILayout.Width(52)))
                    { var t = data; Defer(() => { selected = t; view = View.Detail; }); }
                }
                int idx = i;
                if (GUILayout.Button("✕", data != null ? EditorStyles.miniButtonRight : EditorStyles.miniButton, GUILayout.Width(24)))
                { var t = c; Defer(() => EditArray(t, "enemies", a => a.DeleteArrayElementAtIndex(idx), "군집에서 적 빼기")); }
            }
            if (data == null) EditorGUILayout.HelpBox("빈 칸입니다. 적을 고르거나 지우세요.", MessageType.Warning);
            else if (data.isPhaseVariant) EditorGUILayout.HelpBox($"{EnemyAssets.DisplayName(data)} 은(는) 페이즈 전환용 데이터입니다. 군집에 넣으면 2페이즈 몸으로 바로 나옵니다.", MessageType.Warning);
            else if (data.tier != EnemyTier.Normal) EditorGUILayout.HelpBox($"{EnemyAssets.DisplayName(data)} 은(는) {EnemyAssets.TierName(data.tier)} 등급입니다. 일반 웨이브에 섞는 게 의도인지 확인하세요.", MessageType.Info);
            if (countProp.intValue <= 0) EditorGUILayout.HelpBox("마릿수가 0 이하라 나오지 않습니다.", MessageType.Warning);
        }
        so.ApplyModifiedProperties();

        if (GUILayout.Button("+ 적 추가", GUILayout.Width(90)))
        {
            var menu = new GenericMenu();
            foreach (var e in all.Where(x => !x.isPhaseVariant).OrderBy(x => (int)x.tier).ThenBy(EnemyAssets.DisplayName))
            {
                var t = e;
                menu.AddItem(new GUIContent($"{EnemyAssets.TierName(e.tier)}/{EnemyAssets.DisplayName(e)}"), false, () => Defer(() => EnemyAssets.AddToCluster(c, t, 1)));
            }
            menu.ShowAsContext();
        }

        var map = EnemyAssets.MapData;
        float scaleMin = 1f, scaleMax = 1f;
        if (map != null && map.floorTuning != null && map.floorTuning.Count > 0)
        {
            scaleMin = Mathf.Min(1f, map.floorTuning.Min(f => f.enemyCountScale));
            scaleMax = Mathf.Max(1f, map.floorTuning.Max(f => f.enemyCountScale));
        }
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"웨이브당 총 {total}마리" + (scaleMin != 1f || scaleMax != 1f ? $" (층 마릿수 배율 적용 시 약 {Mathf.CeilToInt(total * scaleMin)}~{Mathf.CeilToInt(total * scaleMax)}마리)" : ""), EditorStyles.boldLabel);
        var valid = c.enemies?.Where(x => x.enemyData != null && x.count > 0).ToList() ?? new List<EnemyCount>();
        if (valid.Count > 0)
        {
            float hp = valid.Sum(x => x.enemyData.maxHP * x.count);
            float dps = valid.Sum(x => EnemyAssets.Dps(x.enemyData) * x.count);
            EditorGUILayout.LabelField($"웨이브 합계: 체력 {hp:0} · 초당 피해 {dps:0.#} (모두 동시에 공격할 때)", EditorStyles.miniLabel);
        }
    }

    // ── 에셋 작업 ─────────────────────────────────────────────────────
    private void OnCreated(EnemyMinionDataSO e)
    {
        Reload();
        search = ""; onlyWarnings = false; tierFilter = 0;
        selected = e; view = View.Detail;
        EditorGUIUtility.PingObject(e);
        Focus();
        Repaint();
    }

    private void Rename(EnemyMinionDataSO e, string newName)
    {
        string err = EnemyAssets.ValidateName(newName, e);
        if (err != null) { EditorUtility.DisplayDialog("이름 변경 실패", err, "확인"); return; }
        string r = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(e), newName);
        if (!string.IsNullOrEmpty(r)) { EditorUtility.DisplayDialog("이름 변경 실패", r, "확인"); return; }
        AssetDatabase.SaveAssets();
        renameTarget = null;
    }

    private static void Prop(SerializedObject so, string name, string label, string tip)
    {
        var p = so.FindProperty(name);
        if (p == null) { EditorGUILayout.LabelField(label, $"(필드 '{name}' 없음)"); return; }
        EditorGUILayout.PropertyField(p, new GUIContent(label, tip), true);
    }
}

/// <summary>적 데이터 · 군집 · 레지스트리 조회와 생성/삭제.</summary>
public static class EnemyAssets
{
    public const string DefaultFolder = "Assets/SOData/Enemy/Enemy Minion Data";
    public const string ClusterFolder = "Assets/SOData/Enemy/Enemy Clusters";

    public static List<EnemyMinionDataSO> All() => AssetDatabase.FindAssets("t:EnemyMinionDataSO")
        .Select(g => AssetDatabase.LoadAssetAtPath<EnemyMinionDataSO>(AssetDatabase.GUIDToAssetPath(g)))
        .Where(e => e != null && !AssetDatabase.GetAssetPath(e).Contains("/Deprecated/")).ToList();

    public static List<EnemyClusterSO> AllClusters() => AssetDatabase.FindAssets("t:EnemyClusterSO")
        .Select(g => AssetDatabase.LoadAssetAtPath<EnemyClusterSO>(AssetDatabase.GUIDToAssetPath(g)))
        .Where(c => c != null).OrderBy(ClusterName, StringComparer.Ordinal).ToList();

    public static string DisplayName(EnemyMinionDataSO e) => e == null ? "(없음)" : string.IsNullOrWhiteSpace(e.minionName) ? e.name : e.minionName;
    public static string ClusterName(EnemyClusterSO c) => c == null ? "(없음)" : string.IsNullOrWhiteSpace(c.clusterName) ? c.name : c.clusterName;

    public static string TierName(EnemyTier t) => t == EnemyTier.Boss ? "보스" : t == EnemyTier.Elite ? "엘리트" : "일반";
    public static Color TierColor(EnemyTier t) => t == EnemyTier.Boss ? new Color(1f, 0.35f, 0.3f) : t == EnemyTier.Elite ? new Color(0.75f, 0.45f, 1f) : new Color(0.7f, 0.7f, 0.7f);

    public static float Dps(EnemyMinionDataSO e) => e == null ? 0f : Mathf.Max(0f, e.attack) * Mathf.Max(0f, e.attackSpeed);
    public static float EffectiveHp(EnemyMinionDataSO e) => e == null ? 0f : e.maxHP / (1f - Mathf.Clamp(e.defense, 0f, 75f) / 100f);

    private static GUIStyle wrapMini;
    public static GUIStyle WrapMini => wrapMini ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

    // ── 레지스트리 ────────────────────────────────────────────────────
    private static MinionRegistrySO minionRegistry;
    private static EnemyClusterRegistrySO clusterRegistry;
    private static MapGenerationDataSO mapData;
    public static MinionRegistrySO MinionRegistry => minionRegistry != null ? minionRegistry : (minionRegistry = LoadFirst<MinionRegistrySO>());
    public static EnemyClusterRegistrySO ClusterRegistry => clusterRegistry != null ? clusterRegistry : (clusterRegistry = LoadFirst<EnemyClusterRegistrySO>());
    public static MapGenerationDataSO MapData => mapData != null ? mapData : (mapData = LoadFirst<MapGenerationDataSO>());

    private static T LoadFirst<T>() where T : UnityEngine.Object
    {
        var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    public static void SetIn<T>(UnityEngine.Object registry, List<T> list, T item, bool include) where T : UnityEngine.Object
    {
        if (registry == null || list == null || item == null || list.Contains(item) == include) return;
        Undo.RecordObject(registry, include ? "목록 등록" : "목록 등록 해제");
        if (include) list.Add(item); else list.RemoveAll(x => x == item);
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssetIfDirty(registry);
        InvalidateUsage();
    }

    // ── 사용처 캐시 ───────────────────────────────────────────────────
    private static Dictionary<AIPatternSO, List<EnemyMinionDataSO>> patternUsers;
    private static Dictionary<EnemyMinionDataSO, List<string>> phaseOwners;
    private static readonly Dictionary<EnemyMinionDataSO, List<string>> warningCache = new Dictionary<EnemyMinionDataSO, List<string>>();
    private static double warningStamp;

    public static void InvalidateUsage()
    {
        patternUsers = null;
        phaseOwners = null;
        warningCache.Clear();
    }

    public static List<EnemyMinionDataSO> PatternUsers(AIPatternSO p)
    {
        if (patternUsers == null)
        {
            patternUsers = new Dictionary<AIPatternSO, List<EnemyMinionDataSO>>();
            foreach (var e in All())
            {
                if (e.aiPattern == null) continue;
                if (!patternUsers.TryGetValue(e.aiPattern, out var l)) patternUsers[e.aiPattern] = l = new List<EnemyMinionDataSO>();
                l.Add(e);
            }
        }
        return p != null && patternUsers.TryGetValue(p, out var list) ? list : new List<EnemyMinionDataSO>();
    }

    /// <summary>이 데이터를 2페이즈 등으로 참조하는 AI 패턴 에셋(직렬화된 EnemyMinionDataSO 필드)을 찾는다.</summary>
    public static List<string> PhaseOwners(EnemyMinionDataSO e)
    {
        if (phaseOwners == null)
        {
            phaseOwners = new Dictionary<EnemyMinionDataSO, List<string>>();
            foreach (var g in AssetDatabase.FindAssets("t:AIPatternSO"))
            {
                var p = AssetDatabase.LoadAssetAtPath<AIPatternSO>(AssetDatabase.GUIDToAssetPath(g));
                if (p == null) continue;
                using (var so = new SerializedObject(p))
                {
                    var it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (it.objectReferenceValue is EnemyMinionDataSO target)
                        {
                            if (!phaseOwners.TryGetValue(target, out var l)) phaseOwners[target] = l = new List<string>();
                            l.Add($"{p.name} ({ObjectNames.NicifyVariableName(it.name)})");
                        }
                    }
                }
            }
        }
        return e != null && phaseOwners.TryGetValue(e, out var list) ? list : new List<string>();
    }

    public static string ClusterFloorsText(EnemyClusterSO c)
    {
        var map = MapData;
        var reg = ClusterRegistry;
        bool registered = reg != null && reg.enemyClusters.Contains(c);
        if (map == null) return registered ? "전체 군집 목록에 등록됨." : "전체 군집 목록에 없음.";
        var tuned = map.floorTuning?.Where(f => f != null && f.clusterPool != null && f.clusterPool.Any(x => x != null)).ToList() ?? new List<MapGenerationDataSO.FloorTuningEntry>();
        var explicitFloors = tuned.Where(f => f.clusterPool.Contains(c)).Select(f => f.floor).OrderBy(x => x).ToList();
        var restricted = tuned.Select(f => f.floor).OrderBy(x => x).ToList();
        var sb = new StringBuilder();
        if (explicitFloors.Count > 0) sb.Append($"층 조절표에서 지정된 층: {string.Join(", ", explicitFloors)}층. ");
        if (registered)
        {
            sb.Append("층 조절표에 군집 목록이 없는 층에서 무작위로 나옵니다");
            if (restricted.Count > 0) sb.Append($" ({string.Join(", ", restricted)}층은 지정 목록만 사용)");
            sb.Append('.');
        }
        else if (explicitFloors.Count == 0) sb.Append("전체 목록에도, 층 조절표에도 없어 게임에 나오지 않습니다.");
        return sb.ToString();
    }

    // ── 검사 ──────────────────────────────────────────────────────────
    public static string RegistryMismatch(EnemyMinionDataSO e)
    {
        var reg = MinionRegistry;
        if (reg == null || e == null) return null;
        bool inNormal = reg.enemyMinionData != null && reg.enemyMinionData.Contains(e);
        bool inBoss = reg.bossMinionData != null && reg.bossMinionData.Contains(e);
        bool inElite = reg.eliteMinionData != null && reg.eliteMinionData.Contains(e);
        switch (e.tier)
        {
            case EnemyTier.Normal:
                if (!inNormal) return $"{reg.name} 일반 적 목록에 없습니다.";
                if (inBoss || inElite) return $"일반 등급인데 {reg.name} 의 엘리트/보스 목록에 들어 있습니다.";
                break;
            case EnemyTier.Elite:
                if (inNormal || inBoss) return $"엘리트 등급인데 {reg.name} 의 일반/보스 목록에 들어 있습니다.";
                if (inElite && e.isPhaseVariant) return "페이즈 전환용 데이터가 엘리트 후보 목록에 들어 있습니다(2페이즈 몸으로 시작함).";
                break;
            case EnemyTier.Boss:
                if (!inBoss) return $"{reg.name} 보스 목록에 없습니다.";
                if (inNormal || inElite) return $"보스 등급인데 {reg.name} 의 일반/엘리트 목록에 들어 있습니다.";
                break;
        }
        return null;
    }

    public static void FixRegistry(EnemyMinionDataSO e)
    {
        var reg = MinionRegistry;
        if (reg == null || e == null) return;
        Undo.RecordObject(reg, "적 목록 분류 고치기");
        reg.enemyMinionData.RemoveAll(x => x == e);
        reg.bossMinionData.RemoveAll(x => x == e);
        if (e.tier != EnemyTier.Elite || e.isPhaseVariant) reg.eliteMinionData.RemoveAll(x => x == e);
        if (e.tier == EnemyTier.Normal) reg.enemyMinionData.Add(e);
        if (e.tier == EnemyTier.Boss) reg.bossMinionData.Add(e);
        EditorUtility.SetDirty(reg);
        AssetDatabase.SaveAssetIfDirty(reg);
        InvalidateUsage();
    }

    public static List<string> Warnings(EnemyMinionDataSO e)
    {
        if (e == null) return new List<string>();
        if (EditorApplication.timeSinceStartup - warningStamp > 1.0) { warningCache.Clear(); warningStamp = EditorApplication.timeSinceStartup; }
        if (warningCache.TryGetValue(e, out var cached)) return cached;

        var w = new List<string>();
        if (string.IsNullOrWhiteSpace(e.minionName)) w.Add("게임 표시 이름이 비어 있습니다.");
        if (e.minionIcon == null) w.Add("아이콘이 없습니다.");
        if (e.minionPrefab == null) w.Add("프리팹이 없어 소환되지 않습니다.");
        else if (e.minionPrefab.GetComponent<BaseEntity>() == null) w.Add("프리팹 루트에 BaseEntity 가 없습니다.");
        if (e.aiPattern == null) w.Add("AI 패턴이 없습니다.");
        if (e.maxHP <= 0) w.Add("최대 체력이 0 이하입니다.");
        if (e.attackSpeed <= 0) w.Add("공격 속도가 0 이하라 공격하지 않습니다.");
        if (e.attack < 0) w.Add("공격력이 음수입니다.");
        if (e.defense > 75) w.Add($"방어력 {e.defense}% — 75%에서 잘립니다.");
        if (e.detectRange < e.attackRange) w.Add("감지 범위가 공격 사거리보다 짧습니다(발견 전에 공격 거리에 들어옴).");
        if (e.baseAccuracy <= 0) w.Add("명중률이 0 이라 절대 맞히지 못합니다.");
        string mismatch = RegistryMismatch(e);
        if (mismatch != null) w.Add(mismatch);

        if (!e.isPhaseVariant)
        {
            var reg = MinionRegistry;
            if (e.tier == EnemyTier.Elite && reg != null && !reg.eliteMinionData.Contains(e))
                w.Add("엘리트 후보 목록에 없어 엘리트 방에 나오지 않습니다.");
            if (e.tier == EnemyTier.Boss && MapData != null && !MapData.floorBosses.Any(f => f != null && f.boss == e))
                w.Add("어느 층 보스로도 배정되지 않았습니다.");
        }
        warningCache[e] = w;
        return w;
    }

    public static string ValidateName(string name, EnemyMinionDataSO self = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return "에셋 파일 이름을 입력하세요.";
        name = name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            return "파일 이름에 쓸 수 없는 문자가 있습니다.";
        if (name.StartsWith(".") || name.EndsWith(".")) return "점(.)으로 시작하거나 끝날 수 없습니다.";
        if (All().Any(e => e != self && string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase)))
            return $"'{name}' 적이 이미 있습니다.";
        return null;
    }

    public static string UniqueName(string baseName)
    {
        var used = new HashSet<string>(All().Select(e => e.name), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName)) return baseName;
        for (int n = 2; ; n++) if (!used.Contains(baseName + " " + n)) return baseName + " " + n;
    }

    public static string FolderFor(EnemyTier tier)
    {
        string f = tier == EnemyTier.Boss ? DefaultFolder + "/Boss" : tier == EnemyTier.Elite ? DefaultFolder + "/Elites" : DefaultFolder;
        if (AssetDatabase.IsValidFolder(f)) return f;
        return AssetDatabase.IsValidFolder(DefaultFolder) ? DefaultFolder : "Assets";
    }

    // ── 생성 ──────────────────────────────────────────────────────────
    public sealed class Request
    {
        public string assetName = "New Enemy";
        public string folder = DefaultFolder;
        public EnemyMinionDataSO copyFrom;
        public string displayName = "새 적";
        public EnemyTier tier = EnemyTier.Normal;
        public AIPatternSO aiPattern;
        public GameObject prefab;
        public bool registerList = true;          // 등급에 맞는 MinionRegistry 목록(일반/보스) 또는 엘리트 후보
        public EnemyClusterSO addToCluster;       // 일반만
        public int clusterCount = 2;
    }

    public static EnemyMinionDataSO Create(Request r, out string error)
    {
        error = ValidateName(r.assetName);
        if (error != null) return null;
        string name = r.assetName.Trim();
        string folder = string.IsNullOrEmpty(r.folder) || !AssetDatabase.IsValidFolder(r.folder) ? FolderFor(r.tier) : r.folder;
        string path = $"{folder}/{name}.asset";
        if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)) { error = $"{path} 에 이미 파일이 있습니다."; return null; }

        EnemyMinionDataSO e;
        if (r.copyFrom != null)
        {
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(r.copyFrom), path)) { error = "복사에 실패했습니다."; return null; }
            e = AssetDatabase.LoadAssetAtPath<EnemyMinionDataSO>(path);
        }
        else
        {
            e = ScriptableObject.CreateInstance<EnemyMinionDataSO>();
            AssetDatabase.CreateAsset(e, path);
        }
        e.minionName = r.displayName ?? "";
        e.tier = r.tier;
        e.isPhaseVariant = r.copyFrom != null && r.tier != EnemyTier.Normal && r.copyFrom.isPhaseVariant;
        if (r.aiPattern != null) e.aiPattern = r.aiPattern;
        if (r.prefab != null) e.minionPrefab = r.prefab;
        EditorUtility.SetDirty(e);

        var reg = MinionRegistry;
        if (r.registerList && reg != null)
        {
            if (r.tier == EnemyTier.Elite) SetIn(reg, reg.eliteMinionData, e, true);
            else FixRegistry(e);
        }
        if (r.tier == EnemyTier.Normal && r.addToCluster != null) AddToCluster(r.addToCluster, e, Mathf.Max(1, r.clusterCount));
        AssetDatabase.SaveAssets();
        InvalidateUsage();
        return e;
    }

    public static void MakePatternUnique(EnemyMinionDataSO e)
    {
        if (e == null || e.aiPattern == null) return;
        string src = AssetDatabase.GetAssetPath(e.aiPattern);
        string dir = Path.GetDirectoryName(src)?.Replace('\\', '/');
        string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{e.name} Pattern.asset");
        if (!AssetDatabase.CopyAsset(src, path)) { EditorUtility.DisplayDialog("패턴 복사", "복사에 실패했습니다.", "확인"); return; }
        var copy = AssetDatabase.LoadAssetAtPath<AIPatternSO>(path);
        Undo.RecordObject(e, "AI 패턴 전용 복사");
        e.aiPattern = copy;
        EditorUtility.SetDirty(e);
        AssetDatabase.SaveAssets();
        InvalidateUsage();
    }

    public static void AssignBossFloor(MapGenerationDataSO map, EnemyMinionDataSO e, int floor)
    {
        if (map == null || e == null) return;
        var existing = map.floorBosses.FirstOrDefault(f => f != null && f.floor == floor);
        if (existing != null && existing.boss == e) return;
        if (existing != null && existing.boss != null &&
            !EditorUtility.DisplayDialog("보스 층 배정", $"{floor}층에는 이미 '{DisplayName(existing.boss)}' 이(가) 배정되어 있습니다. 바꿀까요?", "바꾸기", "취소")) return;
        Undo.RecordObject(map, "보스 층 배정");
        if (existing != null) existing.boss = e;
        else map.floorBosses.Add(new MapGenerationDataSO.FloorBossEntry { floor = floor, boss = e });
        map.floorBosses.Sort((a, b) => (a?.floor ?? 0).CompareTo(b?.floor ?? 0));
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssetIfDirty(map);
        InvalidateUsage();
    }

    // ── 군집 ──────────────────────────────────────────────────────────
    public static void AddToCluster(EnemyClusterSO c, EnemyMinionDataSO e, int count)
    {
        if (c == null || e == null) return;
        Undo.RecordObject(c, "군집에 적 추가");
        c.enemies.Add(new EnemyCount { enemyData = e, count = count });
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssetIfDirty(c);
        InvalidateUsage();
    }

    public static EnemyClusterSO CreateCluster(string displayName, bool register)
    {
        string folder = AssetDatabase.IsValidFolder(ClusterFolder) ? ClusterFolder : "Assets";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeFile(displayName)}.asset");
        var c = ScriptableObject.CreateInstance<EnemyClusterSO>();
        c.clusterName = displayName;
        AssetDatabase.CreateAsset(c, path);
        Undo.RegisterCreatedObjectUndo(c, "새 군집");
        if (register && ClusterRegistry != null) SetIn(ClusterRegistry, ClusterRegistry.enemyClusters, c, true);
        AssetDatabase.SaveAssets();
        return c;
    }

    public static EnemyClusterSO DuplicateCluster(EnemyClusterSO src)
    {
        if (src == null) return null;
        string from = AssetDatabase.GetAssetPath(src);
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(from).Replace('\\', '/') + "/" + src.name + " Copy.asset");
        if (!AssetDatabase.CopyAsset(from, path)) return null;
        var c = AssetDatabase.LoadAssetAtPath<EnemyClusterSO>(path);
        c.clusterName = ClusterName(src) + " (사본)";
        EditorUtility.SetDirty(c);
        if (ClusterRegistry != null && ClusterRegistry.enemyClusters.Contains(src)) SetIn(ClusterRegistry, ClusterRegistry.enemyClusters, c, true);
        AssetDatabase.SaveAssets();
        return c;
    }

    public static bool DeleteCluster(EnemyClusterSO c)
    {
        if (c == null) return false;
        var reg = ClusterRegistry;
        var map = MapData;
        var floors = map?.floorTuning?.Where(f => f != null && f.clusterPool != null && f.clusterPool.Contains(c)).Select(f => f.floor).ToList() ?? new List<int>();
        string msg = $"'{ClusterName(c)}' 군집을 삭제합니다(OS 휴지통으로 이동).\n"
                     + (reg != null && reg.enemyClusters.Contains(c) ? "\n· 전체 군집 목록에서 뺍니다." : "")
                     + (floors.Count > 0 ? $"\n· 층 조절표({string.Join(", ", floors)}층)의 군집 목록에서 뺍니다." : "");
        if (!EditorUtility.DisplayDialog("군집 삭제", msg, "삭제", "취소")) return false;
        if (reg != null && reg.enemyClusters.Contains(c)) { Undo.RecordObject(reg, "군집 삭제"); reg.enemyClusters.RemoveAll(x => x == c); EditorUtility.SetDirty(reg); }
        if (map != null && floors.Count > 0)
        {
            Undo.RecordObject(map, "군집 삭제");
            foreach (var f in map.floorTuning) f?.clusterPool?.RemoveAll(x => x == c);
            EditorUtility.SetDirty(map);
        }
        AssetDatabase.SaveAssets();
        return AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(c));
    }

    private static string SafeFile(string s)
    {
        foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(s) ? "New Cluster" : s.Trim();
    }

    // ── 삭제 ──────────────────────────────────────────────────────────
    public static bool Delete(EnemyMinionDataSO e)
    {
        if (e == null) return false;
        var reg = MinionRegistry;
        var map = MapData;
        var inClusters = AllClusters().Where(c => c.enemies != null && c.enemies.Any(x => x.enemyData == e)).ToList();
        var bossFloors = map?.floorBosses?.Where(f => f != null && f.boss == e).Select(f => f.floor).ToList() ?? new List<int>();

        string path = AssetDatabase.GetAssetPath(e);
        var skip = new HashSet<string> { path };
        if (reg != null) skip.Add(AssetDatabase.GetAssetPath(reg));
        if (map != null) skip.Add(AssetDatabase.GetAssetPath(map));
        foreach (var c in inClusters) skip.Add(AssetDatabase.GetAssetPath(c));
        if (!EquipmentAssets.FindReferences(e, skip, out var assetRefs, out var codeRefs)) return false;

        var msg = new StringBuilder();
        msg.AppendLine($"'{DisplayName(e)}' ({e.name}) 을(를) 삭제합니다.");
        msg.AppendLine("에셋은 OS 휴지통으로 이동합니다(휴지통에서 복구 가능).\n");
        if (reg != null && (reg.enemyMinionData.Contains(e) || reg.eliteMinionData.Contains(e) || reg.bossMinionData.Contains(e)))
            msg.AppendLine($"· {reg.name} 목록에서 자동으로 뺍니다.");
        if (inClusters.Count > 0) msg.AppendLine($"· 군집 {inClusters.Count}개에서 뺍니다: {string.Join(", ", inClusters.Select(ClusterName))}");
        if (bossFloors.Count > 0) msg.AppendLine($"· {string.Join(", ", bossFloors)}층 보스 배정을 해제합니다(그 층은 보스 방이 생기지 않음).");
        if (assetRefs.Count > 0)
        {
            msg.AppendLine("\n다른 에셋·씬·프리팹이 참조 중입니다(삭제 후 빈 칸이 됩니다):");
            foreach (var p in assetRefs.Take(8)) msg.AppendLine("  · " + p);
            if (assetRefs.Count > 8) msg.AppendLine($"  … 외 {assetRefs.Count - 8}곳");
        }
        if (codeRefs.Count > 0)
        {
            msg.AppendLine("\n코드에 이 이름이 적혀 있습니다:");
            foreach (var p in codeRefs.Take(8)) msg.AppendLine("  · " + p);
        }
        msg.AppendLine("\n프리팹·AI 패턴 에셋은 지우지 않습니다.");
        if (!EditorUtility.DisplayDialog("적 삭제", msg.ToString(), "삭제", "취소")) return false;

        if (reg != null)
        {
            Undo.RecordObject(reg, "적 삭제");
            reg.enemyMinionData.RemoveAll(x => x == e);
            reg.eliteMinionData.RemoveAll(x => x == e);
            reg.bossMinionData.RemoveAll(x => x == e);
            EditorUtility.SetDirty(reg);
        }
        foreach (var c in inClusters)
        {
            Undo.RecordObject(c, "적 삭제");
            c.enemies.RemoveAll(x => x.enemyData == e);
            EditorUtility.SetDirty(c);
        }
        if (map != null && bossFloors.Count > 0)
        {
            Undo.RecordObject(map, "적 삭제");
            map.floorBosses.RemoveAll(f => f != null && f.boss == e);
            EditorUtility.SetDirty(map);
        }
        AssetDatabase.SaveAssets();
        InvalidateUsage();

        if (!AssetDatabase.MoveAssetToTrash(path))
        {
            EditorUtility.DisplayDialog("적 삭제", "휴지통으로 옮기지 못했습니다. 목록에서는 이미 빠졌으니 Project 창에서 직접 지워 주세요.", "확인");
            return false;
        }
        return true;
    }
}

/// <summary>새 적 / 적 복제 창.</summary>
public class NewEnemyPopup : EditorWindow
{
    private EnemyAssets.Request req;
    private Action<EnemyMinionDataSO> onCreated;
    private List<EnemyMinionDataSO> sources;
    private List<EnemyClusterSO> clusters;
    private int mode;
    private int sourceIndex;
    private int clusterIndex; // 0 = 넣지 않음
    private bool folderTouched;
    private string error;

    public static void Open(EnemyMinionDataSO copyFrom, Action<EnemyMinionDataSO> onCreated)
    {
        var w = CreateInstance<NewEnemyPopup>();
        w.titleContent = new GUIContent(copyFrom != null ? "적 복제" : "새 적");
        w.onCreated = onCreated;
        w.sources = EnemyAssets.All().OrderBy(e => (int)e.tier).ThenBy(EnemyAssets.DisplayName, StringComparer.Ordinal).ToList();
        w.clusters = EnemyAssets.AllClusters();
        w.req = new EnemyAssets.Request { assetName = EnemyAssets.UniqueName("New Enemy"), folder = EnemyAssets.FolderFor(EnemyTier.Normal) };
        if (copyFrom != null)
        {
            w.mode = 1;
            w.sourceIndex = Mathf.Max(0, w.sources.IndexOf(copyFrom));
            w.ApplySource();
        }
        w.minSize = w.maxSize = new Vector2(480, 520);
        w.ShowUtility();
    }

    private void ApplySource()
    {
        if (sources.Count == 0) { mode = 0; return; }
        var src = sources[Mathf.Clamp(sourceIndex, 0, sources.Count - 1)];
        req.assetName = EnemyAssets.UniqueName(src.name + " Copy");
        req.displayName = EnemyAssets.DisplayName(src) + " (사본)";
        req.tier = src.tier;
        req.aiPattern = src.aiPattern;
        req.prefab = src.minionPrefab;
        req.folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(src))?.Replace('\\', '/') ?? EnemyAssets.FolderFor(src.tier);
        folderTouched = true;
    }

    private void OnGUI()
    {
        if (req == null) { Close(); return; }
        EditorGUIUtility.labelWidth = 130;
        EditorGUILayout.Space(6);

        int nm = GUILayout.Toolbar(mode, new[] { "빈 적으로 시작", "기존 적 복사" });
        if (nm != mode) { mode = nm; if (mode == 1) ApplySource(); }
        EditorGUILayout.Space(4);

        if (mode == 1)
        {
            if (sources.Count == 0) { EditorGUILayout.HelpBox("복사할 적이 없습니다.", MessageType.Info); mode = 0; }
            else
            {
                var labels = sources.Select(s => $"{EnemyAssets.TierName(s.tier)}/{EnemyAssets.DisplayName(s)}  ({s.name})").ToArray();
                int idx = EditorGUILayout.Popup(new GUIContent("복사할 적", "능력치·아이콘·AI 패턴·프리팹을 그대로 가져온다."), sourceIndex, labels);
                if (idx != sourceIndex) { sourceIndex = idx; ApplySource(); }
            }
        }

        EquipmentEditorWindow.Header("이름");
        req.assetName = EditorGUILayout.TextField(new GUIContent("에셋 파일 이름"), req.assetName);
        string nameError = EnemyAssets.ValidateName(req.assetName);
        if (nameError != null) EditorGUILayout.HelpBox(nameError, MessageType.Error);
        req.displayName = EditorGUILayout.TextField(new GUIContent("게임 표시 이름"), req.displayName);

        EquipmentEditorWindow.Header("기본 값");
        var tier = (EnemyTier)EditorGUILayout.EnumPopup(new GUIContent("등급"), req.tier);
        if (tier != req.tier) { req.tier = tier; if (!folderTouched) req.folder = EnemyAssets.FolderFor(tier); }
        req.aiPattern = (AIPatternSO)EditorGUILayout.ObjectField(new GUIContent("AI 패턴", "행동 패턴 에셋. 나중에 편집기에서 바꿀 수 있다."), req.aiPattern, typeof(AIPatternSO), false);
        req.prefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent("몬스터 프리팹", "실제로 소환되는 프리팹"), req.prefab, typeof(GameObject), false);
        if (req.prefab != null && req.prefab.GetComponent<BaseEntity>() == null)
            EditorGUILayout.HelpBox("이 프리팹 루트에는 BaseEntity 가 없습니다.", MessageType.Warning);

        EquipmentEditorWindow.Header("저장 위치 · 등장");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent("폴더"), new GUIContent(req.folder));
            if (GUILayout.Button("변경…", GUILayout.Width(56)))
            {
                string abs = EditorUtility.OpenFolderPanel("적 데이터 저장 폴더", req.folder, "");
                string data = Application.dataPath.Replace('\\', '/');
                if (!string.IsNullOrEmpty(abs))
                {
                    abs = abs.Replace('\\', '/');
                    if (abs == data || abs.StartsWith(data + "/")) { req.folder = "Assets" + abs.Substring(data.Length); folderTouched = true; }
                    else EditorUtility.DisplayDialog("폴더", "프로젝트의 Assets 폴더 안을 골라 주세요.", "확인");
                }
                GUIUtility.ExitGUI();
            }
        }
        var reg = EnemyAssets.MinionRegistry;
        string listName = req.tier == EnemyTier.Elite ? "엘리트 후보 목록에 등록 (엘리트 방에 나옴)" : req.tier == EnemyTier.Boss ? "보스 목록에 등록" : "일반 적 목록에 등록";
        using (new EditorGUI.DisabledScope(reg == null))
            req.registerList = EditorGUILayout.ToggleLeft(new GUIContent(listName + (reg != null ? $" ({reg.name})" : " (레지스트리 없음)")), req.registerList && reg != null);
        if (req.tier == EnemyTier.Normal)
        {
            var names = new[] { "넣지 않음 (나중에)" }.Concat(clusters.Select(EnemyAssets.ClusterName)).ToArray();
            clusterIndex = EditorGUILayout.Popup(new GUIContent("군집에 넣기", "일반 방 웨이브에 나오려면 군집에 들어가야 한다."), Mathf.Clamp(clusterIndex, 0, names.Length - 1), names);
            if (clusterIndex > 0) req.clusterCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("  마릿수"), req.clusterCount));
        }
        else if (req.tier == EnemyTier.Boss)
            EditorGUILayout.HelpBox("보스는 만든 뒤 편집기의 '등장 위치'에서 몇 층 보스인지 지정하세요.", MessageType.None);

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
                    req.addToCluster = req.tier == EnemyTier.Normal && clusterIndex > 0 ? clusters[clusterIndex - 1] : null;
                    var e = EnemyAssets.Create(req, out error);
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
