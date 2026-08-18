#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 해태(Enemy_10_HaeTae, 차저 엘리트)의 애니메이터를 구성하는 에디터 도구입니다.
///
/// [왜 전용 컨트롤러인가]
/// 일반 몬스터는 공용 CharacterBase_Animator.controller + .overrideController 방식인데,
/// AnimatorOverrideController 는 '클립 교체'만 되고 '스테이트 추가'가 안 됩니다.
/// 공용 컨트롤러의 실사용 슬롯은 Idle/Follow/Attack/Die/Stun 5개뿐이라
/// 공격 모션이 5종인 해태를 담을 수 없어서, 해태 전용 AnimatorController 를 따로 만듭니다.
/// (미니언 쪽은 aseprite 임포터가 태그마다 스테이트를 자동 생성한 컨트롤러를 그대로 쓰고
///  MinionSkillCaster 가 Play(태그이름) 으로 직접 지정합니다 — 같은 사상입니다.)
///
/// [스테이트 이름 규칙]
/// Idle / Follow / Attack / Skill 은 BaseEntity.UpdateAnimation 이 AIState.ToString() 으로
/// 직접 Play 하므로 이름이 고정입니다. Die 는 MonsterDeathHandler.deathStateName 기본값입니다.
/// 나머지(Jump_Attack 등)는 EliteChargerAIPatternSO 의 인스펙터 필드로 지정합니다.
///
/// 클립은 .aseprite 서브에셋을 그대로 참조하므로, 아트가 갱신되면 자동으로 따라갑니다.
/// 예외는 Die 하나 — 아직 사망 모션이 없어서 Stun 프레임 + 알파 페이드아웃으로 대체 생성합니다.
/// </summary>
public static class HaeTaeAnimSetup
{
    private const string AsepritePath = "Assets/Resources/Sprites/Enemy/Enemy_10_HaeTae.aseprite";
    private const string OutDir = "Assets/Animations/Character/Monster/HaeTae";
    private const string ControllerPath = OutDir + "/AnimController_HaeTae.controller";
    private const string DieClipPath = OutDir + "/AnimClip_HaeTae_Die.anim";
    private const string ElitePrefabPath = "Assets/Prefabs/Enemy/Elite/Charger Elite.prefab";

    /// <summary>사망 페이드아웃 길이. MonsterDeathHandler.fallbackDelay(기본 1.0초)보다 살짝 짧게 둔다.</summary>
    private const float DieFadeDuration = 0.9f;

    /// <summary>사망 태그로 인정할 이름들. 앞에 있는 것부터 찾는다.</summary>
    private static readonly string[] DeathTagAliases = { "Die", "Dead" };

    /// <summary>
    /// 배속을 맞출 기준점(초). EliteChargerAIPatternSO.PlayState 와 <b>같은 규칙</b>이어야 한다 —
    /// 타격 프레임(OnHitEvent)이 있으면 그 시각, 없으면 클립 끝. 두 곳이 갈리면 리포트가 거짓말을 한다.
    /// </summary>
    private static float SpeedAnchor(AnimationClip clip)
    {
        if (clip == null) return 0f;
        float best = 0f;
        foreach (var e in clip.events)
            if (e.functionName == "OnHitEvent" && (best <= 0.0001f || e.time < best)) best = e.time;
        return best > 0.0001f ? best : clip.length;
    }

    // 스테이트 이름 -> aseprite 태그 이름. Die 는 위 별칭으로 따로 찾는다.
    private static readonly (string state, string tag)[] StateMap =
    {
        ("Idle",         "Idle"),
        ("Follow",       "Move"),          // 스테이트는 Follow(AIState), 클립은 Move(프로젝트 클립 명명 규칙)
        ("Attack",       "Slash_Attack"),  // 공용 경로 폴백. 실제 공격은 아래 전용 스테이트로 재생된다.
        ("Stun",         "Stun"),
        ("Jump_Attack",  "Jump_Attack"),
        ("Slash_Attack", "Slash_Attack"),
        ("Dash_Ready",   "Dash_Ready"),
        ("Dash_Attack",  "Dash_Attack"),
        ("ShockWave",    "ShockWave"),
    };

