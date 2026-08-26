#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 본 마스터(Enemy_11_BoneMaster, 4층 보스)의 애니메이터를 구성하는 에디터 도구입니다.
/// HaeTaeAnimSetup 과 같은 사상이고, 다른 점만 아래에 적습니다.
///
/// [왜 전용 컨트롤러인가]
/// 공용 CharacterBase_Animator 의 실사용 슬롯은 Idle/Follow/Attack/Die/Stun 5개뿐인데
/// 본 마스터는 태그가 14개다. AnimatorOverrideController 는 '클립 교체'만 되고 '스테이트 추가'가
/// 안 되므로 담을 수 없다. 그래서 전용 AnimatorController 를 만든다.
///
/// [해태와 다른 점]
///   · 진짜 사망 태그(Dead)가 있어서 Die 클립을 합성하지 않는다.
///   · 페이즈가 둘이고 브레인 SO 도 둘인데(BoneMasterAIPatternSO / BoneMasterPhase2AIPatternSO)
///     프리팹·애니메이터는 하나를 공유한다. 그래서 두 페이즈의 스테이트를 한 컨트롤러에 다 넣는다.
///   · 루프는 손대지 않는다. aseprite 태그의 repeat 값이 이미 맞게 저작돼 있다
///     (Idle/Walk/Stun/Pattern_Counter = 무한(rep 0), 나머지 공격/패턴 = 1회(rep 1)).
///     임포터가 그걸 그대로 클립 loop 로 옮긴다.
///
/// [스테이트 이름 규칙]
/// Idle / Follow / Attack / Skill 은 BaseEntity.UpdateAnimation 이 AIState.ToString() 으로
/// 직접 Play 하므로 이름이 고정이다. Die 는 MonsterDeathHandler.deathStateName 기본값이다.
/// 나머지는 두 BoneMaster 패턴 SO 의 animState_* 인스펙터 필드로 지정한다.
///
/// 클립은 .aseprite 서브에셋을 그대로 참조하므로 아트가 갱신되면 자동으로 따라간다.
/// </summary>
public static class BoneMasterAnimSetup
{
    private const string AsepritePath = "Assets/Resources/Sprites/Enemy/Enemy_11_BoneMaster.aseprite";
    private const string OutDir = "Assets/Animations/Character/Monster/BoneMaster";
    private const string ControllerPath = OutDir + "/AnimController_BoneMaster.controller";
    private const string BossPrefabPath = "Assets/Prefabs/Enemy/Boss/Boss Bone Master.prefab";

    /// <summary>프로젝트 표준 PPU. 아트를 재익스포트하면 임포터가 유니티 기본값 100으로 되돌려 놓는다.</summary>
    private const float ExpectedPPU = 32f;

    // 스테이트 이름 -> aseprite 태그 이름.
    // 앞의 5개는 공용 경로(BaseEntity/MonsterDeathHandler)가 이름으로 직접 Play 하는 고정 슬롯이고,
    // 나머지는 패턴 SO 가 animState_* 로 골라 쓴다. 태그와 이름이 같은 건 일부러 그대로 뒀다 —
    // 인스펙터에 태그 이름을 그대로 적으면 되게 하려고.
    private static readonly (string state, string tag)[] StateMap =
    {
        ("Idle",                    "Idle"),
        ("Follow",                  "Walk"),          // 스테이트는 Follow(AIState), 클립은 Walk(아트 태그)
        ("Attack",                  "Attack_Sweep"),  // 공용 폴백. 실제 공격은 아래 전용 스테이트로 재생된다.
        ("Stun",                    "Stun"),
        ("Die",                     "Dead"),          // MonsterDeathHandler.deathStateName 기본값이 "Die"

        // --- 페이즈 1 ---
        ("Attack_Prod",             "Attack_Prod"),              // 기본공격: 창 찌르기
        ("Attack_Sweep",            "Attack_Sweep"),             // 기본공격: 휩쓸기
        ("Attack_Jump",             "Attack_Jump"),              // 기본공격: 도약(준비~체공). 마지막 프레임 홀드
        ("Attack_Jump_Fall",        "Attack_Jump_Fall"),         // 기본공격: 낙하~내려찍기. 2프레임에 타격
        ("Pattern_Dash",            "Pattern_Dash"),             // 패턴1: 박치기 돌격(1~3프레임 충전, 4프레임 질주)
        ("Pattern_Prod",            "Pattern_Prod"),             // 패턴2: 견갑 찌르기 3타
        ("Pattern_Counter",         "Pattern_Counter"),          // 패턴3: 카운터 자세(1프레임 홀드)
        ("Pattern_Counter_Success", "Pattern_Counter_Success"),  // 패턴3: 카운터 성공 반격

        // --- 페이즈 2 ---
        ("Pattern_SweepChop",       "Pattern_SweepChop"),        // 패턴1: 회전 베기(3프레임 타격) + 내려찍기(9프레임 타격)
        ("Pattern_DoubleSweep",     "Pattern_DoubleSweep"),      // 패턴2: 2연격
    };

