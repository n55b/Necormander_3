using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 기획 데이터 전체 검사. 창(Tools/기획/전체 검사)과 빌드 전 검사(DesignerValidationBuildHook)가 같은 RunAll 을 쓴다.
///  · 적 · 장비 · 아이템 — 각 편집기의 '확인 필요' 검사를 그대로 부른다(EnemyAssets / EquipmentAssets / ItemEditorGUI.Warnings).
///  · 소환수 — 소환수 관리 창에는 밖에서 부를 검사가 없어 여기서 게임 코드 기준으로 검사한다(MinionWarnings).
///  · 번역 — LocSync 가 검사 결과(키 없는 문구, 읽지 못한 Loc.T/Loc.F)를 밖으로 내주지 않아 하지 않는다.
///    'Tools/번역/번역 표 갱신' 콘솔 요약을 본다. SO 의 LocText 키는 LocSync 가 에디터 시작 때 자동으로 붙인다.
/// 에셋은 읽기만 한다.
/// </summary>
public static class DesignerValidation
{
    public static readonly string[] Areas = { "적", "장비", "아이템", "소환수" };

    public sealed class Issue
    {
        public string area, name, message;
        public UnityEngine.Object asset;
    }

    // 소환수 자동 공격·대쉬 히트박스의 기본값을 가진 플레이어 프리팹(MinionManagerWindow 가 고치는 것과 같은 파일).
    private const string PlayerPrefabPath = "Assets/Prefabs/Player Melee.prefab";

    /// <summary>모든 검사를 돌려 확인 필요 항목을 돌려준다. 같은 에셋의 항목은 붙어 나온다.</summary>
    public static List<Issue> RunAll()
    {
        var list = new List<Issue>();
        EnemyAssets.InvalidateUsage(); // 적 경고는 1초 캐시가 있어, 방금 고친 값이 안 보일 수 있다
        foreach (var e in EnemyAssets.All())
            Collect(list, "적", e, EnemyAssets.DisplayName(e), () => EnemyAssets.Warnings(e));

        var growth = EquipmentAssets.Growth;
        var shop = EquipmentAssets.Shop;
        foreach (var e in EquipmentAssets.All())
            Collect(list, "장비", e, EquipmentAssets.DisplayName(e), () => EquipmentAssets.Warnings(e, growth, shop));

        // 아이템 편집기(ItemDatabaseWindow.Reload)와 같은 목록 — Deprecated 도 빼지 않는다(편집기 경고 수와 맞춤).
        foreach (var i in AssetDatabase.FindAssets("t:ItemSO").Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null))
            Collect(list, "아이템", i, i.DisplayName, () => ItemEditorGUI.Warnings(i));

