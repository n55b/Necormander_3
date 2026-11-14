using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 소환수 관리 툴 (Tools/기획/소환수 관리).
///
///  · 왼쪽  : 메인 소환수 목록. 진화 트리 순서(기본형 → 진화형)로 들여쓰기해서 보여준다.
///  · 수치표: 전 소환수의 스킬/마무리/대쉬 핵심 수치를 한 표에서 비교하고 바로 고친다.
///  · 상세  : 고른 소환수의 전체 설정 + 스페이스바 스킬 에셋을 한 화면에서 편집한다.
///  · 미리보기: 스킬/마무리/대쉬 애니메이션을 게임과 같은 규칙(시퀀스 순서, 시전시간 배속, 위치 이동,
///             이펙트 겹침)으로 재생하고, 타격 이벤트 시점을 타임라인에 표시한다.
///
/// 편집은 전부 SerializedObject 를 거치므로 Undo 가 되고, 에셋에 바로 저장된다.
/// 같은 스킬 에셋을 여러 소환수가 공유하면(예: 기사·용사) 경고를 띄운다 — 하나를 고치면 같이 바뀐다.
/// </summary>
public class MinionManagerWindow : EditorWindow
{
    [MenuItem("Tools/기획/소환수 관리", priority = 0)]
    public static void Open()
    {
        var w = GetWindow<MinionManagerWindow>("소환수 관리");
        w.minSize = new Vector2(960f, 580f);
        w.Show();
    }

    [MenuItem("Tools/기획/상태이상 공통 수치 열기", priority = 20)]
    public static void OpenStatusBalance()
    {
        var bal = Resources.Load<StatusBalanceSO>(StatusRules.BalanceResourcePath);
        if (bal == null) { Debug.LogWarning("[기획] Resources/" + StatusRules.BalanceResourcePath + " 에셋이 없습니다."); return; }
        Selection.activeObject = bal;
        EditorGUIUtility.PingObject(bal);
    }

    private enum Tab { Table, Detail, DashAttack, Standby }
    private static readonly string[] TabLabels = { "수치 비교표", "상세 편집 · 미리보기", "대쉬 공격", "대기 효과(평타)" };

    private class Row
    {
        public MainMinionDataSO data;
        public int depth;
        public bool evolved;
    }

    private readonly List<Row> _rows = new List<Row>();
    private readonly Dictionary<MinionSkillSO, List<MainMinionDataSO>> _skillUsers = new Dictionary<MinionSkillSO, List<MainMinionDataSO>>();
    private MainMinionDataSO _selected;
    private Tab _tab = Tab.Table;
    private string _search = "";
    private Vector2 _listScroll, _tableScroll, _detailScroll;
    private Editor _minionEditor, _skillEditor;
    private bool _skillFold = true;
    private MinionAnimPreviewer _preview;

    private const float ListWidth = 220f;
    private const float PreviewWidth = 360f;

    private void OnEnable()
    {
        _preview = new MinionAnimPreviewer();
        Rebuild();
        EditorApplication.projectChanged += Rebuild;
        Undo.undoRedoPerformed += Repaint;
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= Rebuild;
        Undo.undoRedoPerformed -= Repaint;
        if (_minionEditor != null) DestroyImmediate(_minionEditor);
        if (_skillEditor != null) DestroyImmediate(_skillEditor);
    }

    private void Update()
    {
        if (_preview != null && _preview.Tick()) Repaint();
    }