    // ==========================================================================
    // 공통
    // ==========================================================================
    /// <summary>
    /// 리포트를 파일로 쓴다. 유니티 콘솔은 멀티라인 로그를 첫 줄만 노출하는 경로가 있어서
    /// (MCP read_console 포함) 여러 줄 결과는 파일로 빼는 게 확실하다.
    /// </summary>
    private static void Emit(StringBuilder sb, string fileName)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp"));
        Directory.CreateDirectory(dir);
        string full = Path.Combine(dir, fileName);
        File.WriteAllText(full, sb.ToString());
        Debug.Log($"<color=cyan>[BoneMaster]</color> 리포트: {full}");
    }

    private static Dictionary<string, AnimationClip> LoadClips()
    {
        var clips = new Dictionary<string, AnimationClip>();
        var objs = AssetDatabase.LoadAllAssetsAtPath(AsepritePath);
        if (objs == null) return clips;
        foreach (var o in objs)
        {
            var c = o as AnimationClip;
            if (c != null) clips[c.name] = c;
        }
        return clips;
    }

    /// <summary>이 클립에 박힌 첫 OnHitEvent 시각(초). 없으면 -1.</summary>
    private static float HitEventTime(AnimationClip clip)
    {
        if (clip == null) return -1f;
        float best = -1f;
        foreach (var e in clip.events)
            if (e.functionName == "OnHitEvent" && (best < 0f || e.time < best)) best = e.time;
        return best;
    }

    // ==========================================================================
    // 읽기 전용 리포트 — 뭘 바꾸기 전에 현재 상태부터 본다.
    // ==========================================================================
    [MenuItem("Tools/BoneMaster/Report")]
    public static void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== BoneMaster 현재 상태 ===");
        sb.AppendLine();

        // --- 임포터 ---
        var importer = AssetImporter.GetAtPath(AsepritePath);
        sb.AppendLine($"aseprite: {AsepritePath}");
        if (importer == null)
        {
            sb.AppendLine("  [오류] 임포터를 못 찾음 — 경로 확인.");
            Emit(sb, "BoneMaster_Report.txt");
            return;
        }

        var anySprite = AssetDatabase.LoadAssetAtPath<Sprite>(AsepritePath);
        if (anySprite != null)
        {
            float ppu = anySprite.pixelsPerUnit;
            sb.AppendLine($"  PPU: {ppu}" + (Mathf.Approximately(ppu, ExpectedPPU)
                ? "  (정상)"
                : $"  [경고] 프로젝트 표준은 {ExpectedPPU} 다. 재익스포트하면서 100으로 리셋된 것 같다 — 인스펙터에서 되돌릴 것."));
        }

        // --- 클립 ---
        var clips = LoadClips();
        sb.AppendLine($"  생성된 AnimationClip: {clips.Count}개");
        if (clips.Count == 0)
        {
            sb.AppendLine("  [오류] 클립이 하나도 없다. 임포터의 Generate Animation Clips 확인.");
        }
        sb.AppendLine();
        sb.AppendLine("클립 목록 (이름 / 길이 / 루프 / 타격프레임):");
        var names = new List<string>(clips.Keys);
        names.Sort();
        foreach (var n in names)
        {
            var c = clips[n];
            float hit = HitEventTime(c);
            sb.AppendLine($"  {n,-26} {c.length,6:0.###}s  loop={(c.isLooping ? "O" : "X")}  " +
                          (hit >= 0f ? $"hit={hit:0.###}s ({hit / Mathf.Max(0.0001f, c.length) * 100f:0}%)" : "hit=없음") +
                          $"  events={c.events.Length}");
            foreach (var e in c.events)
                if (e.functionName != "OnHitEvent")
                    sb.AppendLine($"      [경고] 낯선 이벤트 이름: '{e.functionName}' @{e.time:0.###}s " +
                                  "(aseprite 셀 user data 에 'event:' 콜론을 두 번 찍으면 이렇게 된다)");
        }

        // --- 스테이트 매핑 대조 ---
        sb.AppendLine();
        sb.AppendLine("StateMap 대조:");
        foreach (var (state, tag) in StateMap)
            sb.AppendLine($"  {state,-24} <- {tag,-24} {(clips.ContainsKey(tag) ? "OK" : "[없음]")}");

        // --- 컨트롤러 ---
        sb.AppendLine();
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        sb.AppendLine($"컨트롤러: {ControllerPath} — {(ctrl != null ? $"있음 (스테이트 {ctrl.layers[0].stateMachine.states.Length}개)" : "없음 (Build Animator 필요)")}");

        // --- 프리팹 ---
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        sb.AppendLine($"프리팹: {BossPrefabPath} — {(prefab != null ? "있음" : "[없음]")}");
        if (prefab != null)
        {
            var animator = prefab.GetComponentInChildren<Animator>(true);
            var sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
            sb.AppendLine($"  Animator.controller: {(animator?.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "<없음>")}");
            sb.AppendLine($"  SpriteRenderer.sprite: {(sr?.sprite != null ? sr.sprite.name : "<없음>")}");
        }

        Emit(sb, "BoneMaster_Report.txt");
    }

    // ==========================================================================
    // 컨트롤러 생성
    // ==========================================================================
    [MenuItem("Tools/BoneMaster/Build Animator")]
    public static void Build()
    {
        var clips = LoadClips();
        if (clips.Count == 0)
        {
            Debug.LogError("[BoneMaster] aseprite 에서 생성된 AnimationClip 이 하나도 없음. " +
                           "임포터의 Generate Animation Clips 가 꺼져 있는지 확인.");
            return;
        }

        Directory.CreateDirectory(OutDir);
        AssetDatabase.Refresh();

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        var sm = controller.layers[0].stateMachine;

        // 기존 스테이트 전부 제거 후 재구성 (재실행 가능하도록)
        foreach (var s in new List<ChildAnimatorState>(sm.states)) sm.RemoveState(s.state);

        var sb = new StringBuilder();
        sb.AppendLine("=== BoneMaster Animator 생성 ===");
        sb.AppendLine();

        AnimatorState idleState = null;
        int row = 0;
        var missing = new List<string>();

        foreach (var (stateName, tag) in StateMap)
        {
            if (!clips.TryGetValue(tag, out var clip))
            {
                missing.Add($"{stateName} <- 태그 '{tag}' 없음");
                continue;
            }

            var st = sm.AddState(stateName, new Vector3(280f, 60f * row++, 0f));
            st.motion = clip;
            st.writeDefaultValues = false;
            if (stateName == "Idle") idleState = st;

            float hit = HitEventTime(clip);
            sb.AppendLine($"  {stateName,-24} <- {tag,-24} ({clip.length:0.###}s, loop={(clip.isLooping ? "O" : "X")}, " +
                          (hit >= 0f ? $"hit={hit:0.###}s)" : "hit=없음)"));
        }

        if (idleState != null) sm.defaultState = idleState;

        // 트랜지션은 일부러 하나도 걸지 않는다 — 전부 Animator.Play() 로 직접 지정한다.
        // (해태 컨트롤러 / 미니언 aseprite 자동 생성 컨트롤러와 동일한 방식)

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (missing.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  [경고] 연결 못한 스테이트:");
            foreach (var m in missing) sb.AppendLine("    " + m);
        }
        sb.AppendLine();
        sb.AppendLine($"  기본 스테이트: {(idleState != null ? idleState.name : "<없음>")}");
        sb.AppendLine($"  저장: {ControllerPath}");

        Emit(sb, "BoneMaster_Build.txt");
    }

    // ==========================================================================
    // 프리팹 배선 — 임시로 꽂아둔 인형(Doll) 애니메이션을 걷어낸다.
    // ==========================================================================
    [MenuItem("Tools/BoneMaster/Setup Boss Prefab")]
    public static void SetupPrefab()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[BoneMaster] 컨트롤러가 없다: {ControllerPath} — Build Animator 를 먼저 돌릴 것.");
            return;
        }

        var clips = LoadClips();
        if (!clips.TryGetValue("Idle", out var idleClip))
        {
            Debug.LogError("[BoneMaster] Idle 클립을 못 찾음.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== BoneMaster 프리팹 배선 ===");
        sb.AppendLine();

        GameObject root = PrefabUtility.LoadPrefabContents(BossPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[BoneMaster] 프리팹을 못 엶: {BossPrefabPath}");
            return;
        }

        try
        {
            // --- 애니메이터 ---
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                sb.AppendLine("  [오류] Animator 가 없다.");
            }
            else
            {
                var before = animator.runtimeAnimatorController;
                sb.AppendLine($"  Animator.controller: {(before != null ? before.name : "<없음>")} -> {controller.name}");
                if (before is AnimatorOverrideController)
                    sb.AppendLine("    (임시로 꽂혀 있던 인형 오버라이드 컨트롤러를 걷어냈다)");
                animator.runtimeAnimatorController = controller;
                animator.speed = 1f;
            }

            // --- 스프라이트 ---
            // 재생 전(에디터 프리뷰/스폰 첫 프레임) 인형 스프라이트가 한 프레임 보이는 걸 막는다.
            // Idle 클립의 첫 스프라이트 키를 그대로 가져온다 — 프레임 이름 규칙에 안 기댄다.
            Sprite first = FirstSpriteOf(idleClip);
            var sr = root.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null)
            {
                sb.AppendLine("  [오류] SpriteRenderer 가 없다.");
            }
            else if (first == null)
            {
                sb.AppendLine("  [경고] Idle 클립에서 스프라이트 키를 못 찾아 스프라이트는 그대로 뒀다.");
            }
            else
            {
                sb.AppendLine($"  SpriteRenderer.sprite: {(sr.sprite != null ? sr.sprite.name : "<없음>")} -> {first.name}");
                sr.sprite = first;
            }

            PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            sb.AppendLine();
            sb.AppendLine($"  저장: {BossPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Emit(sb, "BoneMaster_Prefab.txt");
    }

    private static Sprite FirstSpriteOf(AnimationClip clip)
    {
        foreach (var bind in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, bind);
            if (keys == null) continue;
            foreach (var k in keys)
                if (k.value is Sprite s) return s;
        }
        return null;
    }

    // ==========================================================================
    // 종단 검증 — 프리팹 / 컨트롤러 / 패턴 SO 가 실제로 서로 물렸는지 본다.
    // ==========================================================================
    [MenuItem("Tools/BoneMaster/Verify")]
    public static void Verify()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== BoneMaster 종단 검증 ===");
        sb.AppendLine();

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            sb.AppendLine($"[실패] 컨트롤러 없음: {ControllerPath}");
            Emit(sb, "BoneMaster_Verify.txt");
            return;
        }

        // --- 스테이트 <-> 클립 ---
        var states = new Dictionary<string, AnimatorState>();
        foreach (var cs in controller.layers[0].stateMachine.states) states[cs.state.name] = cs.state;

        sb.AppendLine($"컨트롤러 스테이트 {states.Count}개");
        foreach (var (state, tag) in StateMap)
        {
            if (!states.TryGetValue(state, out var st)) { sb.AppendLine($"  [실패] {state}: 스테이트 없음"); continue; }
            var m = st.motion as AnimationClip;
            sb.AppendLine($"  {(m != null ? "OK  " : "[실패] ")}{state,-24} " +
                          (m != null
                            ? $"{m.name,-24} {m.length:0.###}s {(m.isLooping ? "(루프)" : "(홀드)")}"
                            : "모션 없음"));
        }

        // --- 공용 경로가 요구하는 고정 슬롯 ---
        sb.AppendLine();
        sb.AppendLine("공용 경로 필수 스테이트:");
        foreach (var required in new[] { "Idle", "Follow", "Attack", "Die", "Stun" })
            sb.AppendLine($"  {(states.ContainsKey(required) ? "OK  " : "[실패] ")}{required}");

        // --- 프리팹 ---
        sb.AppendLine();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        if (prefab == null) sb.AppendLine($"[실패] 프리팹 없음: {BossPrefabPath}");
        else
        {
            var animator = prefab.GetComponentInChildren<Animator>(true);
            var rac = animator != null ? animator.runtimeAnimatorController : null;
            bool wired = rac == controller;
            sb.AppendLine($"  {(wired ? "OK  " : "[실패] ")}프리팹 Animator -> {(rac != null ? rac.name : "<없음>")}");
            if (rac is AnimatorOverrideController)
                sb.AppendLine("      아직 인형 오버라이드 컨트롤러다. Setup Boss Prefab 을 돌릴 것.");

            var sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.sprite != null)
            {
                string spritePath = AssetDatabase.GetAssetPath(sr.sprite);
                sb.AppendLine($"  {(spritePath == AsepritePath ? "OK  " : "[경고] ")}프리팹 스프라이트: {sr.sprite.name} ({spritePath})");
                sb.AppendLine($"  {(Mathf.Approximately(sr.sprite.pixelsPerUnit, ExpectedPPU) ? "OK  " : "[경고] ")}PPU: {sr.sprite.pixelsPerUnit} (표준 {ExpectedPPU})");
            }
        }

        // --- 패턴 SO 의 animState_* 가 실제 스테이트를 가리키는지 ---
        sb.AppendLine();
        sb.AppendLine("패턴 SO animState_* 대조:");
        foreach (var guid in AssetDatabase.FindAssets("t:BossAIPatternSO"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("Bone Master")) continue;

            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (so == null) continue;
            sb.AppendLine($"  {Path.GetFileNameWithoutExtension(path)}");

            var sobj = new SerializedObject(so);
            var it = sobj.GetIterator();
            bool any = false;
            while (it.NextVisible(true))
            {
                if (!it.name.StartsWith("animState_") || it.propertyType != SerializedPropertyType.String) continue;
                any = true;
                string v = it.stringValue;
                bool ok = string.IsNullOrWhiteSpace(v) || states.ContainsKey(v);
                sb.AppendLine($"    {(ok ? "OK  " : "[실패] ")}{it.name,-30} = '{v}'" +
                              (string.IsNullOrWhiteSpace(v) ? "  (비어 있음 -> 공용 Attack 으로 폴백)" : ""));
            }
            if (!any) sb.AppendLine("    [경고] animState_* 필드가 하나도 없다 — SO 코드에 아직 이식이 안 됐다.");
        }

        Emit(sb, "BoneMaster_Verify.txt");
    }
}
#endif