        var minions = Load<MainMinionDataSO>().OrderBy(m => m.name, StringComparer.Ordinal).ToList();
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var combat = player != null ? player.GetComponent<MeleeCombatController>() : null;
        var dodge = player != null ? player.GetComponent<MeleeDodgeController>() : null;
        var ctx = new MinionContext
        {
            growth = growth,
            evolveTargets = new HashSet<MainMinionDataSO>(minions.Where(m => m.evolutions != null)
                .SelectMany(m => m.evolutions.Where(x => x != null && x != m))),
            playerFound = player != null,
            telegraph = combat != null ? combat.TelegraphPrefab : null,
        };
        if (dodge != null)
            using (var so = new SerializedObject(dodge))
            {
                ctx.dashBox = so.FindProperty("dashHitBoxPrefab")?.objectReferenceValue as BaseHitBox;
                ctx.originBox = so.FindProperty("originHitBoxPrefab")?.objectReferenceValue as BaseHitBox;
            }
        foreach (var m in minions)
            Collect(list, "소환수", m, string.IsNullOrWhiteSpace(m.minionName.ko) ? m.name : m.minionName.ko, () => MinionWarnings(m, ctx));
        return list;
    }

    public static string CountText(List<Issue> issues) => string.Join(" · ", Areas.Select(a => $"{a} {issues.Count(i => i.area == a)}"));

    /// <summary>콘솔 한 줄 요약 + 앞의 maxLines 개 항목(항목은 첫 줄만).</summary>
    public static string Summary(List<Issue> issues, int maxLines)
    {
        var sb = new StringBuilder($"[전체 검사] 확인 필요 {issues.Count}건 ({CountText(issues)}) — Tools/기획/전체 검사 에서 보고 고치세요. 빌드는 계속됩니다.");
        foreach (var i in issues.Take(maxLines)) sb.Append($"\n[{i.area}] {i.name}: {i.message.Split('\n')[0]}");
        if (issues.Count > maxLines) sb.Append($"\n… 외 {issues.Count - maxLines}건");
        return sb.ToString();
    }

    private static void Collect(List<Issue> list, string area, UnityEngine.Object asset, string name, Func<List<string>> check)
    {
        List<string> msgs;
        try { msgs = check(); }
        catch (Exception e) { msgs = new List<string> { "검사 중 오류: " + e.Message }; } // 에셋 하나 때문에 전체 검사가 멈추지 않게
        foreach (var m in msgs) list.Add(new Issue { area = area, asset = asset, name = name, message = m });
    }

    // (소환수) Deprecated 폴더는 게임 목록 자동 갱신(GrowthRegistrySO.RefreshRegistry)처럼 뺀다.
    private static List<T> Load<T>() where T : UnityEngine.Object => AssetDatabase.FindAssets("t:" + typeof(T).Name)
        .Select(AssetDatabase.GUIDToAssetPath).Where(p => !p.Contains("/Deprecated/"))
        .Select(p => AssetDatabase.LoadAssetAtPath<T>(p)).Where(x => x != null).Distinct().ToList();

    // ── 소환수 ────────────────────────────────────────────────────────
    private sealed class MinionContext
    {
        public GrowthRegistrySO growth;
        public HashSet<MainMinionDataSO> evolveTargets; // 다른 소환수의 evolutions 에 들어 있는 것
        public bool playerFound;                        // 플레이어 프리팹을 찾았을 때만 '기본값도 없음'을 경고한다
        public GameObject telegraph;                    // MeleeCombatController.telegraphPrefab (자동 공격 기본)
        public BaseHitBox dashBox, originBox;           // MeleeDodgeController 대쉬 기본(경로형 / 출발점형)
    }

    private static List<string> MinionWarnings(MainMinionDataSO m, MinionContext c)
    {
        var w = new List<string>();

        // 등록 · 진화. 세이브 복원(InventoryManager.LoadFromData)은 GrowthRegistry.minionDatas 에서 에셋 이름(+formerAssetNames)으로 찾고,
        // 마을 선택(MinionSelectNPC)·보상 카드(RewardProcessor.GetValidCores)도 이 목록에서 진화형을 빼고 뽑는다.
        if (c.growth != null && !c.growth.minionDatas.Contains(m))
            w.Add((m.isEvolvedForm ? "GrowthRegistry 소환수 목록에 없어 진화 후 세이브를 불러오면 슬롯이 비워집니다"
                                   : "GrowthRegistry 소환수 목록에 없어 마을 선택·보상 카드에 나오지 않고, 세이브를 불러오면 슬롯이 비워집니다")
                  + "(세이브는 이 목록에서 에셋 이름으로 찾음). 'Tools/기획/레지스트리 새로고침'으로 등록하세요.");
        bool isTarget = c.evolveTargets.Contains(m);
        if (isTarget && !m.isEvolvedForm) w.Add("다른 소환수의 진화 형태인데 '진화형' 체크가 꺼져 있어 마을 선택·보상 카드에 시작 소환수로 나옵니다.");
        if (!isTarget && m.isEvolvedForm) w.Add("'진화형'인데 어느 소환수의 진화 목록(evolutions)에도 없어 얻을 방법이 없습니다(마을 선택·보상 카드에서는 빠짐).");

        // 보상 카드·슬롯 표시: rewardItemData 의 이름/아이콘이 비면 소환수 이름/아이콘을 쓴다.
        var rd = m.rewardItemData;
        if (string.IsNullOrWhiteSpace(m.minionName.ko) && (rd == null || string.IsNullOrWhiteSpace(rd.itemName.ko)))
            w.Add("게임 표시 이름이 비어 있습니다(소환수 이름·보상 표시 이름 둘 다 없음).");
        if ((rd == null || rd.icon == null) && m.minionIcon == null)
            w.Add("카드·슬롯 아이콘이 없습니다(보상 표시 아이콘·소환수 아이콘 둘 다 비어 있음).");

        // 우클릭 스킬 (PlayerSkillController: minionSkill 이 없으면 시전하지 않는다)
        if (m.minionSkill == null) w.Add("우클릭 스킬(minionSkill)이 비어 있어 우클릭을 눌러도 아무것도 나가지 않습니다.");
        else
        {
            // MinionActionSkillSO.Execute: useHitBox 인데 프리팹이 없으면 히트박스 없는 '가장 가까운 적 1명 즉시 타격' 분기로 간다.
            if (m.minionSkill is MinionActionSkillSO a && a.useHitBox && a.hitBoxPrefab == null)
                w.Add($"우클릭 스킬 '{a.name}'이 판정(useHitBox)을 켰는데 히트박스 프리팹이 없어, 범위 판정 없이 가장 가까운 적 하나만 때립니다.");
            CheckAnim(w, m.skillAnim, "우클릭 스킬");
        }

        // 자동 공격 (MeleeCombatController.CastSummonBasicAttack). hitCount 0 은 '자동 공격 없음'이라 검사하지 않는다.
        var fin = m.finisher;
        if (fin != null && fin.IsValid)
        {
            var box = fin.hitBoxPrefab != null ? fin.hitBoxPrefab : c.telegraph;
            if (box == null)
            {
                if (c.playerFound) w.Add("자동 공격 히트박스 프리팹이 없고 플레이어 기본값(Player Melee 의 Telegraph Prefab)도 비어 있어 자동 공격 피해가 나가지 않습니다.");
            }
            else if (box.GetComponent<BaseHitBox>() == null)
                w.Add($"자동 공격 히트박스 프리팹 '{box.name}' 루트에 BaseHitBox 가 없어 자동 공격 피해가 나가지 않습니다.");
            CheckAnim(w, m.basicAnim, "자동 공격");
        }

        // 대쉬 공격 (MeleeDodgeController.CanCastSummonDashAttack / MinionDashModifier.DealsDamage)
        var mod = m.dashModifier;
        if (mod != null && mod.hitCount > 0)
        {
            if (!mod.hitAtOrigin && mod.width <= 0f)
                w.Add($"대쉬 공격 타수가 {mod.hitCount}인데 폭(width)이 0이라 판정이 생기지 않습니다(출발점 판정형이 아니면 폭이 필요).");
            else if (mod.hitBoxPrefab == null && c.playerFound && (mod.hitAtOrigin ? c.originBox : c.dashBox) == null)
                w.Add($"대쉬 히트박스 프리팹이 없고 플레이어 기본값(Player Melee 의 {(mod.hitAtOrigin ? "Origin" : "Dash")} Hit Box Prefab)도 비어 있어 대쉬 공격이 나가지 않습니다.");
        }

        // 대쉬 연출 (MeleeDodgeController.PlaySummonDashVisual → MinionSkillCaster.PlayDashSequence). 피해가 없어도 연출은 나간다.
        // 출발점형은 start → hitBox → end 중 처음 채워진 하나만 재생한다.
        var da = m.dashAnim;
        var dashClips = da != null && da.visual != null ? Clips(da.visual) : null;
        if (dashClips != null)
        {
            var names = new HashSet<string>(dashClips.Select(x => x.name));
            var played = new[] { da.startClip, da.hitBoxClip, da.endClip }.Where(s => !string.IsNullOrWhiteSpace(s));
            if (mod != null && mod.hitAtOrigin) played = played.Take(1);
            foreach (var clip in played.Where(x => !names.Contains(x)).Distinct())
                w.Add($"대쉬 연출 클립 '{clip}'이(가) 프리팹 '{da.visual.name}' 애니메이터에 없어 그 자리에서 재생하지 않고 넘어갑니다.");
        }
        return w;
    }

    /// <summary>
    /// MinionSkillCaster.PlaySequenced 와 같은 규칙으로 '언제 때리는지'를 따라간다.
    /// 태그 = 애니메이터 클립 이름(ClipLength 가 이름으로 찾는다). 시퀀스가 비면 OnHitEvent 가 든 첫 클립(없으면 첫 클립)을 고른다.
    /// 판정 시점: (1) OnHitEvent+OnAttackEndEvent 둘 다 → (2) hitEvent 칸의 이벤트(단, 판정은 OnHitEvent 로만 열림) → (3) damageState 태그 → (4) 시퀀스 전체(부정확).
    /// 본체 클립에 이벤트가 없고 effectState 클립에 있으면 그쪽을 이벤트 소스로 본다.
    /// </summary>
    private static void CheckAnim(List<string> w, MinionAnimSet set, string label)
    {
        if (set == null || set.visual == null) { w.Add($"{label} 연출 프리팹(visual)이 비어 있어 인형 없이 히트박스만 나갑니다."); return; }
        string v = set.visual.name;
        var clips = Clips(set.visual);
        if (clips == null || clips.Length == 0) { w.Add($"{label} 프리팹 '{v}'에 애니메이터 컨트롤러(클립)가 없어 그림에 맞춘 타격 시점을 쓸 수 없습니다."); return; }

        var names = new HashSet<string>(clips.Select(c => c.name));
        var seq = set.SequenceTags();
        foreach (var t in seq.Where(t => !names.Contains(t)).Distinct())
            w.Add($"{label} 시퀀스의 '{t}' 클립이 프리팹 '{v}' 애니메이터에 없어 건너뜁니다.");
        if (seq.Length == 0) seq = new[] { (clips.FirstOrDefault(c => c.events.Any(e => e.functionName == "OnHitEvent")) ?? clips[0]).name };

        bool Has(string[] s, string ev) => clips.Any(c => Array.IndexOf(s, c.name) >= 0 && c.events.Any(e => e.functionName == ev));
        var evt = seq;
        if (!string.IsNullOrEmpty(set.effectState))
        {
            var eff = new[] { set.effectState };
            if (!names.Contains(set.effectState)) w.Add($"{label} 이펙트 태그(effectState) '{set.effectState}' 클립이 프리팹 '{v}' 애니메이터에 없습니다.");
            else if (!Has(seq, "OnHitEvent") && !Has(seq, "OnAttackEndEvent") && (Has(eff, "OnHitEvent") || Has(eff, "OnAttackEndEvent"))) evt = eff;
        }

        bool hit = Has(evt, "OnHitEvent");
        if (hit && Has(evt, "OnAttackEndEvent")) return;                                   // (1)
        if (!string.IsNullOrEmpty(set.hitEvent) && Has(evt, set.hitEvent))                 // (2)
        {
            // (2) 로 정해지면 판정은 relay.OnHit 에서만 열리는데, MinionAnimEventRelay 는 OnHitEvent 만 받는다.
            if (!hit) w.Add($"{label}: hitEvent '{set.hitEvent}' 이벤트는 클립에 있지만 게임은 OnHitEvent 만 받아(MinionAnimEventRelay) 판정이 열리지 않습니다(피해 0). " +
                            "hitEvent 칸과 Aseprite 이벤트를 OnHitEvent 로 바꾸세요.");
            return;
        }
        if (!string.IsNullOrEmpty(set.hitEvent))
            w.Add($"{label}: 재생 클립에 hitEvent '{set.hitEvent}' 이벤트가 없습니다(게임이 시전마다 경고). " +
                  (set.hitEvent == "OnHitEvent" ? "Aseprite 타격 프레임 셀 user data 에 event:OnHitEvent 를 넣으세요."
                      : "게임은 OnHitEvent 만 받으므로 hitEvent 칸을 OnHitEvent 로 바꾸고 Aseprite 타격 프레임 셀 user data 에 event:OnHitEvent 를 넣으세요."));
        else if (hit)
            w.Add($"{label}: 클립에 OnHitEvent 가 있지만 'hitEvent' 칸이 비어 있고 OnAttackEndEvent 도 없어 게임이 이 이벤트를 쓰지 않습니다. hitEvent 칸에 OnHitEvent 를 적으세요.");
        if (string.IsNullOrEmpty(set.damageState))                                          // (4)
            w.Add($"{label}: 쓸 수 있는 타격 이벤트가 없고 판정 태그(damageState)도 비어 있어 시퀀스 전체를 판정창으로 칩니다(부정확).");
        else if (Array.IndexOf(seq, set.damageState) < 0 || !names.Contains(set.damageState)) // (3) 인데 그 태그가 재생되지 않음
            w.Add($"{label}: 판정 태그(damageState) '{set.damageState}'가 재생 시퀀스에 없어 판정이 열리지 않습니다(피해 0).");
    }

    // 게임은 인스턴스의 GetComponentInChildren<Animator>() 를 쓴다. 프리팹 에셋에서는 꺼진 자식도 포함해 첫 Animator 를 본다.
    private static AnimationClip[] Clips(GameObject visual)
    {
        var anim = visual.GetComponentInChildren<Animator>(true);
        var rac = anim != null ? anim.runtimeAnimatorController : null;
        return rac == null ? null : rac.animationClips.Where(c => c != null).ToArray();
    }
}

