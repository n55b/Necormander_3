#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 본 마스터(Enemy_11_NecroChief)의 애니메이터와 경고/타격 시각을 배선하는 에디터 도구입니다.
/// HaeTaeAnimSetup 과 같은 사상이고, 다른 점만 아래에 적습니다.
///
/// [왜 전용 컨트롤러인가]
/// 공용 CharacterBase_Animator 의 실사용 슬롯은 Idle/Follow/Attack/Die/Stun 5개뿐인데
/// 본 마스터는 전용 공격/연결 태그가 있다. AnimatorOverrideController 는 '클립 교체'만 되고 '스테이트 추가'가
/// 안 되므로 담을 수 없다. 그래서 전용 AnimatorController 를 만든다.
///
/// [해태와 다른 점]
///   · 사망 태그가 없어 Die에는 Stun을 연결한다. 그림/클립을 합성하지 않는다.
///   · 페이즈가 둘이고 브레인 SO 도 둘인데(BoneMasterAIPatternSO / BoneMasterPhase2AIPatternSO)
///     프리팹·애니메이터는 하나를 공유한다. 그래서 두 페이즈의 스테이트를 한 컨트롤러에 다 넣는다.
///   · 루프는 손대지 않는다. aseprite 태그의 repeat 값이 이미 맞게 저작돼 있다
///     (Idle/Walk/Stun/Jump = 무한(rep 0), 나머지 공격 = 1회(rep 1)).
///     임포터가 그걸 그대로 클립 loop 로 옮긴다.
///
/// [스테이트 이름 규칙]
/// Idle / Follow / Attack / Skill 은 BaseEntity.UpdateAnimation 이 AIState.ToString() 으로
/// 직접 Play 하므로 이름이 고정이다. Die 는 MonsterDeathHandler.deathStateName 기본값이다.
/// 나머지는 두 BoneMaster 패턴 SO 의 animState_* 인스펙터 필드로 지정한다.
///
/// 클립은 .aseprite 서브에셋을 직접 참조한다. 프레임 길이/OnHitEvent를 바꾸면 Setup Boss Prefab으로 시각도 갱신한다.
/// </summary>
public static class BoneMasterAnimSetup
{
    private const string AsepritePath = "Assets/Resources/Sprites/Enemy/Enemy_11_NecroChief.aseprite";
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
        ("Die",                     "Stun"),          // 사망 아트가 생길 때까지 Stun 대체

        // --- 페이즈 1 ---
        ("Attack_Prod",             "Attack_Prod"),              // 기본공격: 창 찌르기
        ("Attack_Sweep",            "Attack_Sweep"),             // 기본공격: 휩쓸기
        ("Jump",                    "Jump"),
        ("Jump_Attack",             "Jump_Attack"),
        ("Attack_Prod_2",           "Attack_Prod_2"),
        ("Attack_Sweep_2",          "Attack_Sweep_2"), // 연결만 준비. 새 패턴을 추가하지 않는다.
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
        // 원본 그림은 건드리지 않는다. Canvas/BottomCenter는 빈 여백 80px까지 발 아래에 붙인다.
        // 전 프레임을 같은 캔버스 높이로 고정해야 무기를 휘두를 때 몸이 위아래로 튀지 않는다.
        var importer = AssetImporter.GetAtPath(AsepritePath) as UnityEditor.U2D.Aseprite.AsepriteImporter;
        if (importer == null) throw new System.InvalidOperationException("NecroChief Aseprite 임포터 없음");
        var footPivot = new Vector2(0.5f, 80f / 256f);
        if (!Mathf.Approximately(importer.spritePixelsPerUnit, ExpectedPPU)
            || importer.pivotSpace != UnityEditor.U2D.Aseprite.PivotSpaces.Canvas
            || importer.pivotAlignment != SpriteAlignment.Custom || importer.customPivotPosition != footPivot)
        {
            importer.spritePixelsPerUnit = ExpectedPPU;
            importer.pivotSpace = UnityEditor.U2D.Aseprite.PivotSpaces.Canvas;
            importer.pivotAlignment = SpriteAlignment.Custom;
            importer.customPivotPosition = footPivot;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }
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