    // ==========================================================================
    // 읽기 전용 리포트 — 뭘 바꾸기 전에 현재 상태부터 본다.
    // ==========================================================================
    /// <summary>
    /// 리포트를 파일로 쓴다. Unity 콘솔은 멀티라인 로그를 첫 줄만 노출하는 경로가 있어서
    /// (MCP read_console 포함) 여러 줄 결과는 파일로 빼는 게 확실하다.
    /// </summary>
    private static void Emit(StringBuilder sb, string fileName)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp"));
        Directory.CreateDirectory(dir);
        string full = Path.Combine(dir, fileName);
        File.WriteAllText(full, sb.ToString());
        Debug.Log($"[HaeTae] 리포트 기록: {full} ({sb.Length} chars)");
    }

    [MenuItem("Tools/HaeTae/Report")]
    public static void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== HaeTae aseprite sub-assets ===");

        var objs = AssetDatabase.LoadAllAssetsAtPath(AsepritePath);
        if (objs == null || objs.Length == 0)
        {
            Debug.LogError($"[HaeTae] 에셋을 못 찾음: {AsepritePath}");
            return;
        }

        foreach (var o in objs)
        {
            if (o == null) { sb.AppendLine("  <null>"); continue; }
            sb.AppendLine($"  [{o.GetType().Name}] {o.name}{(AssetDatabase.IsMainAsset(o) ? "  (MAIN)" : "")}");
        }

        sb.AppendLine();
        sb.AppendLine("=== AnimationClip detail ===");
        foreach (var o in objs)
        {
            var c = o as AnimationClip;
            if (c == null) continue;
            var binds = AnimationUtility.GetObjectReferenceCurveBindings(c);
            sb.Append($"  {c.name,-14} len={c.length:0.###}s fps={c.frameRate} loop={c.isLooping} binds={binds.Length}");
            foreach (var b in binds)
            {
                int keys = AnimationUtility.GetObjectReferenceCurve(c, b).Length;
                sb.Append($" | path='{b.path}' {b.type.Name}.{b.propertyName} keys={keys}");
            }
            var evts = AnimationUtility.GetAnimationEvents(c);
            sb.Append($"  events={evts.Length}");
            foreach (var e in evts) sb.Append($" [{e.functionName}@{e.time:0.###}]");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("=== 생성된 모델 프리팹 ===");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AsepritePath);
        if (model == null) sb.AppendLine("  없음");
        else DumpHierarchy(model, sb);

        sb.AppendLine();
        sb.AppendLine("=== Charger Elite 프리팹 현재 상태 ===");
        var elite = AssetDatabase.LoadAssetAtPath<GameObject>(ElitePrefabPath);
        if (elite == null) sb.AppendLine("  없음");
        else
        {
            DumpHierarchy(elite, sb);
            var sr = elite.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sb.AppendLine($"  SpriteRenderer.sprite = {(sr.sprite != null ? sr.sprite.name : "<none>")}");
            var an = elite.GetComponentInChildren<Animator>();
            if (an != null) sb.AppendLine($"  Animator.controller = {(an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "<none>")}");
            var col = elite.GetComponent<CircleCollider2D>();
            if (col != null) sb.AppendLine($"  CircleCollider2D.radius = {col.radius}  isTrigger={col.isTrigger}");
            var mdh = elite.GetComponent<AstroNuts.Monsters.MonsterDeathHandler>();
            sb.AppendLine($"  MonsterDeathHandler = {(mdh != null ? "있음" : "없음")}");
        }

        Emit(sb, "HaeTae_Report.txt");
    }

    private static void DumpHierarchy(GameObject root, StringBuilder sb)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var comps = new List<string>();
            foreach (var c in t.GetComponents<Component>()) if (c != null) comps.Add(c.GetType().Name);
            string path = AnimationUtility.CalculateTransformPath(t, root.transform);
            sb.AppendLine($"    '{(string.IsNullOrEmpty(path) ? "<root>" : path)}' [{string.Join(",", comps)}] " +
                          $"pos={t.localPosition} scale={t.localScale}");
        }
    }

    // ==========================================================================
    // 컨트롤러 + Die 클립 생성
    // ==========================================================================
    [MenuItem("Tools/HaeTae/Build Animator")]
    public static void Build()
    {
        var objs = AssetDatabase.LoadAllAssetsAtPath(AsepritePath);
        if (objs == null || objs.Length == 0)
        {
            Debug.LogError($"[HaeTae] 에셋을 못 찾음: {AsepritePath}");
            return;
        }

        // 태그 이름 -> 생성된 클립
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var o in objs)
        {
            var c = o as AnimationClip;
            if (c != null) clips[c.name] = c;
        }
        if (clips.Count == 0)
        {
            Debug.LogError("[HaeTae] aseprite 에서 생성된 AnimationClip 이 하나도 없음. " +
                           "임포터의 Generate Animation Clips 가 꺼져 있는지 확인.");
            return;
        }

        Directory.CreateDirectory(OutDir);
        AssetDatabase.Refresh();

        // --- Die ---
        // 아트가 사망 태그를 그려 넣었으면 그걸 쓰고, 없으면 Stun 프레임 + 알파 페이드로 대신 만든다.
        // 태그 이름은 'Die' 든 'Dead' 든 받는다 — 아트마다 다르게 적어서 한쪽만 보면 조용히 폴백 클립이
        // 만들어지고, 진짜 사망 모션이 있는데도 안 쓰이는 사고가 난다(경고도 안 뜬다).
        // 어느 태그를 썼든 <b>스테이트 이름은 항상 "Die"</b> 다 — MonsterDeathHandler 의 기본값이라서.
        string dieTag = null;
        foreach (var t in DeathTagAliases) if (clips.ContainsKey(t)) { dieTag = t; break; }
        AnimationClip dieClip = dieTag != null ? clips[dieTag] : BuildDieClip(clips);

        // --- 컨트롤러 ---
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        var sm = controller.layers[0].stateMachine;

        // 기존 스테이트 전부 제거 후 재구성 (재실행 가능하도록)
        foreach (var s in new List<ChildAnimatorState>(sm.states)) sm.RemoveState(s.state);

        var sb = new StringBuilder();
        sb.AppendLine("=== HaeTae Animator 생성 ===");

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
            st.writeDefaultValues = false; // 알파 페이드가 끝난 뒤 다른 스테이트로 값이 새지 않도록
            if (stateName == "Idle") idleState = st;

            sb.AppendLine($"  {stateName,-14} <- {tag,-14} ({clip.length:0.###}s, loop={clip.isLooping})");
        }

        // Die 는 태그가 아니라 직접 만든 클립
        if (dieClip != null)
        {
            var dieState = sm.AddState("Die", new Vector3(280f, 60f * row++, 0f));
            dieState.motion = dieClip;
            dieState.writeDefaultValues = false;
            sb.AppendLine(dieTag != null
                ? $"  {"Die",-14} <- {dieTag,-14} ({dieClip.length:0.###}s, 진짜 사망 태그)"
                : $"  {"Die",-14} <- {"(대체 생성)",-14} ({dieClip.length:0.###}s, Stun 프레임 + 페이드아웃)");
        }

        if (idleState != null) sm.defaultState = idleState;

        // 트랜지션은 일부러 하나도 걸지 않는다 — 전부 Animator.Play() 로 직접 지정한다.
        // (미니언 쪽 aseprite 자동 생성 컨트롤러와 동일한 방식)

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

        Emit(sb, "HaeTae_Build.txt");
    }

    // ==========================================================================
    // 종단 검증 — 프리팹 / 컨트롤러 / SO 가 실제로 서로 물렸는지 본다
    // ==========================================================================
    [MenuItem("Tools/HaeTae/Verify")]
    public static void Verify()
    {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string msg) { sb.AppendLine($"  [{(ok ? "OK  " : "FAIL")}] {msg}"); if (!ok) fail++; }

        sb.AppendLine("=== HaeTae 종단 검증 ===");

        // --- 컨트롤러 ---
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Check(controller != null, $"컨트롤러 존재: {ControllerPath}");
        if (controller == null) { Emit(sb, "HaeTae_Verify.txt"); return; }

        var states = new Dictionary<string, AnimatorState>();
        foreach (var cs in controller.layers[0].stateMachine.states) states[cs.state.name] = cs.state;
        sb.AppendLine($"  컨트롤러 스테이트({states.Count}): {string.Join(", ", states.Keys)}");

        // 코드가 이름으로 직접 Play 하는 것들 — 하나라도 없으면 그 상황에서 애니가 안 나온다
        foreach (var required in new[] { "Idle", "Follow", "Attack", "Die", "Stun" })
            Check(states.ContainsKey(required),
                  $"코드 요구 스테이트 '{required}' 존재 " +
                  (required == "Die" ? "(MonsterDeathHandler)" : "(BaseEntity.UpdateAnimation / BaseEntity 기절)"));

        foreach (var kv in states)
        {
            var m = kv.Value.motion as AnimationClip;
            Check(m != null && !string.IsNullOrEmpty(m.name),
                  $"'{kv.Key}' 에 이름 있는 클립 연결됨" +
                  (m != null ? $" ({(string.IsNullOrEmpty(m.name) ? "<이름 없음>" : m.name)}, {m.length:0.###}s, loop={m.isLooping})" : ""));
        }

        // --- 프리팹 ---
        var elite = AssetDatabase.LoadAssetAtPath<GameObject>(ElitePrefabPath);
        Check(elite != null, $"프리팹 존재: {ElitePrefabPath}");
        if (elite != null)
        {
            var an = elite.GetComponent<Animator>();
            Check(an != null && an.runtimeAnimatorController == controller,
                  $"프리팹 Animator.controller == AnimController_HaeTae " +
                  $"(실제: {(an?.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "<none>")})");

            var sr = elite.GetComponent<SpriteRenderer>();
            bool fromAse = sr != null && sr.sprite != null &&
                           AssetDatabase.GetAssetPath(sr.sprite) == AsepritePath;
            Check(fromAse, $"프리팹 스프라이트가 해태 aseprite 출신 (실제: {(sr?.sprite != null ? sr.sprite.name : "<none>")})");

            Check(Mathf.Approximately(elite.transform.localScale.x, RootScale),
                  $"루트 스케일 == {RootScale} (실제: {elite.transform.localScale.x})");

            // 그림자: Shadow.aseprite 는 자기 피벗이 타원보다 11px 아래라, 형제 프리팹 전부
            // y = -0.5 × scale 로 상쇄한다. 이걸 어기면 그림자가 몸통 한가운데로 떠오른다.
            var shadow = elite.transform.Find("Shadow");
            Check(shadow != null && Mathf.Approximately(shadow.localPosition.y, ShadowPivotCompensation * shadow.localScale.x),
                  $"그림자 y == -0.5 × scale (피벗 보정) " +
                  $"(실제 y={shadow?.localPosition.y}, scale={shadow?.localScale.x}, 기대={(shadow != null ? ShadowPivotCompensation * shadow.localScale.x : 0f)})");

            // 머리 위 UI: 이 프로젝트 모든 적이 Canvas 월드 스케일 1.0 이다.
            var canvas = elite.transform.Find("Canvas");
            float canvasWorld = canvas != null ? canvas.localScale.x * elite.transform.localScale.x : 0f;
            Check(canvas != null && Mathf.Approximately(canvasWorld, 1f),
                  $"Canvas 월드 스케일 == 1.0 (다른 적들과 동일) (실제: {canvasWorld})");

            // 콜라이더: 원이 지면에 접해야 한다(offset.y == radius). 일반 몹 6종의 규칙.
            var col2 = elite.GetComponent<CircleCollider2D>();
            Check(col2 != null && Mathf.Approximately(col2.offset.y, col2.radius),
                  $"콜라이더 offset.y == radius (지면 접함) (실제 offset.y={col2?.offset.y}, radius={col2?.radius})");

            var ind = elite.GetComponentInChildren<EntityDirectionIndicator>(true);
            if (ind != null)
            {
                float iv = new SerializedObject(ind).FindProperty("indicatorScale").floatValue;
                Check(Mathf.Approximately(iv, IndicatorScale),
                      $"방향 인디케이터 indicatorScale == {IndicatorScale} (교체 전 월드 크기 보존) (실제: {iv})");
            }

            var vf = elite.transform.Find("vec_float");
            Check(vf != null && Mathf.Approximately(vf.localPosition.y * elite.transform.localScale.y, FloatTextWorldY),
                  $"플로팅 텍스트 월드 y == {FloatTextWorldY} (실제: {(vf != null ? vf.localPosition.y * elite.transform.localScale.y : 0f)})");

            if (sr != null && sr.sprite != null)
                sb.AppendLine($"  스프라이트 bounds: {sr.sprite.bounds} (발밑=0 이면 center.y>0)");
        }

        // --- SO: 새로 추가한 필드가 기존 에셋에서 기본값으로 살아났는지 ---
        sb.AppendLine();
        sb.AppendLine("  --- EliteChargerAIPatternSO 에셋 ---");
        foreach (var guid in AssetDatabase.FindAssets("t:EliteChargerAIPatternSO"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<EliteChargerAIPatternSO>(p);
            if (so == null) continue;
            sb.AppendLine($"  {p}");

            // speedMatched = 코드가 실제로 PlayState 에 duration 을 넘기는지. 1프레임 홀드 포즈와
            // 루프 클립은 넘기지 않으므로(늘려도 정지 화면 / 루프는 무의미) 여기서도 그대로 표시한다.
            var pairs = new (string label, string state, float windup, bool speedMatched)[]
            {
                ("① 미니 돌진 찍기", so.animState_Stab,        so.stabWindup,          true),
                ("② 돌진 예비",      so.animState_ChargeReady, so.normalChargeWindup,  false),
                ("② 돌진 질주",      so.animState_Charge,      0f,                     false),
                ("③ 휩쓸기",         so.animState_Sweep,       so.sweepWindup,         true),
                ("패턴3 충격파",     so.animState_Slam,        so.slamPreCastDelay,    true),
            };
            sb.AppendLine($"    matchAnimSpeedToWindup = {so.matchAnimSpeedToWindup}");
            Check(Mathf.Approximately(so.patternLabelOffset.y * RootScale, PatternLabelWorldY),
                  $"patternLabelOffset 월드 y == {PatternLabelWorldY} (실제: {so.patternLabelOffset.y * RootScale})");
            foreach (var (label, state, windup, speedMatched) in pairs)
            {
                bool exists = !string.IsNullOrWhiteSpace(state) && states.ContainsKey(state);
                Check(exists, $"{label,-16} -> 스테이트 '{state}' 존재");
                if (!exists) continue;
                var clip = states[state].motion as AnimationClip;
                if (clip == null) continue;

                if (speedMatched && so.matchAnimSpeedToWindup && windup > 0.0001f)
                {
                    float anchorTime = SpeedAnchor(clip);
                    float sp = Mathf.Clamp(anchorTime / windup, 0.05f, 20f);
                    bool byHit = anchorTime < clip.length - 0.0001f;
                    sb.AppendLine($"        클립 {clip.length:0.###}s, " +
                                  (byHit ? $"타격 프레임 {anchorTime:0.###}s" : "타격 프레임 없음 -> 클립 끝") +
                                  $" -> 예비동작 {windup:0.###}s 에 맞춤 (speed {sp:0.###})");
                    if (!byHit)
                        sb.AppendLine($"        [주의] '{state}' 클립에 OnHitEvent 가 없다. aseprite 셀 user data 에 " +
                                      "event:OnHitEvent 를 박으면 타격 순간과 판정이 정확히 맞는다(콜론 하나!).");
                }
                else
                {
                    sb.AppendLine($"        클립 {clip.length:0.###}s, speed 1 " +
                                  (clip.isLooping ? "(루프 — 길이 무관)" : "(홀드 — 마지막 프레임 유지)"));
                }
            }
        }

        // --- 패턴3 타임라인: 파동 횟수와 내려찍기 모션 횟수가 같은지 ---
        foreach (var guid in AssetDatabase.FindAssets("t:EliteChargerAIPatternSO"))
        {
            var so = AssetDatabase.LoadAssetAtPath<EliteChargerAIPatternSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null || !states.TryGetValue(so.animState_Slam ?? "", out var slamState)) continue;
            var slam = slamState.motion as AnimationClip;
            if (slam == null) continue;

            sb.AppendLine();
            sb.AppendLine("  --- 패턴3 바닥 충격파 타임라인 ---");
            float t = 0f;
            int slams = 0;
            sb.AppendLine($"    t={t,5:0.00}s  내려찍기 #{++slams} 재생 시작 " +
                          $"(클립 {slam.length:0.###}s, 타격 {SpeedAnchor(slam):0.###}s -> {so.slamPreCastDelay:0.##}s, speed {SpeedAnchor(slam) / so.slamPreCastDelay:0.###})");
            t += so.slamPreCastDelay;
            for (int w = 0; w < so.slamWaveCount; w++)
            {
                sb.AppendLine($"    t={t,5:0.00}s  파동 #{w + 1} 발사 (확산 {so.slamWaveExpandTime:0.##}s)");
                t += so.slamWaveExpandTime;
                if (w < so.slamWaveCount - 1)
                {
                    sb.AppendLine($"    t={t,5:0.00}s  내려찍기 #{++slams} 재생 시작 " +
                                  $"(클립 {slam.length:0.###}s, 타격 {SpeedAnchor(slam):0.###}s -> {so.slamWaveInterval:0.##}s, speed {SpeedAnchor(slam) / so.slamWaveInterval:0.###})");
                    t += so.slamWaveInterval;
                }
            }
            sb.AppendLine($"    t={t,5:0.00}s  종료");
            Check(slams == so.slamWaveCount,
                  $"내려찍기 모션 횟수({slams}) == 파동 횟수({so.slamWaveCount})");
        }

        sb.AppendLine();
        sb.AppendLine(fail == 0 ? "  ===> 전부 통과" : $"  ===> 실패 {fail}건");
        Emit(sb, "HaeTae_Verify.txt");
    }

    // ==========================================================================
    // Charger Elite 프리팹을 해태로 교체
    // ==========================================================================
    // --------------------------------------------------------------------------
    // [26/08/17 수정] 아래 값들은 처음에 "피벗이 중앙 -> 발밑으로 바뀌었다"는 잘못된 전제로 잡았다가
    // 전부 다시 계산한 것이다. 실제로는 교체 전 몸통 스프라이트(Enemy_06_DashDoll_64px)도
    // 이미 alignment 7(BottomCenter) = 발밑 피벗이었다(32개 중 31개). 즉 몸통 원점은 원래부터 발밑이었고,
    // 이번에 바뀐 건 '루트 스케일 2 -> 1' 하나뿐이다. 그래서 기준은 "월드 크기/위치를 그대로 보존"이다.
    // --------------------------------------------------------------------------

    /// <summary>루트 스케일. 해태는 그림 자체가 크게 그려져 있어서(idle 119px ≒ 3.72u) 1이면 충분하다.</summary>
    private const float RootScale = 1f;

    /// <summary>몸통 콜라이더 반경. 기존 실효값(0.5 × 루트스케일 2 = 1.0)을 유지한다.</summary>
    private const float ColliderRadius = 1.0f;

    /// <summary>
    /// 그림자 로컬 y = 이 값 × 그림자 스케일.
    /// Shadow.aseprite 는 자기 피벗이 타원보다 11px 아래에 있어서(pivot y -1.2222 × 9px) 스프라이트가
    /// 자기 원점보다 위에 그려진다. 형제 프리팹들이 전부 y = -0.5 × scale 로 이걸 상쇄한다
    /// (Dash_Doll -0.5/1, LionMask -1/2, Lion·Mask·Melee·Range·Necromancer -0.5/1).
    /// </summary>
    private const float ShadowPivotCompensation = -0.5f;

    /// <summary>
    /// 머리 위 UI(Canvas)의 로컬 스케일. 이 프로젝트의 모든 적은 Canvas 월드 스케일이 1.0 이다
    /// (엘리트: 루트 2 × 캔버스 0.5, 일반 몹: 루트 1 × 캔버스 1). 루트를 1로 내렸으므로 1.0 으로 올려야
    /// HP바/디버프 아이콘이 다른 적들과 같은 크기로 보인다.
    /// </summary>
    private const float CanvasLocalScale = 1.0f;

    /// <summary>플로팅 데미지 텍스트 앵커의 월드 높이. 교체 전 값(로컬 1.25 × 루트 2)을 그대로 보존한다.</summary>
    private const float FloatTextWorldY = 2.5f;

    /// <summary>
    /// 방향 인디케이터 크기. 자식 localScale 은 EntityDirectionIndicator.ApplyScale() 이 매번
    /// indicatorScale 로 덮어쓰므로 트랜스폼이 아니라 이 필드를 고쳐야 한다.
    /// 교체 전 월드 크기(루트 2 × indicatorScale 1 = 2)를 보존한다.
    /// </summary>
    private const float IndicatorScale = 2.0f;

    /// <summary>보스 머리 위 패턴 이름 라벨의 월드 높이. 교체 전 값(로컬 1.3 × 루트 2)을 보존한다.</summary>
    private const float PatternLabelWorldY = 2.6f;

    [MenuItem("Tools/HaeTae/Setup Charger Elite Prefab")]
    public static void SetupPrefab()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[HaeTae] 컨트롤러가 없다. 먼저 'Build Animator' 를 실행할 것: {ControllerPath}");
            return;
        }

        // Idle 클립이 실제로 쓰는 첫 스프라이트를 기준으로 삼는다(프레임 번호 하드코딩 회피).
        // 단, 클립 커브에서 꺼낸 참조를 그대로 쓰지 않고 '이름'만 얻어서 에셋에서 다시 로드한다.
        // 커브에서 꺼낸 참조는 재임포트 시점에 따라 stale 일 수 있어서, 그대로 대입하면
        // 프리팹에 override 가 기록되지 않고 조용히 예전 값이 남는다(실제로 한 번 그랬다).
        var all = AssetDatabase.LoadAllAssetsAtPath(AsepritePath);
        string idleSpriteName = null;
        foreach (var o in all)
        {
            var c = o as AnimationClip;
            if (c == null || c.name != "Idle") continue;
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(c))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(c, b);
                if (keys != null && keys.Length > 0 && keys[0].value != null) { idleSpriteName = keys[0].value.name; break; }
            }
            break;
        }
        Sprite idleSprite = null;
        foreach (var o in all)
        {
            var s = o as Sprite;
            if (s != null && s.name == idleSpriteName) { idleSprite = s; break; }
        }
        if (idleSprite == null) { Debug.LogError($"[HaeTae] Idle 스프라이트를 못 찾음 (이름='{idleSpriteName}')."); return; }

        // 피벗이 발밑이므로 bounds.max.y = 발밑에서 머리끝까지의 높이(유닛).
        float spriteTop = idleSprite.bounds.max.y;
        float spriteWidth = idleSprite.bounds.size.x;

        var sb = new StringBuilder();
        sb.AppendLine("=== Charger Elite 프리팹 세팅 ===");
        sb.AppendLine($"  기준 스프라이트: {idleSprite.name}  {idleSprite.rect.width}x{idleSprite.rect.height}px " +
                      $"→ {spriteWidth:0.##} x {spriteTop:0.##} units (PPU {idleSprite.pixelsPerUnit})");
        sb.AppendLine($"  피벗(발밑 기준) = {idleSprite.pivot}, bounds = {idleSprite.bounds}");
        sb.AppendLine();

        GameObject root = PrefabUtility.LoadPrefabContents(ElitePrefabPath);
        if (root == null) { Debug.LogError($"[HaeTae] 프리팹 로드 실패: {ElitePrefabPath}"); return; }

        try
        {
            void Log(string what, object before, object after) =>
                sb.AppendLine($"  {what,-34} {before}  ->  {after}");

            // --- 루트 스케일 ---
            var rt = root.transform;
            Log("root.localScale", rt.localScale, Vector3.one * RootScale);
            rt.localScale = Vector3.one * RootScale;

            // --- 스프라이트 / 컨트롤러 ---
            var sr = root.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                Log("SpriteRenderer.sprite", sr.sprite != null ? sr.sprite.name : "<none>", idleSprite.name);
                sr.sprite = idleSprite;
                if (sr.color.a < 1f) { Log("SpriteRenderer.color.a", sr.color.a, 1f); var c = sr.color; c.a = 1f; sr.color = c; }
            }
            var an = root.GetComponent<Animator>();
            if (an != null)
            {
                Log("Animator.controller",
                    an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "<none>", controller.name);
                an.runtimeAnimatorController = controller;
            }

            // --- 콜라이더 ---
            var col = root.GetComponent<CircleCollider2D>();
            if (col != null)
            {
                Log("CircleCollider2D.radius", col.radius, ColliderRadius);
                col.radius = ColliderRadius;
                // 원이 지면에 접하도록 올린다(offset.y == radius). 일반 몹 6종이 전부 이 규칙이다
                // (Dash_Doll 0.5/0.5, Lion 0.4/0.4, Mask 0.4/0.4, Melee·Range·Necromancer 0.5/0.5).
                var off = new Vector2(0f, ColliderRadius);
                Log("CircleCollider2D.offset", col.offset, off);
                col.offset = off;
            }

            // --- 그림자: 스프라이트 자체 피벗 보정이 필요하다 (y = -0.5 × scale) ---
            var shadow = rt.Find("Shadow");
            if (shadow != null)
            {
                // 타원 폭은 몸통 폭의 62.5% 가 되도록. (그림자 스프라이트는 20x9px = 0.625 x 0.28u 이므로
                // scale = spriteWidth 를 주면 타원 실폭이 spriteWidth × 0.625 가 된다 — 교체 전 비율과 같다.)
                var s = new Vector3(spriteWidth, spriteWidth, 1f);
                Log("Shadow.localScale", shadow.localScale, s);
                shadow.localScale = s;

                var sp = new Vector3(0f, ShadowPivotCompensation * spriteWidth, 0f);
                Log("Shadow.localPosition", shadow.localPosition, sp);
                shadow.localPosition = sp;
            }

            // --- 머리 위 UI (WorldHPBar 는 위치를 안 건드린다 = 수동 배치) ---
            var canvas = rt.Find("Canvas");
            if (canvas != null)
            {
                // 루트가 2 -> 1 이 되면서 월드 스케일이 0.5 로 반토막 났다. 다른 적들과 똑같이 1.0 으로 되돌린다.
                var cs = new Vector3(CanvasLocalScale, CanvasLocalScale, CanvasLocalScale);
                Log("Canvas.localScale", canvas.localScale, cs);
                canvas.localScale = cs;

                // 위치는 건드리지 않는다 — 다른 엘리트들과 동일하게 로컬 0 이면 HP바가 월드 0.75 에 온다.
                Log("Canvas.localPosition", canvas.localPosition, Vector3.zero);
                canvas.localPosition = Vector3.zero;
            }
            var vecFloat = rt.Find("vec_float");
            if (vecFloat != null)
            {
                var fp = new Vector3(0f, FloatTextWorldY / RootScale, 0f);
                Log("vec_float.localPosition", vecFloat.localPosition, fp);
                vecFloat.localPosition = fp;
            }

            // --- 방향 인디케이터: 트랜스폼이 아니라 컴포넌트 필드를 고쳐야 한다 ---
            var indicator = root.GetComponentInChildren<EntityDirectionIndicator>(true);
            if (indicator != null)
            {
                var so = new SerializedObject(indicator);
                var prop = so.FindProperty("indicatorScale");
                if (prop != null)
                {
                    Log("EntityDirectionIndicator.indicatorScale", prop.floatValue, IndicatorScale);
                    prop.floatValue = IndicatorScale;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, ElitePrefabPath);
            sb.AppendLine();
            sb.AppendLine($"  저장: {ElitePrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // --- 저장 후 재확인 + 필요하면 SerializedObject 로 직접 박는다 ---
        // 프리팹 '변형(variant)'에서 상속된 컴포넌트의 오브젝트 참조는 LoadPrefabContents 경로로
        // 안 박히는 경우가 있었다(m_Sprite 가 예전 값 그대로 남음). 그래서 결과를 반드시 검사하고,
        // 어긋나 있으면 에셋에 직접 써서 override 를 확정한다.
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(ElitePrefabPath);
        var savedSr = saved != null ? saved.GetComponent<SpriteRenderer>() : null;
        if (savedSr != null && savedSr.sprite != idleSprite)
        {
            sb.AppendLine($"  [보정] 저장 후에도 sprite 가 '{(savedSr.sprite != null ? savedSr.sprite.name : "<none>")}' 였다. " +
                          "SerializedObject 로 직접 기록한다.");
            var so = new SerializedObject(savedSr);
            so.FindProperty("m_Sprite").objectReferenceValue = idleSprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(saved);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            saved = AssetDatabase.LoadAssetAtPath<GameObject>(ElitePrefabPath);
            savedSr = saved != null ? saved.GetComponent<SpriteRenderer>() : null;
        }
        sb.AppendLine($"  최종 sprite = {(savedSr != null && savedSr.sprite != null ? savedSr.sprite.name : "<none>")} " +
                      $"(기대: {idleSprite.name})");

        // --- 패턴 이름 라벨 높이 (SO 에 있다) ---
        // CreatePatternLabel 이 라벨을 entity.transform 자식으로 붙이고 localPosition = patternLabelOffset 을
        // 주므로, 루트 스케일이 2 -> 1 이 되면 라벨 높이도 그대로 반토막 난다. 월드 높이를 보존한다.
        sb.AppendLine();
        foreach (var guid in AssetDatabase.FindAssets("t:EliteChargerAIPatternSO"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var pattern = AssetDatabase.LoadAssetAtPath<EliteChargerAIPatternSO>(p);
            if (pattern == null) continue;

            float want = PatternLabelWorldY / RootScale;
            if (!Mathf.Approximately(pattern.patternLabelOffset.y, want))
            {
                var pso = new SerializedObject(pattern);
                var prop = pso.FindProperty("patternLabelOffset");
                sb.AppendLine($"  {p}");
                sb.AppendLine($"    patternLabelOffset.y  {pattern.patternLabelOffset.y}  ->  {want}");
                prop.vector3Value = new Vector3(pattern.patternLabelOffset.x, want, pattern.patternLabelOffset.z);
                pso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pattern);
            }
            else sb.AppendLine($"  {p}\n    patternLabelOffset.y 이미 {want} — 변경 없음");
        }
        AssetDatabase.SaveAssets();

        Emit(sb, "HaeTae_Prefab.txt");
    }

    /// <summary>
    /// 사망 클립을 만든다. 전용 사망 모션이 아직 없어서 Stun 클립의 첫 프레임(뻗은 자세)을
    /// 그대로 띄운 채 알파만 0으로 떨어뜨린다.
    ///
    /// aseprite 에 진짜 'Die' 태그가 생기면 Build() 가 알아서 그쪽을 쓰므로 이 함수는 자동으로 안 불린다.
    /// (그때 이 함수와 AnimClip_HaeTae_Die.anim 을 지워도 되지만, 안 지워도 아무 일 안 일어난다.)
    /// </summary>
    private static AnimationClip BuildDieClip(Dictionary<string, AnimationClip> clips)
    {
        if (!clips.TryGetValue("Stun", out var stunClip))
        {
            Debug.LogWarning("[HaeTae] 'Stun' 태그가 없어 Die 클립을 만들지 못했다.");
            return null;
        }

        // Stun 클립이 실제로 표시하는 스프라이트를 그대로 쓴다 (프레임 번호 하드코딩 회피)
        Sprite stunSprite = null;
        foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(stunClip))
        {
            var keys = AnimationUtility.GetObjectReferenceCurve(stunClip, b);
            if (keys != null && keys.Length > 0) { stunSprite = keys[0].value as Sprite; break; }
        }
        if (stunSprite == null)
        {
            Debug.LogWarning("[HaeTae] Stun 클립에서 스프라이트를 못 찾아 Die 클립을 만들지 못했다.");
            return null;
        }

        // 이름을 반드시 넣는다. 아래 CopySerialized 가 이름까지 복사하기 때문에, 비워두면
        // 두 번째 실행부터 기존 에셋의 이름이 빈 문자열로 덮어써진다(실제로 그렇게 됐었다).
        var clip = new AnimationClip { frameRate = 12f, name = Path.GetFileNameWithoutExtension(DieClipPath) };

        var spriteBinding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, spriteBinding, new[]
        {
            new ObjectReferenceKeyframe { time = 0f,               value = stunSprite },
            new ObjectReferenceKeyframe { time = DieFadeDuration,  value = stunSprite },
        });

        var alpha = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(DieFadeDuration * 0.35f, 1f),
            new Keyframe(DieFadeDuration, 0f));
        clip.SetCurve("", typeof(SpriteRenderer), "m_Color.a", alpha);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(DieClipPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(clip, existing);
            Object.DestroyImmediate(clip);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(clip, DieClipPath);
        return clip;
    }
}
#endif