/// <summary>빌드 직전에 같은 검사를 돌려 콘솔에 경고 요약 한 줄을 남긴다. 빌드는 절대 막지 않는다.</summary>
public class DesignerValidationBuildHook : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        try
        {
            var issues = DesignerValidation.RunAll();
            if (issues.Count > 0) Debug.LogWarning(DesignerValidation.Summary(issues, 20));
        }
        catch (Exception e) { Debug.LogWarning("[전체 검사] 검사 중 오류가 나서 건너뜁니다(빌드는 계속): " + e.Message); }
    }
}

/// <summary>
/// 전체 검사 창 (Tools/기획/전체 검사). 적·장비·아이템·소환수의 '확인 필요' 항목을 분야별로 모아 보여 준다.
/// 줄을 누르면 해당 편집기에서 그 에셋을 연다(소환수는 소환수 관리 창의 상세 탭).
/// 검사는 창을 열 때 · '다시 검사' · 프로젝트 변경(0.5초 모아서) 때만 돌고, 그리기마다 돌지 않는다.
/// </summary>
public class DesignerValidationWindow : EditorWindow
{
    private sealed class Group
    {
        public UnityEngine.Object asset;
        public string name, path;
        public Sprite thumb;
        public readonly List<string> messages = new List<string>();
    }