        // 같은 이름은 참조를 보존하고, 새 아트에 없는 레거시 상태만 제거한다.
        var existing = new Dictionary<string, AnimatorState>();
        foreach (var s in sm.states) existing[s.state.name] = s.state;
        foreach (var s in new List<ChildAnimatorState>(sm.states))
            if (!System.Array.Exists(StateMap, pair => pair.state == s.state.name)) sm.RemoveState(s.state);

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

            var st = existing.TryGetValue(stateName, out var saved) ? saved
                : sm.AddState(stateName, new Vector3(280f, 60f * row++, 0f));
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
                // 이전 아트(폭 122px)용 그림자 크기를 새 Idle(61px)에 맞춘다. 그림자 자체 피벗도 반영.
                var shadow = root.transform.Find("Shadow");
                var shadowRenderer = shadow != null ? shadow.GetComponent<SpriteRenderer>() : null;
                if (shadowRenderer != null && shadowRenderer.sprite != null)
                {
                    float scale = first.rect.width / first.pixelsPerUnit;
                    shadow.localScale = new Vector3(scale, scale, 1f);
                    var center = shadowRenderer.sprite.bounds.center;
                    shadow.localPosition = new Vector3(-center.x * scale, -center.y * scale, shadow.localPosition.z);
                    sb.AppendLine($"  발 Y={sr.bounds.min.y:0.###}, 그림자 중심 Y={shadowRenderer.bounds.center.y:0.###}");
                }
            }

            var boss = root.GetComponent<BoneMasterController>();
            var serialized = new SerializedObject(boss);
            var motions = serialized.FindProperty("attackMotions");
            var attacks = new List<(string state, AnimationClip clip, float warning, float hit)>();
            foreach (var (state, tag) in StateMap)
            {
                if (state == "Attack" || !clips.TryGetValue(tag, out var clip)) continue;
                float hit = HitEventTime(clip);
                if (hit < 0f) continue;
                float warning = WarningTime(clip, hit);
                attacks.Add((state, clip, warning, hit));
                sb.AppendLine($"  {state}: 즉시 경고 프레임({warning:0.###}s) -> 설정한 예고 시간 -> 타격({hit:0.###}s)");
            }
            motions.arraySize = attacks.Count;
            for (int i = 0; i < attacks.Count; i++)
            {
                var item = motions.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("state").stringValue = attacks[i].state;
                item.FindPropertyRelative("clip").objectReferenceValue = attacks[i].clip;
                item.FindPropertyRelative("warningTime").floatValue = attacks[i].warning;
                item.FindPropertyRelative("hitTime").floatValue = attacks[i].hit;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            sb.AppendLine();
            sb.AppendLine($"  저장: {BossPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // 이름이 바뀐 상태만 이행한다. 디자이너가 조정한 시간/거리 값은 그대로 보존한다.
        foreach (var guid in AssetDatabase.FindAssets("t:BossAIPatternSO"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset is BoneMasterAIPatternSO p1)
            {
                p1.animState_Jump = "Jump";
                p1.animState_JumpFall = "Jump_Attack";
            }
            else if (asset is BoneMasterPhase2AIPatternSO p2)
            {
                p2.animState_Jump = "Jump";
                p2.animState_JumpFall = "Jump_Attack";
                p2.animState_Slam = "Jump_Attack";
                p2.animState_ThrustFollowup = "Attack_Prod_2";
            }
            else continue;
            EditorUtility.SetDirty(asset);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Emit(sb, "BoneMaster_Prefab.txt");
    }

    /// <summary>FPS 역산 금지: Aseprite는 프레임별 시간이 다르므로 바로 전의 서로 다른 스프라이트 키를 찾는다.</summary>
    public static float WarningTime(AnimationClip clip, float hit)
    {
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            if (binding.type != typeof(SpriteRenderer) || binding.propertyName != "m_Sprite") continue;
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            Object previous = null;
            float warning = -1f;
            foreach (var key in keys)
            {
                if (key.time >= hit - 0.0001f) break;
                if (key.value != previous) { warning = key.time; previous = key.value; }
            }
            if (warning >= 0f) return warning;
        }
        throw new System.InvalidOperationException($"{clip.name}: OnHitEvent 이전 스프라이트 키 없음");
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