    // ── 데이터 수집 ─────────────────────────────────────────────────
    private void Rebuild()
    {
        var all = AssetDatabase.FindAssets("t:MainMinionDataSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null).Distinct().ToList();

        // 다른 소환수의 evolutions 에 들어 있으면 '진화형' — 트리의 뿌리가 아니다.
        var children = new HashSet<MainMinionDataSO>();
        foreach (var m in all)
            if (m.evolutions != null)
                foreach (var e in m.evolutions)
                    if (e != null && e != m) children.Add(e);

        _rows.Clear();
        var visited = new HashSet<MainMinionDataSO>();
        foreach (var root in all.Where(m => !children.Contains(m)).OrderBy(m => m.name))
            AddTree(root, 0, visited);
        // 순환 등으로 빠진 것도 끝에 붙인다.
        foreach (var m in all.Where(m => !visited.Contains(m)).OrderBy(m => m.name))
            AddTree(m, 0, visited);

        _skillUsers.Clear();
        foreach (var m in all)
        {
            if (m.minionSkill == null) continue;
            if (!_skillUsers.TryGetValue(m.minionSkill, out var list)) _skillUsers[m.minionSkill] = list = new List<MainMinionDataSO>();
            list.Add(m);
        }

        if (_selected == null && _rows.Count > 0) Select(_rows[0].data);
        Repaint();
    }

    private void AddTree(MainMinionDataSO m, int depth, HashSet<MainMinionDataSO> visited)
    {
        if (m == null || !visited.Add(m)) return;
        _rows.Add(new Row { data = m, depth = depth, evolved = depth > 0 || m.isEvolvedForm });
        if (m.evolutions == null) return;
        foreach (var e in m.evolutions) AddTree(e, depth + 1, visited);
    }

    private void Select(MainMinionDataSO m)
    {
        if (_selected == m) return;
        _selected = m;
        GUI.FocusControl(null);
        _preview.SetMinion(m);
    }

    private static string DisplayName(MainMinionDataSO m)
        => string.IsNullOrEmpty(m.minionName) ? m.name : $"{m.minionName}  <size=10><color=#888>{m.name.Replace(" Minion", "")}</color></size>";

    // ── GUI ─────────────────────────────────────────────────────────
    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            _tab = (Tab)GUILayout.Toolbar((int)_tab, TabLabels, EditorStyles.toolbarButton, GUILayout.Width(540f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("상태이상 공통 수치", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                OpenStatusBalance();
            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70f))) Rebuild();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawList();
            using (new EditorGUILayout.VerticalScope())
            {
                if (_tab == Tab.Table) DrawTable();
                else if (_tab == Tab.Detail) DrawDetail();
                else if (_tab == Tab.DashAttack) DrawDashAttack();
                else DrawStandby();
            }
        }
    }

    private void DrawList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
        {
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, "box");
            var rich = new GUIStyle(EditorStyles.label) { richText = true };
            foreach (var r in _rows)
            {
                var m = r.data;
                if (m == null) continue;
                if (!string.IsNullOrEmpty(_search)
                    && (m.minionName ?? "").IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0
                    && m.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                Rect rect = GUILayoutUtility.GetRect(ListWidth - 24f, 30f);
                bool sel = m == _selected;
                if (sel) EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.35f));
                else if (rect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));

                float x = rect.x + 4f + r.depth * 14f;
                if (r.depth > 0) GUI.Label(new Rect(x - 12f, rect.y + 6f, 12f, 18f), "└", EditorStyles.miniLabel);
                MinionAnimPreviewer.DrawSprite(new Rect(x, rect.y + 2f, 26f, 26f), m.minionIcon);
                GUI.Label(new Rect(x + 30f, rect.y + 6f, rect.width - (x - rect.x) - 30f, 18f), DisplayName(m), rich);

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    Select(m);
                    if (Event.current.clickCount == 2) _tab = Tab.Detail;
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("한 번 클릭: 선택\n두 번 클릭: 상세 편집", MessageType.None);
        }
    }

    // ── 수치 비교표 ─────────────────────────────────────────────────
    private struct Col
    {
        public string title; public float width; public string tip;
        public Col(string t, float w, string tip = null) { title = t; width = w; this.tip = tip; }
    }

    private static readonly Col[] Cols =
    {
        new Col("소환수", 120f),
        new Col("스킬", 110f, "스페이스바 스킬 에셋 이름"),
        new Col("쿨타임", 52f, "스킬 쿨타임(초)"),
        new Col("배율", 52f, "스킬 타당 피해 = 플레이어 ATK x 배율"),
        new Col("타수", 40f, "스킬 타격 횟수(구역 충격파면 구역 수)"),
        new Col("총배율", 52f, "배율 x 타수 — 한 번 시전으로 한 대상이 받는 최대 피해(ATK 배수)"),
        new Col("스킬 상태", 82f, "스킬 적중 시 거는 상태이상"),
        new Col("마무리 배율", 64f, "평타 첫 타에 소환수가 넣는 일격의 배율"),
        new Col("타수", 40f),
        new Col("마무리 상태", 82f),
        new Col("대쉬 배율", 60f, "대쉬 공격 타당 피해 = 플레이어 ATK x 배율. LShift 대쉬에서는 '대기 소환수'의 값이 쓰입니다(대쉬 공격 탭 참고)."),
        new Col("타수", 40f),
        new Col("대쉬 상태", 82f),
        new Col("강화 배율(평/대/스)", 150f, "보상방 강화 1단계 배율: 평타/대쉬/스킬"),
    };

    private void DrawTable()
    {
        EditorGUILayout.HelpBox("칸을 바로 고치면 에셋에 저장됩니다(Ctrl+Z 가능). 노란 칸 = 여러 소환수가 같이 쓰는 스킬이라 같이 바뀝니다.", MessageType.Info);
        _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll);

        float total = Cols.Sum(c => c.width) + Cols.Length * 4f;
        Rect head = GUILayoutUtility.GetRect(total, 20f, GUILayout.Width(total));
        EditorGUI.DrawRect(head, new Color(0f, 0f, 0f, 0.25f));
        float hx = head.x;
        foreach (var c in Cols)
        {
            GUI.Label(new Rect(hx + 2f, head.y + 2f, c.width, 16f), new GUIContent(c.title, c.tip), EditorStyles.miniBoldLabel);
            hx += c.width + 4f;
        }

        int i = 0;
        foreach (var r in _rows)
        {
            var m = r.data;
            if (m == null) continue;
            Rect line = GUILayoutUtility.GetRect(total, 22f, GUILayout.Width(total));
            if (m == _selected) EditorGUI.DrawRect(line, new Color(0.24f, 0.48f, 0.9f, 0.25f));
            else if (i++ % 2 == 1) EditorGUI.DrawRect(line, new Color(1f, 1f, 1f, 0.03f));

            var so = new SerializedObject(m);
            so.Update();
            var skill = m.minionSkill as MinionActionSkillSO;
            SerializedObject sso = skill != null ? new SerializedObject(skill) : null;
            sso?.Update();
            bool shared = m.minionSkill != null && _skillUsers.TryGetValue(m.minionSkill, out var users) && users.Count > 1;

            float x = line.x;
            Rect Next(int col) { var rc = new Rect(x + 2f, line.y + 2f, Cols[col].width, 18f); x += Cols[col].width + 4f; return rc; }

            // 0 이름
            var nameRect = Next(0);
            string indent = new string(' ', r.depth * 3) + (r.depth > 0 ? "└ " : "");
            if (GUI.Button(nameRect, indent + (string.IsNullOrEmpty(m.minionName) ? m.name : m.minionName), EditorStyles.label))
            { Select(m); if (Event.current.clickCount == 2) _tab = Tab.Detail; }

            // 1 스킬
            var skRect = Next(1);
            if (shared) EditorGUI.DrawRect(skRect, new Color(1f, 0.85f, 0.2f, 0.18f));
            string skName = m.minionSkill != null ? m.minionSkill.name.Replace("_Active", "") : "(없음)";
            if (GUI.Button(skRect, new GUIContent(skName, shared ? "공유: " + string.Join(", ", _skillUsers[m.minionSkill].Select(u => u.minionName)) : "클릭하면 에셋 선택"), EditorStyles.label)
                && m.minionSkill != null) EditorGUIUtility.PingObject(m.minionSkill);

            if (sso != null)
            {
                Field(Next(2), sso, "cooldownTime", shared);
                Field(Next(3), sso, "damageMultiplier", shared);
                bool zones = skill.zoneCount > 1 && skill.hitBoxSize.x > 0f && skill.hitBoxSize.y > 0f;
                Field(Next(4), sso, zones ? "zoneCount" : "hitCount", shared);
                int hits = zones ? skill.zoneCount : Mathf.Max(1, skill.hitCount);
                GUI.Label(Next(5), $"x{skill.damageMultiplier * hits:0.##}", EditorStyles.miniLabel);
                Field(Next(6), sso, "onHitStatus", shared);
            }
            else
            {
                for (int c = 2; c <= 6; c++) GUI.Label(Next(c), "-", EditorStyles.centeredGreyMiniLabel);
            }

            Field(Next(7), so, "finisher.damageMultiplier");
            Field(Next(8), so, "finisher.hitCount");
            Field(Next(9), so, "finisher.onHitStatus");
            Field(Next(10), so, "dashModifier.damageMultiplier");
            Field(Next(11), so, "dashModifier.hitCount");
            Field(Next(12), so, "dashModifier.onHitStatus");

            var enh = Next(13);
            var steps = so.FindProperty("enhanceSteps");
            if (steps != null && steps.arraySize > 0)
            {
                var s0 = steps.GetArrayElementAtIndex(0);
                float w = (enh.width - 8f) / 3f;
                EditorGUI.PropertyField(new Rect(enh.x, enh.y, w, enh.height), s0.FindPropertyRelative("basicAttackMultiplier"), GUIContent.none);
                EditorGUI.PropertyField(new Rect(enh.x + w + 4f, enh.y, w, enh.height), s0.FindPropertyRelative("dashDamageMultiplier"), GUIContent.none);
                EditorGUI.PropertyField(new Rect(enh.x + (w + 4f) * 2f, enh.y, w, enh.height), s0.FindPropertyRelative("skillDamageMultiplier"), GUIContent.none);
            }
            else GUI.Label(enh, "-", EditorStyles.centeredGreyMiniLabel);

            if (so.ApplyModifiedProperties()) _preview.MarkDirty();
            sso?.ApplyModifiedProperties();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.HelpBox(
            "스킬 쿨타임·배율·타수·상태이상은 '스킬 에셋'에, 마무리·대쉬·강화는 '소환수 에셋'에 저장됩니다.\n" +
            "상태이상 지속시간·출혈 비율 같은 공통 수치는 위 '상태이상 공통 수치' 버튼으로 엽니다.", MessageType.None);
    }

    private static void Field(Rect r, SerializedObject so, string path, bool shared = false)
    {
        var p = so.FindProperty(path);
        if (p == null) { GUI.Label(r, "?", EditorStyles.centeredGreyMiniLabel); return; }
        if (shared) EditorGUI.DrawRect(r, new Color(1f, 0.85f, 0.2f, 0.18f));
        EditorGUI.PropertyField(r, p, GUIContent.none);
    }

    // ── 대쉬 공격 ──────────────────────────────────────────────────
    private const string PlayerPrefabPath = "Assets/Prefabs/Player Melee.prefab";
    private Vector2 _dashScroll;

    private static readonly Col[] DashCols =
    {
        new Col("소환수", 120f),
        new Col("판정", 70f, "경로 = 대쉬 경로를 훑는 박스 / 출발점 = 대쉬 출발 자리에 원형 1발"),
        new Col("피해 배율", 60f, "타당 피해 = 플레이어 ATK x 배율"),
        new Col("타수", 40f, "0 이면 피해 없음(연출만)"),
        new Col("폭", 50f, "경로 판정의 폭(유닛). 0 이면 경로 판정 없음"),
        new Col("원 반지름", 60f, "출발점 판정의 반지름(유닛)"),
        new Col("속성", 80f),
        new Col("상태이상", 90f),
        new Col("밀침", 36f, "맞은 적을 대쉬 방향으로 밀친다"),
        new Col("밀침 거리", 56f),
        new Col("전용 히트박스", 130f, "비우면 플레이어 프리팹의 기본 히트박스를 씁니다"),
        new Col("연출", 40f, "dashAnim 연출 유무"),
        new Col("상태", 110f, "실제로 판정이 나갈 수 있는지"),
    };

    private void OnInspectorUpdate()
    {
        if ((_tab == Tab.DashAttack || _tab == Tab.Standby) && Application.isPlaying) Repaint();
    }

    private static string MinionLabel(MinionDataSO m) => m == null ? "(비어 있음)" : (string.IsNullOrEmpty(m.minionName) ? m.name : m.minionName);

    private void DrawDashAttack()
    {
        _dashScroll = EditorGUILayout.BeginScrollView(_dashScroll);

        // 1) 발동 규칙 — 플레이어 프리팹의 MeleeDodgeController 값을 바로 고친다.
        EditorGUILayout.LabelField("LShift 대쉬 공격 발동 규칙", EditorStyles.boldLabel);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var dodgeAsset = prefab != null ? prefab.GetComponent<MeleeDodgeController>() : null;
        Object defaultPath = null, defaultOrigin = null;
        if (dodgeAsset == null)
        {
            EditorGUILayout.HelpBox(PlayerPrefabPath + " 에서 MeleeDodgeController 를 찾지 못했습니다.", MessageType.Error);
        }
        else
        {
            var so = new SerializedObject(dodgeAsset);
            so.Update();
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.PropertyField(so.FindProperty("playerDashAttackSource"), new GUIContent("대쉬 공격을 내는 소환수",
                    "기본값: 대기 소환수(필드에 없는 쪽). 플레이어 프리팹(Player Melee)에 저장됩니다."));
                EditorGUILayout.PropertyField(so.FindProperty("dashHitBoxPrefab"), new GUIContent("기본 경로 히트박스", "소환수에 전용 히트박스가 없을 때 쓰는 경로형 히트박스"));
                EditorGUILayout.PropertyField(so.FindProperty("originHitBoxPrefab"), new GUIContent("기본 출발점 히트박스", "소환수에 전용 히트박스가 없을 때 쓰는 출발점(원형) 히트박스"));
                if (so.ApplyModifiedProperties())
                {
                    AssetDatabase.SaveAssetIfDirty(prefab);
                    SyncLiveDodge(dodgeAsset);
                }
            }
            defaultPath = so.FindProperty("dashHitBoxPrefab").objectReferenceValue;
            defaultOrigin = so.FindProperty("originHitBoxPrefab").objectReferenceValue;

            string rule;
            switch (dodgeAsset.DashAttackSource)
            {
                case MeleeDodgeController.PlayerDashAttackSource.StandbySummon:
                    rule = "LShift 대쉬 → 필드에 나와 있지 않은 '대기 소환수'의 대쉬 공격이 대쉬 경로에 나갑니다.\n" +
                           "소환수가 1마리뿐이면 대기 소환수가 없어 대쉬 공격이 나가지 않습니다. Space 로 스왑하면 대쉬 공격을 내는 소환수도 바뀝니다.";
                    break;
                case MeleeDodgeController.PlayerDashAttackSource.ActiveSummon:
                    rule = "LShift 대쉬 → 필드에 나와 있는 '활성 소환수'의 대쉬 공격이 나갑니다(예전 방식).";
                    break;
                default:
                    rule = "LShift 대쉬에 소환수 대쉬 공격이 붙지 않습니다(순수 이동).";
                    break;
            }
            EditorGUILayout.HelpBox(rule + "\n대쉬 거리는 소환수와 무관하게 항상 같고, 판정은 실제 이동 경로를 덮습니다.", MessageType.Info);
        }

        // 1-b) Space 스왑 공격 — 플레이어 프리팹의 PlayerSkillController 값.
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Space 스왑 대쉬 공격", EditorStyles.boldLabel);
        var skillAsset = prefab != null ? prefab.GetComponent<PlayerSkillController>() : null;
        if (skillAsset == null)
        {
            EditorGUILayout.HelpBox(PlayerPrefabPath + " 에서 PlayerSkillController 를 찾지 못했습니다.", MessageType.Error);
        }
        else
        {
            var sso = new SerializedObject(skillAsset);
            sso.Update();
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.PropertyField(sso.FindProperty("swapTriggersSkill"), new GUIContent("스왑 때 대쉬 공격", "끄면 스왑은 항상 공격 없이 제자리 교대로 동작합니다."));
                EditorGUILayout.PropertyField(sso.FindProperty("swapAttackRange"), new GUIContent("적 탐지 범위", "플레이어 기준(유닛). 이 안에 적이 없으면 공격하지 않고, 스왑 전 소환수가 있던 자리에서 새 소환수가 나타납니다."));
                EditorGUILayout.PropertyField(sso.FindProperty("swapCooldown"), new GUIContent("스왑 쿨타임(초)"));
                if (sso.ApplyModifiedProperties())
                {
                    AssetDatabase.SaveAssetIfDirty(prefab);
                    SyncLive(skillAsset, "swapTriggersSkill", "swapAttackRange", "swapCooldown");
                }
                if (dodgeAsset != null)
                    EditorGUILayout.LabelField("공격 사거리 (모든 소환수 공통)", $"{dodgeAsset.SummonDashAttackDistance:0.##} 유닛  (대쉬 속도 x 대쉬 시간)");
            }
            EditorGUILayout.HelpBox("범위 안에 적이 있으면: 스왑 전 소환수가 플레이어 자리에서 마우스 방향으로 대쉬 공격을 하고, 새 소환수는 플레이어 곁에 나타납니다.\n" +
                                    "범위 안에 적이 없으면: 공격 없이 스왑 전 소환수가 있던 자리에서 새 소환수로 교대합니다.", MessageType.Info);
            if (Application.isPlaying)
            {
                var liveSkill = FindAnyObjectByType<PlayerSkillController>();
                if (liveSkill != null)
                    EditorGUILayout.LabelField("지금 스왑하면", liveSkill.HasEnemyInSwapRange() ? "대쉬 공격 후 교대" : "공격 없이 제자리 교대 (범위 안에 적 없음)", EditorStyles.boldLabel);
            }
        }

        // 2) 플레이 중 상태
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("지금 상태", EditorStyles.boldLabel);
        MainMinionDataSO next = null;
        using (new EditorGUILayout.VerticalScope("box"))
        {
            var inv = Application.isPlaying ? InventoryManager.Instance : null;
            var live = Application.isPlaying ? FindAnyObjectByType<MeleeDodgeController>() : null;
            if (!Application.isPlaying)
                EditorGUILayout.LabelField("플레이 모드에서 지금 어느 소환수가 대쉬 공격을 내는지 표시됩니다.", EditorStyles.miniLabel);
            else if (inv == null || live == null)
                EditorGUILayout.LabelField("플레이어/인벤토리를 찾는 중...", EditorStyles.miniLabel);
            else
            {
                for (int i = 0; i < InventoryManager.SLOT_COUNT; i++)
                {
                    bool onField = i == inv.ActiveSummonSlot;
                    EditorGUILayout.LabelField($"슬롯 {i + 1}", $"{MinionLabel(inv.GetSummonAt(i))}   {(onField ? "[필드 · 활성]" : "[대기]")}");
                }
                next = live.GetPlayerDashAttackMinion();
                string nextText;
                if (next != null)
                    nextText = MinionLabel(next) + (live.CanDealPlayerDashAttack(next) ? "" : "  (판정 없음 — 연출만)");
                else if (live.DashAttackSource == MeleeDodgeController.PlayerDashAttackSource.Off)
                    nextText = "없음 (꺼짐)";
                else if (live.DashAttackSource == MeleeDodgeController.PlayerDashAttackSource.StandbySummon && inv.EquippedSummonCount < InventoryManager.SLOT_COUNT)
                    nextText = "없음 (소환수가 1마리 이하라 대기 소환수 없음)";
                else nextText = "없음";
                var bold = new GUIStyle(EditorStyles.boldLabel);
                EditorGUILayout.LabelField("다음 LShift 대쉬 공격", nextText, bold);
                EditorGUILayout.LabelField("마지막으로 나간 대쉬 공격", MinionLabel(live.LastDashAttackMinion));
                if (live.DashAttackSource != dodgeAsset?.DashAttackSource)
                    EditorGUILayout.HelpBox("플레이 중인 플레이어의 값이 프리팹과 다릅니다.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(inv.EquippedSummonCount < InventoryManager.SLOT_COUNT))
                        if (GUILayout.Button("활성 슬롯 전환 (테스트용, 스왑 공격 없음)", GUILayout.Width(260f)))
                            inv.SetActiveSummonSlot(1 - inv.ActiveSummonSlot);
                }
            }
        }

        // 3) 소환수별 대쉬 공격 수치
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("소환수별 대쉬 공격", EditorStyles.boldLabel);
        float total = DashCols.Sum(c => c.width) + DashCols.Length * 4f;
        Rect head = GUILayoutUtility.GetRect(total, 20f, GUILayout.Width(total));
        EditorGUI.DrawRect(head, new Color(0f, 0f, 0f, 0.25f));
        float hx = head.x;
        foreach (var c in DashCols)
        {
            GUI.Label(new Rect(hx + 2f, head.y + 2f, c.width, 16f), new GUIContent(c.title, c.tip), EditorStyles.miniBoldLabel);
            hx += c.width + 4f;
        }

        int row = 0;
        foreach (var r in _rows)
        {
            var m = r.data;
            if (m == null) continue;
            Rect line = GUILayoutUtility.GetRect(total, 22f, GUILayout.Width(total));
            if (m == next) EditorGUI.DrawRect(line, new Color(0.3f, 0.85f, 0.4f, 0.22f));
            else if (m == _selected) EditorGUI.DrawRect(line, new Color(0.24f, 0.48f, 0.9f, 0.25f));
            else if (row % 2 == 1) EditorGUI.DrawRect(line, new Color(1f, 1f, 1f, 0.03f));
            row++;

            var so = new SerializedObject(m);
            so.Update();
            float x = line.x;
            Rect Next(int col) { var rc = new Rect(x + 2f, line.y + 2f, DashCols[col].width, 18f); x += DashCols[col].width + 4f; return rc; }

            string indent = new string(' ', r.depth * 3) + (r.depth > 0 ? "└ " : "");
            if (GUI.Button(Next(0), indent + MinionLabel(m), EditorStyles.label)) Select(m);

            var origin = so.FindProperty("dashModifier.hitAtOrigin");
            var modeRect = Next(1);
            if (origin != null)
            {
                int mode = EditorGUI.Popup(modeRect, origin.boolValue ? 1 : 0, new[] { "경로", "출발점" });
                if ((mode == 1) != origin.boolValue) origin.boolValue = mode == 1;
            }
            Field(Next(2), so, "dashModifier.damageMultiplier");
            Field(Next(3), so, "dashModifier.hitCount");
            bool atOrigin = origin != null && origin.boolValue;
            using (new EditorGUI.DisabledScope(atOrigin)) Field(Next(4), so, "dashModifier.width");
            using (new EditorGUI.DisabledScope(!atOrigin)) Field(Next(5), so, "dashModifier.originRadius");
            Field(Next(6), so, "dashModifier.element");
            Field(Next(7), so, "dashModifier.onHitStatus");
            Field(Next(8), so, "dashModifier.pushesEnemies");
            using (new EditorGUI.DisabledScope(!so.FindProperty("dashModifier.pushesEnemies").boolValue)) Field(Next(9), so, "dashModifier.pushForce");
            Field(Next(10), so, "dashModifier.hitBoxPrefab");
            bool visual = m.dashAnim != null && m.dashAnim.HasVisual;
            GUI.Label(Next(11), visual ? "있음" : "-", EditorStyles.centeredGreyMiniLabel);

            var mod = m.dashModifier;
            string state; Color col;
            bool hasPrefab = mod != null && (mod.hitBoxPrefab != null || (mod.hitAtOrigin ? defaultOrigin : defaultPath) != null);
            if (mod == null || !mod.DealsDamage) { state = visual ? "연출만 (피해 없음)" : "없음"; col = new Color(0.6f, 0.6f, 0.6f); }
            else if (!hasPrefab) { state = "히트박스 프리팹 없음"; col = new Color(1f, 0.45f, 0.3f); }
            else { state = "발동 가능"; col = new Color(0.4f, 0.85f, 0.45f); }
            var st = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = col } };
            GUI.Label(Next(12), state, st);

            if (so.ApplyModifiedProperties()) _preview.MarkDirty();
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(
            "초록 줄 = 플레이 중 다음 LShift 대쉬 공격을 낼 소환수.\n" +
            "대쉬 거리와 스왑 대쉬 공격 사거리는 모든 소환수가 같습니다(소환수별 거리 배율 사용 안 함).\n" +
            "강화 단계의 대쉬 피해 배율과 무기의 '소환수 대쉬 피해 배율'도 그대로 곱해집니다.", MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    /// <summary>플레이 중이면 프리팹 컴포넌트의 지정 값을 같은 타입의 살아 있는 컴포넌트에도 넣는다.</summary>
    private static void SyncLive<T>(T asset, params string[] paths) where T : Component
    {
        if (!Application.isPlaying) return;
        var src = new SerializedObject(asset);
        foreach (var live in FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            var dst = new SerializedObject(live);
            foreach (var path in paths) dst.CopyFromSerializedProperty(src.FindProperty(path));
            dst.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>플레이 중이면 프리팹에서 바뀐 값을 지금 플레이어에도 바로 넣는다.</summary>
    private static void SyncLiveDodge(MeleeDodgeController asset)
    {
        if (!Application.isPlaying) return;
        foreach (var live in FindObjectsByType<MeleeDodgeController>(FindObjectsSortMode.None))
        {
            var src = new SerializedObject(asset);
            var dst = new SerializedObject(live);
            foreach (var path in new[] { "playerDashAttackSource", "dashHitBoxPrefab", "originHitBoxPrefab" })
                dst.CopyFromSerializedProperty(src.FindProperty(path));
            dst.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ── 대기 효과(평타) ───────────────────────────────────────────
    private Vector2 _standbyScroll;
    private float _bulkChance = 0.5f;

    private static readonly Col[] StandbyCols =
    {
        new Col("소환수 / 효과", 140f),
        new Col("상태이상", 100f, "대기 중일 때 플레이어 평타에 거는 상태이상"),
        new Col("확률", 140f, "이 효과만의 확률. 휘두르기 1회마다 효과별로 따로 추첨합니다. 1 = 항상"),
        new Col("적용 타", 80f, "모든 타 / 3타만"),
        new Col("지속(초)", 60f, "0 이면 상태이상 공통 수치의 기본 지속시간"),
        new Col("", 24f),
    };

    private void DrawStandby()
    {
        _standbyScroll = EditorGUILayout.BeginScrollView(_standbyScroll);

        // 1) 규칙
        EditorGUILayout.LabelField("대기 소환수 효과 → 플레이어 평타", EditorStyles.boldLabel);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var meleeAsset = prefab != null ? prefab.GetComponent<MeleeCombatController>() : null;
        if (meleeAsset == null)
            EditorGUILayout.HelpBox(PlayerPrefabPath + " 에서 MeleeCombatController 를 찾지 못했습니다.", MessageType.Error);
        else
        {
            var so = new SerializedObject(meleeAsset);
            so.Update();
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.PropertyField(so.FindProperty("standbyInfusionEnabled"), new GUIContent("대기 효과 사용", "끄면 대기 소환수 효과가 평타에 묻지 않습니다. 플레이어 프리팹에 저장됩니다."));
                if (so.ApplyModifiedProperties())
                {
                    AssetDatabase.SaveAssetIfDirty(prefab);
                    SyncLive(meleeAsset, "standbyInfusionEnabled");
                }
            }
        }
        EditorGUILayout.HelpBox("소환수 2마리를 장착하면, 필드에 나오지 않은(현현되지 않은) 대기 소환수의 효과가 플레이어 평타에 붙습니다.\n" +
                                "소환수마다 효과를 여러 개 넣을 수 있고, 효과마다 확률을 따로 추첨합니다(동시에 여러 개가 붙을 수 있음).\n" +
                                "붙으면 그 휘두르기에 맞은 적 모두에게 걸립니다. Space 로 스왑하면 효과도 바뀝니다.\n" +
                                "빙결 시간·출혈 피해·취약 배율 같은 상태이상 자체 수치는 위 '상태이상 공통 수치' 버튼에서 고칩니다.", MessageType.Info);

        // 2) 플레이 중 상태
        MainMinionDataSO source = null;
        using (new EditorGUILayout.VerticalScope("box"))
        {
            var live = Application.isPlaying ? FindAnyObjectByType<MeleeCombatController>() : null;
            var liveSkill = Application.isPlaying ? FindAnyObjectByType<PlayerSkillController>() : null;
            if (!Application.isPlaying) EditorGUILayout.LabelField("플레이 모드에서 지금 평타에 묻는 효과가 표시됩니다.", EditorStyles.miniLabel);
            else if (live == null || liveSkill == null) EditorGUILayout.LabelField("플레이어를 찾는 중...", EditorStyles.miniLabel);
            else
            {
                var standby = liveSkill.StandbySummon;
                source = live.GetStandbyInfusionSource();
                EditorGUILayout.LabelField("대기 소환수", standby != null ? MinionLabel(standby) : "없음 (소환수 1마리 이하)");
                string now = source != null ? MinionStandbyInfusion.DescribeAll(source.standbyInfusions)
                    : !live.StandbyInfusionEnabled ? "꺼짐" : standby != null ? "이 소환수는 대기 효과가 없음" : "없음";
                EditorGUILayout.LabelField("지금 평타에 묻는 효과", now, EditorStyles.boldLabel);
                string last = live.LastInfusionStatuses.Count > 0
                    ? string.Join(", ", live.LastInfusionStatuses.Select(MinionStandbyInfusion.StatusLabel)) : "없음";
                EditorGUILayout.LabelField("마지막 휘두르기에 붙은 효과", last);
            }
        }

        // 3) 소환수별
        EditorGUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("소환수별 대기 효과", EditorStyles.boldLabel, GUILayout.Width(140f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("모든 효과 확률", GUILayout.Width(90f));
            _bulkChance = EditorGUILayout.Slider(_bulkChance, 0f, 1f, GUILayout.Width(160f));
            if (GUILayout.Button("일괄 적용", GUILayout.Width(70f))) SetAllStandbyChance(_bulkChance);
            if (GUILayout.Button(new GUIContent("빈 소환수 채우기", "대기 효과가 하나도 없는 소환수에, 스킬 → 마무리 → 대쉬 순서로 찾은 상태이상을 50% 확률로 넣습니다."), GUILayout.Width(110f)))
                FillStandbyInfusionDefaults();
        }
        float total = StandbyCols.Sum(c => c.width) + StandbyCols.Length * 4f + 320f;
        Rect head = GUILayoutUtility.GetRect(total, 20f, GUILayout.Width(total));
        EditorGUI.DrawRect(head, new Color(0f, 0f, 0f, 0.25f));
        float hx = head.x;
        foreach (var c in StandbyCols)
        {
            GUI.Label(new Rect(hx + 2f, head.y + 2f, c.width, 16f), new GUIContent(c.title, c.tip), EditorStyles.miniBoldLabel);
            hx += c.width + 4f;
        }
        GUI.Label(new Rect(hx + 2f, head.y + 2f, 320f, 16f), new GUIContent("설명", "스킬 설명창(V)의 대기 소환수 칸에 나오는 문구"), EditorStyles.miniBoldLabel);

        foreach (var r in _rows)
        {
            var m = r.data;
            if (m == null) continue;
            var so = new SerializedObject(m);
            so.Update();
            var list = so.FindProperty("standbyInfusions");

            // 소환수 줄
            Rect line = GUILayoutUtility.GetRect(total, 22f, GUILayout.Width(total));
            EditorGUI.DrawRect(line, m == source ? new Color(0.3f, 0.85f, 0.4f, 0.22f)
                                   : m == _selected ? new Color(0.24f, 0.48f, 0.9f, 0.25f) : new Color(1f, 1f, 1f, 0.05f));
            string indent = new string(' ', r.depth * 3) + (r.depth > 0 ? "└ " : "");
            if (GUI.Button(new Rect(line.x + 2f, line.y + 2f, 240f, 18f), indent + MinionLabel(m), EditorStyles.boldLabel)) Select(m);
            GUI.Label(new Rect(line.x + 248f, line.y + 2f, 160f, 18f), "스킬 상태: " + MinionStandbyInfusion.StatusLabel(SkillStatus(m)), EditorStyles.miniLabel);
            if (GUI.Button(new Rect(line.x + 412f, line.y + 2f, 80f, 18f), "+ 효과 추가", EditorStyles.miniButton) && list != null)
            {
                int i = list.arraySize;
                list.InsertArrayElementAtIndex(i);
                var e = list.GetArrayElementAtIndex(i);
                if (i == 0)
                {
                    var st = SkillStatus(m);
                    e.FindPropertyRelative("status").intValue = (int)(st == StatusType.None ? StatusType.Freeze : st);
                    e.FindPropertyRelative("chance").floatValue = 0.5f;
                    e.FindPropertyRelative("swings").intValue = 0;
                    e.FindPropertyRelative("duration").floatValue = 0f;
                }
            }
            GUI.Label(new Rect(line.x + 500f, line.y + 2f, total - 500f, 18f), MinionStandbyInfusion.DescribeAll(m.standbyInfusions), EditorStyles.miniLabel);

            // 효과 줄
            int remove = -1;
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                Rect row = GUILayoutUtility.GetRect(total, 20f, GUILayout.Width(total));
                float x = row.x;
                Rect Next(int col) { var rc = new Rect(x + 2f, row.y + 1f, StandbyCols[col].width, 18f); x += StandbyCols[col].width + 4f; return rc; }
                GUI.Label(Next(0), $"      효과 {i + 1}", EditorStyles.miniLabel);
                EditorGUI.PropertyField(Next(1), e.FindPropertyRelative("status"), GUIContent.none);
                EditorGUI.PropertyField(Next(2), e.FindPropertyRelative("chance"), GUIContent.none);
                EditorGUI.PropertyField(Next(3), e.FindPropertyRelative("swings"), GUIContent.none);
                EditorGUI.PropertyField(Next(4), e.FindPropertyRelative("duration"), GUIContent.none);
                if (GUI.Button(Next(5), new GUIContent("✕", "이 효과 삭제"), EditorStyles.miniButton)) remove = i;
            }
            if (remove >= 0) list.DeleteArrayElementAtIndex(remove);
            if (so.ApplyModifiedProperties()) _preview.MarkDirty();
            GUILayout.Space(2f);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox("초록 줄 = 플레이 중 지금 평타에 효과를 묻히는 대기 소환수.\n값은 각 소환수 에셋(standbyInfusions)에 바로 저장됩니다(Ctrl+Z 가능).", MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    /// <summary>소환수가 스킬 → 마무리 → 대쉬 순서로 가진 첫 상태이상.</summary>
    private static StatusType SkillStatus(MainMinionDataSO m)
    {
        var skill = m.minionSkill as MinionActionSkillSO;
        if (skill != null && skill.onHitStatus != StatusType.None) return skill.onHitStatus;
        if (m.finisher != null && m.finisher.onHitStatus != StatusType.None) return m.finisher.onHitStatus;
        if (m.dashModifier != null && m.dashModifier.onHitStatus != StatusType.None) return m.dashModifier.onHitStatus;
        return StatusType.None;
    }

    private static IEnumerable<MainMinionDataSO> AllMainMinions()
        => AssetDatabase.FindAssets("t:MainMinionDataSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null);

    [MenuItem("Tools/기획/대기 효과 빈 소환수 채우기 (스킬 상태이상, 50%)", priority = 21)]
    public static void FillStandbyInfusionDefaults()
    {
        int changed = 0;
        foreach (var m in AllMainMinions())
        {
            if (m.standbyInfusions != null && m.standbyInfusions.Length > 0) continue;
            var status = SkillStatus(m);
            if (status == StatusType.None) continue;
            var so = new SerializedObject(m);
            so.Update();
            var list = so.FindProperty("standbyInfusions");
            list.arraySize = 1;
            var e = list.GetArrayElementAtIndex(0);
            e.FindPropertyRelative("status").intValue = (int)status;
            e.FindPropertyRelative("chance").floatValue = 0.5f;
            e.FindPropertyRelative("swings").intValue = 0;
            e.FindPropertyRelative("duration").floatValue = 0f;
            so.ApplyModifiedProperties();
            changed++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[기획] 대기 효과 빈 소환수 채우기 완료: {changed}개");
    }

    /// <summary>모든 소환수의 모든 대기 효과 확률을 한 값으로 맞춘다.</summary>
    private static void SetAllStandbyChance(float chance)
    {
        foreach (var m in AllMainMinions())
        {
            var so = new SerializedObject(m);
            so.Update();
            var list = so.FindProperty("standbyInfusions");
            for (int i = 0; list != null && i < list.arraySize; i++)
                list.GetArrayElementAtIndex(i).FindPropertyRelative("chance").floatValue = chance;
            so.ApplyModifiedProperties();
        }
        AssetDatabase.SaveAssets();
    }

    // ── 상세 편집 ───────────────────────────────────────────────────
    private void DrawDetail()
    {
        if (_selected == null) { EditorGUILayout.HelpBox("왼쪽에서 소환수를 고르세요.", MessageType.Info); return; }

        // 헤더
        using (new EditorGUILayout.HorizontalScope("box"))
        {
            Rect icon = GUILayoutUtility.GetRect(48f, 48f, GUILayout.Width(48f), GUILayout.Height(48f));
            MinionAnimPreviewer.DrawSprite(icon, _selected.minionIcon);
            using (new EditorGUILayout.VerticalScope())
            {
                var big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
                GUILayout.Label(string.IsNullOrEmpty(_selected.minionName) ? _selected.name : _selected.minionName, big);
                var parents = _rows.Where(r => r.data != null && r.data.evolutions != null && r.data.evolutions.Contains(_selected)).Select(r => r.data.minionName);
                var evo = _selected.evolutions != null ? _selected.evolutions.Where(e => e != null).Select(e => e.minionName) : Enumerable.Empty<string>();
                string line = _selected.name;
                if (parents.Any()) line += "   ·   진화 전: " + string.Join(", ", parents);
                if (evo.Any()) line += "   ·   진화: " + string.Join(", ", evo);
                GUILayout.Label(line, EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(110f)))
            {
                if (GUILayout.Button("소환수 에셋 선택")) { Selection.activeObject = _selected; EditorGUIUtility.PingObject(_selected); }
                using (new EditorGUI.DisabledScope(_selected.minionSkill == null))
                    if (GUILayout.Button("스킬 에셋 선택")) { Selection.activeObject = _selected.minionSkill; EditorGUIUtility.PingObject(_selected.minionSkill); }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            // 인스펙터
            using (new EditorGUILayout.VerticalScope())
            {
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

                if (_selected.minionSkill != null)
                {
                    _skillFold = EditorGUILayout.Foldout(_skillFold, $"스페이스바 스킬 · {_selected.minionSkill.skillName}  ({_selected.minionSkill.name})", true, EditorStyles.foldoutHeader);
                    if (_skillFold)
                    {
                        if (_skillUsers.TryGetValue(_selected.minionSkill, out var users) && users.Count > 1)
                            EditorGUILayout.HelpBox("이 스킬 에셋은 " + string.Join(", ", users.Select(u => u.minionName)) +
                                                    " 이(가) 같이 씁니다. 여기서 고치면 모두 바뀝니다.", MessageType.Warning);
                        Editor.CreateCachedEditor(_selected.minionSkill, null, ref _skillEditor);
                        EditorGUI.BeginChangeCheck();
                        using (new EditorGUILayout.VerticalScope("box")) _skillEditor.OnInspectorGUI();
                        if (EditorGUI.EndChangeCheck()) _preview.MarkDirty();
                    }
                    EditorGUILayout.Space(6f);
                }

                EditorGUILayout.LabelField("소환수 설정 (마무리 · 대쉬 · 애니메이션 · 강화 · 진화)", EditorStyles.boldLabel);
                Editor.CreateCachedEditor(_selected, null, ref _minionEditor);
                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.VerticalScope("box")) _minionEditor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck()) _preview.MarkDirty();

                EditorGUILayout.EndScrollView();
            }

            // 미리보기
            using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(PreviewWidth)))
                _preview.Draw(PreviewWidth - 12f);
        }
    }
}

/// <summary>
/// 소환수 애니메이션 미리보기. 플레이 모드 없이 스프라이트 프레임을 그려서 재생한다.
/// 게임(MinionSkillCaster)과 같은 규칙을 흉내 낸다:
///  · sequence 의 태그를 순서대로 잇고, 전체를 duration(시전시간)에 맞춰 배속한다.
///  · 각 태그의 offset/snap/movementCurve 로 인형 위치를 옮긴다(오른쪽을 볼 때 기준).
///  · effectState 는 본체 뒤에 겹쳐 그린다.
///  · OnHitEvent / OnAttackEndEvent 위치와 damageState 구간을 타임라인에 표시한다.
/// 에셋은 아무것도 바꾸지 않는다.
/// </summary>
public class MinionAnimPreviewer
{
    private enum Source { Skill, Basic, Dash, AllClips }
    private static readonly string[] SourceLabels = { "스킬", "마무리(평타)", "대쉬", "클립 전체" };

    private class Segment
    {
        public string tag;
        public AnimationClip clip;
        public ObjectReferenceKeyframe[] frames;
        public AnimationEvent[] events;
        public float start;      // 배속 적용된 타임라인 기준 시작(초)
        public float length;     // 배속 적용된 길이(초)
        public float rawLength;  // 클립 원래 길이
        public Vector2 offset;
        public bool snap;
        public AnimationCurve curve;
        public bool moves;
        public bool missing;
    }

    private MainMinionDataSO _minion;
    private Source _source = Source.Skill;
    private bool _dirty = true;

    private readonly List<Segment> _segments = new List<Segment>();
    private Segment _effect;               // 겹쳐 재생할 이펙트(전체 타임라인에 맞춰 늘림)
    private string _damageState, _hitEvent;
    private float _total, _rawTotal;
    private readonly List<string> _notes = new List<string>();

    private List<AnimationClip> _allClips = new List<AnimationClip>();
    private string[] _allClipNames = new string[0];
    private int _clipIndex;

    private float _time;
    private bool _playing = true, _loop = true;
    private int _speedIndex = 2;
    private static readonly float[] Speeds = { 0.25f, 0.5f, 1f };
    private static readonly string[] SpeedLabels = { "0.25배", "0.5배", "1배" };
    private double _lastTick;
    private bool _faceLeft;
    private bool _showGrid = true;

    public void SetMinion(MainMinionDataSO m)
    {
        _minion = m;
        _time = 0f;
        _dirty = true;
    }

    public void MarkDirty() => _dirty = true;

    /// <summary>재생 중이면 시간을 진행시키고 true(다시 그려야 함)를 돌려준다.</summary>
    public bool Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTick);
        _lastTick = now;
        if (!_playing || _total <= 0f) return false;
        _time += Mathf.Min(dt, 0.1f) * Speeds[_speedIndex];
        if (_time > _total)
        {
            if (_loop) _time = _total > 0f ? _time % (_total + 0.35f) : 0f; // 끝에서 잠깐 멈췄다가 다시
            else { _time = _total; _playing = false; }
        }
        return true;
    }

    // ── 시퀀스 구성 ─────────────────────────────────────────────────
    private void BuildIfNeeded()
    {
        if (!_dirty) return;
        _dirty = false;
        _segments.Clear();
        _effect = null;
        _notes.Clear();
        _damageState = _hitEvent = null;
        _total = _rawTotal = 0f;
        _allClips.Clear();
        if (_minion == null) return;

        GameObject visual = null;
        switch (_source)
        {
            case Source.Skill: visual = _minion.skillAnim?.visual; break;
            case Source.Basic: visual = _minion.basicAnim?.visual; break;
            case Source.Dash: visual = _minion.dashAnim?.visual; break;
            case Source.AllClips:
                visual = _minion.skillAnim?.visual ?? _minion.basicAnim?.visual ?? _minion.dashAnim?.visual; break;
        }
        if (visual == null) { _notes.Add("이 항목에 visual(애니메이션 프리팹)이 비어 있습니다. 게임에서는 히트박스만 나갑니다."); return; }

        var lookup = ClipLookup.Build(visual);
        _allClips = lookup.clips;
        _allClipNames = _allClips.Select(c => $"{c.name}  ({c.length:0.00}초)").ToArray();
        if (lookup.clips.Count == 0) { _notes.Add($"'{visual.name}' 에서 Animator/애니메이션 클립을 찾지 못했습니다."); return; }

        if (_source == Source.AllClips)
        {
            _clipIndex = Mathf.Clamp(_clipIndex, 0, _allClips.Count - 1);
            AddSegment(lookup, _allClips[_clipIndex].name, _allClips[_clipIndex], Vector2.zero, false, null);
            Rescale(0f);
            return;
        }

        if (_source == Source.Dash)
        {
            var d = _minion.dashAnim;
            var dm = _minion.dashModifier;
            bool originOnly = dm != null && dm.hitAtOrigin;
            foreach (var (tag, label) in new[] { (d.startClip, "출발"), (d.hitBoxClip, "히트박스"), (d.endClip, "종료") })
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                if (originOnly && label != "출발") { _notes.Add($"출발점 판정형 대쉬라 '{label}' 클립({tag})은 게임에서 무시됩니다."); continue; }
                var seg = AddSegment(lookup, tag, lookup.Find(tag), Vector2.zero, false, null);
                if (seg != null) { seg.tag = $"{label}:{tag}"; if (d.clipDuration > 0f) { seg.length = d.clipDuration; } }
            }
            // 대쉬는 클립마다 clipDuration(0=원래 길이) — 전체 배속이 아니다.
            float t = 0f;
            foreach (var s in _segments) { s.start = t; t += s.length; }
            _total = t; _rawTotal = _segments.Sum(s => s.rawLength);
            _notes.Add("대쉬 연출은 실제로는 출발/히트박스/종료 '서로 다른 위치'에서 차례로 재생됩니다. 여기선 한 자리에서 이어서 보여줍니다.");
            return;
        }

        var set = _source == Source.Skill ? _minion.skillAnim : _minion.basicAnim;
        _damageState = set.damageState;
        _hitEvent = string.IsNullOrEmpty(set.hitEvent) ? "OnHitEvent" : set.hitEvent;

        var tags = set.sequence != null ? set.sequence.Where(p => p != null && !string.IsNullOrWhiteSpace(p.tag)).ToList() : new List<AnimPhase>();
        if (tags.Count == 0)
        {
            // 게임도 시퀀스가 비면 OnHitEvent 가 박힌 클립을 자동으로 골라 재생한다.
            var auto = lookup.clips.FirstOrDefault(c => AnimationUtility.GetAnimationEvents(c).Any(e => e.functionName == _hitEvent)) ?? lookup.clips[0];
            _notes.Add($"sequence 가 비어 있어 '{auto.name}' 클립을 자동으로 고른 것으로 보여줍니다.");
            AddSegment(lookup, auto.name, auto, Vector2.zero, false, null);
        }
        else
        {
            foreach (var p in tags) AddSegment(lookup, p.tag, lookup.Find(p.tag), p.offset, p.snap, p.movementCurve);
        }

        if (!string.IsNullOrWhiteSpace(set.effectState))
        {
            var ec = lookup.Find(set.effectState);
            if (ec == null) _notes.Add($"effectState '{set.effectState}' 클립을 찾지 못했습니다.");
            else _effect = MakeSegment(set.effectState, ec);
        }

        Rescale(set.duration);

        bool hasEvent = _segments.Any(s => s.events != null && s.events.Any(e => e.functionName == _hitEvent))
                        || (_effect != null && _effect.events.Any(e => e.functionName == _hitEvent));
        if (!hasEvent)
        {
            if (!string.IsNullOrEmpty(_damageState)) _notes.Add($"타격 이벤트가 없어 damageState '{_damageState}' 구간을 판정창으로 씁니다.");
            else _notes.Add("타격 이벤트도 damageState 도 없어, 게임에선 시퀀스 전체를 판정창으로 칩니다(부정확).");
        }
    }

    private Segment MakeSegment(string tag, AnimationClip clip)
    {
        var seg = new Segment { tag = tag, clip = clip };
        if (clip == null) { seg.missing = true; seg.rawLength = 0.3f; seg.events = new AnimationEvent[0]; return seg; }
        seg.rawLength = Mathf.Max(0.01f, clip.length);
        seg.events = AnimationUtility.GetAnimationEvents(clip);
        var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .Where(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
            .OrderBy(b => b.path.Length).FirstOrDefault();
        if (binding.type != null) seg.frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
        return seg;
    }

    private Segment AddSegment(ClipLookup lookup, string tag, AnimationClip clip, Vector2 offset, bool snap, AnimationCurve curve)
    {
        var seg = MakeSegment(tag, clip);
        if (seg.missing) _notes.Add($"태그 '{tag}' 에 해당하는 클립이 없습니다. 게임에서도 재생되지 않습니다.");
        seg.offset = offset;
        seg.snap = snap;
        seg.curve = curve;
        seg.moves = offset.sqrMagnitude > 0.0001f;
        seg.length = seg.rawLength;
        _segments.Add(seg);
        return seg;
    }

    /// <summary>시퀀스 전체를 duration 에 맞춰 늘이거나 줄인다(0 이하면 1초 — MinionAnimSet.ResolvedDuration 과 동일).</summary>
    private void Rescale(float duration)
    {
        _rawTotal = _segments.Sum(s => s.rawLength);
        float target = _source == Source.AllClips ? _rawTotal : (duration > 0f ? duration : 1f);
        float k = _rawTotal > 0f ? target / _rawTotal : 1f;
        float t = 0f;
        foreach (var s in _segments) { s.start = t; s.length = s.rawLength * k; t += s.length; }
        _total = t;
        if (_effect != null) { _effect.start = 0f; _effect.length = _total; }
        if (_time > _total) _time = 0f;
    }

    // ── 그리기 ─────────────────────────────────────────────────────
    public void Draw(float width)
    {
        if (_minion == null) { EditorGUILayout.HelpBox("소환수를 고르세요.", MessageType.Info); return; }

        EditorGUI.BeginChangeCheck();
        _source = (Source)GUILayout.Toolbar((int)_source, SourceLabels);
        if (EditorGUI.EndChangeCheck()) { _dirty = true; _time = 0f; }
        BuildIfNeeded();

        if (_source == Source.AllClips && _allClips.Count > 0)
        {
            EditorGUI.BeginChangeCheck();
            _clipIndex = EditorGUILayout.Popup(_clipIndex, _allClipNames);
            if (EditorGUI.EndChangeCheck()) { _dirty = true; _time = 0f; BuildIfNeeded(); }
        }

        // 화면
        Rect view = GUILayoutUtility.GetRect(width, 260f, GUILayout.Width(width), GUILayout.Height(260f));
        EditorGUI.DrawRect(view, new Color(0.16f, 0.17f, 0.2f));
        DrawStage(view);

        // 타임라인
        Rect tl = GUILayoutUtility.GetRect(width, 46f, GUILayout.Width(width));
        DrawTimeline(tl);

        // 컨트롤
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(_playing ? "❚❚ 정지" : "▶ 재생", GUILayout.Width(70f)))
            {
                if (!_playing && _time >= _total) _time = 0f;
                _playing = !_playing;
            }
            if (GUILayout.Button("⟲", GUILayout.Width(26f))) _time = 0f;
            _loop = GUILayout.Toggle(_loop, "반복", GUILayout.Width(46f));
            _faceLeft = GUILayout.Toggle(_faceLeft, "왼쪽 보기", GUILayout.Width(72f));
            _speedIndex = EditorGUILayout.Popup(_speedIndex, SpeedLabels, GUILayout.Width(60f));
        }
        EditorGUI.BeginChangeCheck();
        float scrub = EditorGUILayout.Slider(_time, 0f, Mathf.Max(0.01f, _total));
        if (EditorGUI.EndChangeCheck()) { _time = scrub; _playing = false; }
        _showGrid = EditorGUILayout.ToggleLeft("격자(1유닛) 표시", _showGrid);

        // 정보
        if (_source == Source.Skill || _source == Source.Basic)
        {
            var set = _source == Source.Skill ? _minion.skillAnim : _minion.basicAnim;
            float speed = _total > 0f ? _rawTotal / _total : 1f;
            EditorGUILayout.LabelField($"시전시간 {_total:0.00}초  ·  원본 {_rawTotal:0.00}초  ·  재생속도 x{speed:0.##}", EditorStyles.miniLabel);
            if (set.duration <= 0f) EditorGUILayout.LabelField("duration 이 0 이라 1초로 칩니다.", EditorStyles.miniLabel);
        }
        else EditorGUILayout.LabelField($"길이 {_total:0.00}초", EditorStyles.miniLabel);

        var cur = SegmentAt(_time);
        if (cur != null) EditorGUILayout.LabelField($"현재: {cur.tag}" + (cur.clip != null ? $"  ({cur.clip.name})" : ""), EditorStyles.miniLabel);

        foreach (var n in _notes) EditorGUILayout.HelpBox(n, MessageType.None);
        EditorGUILayout.HelpBox("● 빨강 = 타격 이벤트   ● 회색 = 공격 끝 이벤트   주황 띠 = damageState 구간", MessageType.None);
    }

    private Segment SegmentAt(float t)
    {
        if (_segments.Count == 0) return null;
        foreach (var s in _segments) if (t < s.start + s.length) return s;
        return _segments[_segments.Count - 1];
    }

    private static Sprite FrameAt(Segment s, float timelineT)
    {
        if (s == null || s.frames == null || s.frames.Length == 0) return null;
        float local = s.length > 0f ? Mathf.Clamp01((timelineT - s.start) / s.length) * s.rawLength : 0f;
        Object found = s.frames[0].value;
        foreach (var k in s.frames) { if (k.time <= local + 0.0001f) found = k.value; else break; }
        return found as Sprite;
    }

    private Vector2 PositionAt(float t)
    {
        Vector2 pos = Vector2.zero;
        foreach (var s in _segments)
        {
            if (t < s.start) break;
            if (!s.moves) continue;
            float u = s.length > 0f ? Mathf.Clamp01((t - s.start) / s.length) : 1f;
            if (s.snap) { pos = s.offset; continue; }
            float k = s.curve != null && s.curve.length > 0 ? s.curve.Evaluate(u) : u;
            pos = Vector2.LerpUnclamped(pos, s.offset, k);
        }
        return pos;
    }

    private void DrawStage(Rect view)
    {
        var main = SegmentAt(_time);
        Sprite body = FrameAt(main, _time);
        Sprite fx = _effect != null ? FrameAt(_effect, _time) : null;

        // 화면 배율: 등장하는 스프라이트 중 가장 큰 것이 화면의 55% 안에 들도록, 정수 배(픽셀아트).
        float ppu = 16f, maxW = 16f, maxH = 16f;
        foreach (var s in _segments.Concat(_effect != null ? new[] { _effect } : new Segment[0]))
        {
            if (s.frames == null) continue;
            foreach (var k in s.frames)
                if (k.value is Sprite sp)
                {
                    ppu = sp.pixelsPerUnit;
                    maxW = Mathf.Max(maxW, sp.rect.width); maxH = Mathf.Max(maxH, sp.rect.height);
                }
        }
        float maxOffset = _segments.Count > 0 ? _segments.Max(s => Mathf.Abs(s.offset.x)) : 0f;
        float scale = Mathf.Max(1f, Mathf.Floor(Mathf.Min(view.width * 0.55f / (maxW + maxOffset * ppu * 2f), view.height * 0.55f / maxH)));
        float unit = ppu * scale; // 1 유닛 = 화면 픽셀

        Vector2 origin = new Vector2(view.center.x, view.y + view.height * 0.68f);

        if (_showGrid)
        {
            var gc = new Color(1f, 1f, 1f, 0.06f);
            for (float x = origin.x % unit; x < view.xMax; x += unit) if (x > view.x) EditorGUI.DrawRect(new Rect(x, view.y, 1f, view.height), gc);
            for (float y = view.y + (origin.y - view.y) % unit; y < view.yMax; y += unit) EditorGUI.DrawRect(new Rect(view.x, y, view.width, 1f), gc);
        }
        EditorGUI.DrawRect(new Rect(view.x, origin.y, view.width, 1f), new Color(1f, 1f, 1f, 0.18f));
        EditorGUI.DrawRect(new Rect(origin.x - 6f, origin.y - 1f, 13f, 3f), new Color(0.4f, 0.9f, 1f, 0.7f)); // 스폰 지점

        Vector2 pos = PositionAt(_time);
        if (_faceLeft) pos.x = -pos.x;
        Vector2 at = origin + new Vector2(pos.x * unit, -pos.y * unit);

        GUI.BeginClip(view);
        Vector2 local = at - view.position;
        if (fx != null) DrawSpriteAtPivot(local, fx, scale, _faceLeft, 0.85f);
        if (body != null) DrawSpriteAtPivot(local, body, scale, _faceLeft, 1f);
        GUI.EndClip();

        if (body == null && fx == null)
            GUI.Label(view, _segments.Count == 0 ? "재생할 애니메이션 없음" : "(이 클립에 스프라이트 프레임이 없음)", EditorStyles.centeredGreyMiniLabel);

        // 타격 순간 번쩍임
        if (IsNearEvent(_time, _hitEvent, 0.05f))
            EditorGUI.DrawRect(new Rect(view.x, view.y, view.width, 3f), new Color(1f, 0.25f, 0.25f, 0.9f));
    }

    private bool IsNearEvent(float t, string fn, float window)
    {
        if (string.IsNullOrEmpty(fn)) return false;
        foreach (var e in EventTimes(fn)) if (t >= e && t < e + window) return true;
        return false;
    }

    private IEnumerable<float> EventTimes(string fn)
    {
        foreach (var s in _segments.Concat(_effect != null ? new[] { _effect } : new Segment[0]))
        {
            if (s.events == null) continue;
            foreach (var e in s.events)
                if (e.functionName == fn) yield return s.start + (s.rawLength > 0f ? e.time / s.rawLength : 0f) * s.length;
        }
    }

    private void DrawTimeline(Rect r)
    {
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
        if (_total <= 0f) return;
        float X(float t) => r.x + 2f + (r.width - 4f) * Mathf.Clamp01(t / _total);

        var palette = new[] { new Color(0.3f, 0.55f, 0.9f, 0.6f), new Color(0.35f, 0.75f, 0.5f, 0.6f), new Color(0.65f, 0.45f, 0.85f, 0.6f) };
        for (int i = 0; i < _segments.Count; i++)
        {
            var s = _segments[i];
            var seg = new Rect(X(s.start), r.y + 4f, Mathf.Max(2f, X(s.start + s.length) - X(s.start) - 1f), 18f);
            EditorGUI.DrawRect(seg, s.missing ? new Color(0.8f, 0.2f, 0.2f, 0.6f) : palette[i % palette.Length]);
            GUI.Label(seg, s.tag, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(_damageState) && s.tag == _damageState)
                EditorGUI.DrawRect(new Rect(seg.x, r.y + 23f, seg.width, 4f), new Color(1f, 0.6f, 0.15f, 0.9f));
        }
        if (_effect != null)
        {
            var er = new Rect(X(0f), r.y + 28f, X(_total) - X(0f), 6f);
            EditorGUI.DrawRect(er, new Color(1f, 1f, 1f, 0.12f));
        }

        foreach (float t in EventTimes(_hitEvent ?? "OnHitEvent"))
            EditorGUI.DrawRect(new Rect(X(t) - 2f, r.y + 2f, 4f, 40f), new Color(1f, 0.25f, 0.25f, 0.95f));
        foreach (float t in EventTimes("OnAttackEndEvent"))
            EditorGUI.DrawRect(new Rect(X(t) - 1f, r.y + 2f, 3f, 40f), new Color(0.7f, 0.7f, 0.7f, 0.9f));

        EditorGUI.DrawRect(new Rect(X(_time) - 1f, r.y, 2f, r.height), Color.white);
        GUI.Label(new Rect(r.x + 2f, r.yMax - 14f, 120f, 14f), $"{_time:0.00} / {_total:0.00}초", EditorStyles.miniLabel);

        // 타임라인 클릭/드래그로 이동
        var ev = Event.current;
        if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag) && r.Contains(ev.mousePosition))
        {
            _time = Mathf.Clamp01((ev.mousePosition.x - r.x - 2f) / (r.width - 4f)) * _total;
            _playing = false;
            ev.Use();
        }
    }

    // ── 스프라이트 그리기 유틸 ──────────────────────────────────────
    /// <summary>스프라이트를 rect 안에 비율을 지켜 그린다(아이콘용).</summary>
    public static void DrawSprite(Rect r, Sprite s)
    {
        if (s == null || s.texture == null) { EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.2f)); return; }
        var tr = s.textureRect;
        float k = Mathf.Min(r.width / tr.width, r.height / tr.height);
        var dst = new Rect(0f, 0f, tr.width * k, tr.height * k) { center = r.center };
        GUI.DrawTextureWithTexCoords(dst, s.texture, UV(s));
    }

    private static void DrawSpriteAtPivot(Vector2 at, Sprite s, float scale, bool flip, float alpha)
    {
        if (s == null || s.texture == null) return;
        var tr = s.textureRect;
        Vector2 pivot = s.pivot; // 픽셀, 왼쪽 아래 기준
        float w = tr.width * scale, h = tr.height * scale;
        float px = (flip ? tr.width - pivot.x : pivot.x) * scale;
        var dst = new Rect(at.x - px, at.y - (tr.height - pivot.y) * scale, w, h);
        var uv = UV(s);
        if (flip) uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTextureWithTexCoords(dst, s.texture, uv);
        GUI.color = prev;
    }

    private static Rect UV(Sprite s)
    {
        var tr = s.textureRect;
        var tex = s.texture;
        return new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
    }

    // ── 태그 → 클립 찾기 ────────────────────────────────────────────
    private class ClipLookup
    {
        public readonly List<AnimationClip> clips = new List<AnimationClip>();
        private readonly Dictionary<string, AnimationClip> _byState = new Dictionary<string, AnimationClip>();

        public static ClipLookup Build(GameObject visual)
        {
            var l = new ClipLookup();
            foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
            {
                var rac = animator.runtimeAnimatorController;
                if (rac == null) continue;
                foreach (var c in rac.animationClips) if (c != null && !l.clips.Contains(c)) l.clips.Add(c);
                var ac = rac as AnimatorController ?? (rac as AnimatorOverrideController)?.runtimeAnimatorController as AnimatorController;
                if (ac == null) continue;
                foreach (var layer in ac.layers) l.Collect(layer.stateMachine);
            }
            return l;
        }

        private void Collect(AnimatorStateMachine sm)
        {
            if (sm == null) return;
            foreach (var cs in sm.states)
                if (cs.state != null && cs.state.motion is AnimationClip clip && !_byState.ContainsKey(cs.state.name))
                    _byState[cs.state.name] = clip;
            foreach (var sub in sm.stateMachines) Collect(sub.stateMachine);
        }

        /// <summary>게임은 태그 = 애니메이터 상태 이름으로 재생한다. 상태가 없으면 클립 이름으로 한 번 더 찾는다.</summary>
        public AnimationClip Find(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            if (_byState.TryGetValue(tag, out var c)) return c;
            return clips.FirstOrDefault(x => x.name == tag)
                ?? clips.FirstOrDefault(x => x.name.EndsWith("_" + tag) || x.name.EndsWith(" " + tag) || x.name.EndsWith("." + tag));
        }
    }
}