    private const string Help =
        "프로젝트의 기획 데이터에서 '확인 필요' 항목을 모읍니다. 줄을 누르면 해당 편집기에서 엽니다.\n" +
        "편집기에서 고친 뒤에는 '다시 검사'를 누르세요(에셋 파일이 추가·삭제·변경되면 자동으로 다시 검사). " +
        "빌드할 때도 같은 검사가 콘솔 경고 요약으로 남습니다(빌드는 막지 않음).\n" +
        "번역(키 없는 문구, 읽을 수 없는 Loc.T/Loc.F 호출)은 여기서 검사하지 않습니다 — 'Tools/번역/번역 표 갱신' 실행 후 콘솔 요약을 보세요.";

    private const string MinionNote =
        "· 애니메이션은 게임처럼 '태그 = 프리팹 Animator 의 클립 이름'으로 찾고, 타격 시점은 OnHitEvent+OnAttackEndEvent → hitEvent 칸 → damageState 태그 → 시퀀스 전체 순서로 정해진다고 보고 검사합니다(MinionSkillCaster).\n" +
        "· 자동 공격·대쉬 히트박스 기본값은 Assets/Prefabs/Player Melee.prefab 의 값으로 봅니다(씬에서 덮어쓴 값은 보지 않음).";

    [SerializeField] private List<string> collapsed = new List<string>();
    private Dictionary<string, List<Group>> byArea = new Dictionary<string, List<Group>>();
    private Dictionary<string, int> issueCount = new Dictionary<string, int>();
    private string summary = "";
    private Vector2 scroll;
    private double rescanAt;
    private static GUIStyle boldFoldout;

