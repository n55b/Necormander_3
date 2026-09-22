using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>씬/프리팹을 저장하지 않는 보스 전투 회귀 검사. 플레이를 끈 상태에서 메뉴로 실행한다.</summary>
public static class BoneMasterCombatCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string DataPath = "Assets/SOData/Enemy/Enemy AI Patterns/Boss/";

    [MenuItem("Tools/BoneMaster/Verify Motion Timing")]
    public static void VerifyMotionTiming()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var scene = EditorSceneManager.NewPreviewScene();
        BoneMasterController boss = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Boss/Boss Bone Master.prefab");
            var source = prefab.GetComponent<BoneMasterController>();
            var bodyRenderer = prefab.GetComponent<SpriteRenderer>();
            Check(bodyRenderer.color == Color.white, "보스 본체 원색 유지 (외곽선으로 본체 틴트를 덮지 않음)");
            var bodySprite = bodyRenderer.sprite;
            Check(Mathf.Abs(bodySprite.bounds.min.y) <= 1f / 32f,
                $"Idle 발이 원점 위로 떠 있지 않음 (현재 Y={bodySprite.bounds.min.y:0.###})");
            var shadow = prefab.transform.Find("Shadow").GetComponent<SpriteRenderer>();
            Check(Mathf.Abs(shadow.bounds.center.y - prefab.transform.position.y) <= 1f / 32f, "그림자 중심과 발 기준점 일치");
            var motions = (BoneMasterController.AttackMotion[])typeof(BoneMasterController)
                .GetField("attackMotions", Private).GetValue(source);
            Check(motions != null && motions.Length == 5, "공격 클립 5종 경고/타격 배선");
            var go = new GameObject("MotionTimingCheck");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = bodyRenderer.color;
            var anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = prefab.GetComponentInChildren<Animator>().runtimeAnimatorController;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            boss = go.AddComponent<BoneMasterController>();
            boss.enabled = false;
            typeof(BaseEntity).GetField("_animator", Private).SetValue(boss, anim);
            typeof(BoneMasterController).GetField("attackMotions", Private).SetValue(boss, motions);
            var status = go.AddComponent<CharacterStatus>();
            status.ApplySuperArmor(source.superArmorGauge);
            var visual = go.AddComponent<CharacterVisualFeedback>();
            var visualType = typeof(CharacterVisualFeedback);
            visualType.GetField("_sr", Private).SetValue(visual, sr);
            visualType.GetField("_status", Private).SetValue(visual, status);
            var outlineSetting = visualType.GetField("showSuperArmorOutline", Private);
            bool bossOutline = (bool)outlineSetting.GetValue(prefab.GetComponentInChildren<CharacterVisualFeedback>(true));
            Check(!bossOutline, "본 마스터 프리팹 상시 외곽선 비활성");
            outlineSetting.SetValue(visual, bossOutline);
            Call(visual, "UpdateSuperArmorOverlay");
            Check(status.HasSuperArmor && go.transform.Find("SuperArmorOverlay") == null, "슈퍼아머 유지 / 외곽선 미생성");
            var commonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Enemy.prefab");
            bool commonOutline = (bool)outlineSetting.GetValue(commonPrefab.GetComponentInChildren<CharacterVisualFeedback>(true));
            Check(commonOutline, "공용 적 프리팹 외곽선 설정 유지");
            outlineSetting.SetValue(visual, commonOutline);
            Call(visual, "UpdateSuperArmorOverlay");
            Check(go.transform.Find("SuperArmorOverlay") != null, "다른 유닛의 슈퍼아머 외곽선 생성 유지");
            anim.Rebind();
            foreach (var motion in motions)
            {
                Check(motion.warningTime < motion.hitTime && motion.hitTime < motion.clip.length, motion.state + " 시간 순서");
                Check(Mathf.Approximately(motion.warningTime, BoneMasterAnimSetup.WarningTime(motion.clip, motion.hitTime)),
                    motion.state + " 가변 길이 경고 프레임");
                var immediate = new BossCounterTelegraph.Result();
                boss.PrepareAttack(motion.state, immediate);
                Check(!immediate.Hijacked && anim.speed == 0f, motion.state + " 대기 없이 즉시 경고 자세");
                var warning = sr.sprite;
                Check(warning != null && warning.pixelsPerUnit == 32f, motion.state + " 32 PPU");
                anim.Update(.8f);
                Check(sr.sprite == warning && anim.speed == 0f, motion.state + " 경고 자세 유지");
                boss.ReleaseAttack();
                Check(sr.sprite != warning && anim.speed == 1f && boss.HasFiredHitEvent, motion.state + " 타격 프레임 해제");
                boss.HasFiredHitEvent = false;
                anim.Update(.001f);
                Check(!boss.HasFiredHitEvent, motion.state + " OnHitEvent 이중 발사 방지");
                anim.speed = 0f;
                boss.StopActivePattern();
                Check(anim.speed == 1f, motion.state + " 취소 시 정지 복원");
            }
            var result = new BossCounterTelegraph.Result();
            boss.PrepareAttack("Jump_Attack", result);
            Check(!result.Hijacked && anim.speed == 0f, "내려찍기 즉시 경고 홀드");
            boss.ReleaseAttack();
            var p2 = AssetDatabase.LoadAssetAtPath<BoneMasterPhase2AIPatternSO>(DataPath + "Bone Master Phase 2 AI Pattern.asset");
            Check(p2.animState_ThrustFollowup == "Attack_Prod_2" && p2.animState_Slam == "Jump_Attack", "2타/내려찍기 클립 연결");
            Debug.Log("[BoneMasterMotionCheck] PASS — 보스 본체 원색/상시 외곽선 OFF/슈퍼아머 유지/공용 외곽선 유지, 발/그림자 접지, 5종 즉시 경고/타격 해제/이벤트 단발/취소 복원/PPU/후속 연결");
        }
        finally
        {
            if (boss != null)
            {
                typeof(BoneMasterController).GetField("_lastTelegraphCleanupFrame", Private).SetValue(boss, Time.frameCount);
                boss.Animator.runtimeAnimatorController = null;
                Object.DestroyImmediate(boss.gameObject);
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [MenuItem("Tools/BoneMaster/Verify Combat Rules")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        var scene = EditorSceneManager.NewPreviewScene();
        var randomState = UnityEngine.Random.state;
        BoneMasterController boss = null;
        BossCounterGauge gauge = null;
        BoneMasterPhase2AIPatternSO phase2 = null;
        IEnumerator telegraph = null;
        try
        {
            var p1 = AssetDatabase.LoadAssetAtPath<BoneMasterAIPatternSO>(DataPath + "Bone Master AI Pattern.asset");
            var p2 = AssetDatabase.LoadAssetAtPath<BoneMasterPhase2AIPatternSO>(DataPath + "Bone Master Phase 2 AI Pattern.asset");
            Check(p1 != null && p2 != null, "패턴 SO 연결");
            Check(Mathf.Approximately(p1.realCounterChance, .3f) && Mathf.Approximately(p2.realCounterChance, .3f)
                && Mathf.Approximately(p1.fakeCounterChance, .2f) && Mathf.Approximately(p2.fakeCounterChance, .2f), "두 SO 확률 30:20:50");
            Check(p1.fakeCounterHealPerHit == 5f && p2.fakeCounterHealPerHit == 5f, "두 SO 회복량 5");
            Check(p2.executionCounterAttempts == 3 && p2.executionLinesPerVolley == 2, "집행 3기회 / 동시 2줄");

            UnityEngine.Random.InitState(907);
            int[] counts = new int[3];
            for (int i = 0; i < 10000; i++)
                counts[(int)BossCounterTelegraph.Roll(true, p1.realCounterChance, p1.fakeCounterChance)]++;
            Check(Mathf.Abs(counts[0] - 5000) < 250 && Mathf.Abs(counts[1] - 3000) < 250
                && Mathf.Abs(counts[2] - 2000) < 250, "독립 추첨 분포");
            Check(BossCounterTelegraph.Roll(false, 1f, 1f) == BossCounterTelegraph.Kind.None, "카운터 없는 후속타");

            var go = new GameObject("BoneMasterCombatCheck");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var body = go.AddComponent<SpriteRenderer>();
            boss = go.AddComponent<BoneMasterController>();
            boss.team = Team.Enemy;
            var stat = go.AddComponent<CharacterStat>();
            var health = go.GetComponent<CharacterHealth>();
            health.Init(stat, null);
            typeof(CharacterStat).GetField("<Health>k__BackingField", Private).SetValue(stat, health);
            typeof(BaseEntity).GetField("_stats", Private).SetValue(boss, stat);
            typeof(BaseEntity).GetField("_sr", Private).SetValue(boss, body);
            typeof(BoneMasterController).GetField("_bodyRenderers", Private).SetValue(boss, new[] { body });
            gauge = go.AddComponent<BossCounterGauge>();
            Call(gauge, "Awake");
            Call(gauge, "OnDisable");
            Call(gauge, "OnEnable");
            typeof(BoneMasterController).GetField("<CounterGauge>k__BackingField", Private).SetValue(boss, gauge);

            var hit = new DamageInfo(10f, DamageType.Physical, null, category: DamageCategory.Skill);
            boss.CurrentState = AIState.Attack;
            var result = new BossCounterTelegraph.Result();
            telegraph = BossCounterTelegraph.Run(boss, boss, 60f, Vector2.right,
                BossCounterTelegraph.Kind.Fake, Color.red, 1f, result);
            Check(telegraph.MoveNext(), "빨강 예고 시작");
            health.GetDamage(hit);
            Check(Mathf.Approximately(health.CurHP, health.MaxHP - 5f), "만피에서도 피격 후 5 회복");
            health.GetDamage(hit);
            Check(Mathf.Approximately(health.CurHP, health.MaxHP - 10f), "두 번째 타격도 5 회복");
            Check(telegraph.MoveNext() && !result.Countered, "빨강 피격으로 예고를 단축하지 않음");
            ((IDisposable)telegraph).Dispose();
            telegraph = null;
            float hp = health.CurHP;
            health.GetDamage(hit);
            Check(Mathf.Approximately(health.CurHP, hp - 10f), "창 종료 후 회복 이월 없음");

            gauge.OpenHealingWindow(5f);
            var dot = new DamageInfo(1f, DamageType.Poison, null, category: DamageCategory.Debuff);
            hp = health.CurHP;
            health.GetDamage(dot);
            Check(Mathf.Approximately(health.CurHP, hp - 1f), "도트로는 회복하지 않음");
            health.Invincible = true;
            health.GetDamage(hit);
            Check(Mathf.Approximately(health.CurHP, hp - 1f), "무적 피격은 회복하지 않음");
            health.Invincible = false;

            result = new BossCounterTelegraph.Result();
            telegraph = BossCounterTelegraph.Run(boss, boss, 60f, Vector2.right,
                BossCounterTelegraph.Kind.None, Color.gray, 1f, result);
            Check(telegraph.MoveNext(), "회색 예고 시작");
            hp = health.CurHP;
            health.GetDamage(hit);
            Check(telegraph.MoveNext() && Mathf.Approximately(health.CurHP, hp - 10f), "회색은 피해만 받음");
            ((IDisposable)telegraph).Dispose();
            telegraph = null;

            result = new BossCounterTelegraph.Result();
            telegraph = BossCounterTelegraph.Run(boss, boss, 60f, Vector2.right,
                BossCounterTelegraph.Kind.Real, Color.yellow, 1f, result);
            Check(telegraph.MoveNext(), "노랑 예고 시작");
            health.GetDamage(hit);
            Check(!telegraph.MoveNext() && result.Countered, "노랑 한 타격 파훼 유지");
            telegraph = null;

            var arrow = new GameObject("DirectionIndicator");
            arrow.transform.SetParent(go.transform, false);
            var indicator = arrow.AddComponent<EntityDirectionIndicator>();
            Call(indicator, "Awake");
            Check(indicator.IsActive, "등장 시 로케이터 활성");
            boss.SetHidden(true);
            Check(!indicator.IsActive, "은신 시 하단/화면 밖 로케이터 숨김");
            boss.SetHidden(false);
            Check(indicator.IsActive, "등장 시 로케이터 복구");

            phase2 = Object.Instantiate(p2);
            // 연출/피해 자식 루틴은 여기서는 실행하지 않고 카운터 결과만 주입한다.
            // 실제 ExecutionRoutine의 종료 조건을 검사하므로 성공 수 while로 되돌리면 실패한다.
            CheckExecution(phase2, boss, false);
            CheckExecution(phase2, boss, true);
            Debug.Log($"[BoneMasterCombatCheck] PASS — 3회 종료(전부 실패/성공), 빨강 다단회복/타이밍/종료, 회색, 노랑, 은신. 추첨 회색/노랑/빨강={counts[0]}/{counts[1]}/{counts[2]}");
        }
        finally
        {
            (telegraph as IDisposable)?.Dispose();
            if (gauge != null) Call(gauge, "OnDisable");
            if (boss != null)
            {
                // 프리뷰 해제 때 실씬의 전조를 검색하지 않는다.
                typeof(BoneMasterController).GetField("_lastTelegraphCleanupFrame", Private).SetValue(boss, Time.frameCount);
                boss.SetHidden(false);
            }
            if (phase2 != null) Object.DestroyImmediate(phase2);
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Random.state = randomState;
        }
    }

    private static void CheckExecution(BoneMasterPhase2AIPatternSO phase2, BaseEntity boss, bool success)
    {
        boss.CurrentState = AIState.Skill;
        var routine = (IEnumerator)typeof(BoneMasterPhase2AIPatternSO).GetMethod("ExecutionRoutine", Private)
            .Invoke(phase2, new object[] { boss });
        int attempts = 0, volleys = 0;
        bool ended = false;
        try
        {
            for (int budget = 0; budget < 100; budget++)
            {
                if (!routine.MoveNext()) { ended = true; break; }
                if (!(routine.Current is IEnumerator nested)) continue;
                if (nested.GetType().Name.Contains("ExecutionLine")) volleys++;
                if (!nested.GetType().Name.Contains("ExecutionStrike")) continue;
                attempts++;
                var result = (BossCounterTelegraph.Result)nested.GetType().GetField("res", Private | BindingFlags.Public).GetValue(nested);
                result.Countered = success;
            }
            Check(ended && attempts == 3 && volleys == 3 * phase2.executionLinesPerCycle,
                $"집행 {(success ? "전부 성공" : "전부 실패")}해도 정확히 3회 종료");
        }
        finally { (routine as IDisposable)?.Dispose(); }
    }

    private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[BoneMasterCombatCheck] FAIL — " + message);
    }
}