    [MenuItem("Tools/기획/전체 검사", priority = 9)]
    public static void Open()
    {
        var w = GetWindow<DesignerValidationWindow>();
        w.titleContent = new GUIContent("전체 검사");
        w.minSize = new Vector2(520, 320);
        w.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("전체 검사");
        Scan();
        EditorApplication.projectChanged += OnProjectChanged;
    }

    private void OnDisable() => EditorApplication.projectChanged -= OnProjectChanged;

    // 가져오기 한 번에 projectChanged 가 여러 번 올 수 있어 0.5초 모았다가 한 번만 검사한다.
    private void OnProjectChanged() => rescanAt = EditorApplication.timeSinceStartup + 0.5;

    private void Update()
    {
        if (rescanAt <= 0 || EditorApplication.timeSinceStartup < rescanAt) return;
        rescanAt = 0;
        Scan();
        Repaint();
    }

    /// <summary>화면 구성이 바뀌는 동작은 다음 프레임으로 미룬다(IMGUI 레이아웃 불일치 방지).</summary>
    private void Defer(Action action)
    {
        EditorApplication.delayCall += () => { if (this == null) return; action(); Repaint(); };
    }

    private void Scan()
    {
        EnemyAssets.ClearPreviews(); // 적 썸네일 캐시는 몬스터 편집기 Reload 때만 비워져, 그림을 다시 내보낸 뒤 옛 그림이 남는다
        var issues = DesignerValidation.RunAll();
        byArea = new Dictionary<string, List<Group>>();
        issueCount = new Dictionary<string, int>();
        foreach (var i in issues)
        {
            if (!byArea.TryGetValue(i.area, out var list)) byArea[i.area] = list = new List<Group>();
            var g = list.Count > 0 && list[list.Count - 1].asset == i.asset ? list[list.Count - 1] : null;
            if (g == null) list.Add(g = new Group { asset = i.asset, name = i.name, path = AssetDatabase.GetAssetPath(i.asset), thumb = Thumb(i.asset) });
            g.messages.Add(i.message);
            issueCount[i.area] = (issueCount.TryGetValue(i.area, out var n) ? n : 0) + 1;
        }
        summary = $"확인 필요 {issues.Count}건  ({DesignerValidation.CountText(issues)})  · {DateTime.Now:HH:mm:ss} 검사";
    }

    private static Sprite Thumb(UnityEngine.Object o)
    {
        switch (o)
        {
            case EnemyMinionDataSO e: return EnemyAssets.Preview(e);
            case EquipmentSO e: return e.icon;
            case ItemSO i: return i.icon;
            case MainMinionDataSO m: return m.rewardItemData != null && m.rewardItemData.icon != null ? m.rewardItemData.icon : m.minionIcon; // 보상 카드와 같은 순서
            default: return null;
        }
    }

    private static void OpenAsset(UnityEngine.Object asset)
    {
        if (asset == null) return; // 검사 뒤에 지워졌다
        switch (asset)
        {
            case EnemyMinionDataSO e: EnemyEditorWindow.Open(e); break;
            case EquipmentSO e: EquipmentEditorWindow.Open(e); break;
            case ItemSO i: ItemDatabaseWindow.Open(i); break;
            case MainMinionDataSO m: MinionManagerWindow.Open(m); break;
            default: Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); break;
        }
    }

    private static string AreaTip(string area)
    {
        switch (area)
        {
            case "적": return "몬스터 편집기의 '확인 필요' 검사와 같습니다. 줄을 누르면 몬스터 편집기에서 엽니다.";
            case "장비": return "장비 편집기의 '확인 필요' 검사와 같습니다. 줄을 누르면 장비 편집기에서 엽니다.";
            case "아이템": return "아이템 편집기의 '확인 필요' 검사와 같습니다. 줄을 누르면 아이템 편집기에서 엽니다.";
            default: return "이 창이 게임 코드 기준으로 검사합니다(등록·진화·표시·우클릭 스킬·자동 공격·대쉬·애니메이션 타격 시점).\n" +
                            "줄을 누르면 소환수 관리 창의 상세 탭에서 그 소환수를 엽니다.";
        }
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button(new GUIContent("다시 검사", "적·장비·아이템·소환수 데이터를 지금 다시 검사합니다."), EditorStyles.toolbarButton, GUILayout.Width(70)))
                Defer(Scan);
            GUILayout.Space(6);
            GUILayout.Label(new GUIContent(summary, summary), EditorStyles.miniLabel, GUILayout.MinWidth(60));
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.HelpBox(Help, MessageType.None);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var area in DesignerValidation.Areas) DrawArea(area);
        EditorGUILayout.Space(8);
        EditorGUILayout.EndScrollView();
    }

    private void DrawArea(string area)
    {
        var list = byArea.TryGetValue(area, out var l) ? l : null;
        int n = issueCount.TryGetValue(area, out var c) ? c : 0;
        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            bool open = !collapsed.Contains(area);
            string label = n == 0 ? $"{area} — 문제 없음" : $"{area} — 확인 필요 {n}건 · {list.Count}개";
            boldFoldout ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            bool now = EditorGUILayout.Foldout(open, new GUIContent(label, AreaTip(area)), true, boldFoldout);
            if (now != open) { if (now) collapsed.Remove(area); else collapsed.Add(area); }
            if (area == "소환수" && GUILayout.Button(new GUIContent("소환수 관리 열기", "Tools/기획/소환수 관리 창을 엽니다."), EditorStyles.miniButton, GUILayout.Width(100)))
                Defer(MinionManagerWindow.Open);
        }
        var line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.35f));
        if (collapsed.Contains(area)) return;

        if (area == "소환수") EditorGUILayout.LabelField(MinionNote, EnemyAssets.WrapMini);
        if (list == null) return;
        foreach (var g in list) DrawRow(g);
    }

    private void DrawRow(Group g)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            var tr = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
            EquipmentEditorPanel.DrawSprite(tr, g.thumb);
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label(new GUIContent(g.name, g.path + "\n누르면 편집기에서 엽니다."), EditorStyles.boldLabel);
                foreach (var m in g.messages) GUILayout.Label("· " + m, EnemyAssets.WrapMini);
            }
        }
        var r = GUILayoutUtility.GetLastRect();
        EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            var target = g.asset;
            Defer(() => OpenAsset(target));
            Event.current.Use();
        }
    }
}
