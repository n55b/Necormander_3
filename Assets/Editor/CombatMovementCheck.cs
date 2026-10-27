using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>임시 씬에서 조준/유도탄/Wall 대쉬 회귀 검사. 씬과 에셋은 저장하지 않는다.</summary>
public static class CombatMovementCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector2 Offset = new Vector2(2000f, 2000f);

    [MenuItem("Tools/Combat/Verify Player Attack Animation")]
    public static void RunAttackAnimation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var host = Make(scene, "Attack animation check");
            host.transform.position = Offset;
            var player = host.AddComponent<PlayerController>();
            player.enabled = false;
            var melee = host.AddComponent<MeleeCombatController>();
            melee.enabled = false;
            Set(melee, "_player", player);
            Call(player, "CachingAnim");
            var body = Make(scene, "Body", typeof(SpriteRenderer), typeof(Animator), typeof(BodyAnimationEventRelay));
            body.transform.SetParent(host.transform, false);
            var hand = Make(scene, "Hand", typeof(SpriteRenderer), typeof(PlayerHandAimController));
            hand.transform.SetParent(body.transform, false);
            var aim = hand.GetComponent<PlayerHandAimController>();
            Call(aim, "Awake");
            var relay = body.GetComponent<BodyAnimationEventRelay>();
            Call(relay, "Awake");
            var animator = body.GetComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animations/Player/ViperHandGenerated/ViperHand.overrideController");
            // 이벤트의 시점은 클립에서 읽고 수동 전달한다. 실제 피격/발자국/물리 시뮬레이션은 실행하지 않는다.
            animator.fireEvents = false;
            Set(player, "BodyAnimator", animator);
            var hitbox = Make(scene, "Live hitbox", typeof(BaseHitBox));
            hitbox.transform.SetParent(host.transform, false);
            Set(melee, "_lastAttackTime", Time.time - 10f);
            Set(melee, "<CurrentAttackDir>k__BackingField", Vector2.right);
            Set(player, "moveInput", Vector2.left);
            Set(player, "MoveDirection", Vector3.left);

            // 공속이 빠르면 복귀 이벤트가 0.2초 판정창보다 먼저 온다.
            host.transform.localScale = new Vector3(-1f, 1f, 1f);
            Set(melee, "_activeHitbox", hitbox.GetComponent<BaseHitBox>());
            Call(melee, "PlayAttackAnimation", 0);
            animator.Update(0f);
            animator.speed = 1.5f;
            animator.Update(.30f);
            relay.CanChangeAnimState();
            Call(player, "Update");
            animator.Update(0f);
            bool fast = !player.canChangeState && host.transform.localScale.x < 0f &&
                        animator.GetCurrentAnimatorStateInfo(0).IsName("Attack_Light1");

            // 기본 공속 3타: 판정은 먼저 끝나도 모션은 0.667초까지 남는다.
            Set(melee, "_activeHitbox", null);
            player.CanChangeAnimState();
            Call(melee, "PlayAttackAnimation", 2);
            animator.Update(0f);
            animator.speed = 1f;
            animator.Update(.50f);
            Set(melee, "<CurrentAttackDir>k__BackingField", Vector2.up);
            Call(aim, "LateUpdate");
            bool slow = melee.IsAttacking && Quaternion.Angle(hand.transform.localRotation, Quaternion.identity) > 80f;
            Debug.Log($"[PlayerAnimationCheck] fast/reverse movement protected={fast}, third-hit tail/aim protected={slow}");
            Check(fast && slow, "공격 종료와 이동/무기 조준 복귀의 수명 일치");

            int cases = 0;
            foreach (string variant in new[] { "", "Viper", "ViperSC", "ViperAS" })
            {
                string folder = "Assets/Animations/Player/";
                string prefix = "Player_AnimClip_";
                string controller = folder + "Player_Animator.controller";
                if (variant.Length > 0)
                {
                    folder += variant + "HandGenerated/";
                    prefix = variant + "_";
                    controller = folder + "ViperHand.overrideController";
                }
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controller);
                foreach (float speed in new[] { .5f, 1f, 1.5f, 3f })
                for (int swing = 0; swing < 3; swing++)
                {
                    string state = new[] { "Attack_Light1", "Attack_Light2", "Attack_Medium" }[swing];
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + prefix + state + ".anim");
                    var events = AnimationUtility.GetAnimationEvents(clip);
                    float recovery = Array.Find(events, e => e.functionName == "CanChangeAnimState").time / speed;
                    float hit = Array.Find(events, e => e.functionName == "OnAttackHitFrame").time / speed;
                    float fallbackHit = (swing == 2 ? .4f : .2f) / speed;
                    float hitboxEnd = Mathf.Min(hit, fallbackHit) + .2f;
                    float attackEnd = Mathf.Max(recovery, hitboxEnd, .3f);
                    float probe = (Mathf.Min(recovery, hitboxEnd) + attackEnd) * .5f;
                    if (attackEnd - probe < .001f) probe = attackEnd * .9f;
                    for (int direction = 0; direction < 8; direction++)
                    for (int movement = 0; movement < 9; movement++)
                    {
                        Vector2 dir = Quaternion.Euler(0, 0, direction * 45f) * Vector2.right;
                        Vector2 move = movement == 8 ? Vector2.zero :
                            (Vector2)(Quaternion.Euler(0, 0, movement * 45f) * Vector2.right);
                        string label = $"{variant}/{state}/speed={speed}/aim={direction}/move={movement}";
                        Set(melee, "_activeHitbox", null);
                        Set(melee, "_lastAttackTime", Time.time - 10f);
                        player.CanChangeAnimState();
                        player.PlayAllAnim("Idle");
                        animator.Update(0f);
                        Set(player, "moveInput", move);
                        Set(player, "MoveDirection", (Vector3)move);
                        Set(melee, "<CurrentAttackDir>k__BackingField", dir);
                        host.transform.localScale = new Vector3(dir.x >= 0f ? -1f : 1f, 1f, 1f);
                        float facing = host.transform.localScale.x;
                        Set(melee, "_activeHitbox", hitbox.GetComponent<BaseHitBox>());
                        Set(melee, "_lastAttackTime", Time.time);
                        Call(melee, "PlayAttackAnimation", swing);
                        animator.Update(0f);
                        player.SetAttackAnimSpeed(speed);
                        animator.Update(probe);
                        Set(melee, "_lastAttackTime", Time.time - probe);
                        if (probe >= hitboxEnd) Set(melee, "_activeHitbox", null);
                        if (probe >= recovery) relay.CanChangeAnimState();
                        // 스크립트 Update 순서가 뒤바뀌어도 동일해야 한다.
                        if (movement % 2 == 0) Call(melee, "Update");
                        Call(player, "Update");
                        if (movement % 2 != 0) Call(melee, "Update");
                        animator.Update(0f);
                        Call(aim, "LateUpdate");
                        Check(melee.IsAttacking && !player.canChangeState &&
                              animator.GetCurrentAnimatorStateInfo(0).IsName(state), "중간에 Walk/Idle/다음 타로 끊기지 않음: " + label);
                        Check(host.transform.localScale.x == facing && Mathf.Approximately(animator.speed, speed),
                            "이동 방향/속도가 평타를 덮지 않음: " + label);
                        Check(Vector2.Dot(hand.transform.TransformVector(Vector3.left).normalized, dir) > .999f,
                            "판정 종료와 모션 종료 사이에도 무기는 실제 조준 유지: " + label);

                        Set(melee, "_activeHitbox", null);
                        Set(melee, "_lastAttackTime", Time.time - attackEnd - .01f);
                        animator.Update(attackEnd - probe + .01f);
                        relay.CanChangeAnimState();
                        Call(melee, "Update");
                        Call(player, "Update");
                        animator.Update(0f);
                        Call(aim, "LateUpdate");
                        Check(!melee.IsAttacking && player.canChangeState && player.SpeedMultiplier == 1f &&
                              animator.GetCurrentAnimatorStateInfo(0).IsName(movement == 8 ? "Idle" : "Walk"),
                            "완료 후 홀드 이동/정지 복귀: " + label);
                        Check(Quaternion.Angle(hand.transform.localRotation, Quaternion.identity) < .01f,
                            "완료 후 무기 각도 초기화: " + label);
                        cases++;
                    }
                }
            }

            // 취소 후 새 대쉬/가드의 잠금을 이전 평타 Update가 풀면 안 된다.
            Set(melee, "_lastAttackTime", Time.time);
            Call(melee, "PlayAttackAnimation", 2);
            melee.CancelPlayerAttack();
            Call(aim, "LateUpdate");
            Check(!melee.IsAimingAttack && player.canChangeState && player.SpeedMultiplier == 1f,
                "대쉬/가드 취소가 평타 모션·방향·이동 잠금을 모두 회수");
            player.LockAnimState(0f);
            Set(melee, "_lastAttackTime", Time.time - 10f);
            Call(melee, "Update");
            Check(!player.canChangeState, "이전 평타 완료가 새 행동 잠금을 해제하지 않음");
            player.CanChangeAnimState();
            // 기존 3초 안전장치도 같은 CanChangeAnimState 경로를 통해 평타 플래그까지 해제한다.
            Call(melee, "PlayAttackAnimation", 1);
            player.CanChangeAnimState();
            Check(!melee.IsAttacking && !melee.IsAimingAttack && player.canChangeState, "이벤트 누락 안전 해제");
            Debug.Log($"[PlayerAnimationCheck] PASS: {cases} direction/movement/speed/combo/controller combinations; cancellation and timeout unlock path. Isolated Animator/state checks, not live combat.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/Combat/Verify Hitstun Icon Hidden")]
    public static void RunStatusIcons()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var scene = EditorSceneManager.NewPreviewScene();
        var previousPool = DebuffPool.Instance;
        var poolProperty = typeof(Singleton<DebuffPool>).GetProperty("Instance");
        try
        {
            var pool = Make(scene, "Status icon test pool", typeof(DebuffPool)).GetComponent<DebuffPool>();
            Set(pool, "iconPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/DebuffIcon.prefab"));
            poolProperty.SetValue(null, pool);
            var panel = Make(scene, "Status icon test panel", typeof(RectTransform), typeof(Panel_Debuff)).GetComponent<Panel_Debuff>();
            var host = Make(scene, "Status icon test unit", typeof(CharacterStatus), typeof(EnemyDebuffTerminal));
            var status = host.GetComponent<CharacterStatus>();
            var terminal = host.GetComponent<EnemyDebuffTerminal>();
            Set(terminal, "debuffData", AssetDatabase.LoadAssetAtPath<DebuffDataSO>("Assets/SOData/Registry/DebuffData.asset"));
            Set(terminal, "debuffUI", panel);
            Set(status, "debuffTerminal", terminal);
            var icons = (System.Collections.IDictionary)Get(panel, "activeIcons");
            for (int i = 0; i < 3; i++)
            {
                status.ApplyStatus(StatusType.Hitstun, .2f);
                Check(status.HasStatus(StatusType.Hitstun) && status.IsActionBlocked, "평타 경직 및 행동 제한 유지");
                Check(icons.Count == 0 && panel.transform.childCount == 0, "평타 경직 아이콘 생성 안 함");
                var states = (System.Collections.IDictionary)Get(status, "_statuses");
                var instance = states[StatusType.Hitstun];
                var endTime = instance.GetType().GetField("EndTime");
                Check(Mathf.Abs((float)endTime.GetValue(instance) - (Time.time + .2f)) < .01f, "경직 지속시간 유지");
                endTime.SetValue(instance, Time.time - 1f);
                Call(status, "UpdateStatuses");
                Check(!status.HasStatus(StatusType.Hitstun) && !status.IsActionBlocked, "경직 만료 및 행동 제한 해제 유지");
            }
            foreach (var type in new[] { StatusType.Stun, StatusType.Freeze, StatusType.Bleed, StatusType.Poison, StatusType.BloodPop })
            {
                status.ApplyStatus(type, 1f);
                Check(status.HasStatus(type) && icons.Contains(type), $"다른 상태이상 아이콘 유지: {type}");
                status.RemoveStatus(type);
                Check(icons.Count == 0, $"아이콘 제거 및 풀 반환: {type}");
            }
            var boss = host.AddComponent<BoneMasterController>();
            status.ApplyStatus(StatusType.Poison, 10f);
            boss.SetHidden(true);
            Check(!panel.gameObject.activeSelf && status.HasStatus(StatusType.Poison) && icons.Contains(StatusType.Poison),
                "보스 은신: 아이콘만 숨기고 상태/기존 아이콘 유지");
            status.ApplyStatus(StatusType.Bleed, 10f);
            status.RemoveStatus(StatusType.Poison);
            Check(!panel.gameObject.activeSelf && icons.Contains(StatusType.Bleed) && !icons.Contains(StatusType.Poison),
                "은신 중 새 상태/해제도 숨긴 채 갱신");
            boss.SetHidden(false);
            Check(panel.gameObject.activeSelf && icons.Count == 1, "재등장 시 현재 상태만 복구");
            status.RemoveStatus(StatusType.Bleed);
            typeof(BoneMasterController).GetField("_lastTelegraphCleanupFrame", Private).SetValue(boss, Time.frameCount);
            Debug.Log("[StatusIconCheck] PASS: Hitstun suppressed, other icons preserved; boss hide/update/show preserves status effects.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            poolProperty.SetValue(null, previousPool);
        }
    }

    [MenuItem("Tools/Combat/Verify Player Locomotion Feel")]
    public static void RunLocomotion()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        if (Application.isBatchMode)
            EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Single);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        float previousTimeScale = Time.timeScale;
        try
        {
            var host = Make(scene, "Locomotion feel check");
            var rb = host.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            var stat = host.AddComponent<CharacterStat>();
            typeof(CharacterStat).GetProperty("Health").SetValue(stat, host.GetComponent<CharacterHealth>());
            var player = host.AddComponent<PlayerController>();
            player.enabled = false;
            Set(player, "stat", stat);
            Set(player, "_rb", rb);
            Call(player, "CachingAnim");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab").GetComponent<PlayerController>();
            foreach (string field in new[] { "movementAccelerationTime", "movementSmoothTime", "movementTurnTime" })
                Set(player, field, Get(prefab, field));

            Vector2 Motion() => (Vector2)Get(player, "_smoothedMoveInput");
            void Reset() => Call(player, "StopLocomotion", true);
            void Step(Vector2 input, float dt) => Call(player, "AdvanceLocomotion", input, dt);
            foreach (float dt in new[] { .02f, 1f / 60f, 1f / 120f })
            {
                Reset();
                Step(Vector2.right, dt);
                Check(Motion().x > 0f && Motion().x < .8f, "출발은 즉시 반응하되 최대 속도로 튀지 않음");
                for (float t = dt; t < .16f; t += dt) Step(Vector2.right, dt);
                Check(Motion().x > .98f, "0.16초 안에 정상 달리기 속도 도달");
                Set(player, "moveInput", Vector2.zero);
                Step(Vector2.zero, dt);
                Check(Motion().x > .1f && Motion().x < 1f, "키 해제 후 짧은 감속");
                Call(player, "UpdateLocomotionAnimation");
                Check(ReferenceEquals(Get(player, "currentAnimState"), player.walkState), "감속 중 Walk 유지");
                float drift = Motion().magnitude * stat.MOVESPEED * dt;
                for (float t = dt; t < .25f; t += dt)
                {
                    Step(Vector2.zero, dt);
                    drift += Motion().magnitude * stat.MOVESPEED * dt;
                }
                Call(player, "UpdateLocomotionAnimation");
                Check(Motion() == Vector2.zero && drift < .4f, "0.25초 내 완전 정지, 기본 속도에서 잔여 이동 0.4유닛 미만");
                Check(ReferenceEquals(Get(player, "currentAnimState"), player.idleState), "정지 후 Idle 전환");

                Set(player, "_smoothedMoveInput", Vector2.right);
                for (float t = 0f; t < .04f; t += dt) Step(Vector2.left, dt);
                Check(Motion().x < -.5f, "급반전 0.04초 안에 반대 방향 50% 이상");
                for (int i = 0; i < 120; i++)
                {
                    Step(i % 2 == 0 ? new Vector2(1f, 1f) : new Vector2(-1f, 1f), dt);
                    Check(float.IsFinite(Motion().x) && float.IsFinite(Motion().y) && Motion().sqrMagnitude <= 1.00001f,
                        "연속 방향 전환·대각선 입력은 최대 속도를 넘지 않음");
                }
                for (float t = 0f; t < .4f; t += dt) Step(Vector2.right * .25f, dt);
                Check((Motion() - Vector2.right * .25f).magnitude < .001f, "아날로그 입력 크기 보존");
            }

            foreach (Vector2 input in new[] { Vector2.right, Vector2.up, Vector2.zero })
            {
                Set(player, "moveInput", input);
                Set(player, "_isAttackDashing", true);
                rb.linearVelocity = Vector2.left * 20f;
                player.ResumeLocomotionAfterDash();
                Check(rb.linearVelocity == input * stat.MOVESPEED && !(bool)Get(player, "_isAttackDashing"),
                    $"대시 종료 시 현재 입력으로 걷기/정지, 이전 평타 전진 재개 안 함: input={input}, velocity={rb.linearVelocity}, paused={PlayerController.IsGameplayPaused}");
            }
            Set(player, "moveInput", Vector2.left);
            player.canChangeState = false;
            Set(player, "_lastAnimSpeed", 2f);
            player.transform.localScale = Vector3.one;
            Call(player, "UpdateLocomotionAnimation");
            Check((float)Get(player, "_lastAnimSpeed") == 2f && player.transform.localScale == Vector3.one,
                "공격 잠금 중 모션 속도·방향 유지");
            player.canChangeState = true;
            Set(player, "moveInput", Vector2.right);
            Time.timeScale = 0f;
            player.ResumeLocomotionAfterDash();
            Check(rb.linearVelocity == Vector2.zero && Motion() == Vector2.zero, "일시정지 중 대시 종료가 걷기를 재개하지 않음");
            Time.timeScale = 1f;
            player.SetInputBlocked(true);
            Set(player, "moveInput", Vector2.right);
            player.ResumeLocomotionAfterDash();
            Check(rb.linearVelocity == Vector2.zero, "방 이동 입력 차단 우선");
            Set(player, "_inputBlocked", false);
            rb.linearVelocity = Vector2.left * 20f;
            Call(player, "FixedUpdate");
            Check(rb.linearVelocity == Vector2.left * 20f, "기존 고속 넉백 보호 유지");
            Debug.Log("[LocomotionFeelCheck] PASS: acceleration, short stop, turn, analog/diagonal, animation locks, dash continuity, pause/room block and knockback; 3 physics rates.");
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previousScene);
        }
    }

    [MenuItem("Tools/Combat/Verify Room Transition Input")]
    public static void RunInput()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        var scene = EditorSceneManager.NewPreviewScene();
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var previousSettings = InputSystem.settings;
        var testSettings = Object.Instantiate(previousSettings);
        InputActionAsset actions = null;
        try
        {
            InputSystem.settings = testSettings;
            testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            var host = Make(scene, "Room input check");
            host.SetActive(false); // Awake/Start의 게임 초기화 및 저장 접근 없이 입력 경로만 검사
            var rb = host.AddComponent<Rigidbody2D>();
            var stat = host.AddComponent<CharacterStat>();
            typeof(CharacterStat).GetProperty("Health").SetValue(stat, host.GetComponent<CharacterHealth>());
            var player = host.AddComponent<PlayerController>();
            Set(player, "stat", stat);
            Set(player, "_rb", rb);
            Call(player, "CachingAnim");
            var input = host.AddComponent<PlayerInput>();
            actions = Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/PlayerInputSystem.inputactions"));
            actions.devices = new InputDevice[] { keyboard };
            input.actions = actions;
            var move = input.actions.FindAction("Player/Move", true);
            move.performed += player.OnMove;
            move.canceled += player.OnMove;
            move.Enable();

            void Press(params Key[] keys)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                InputSystem.Update();
            }

            // 실제 문과 동일하게 차단 → 암전 중 입력 변화 → 해제를 반복한다.
            for (int i = 0; i < 3; i++)
            {
                Press(Key.D);
                Check(player.MoveInput == Vector2.right, $"차단 전 이동 입력: action={move.ReadValue<Vector2>()}, player={player.MoveInput}");
                Set(player, "_smoothedMoveInput", Vector2.right);
                Set(player, "_moveInputVelocity", Vector2.right * 2f);
                Set(player, "MoveDirection", Vector3.right);
                player.TransitionToState(player.walkState);
                Set(player, "_lastAnimSpeed", 2f);
                rb.linearVelocity = Vector2.right * 5f;
                player.SetInputBlocked(true);
                Check(player.MoveInput == Vector2.zero && rb.linearVelocity == Vector2.zero, "차단 중 정지");
                Check(ReferenceEquals(Get(player, "currentAnimState"), player.idleState)
                    && (float)Get(player, "_lastAnimSpeed") == 1f, "차단 즉시 걷기 종료 및 Idle 속도 복구");
                player.SetInputBlocked(false);
                Check(player.MoveInput == Vector2.right && (Vector3)Get(player, "MoveDirection") == Vector3.right,
                    "계속 누른 키는 새 이벤트 없이 복구");

                player.SetInputBlocked(true);
                Press(); // 암전 중 손을 떼었으면 새 방에서 관성이 되살아나면 안 된다.
                player.SetInputBlocked(false);
                Check(player.MoveInput == Vector2.zero && (Vector3)Get(player, "MoveDirection") == Vector3.zero
                    && (Vector2)Get(player, "_smoothedMoveInput") == Vector2.zero
                    && (Vector2)Get(player, "_moveInputVelocity") == Vector2.zero
                    && rb.linearVelocity == Vector2.zero, "손을 뗀 채 복구 시 입력/보간/물리 관성 없음");

                player.SetInputBlocked(true);
                Press(Key.W, Key.A);
                Check(player.MoveInput == Vector2.zero, "차단 중 새 입력으로 움직이지 않음");
                player.SetInputBlocked(false);
                Check((player.MoveInput - new Vector2(-1f, 1f).normalized).sqrMagnitude < .000001f,
                    "차단 중 바꾼 대각선 방향을 현재 바인딩에서 복구");
                Press();
                Check(player.MoveInput == Vector2.zero, "복구 후 키 해제 콜백 유지");
            }

            Press(Key.D);
            player.SetInputBlocked(true);
            move.Disable();
            player.SetInputBlocked(false);
            Check(player.MoveInput == Vector2.zero, "비활성 이동 액션은 복구하지 않음");
            player.TransitionToState(player.walkState);
            player.canChangeState = false;
            player.SetInputBlocked(true);
            Check(ReferenceEquals(Get(player, "currentAnimState"), player.walkState), "공격/대쉬 애니메이션 잠금은 강제로 해제하지 않음");
            Debug.Log("[RoomInputCheck] PASS: held/released/changed direction, no stale smoothing, immediate Idle, animation lock preserved, canceled and disabled action; 3 repeated transitions.");
        }
        finally
        {
            if (actions != null) actions.Disable();
            EditorSceneManager.ClosePreviewScene(scene);
            if (actions != null) Object.DestroyImmediate(actions);
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings = previousSettings;
            Object.DestroyImmediate(testSettings);
        }
    }

    [MenuItem("Tools/Combat/Verify Aim Homing And Dash")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        // 새 배치 에디터는 이름 없는 씬으로 시작해서 Additive 생성이 금지된다. 기존 씬을 읽기만 한다.
        if (Application.isBatchMode)
            EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Single);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previous = MapGenerator.Instance;
        Tile tile = null;
        try
        {
            CheckAim();
            CheckHoming(scene);
            var host = Make(scene, "Inactive Generator");
            host.SetActive(false);
            var map = host.AddComponent<MapGenerator>();
            MapGenerator.Instance = map;
            var grid = Make(scene, "Grid", typeof(Grid));
            grid.transform.position = Offset;
            grid.transform.localScale = new Vector3(.5f, .5f, 1f);
            var ground = MakeTilemap(grid, "Ground");
            var wall = MakeTilemap(grid, "Wall");
            var water = MakeTilemap(grid, "Unsteppable");
            Set(map, "globalGroundTilemap", ground);
            Set(map, "globalWallTilemap", wall);
            Set(map, "globalUnsteppableTilemap", water);
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.colliderType = Tile.ColliderType.None; // Wall에 콜라이더가 없어도 통과 불가
            for (int y = -6; y < 12; y++)
            for (int x = -6; x < 22; x++)
                ground.SetTile(new Vector3Int(x, y, 0), tile);

            Check(ground.WorldToCell(Offset + new Vector2(2.25f, .25f)) == new Vector3Int(4, 0, 0),
                "실제 0.5 월드유닛 Grid 좌표 변환");
            Vector2 start = Offset + new Vector2(.25f, .25f);
            wall.SetTile(new Vector3Int(4, 0, 0), tile);
            float distance = map.GetDistanceBeforeWall(start, Vector2.right, 5f, .1f);
            Check(distance > 1.6f && distance < 1.66f, "콜라이더 없는 Wall 앞에서 몸통 폭만큼 정지");
            Vector2 landing = SkillCombatUtil.GetSafeDestination(start, Vector2.right, 5f, .1f);
            Check(landing.x < Offset.x + 1.91f, "벽 뒤 Ground로 착지 불가");
            distance = map.GetDistanceBeforeWall(Offset + new Vector2(.25f, .55f), Vector2.right, 5f, .1f);
            Check(distance < 1.66f, "중심선이 빗나가는 Wall 모서리도 몸통으로 검사");
            Vector2 contact = Offset + new Vector2(1.95f, .25f);
            Check(map.GetDistanceBeforeWall(contact, Vector2.right, 2f, .1f) == 0f, "시작 겹침 0은 이동 불가");
            Check(map.GetDistanceBeforeWall(contact, Vector2.left, 2f, .1f) == 2f, "벽에서 멀어지는 대쉬 허용");
            Check(map.GetDistanceBeforeWall(contact, Vector2.up, 2f, .1f) == 2f, "벽과 나란한 대쉬 허용");

            // 다양한 대각선/셀 중심 보정도 반환된 최종 경로가 Wall을 가로질러서는 안 된다.
            for (int i = -12; i <= 12; i++)
            {
                Vector2 direction = new Vector2(1f, i * .08f).normalized;
                landing = SkillCombatUtil.GetSafeDestination(start, direction, 5f, .12f);
                Vector2 travel = landing - start;
                Check((SkillCombatUtil.ClampToWall(start, travel, travel.magnitude, .12f) - landing).sqrMagnitude < .000001f,
                    "대각선/착지 보정 뒤 최종 경로 재검사 " + i);
            }

            wall.ClearAllTiles();
            for (int x = 4; x <= 6; x++)
            {
                ground.SetTile(new Vector3Int(x, 0, 0), null);
                water.SetTile(new Vector3Int(x, 0, 0), tile);
            }
            Check(map.HasUnsteppableBetween(start, Vector2.right, 3f), "물 구간은 대쉬 연장 대상으로 유지");
            landing = SkillCombatUtil.GetSafeDestination(start, Vector2.right, 4.5f, .1f);
            Check(landing.x > Offset.x + 4.7f, "Unsteppable만 건너서 Ground 착지 허용");
            ground.SetTile(new Vector3Int(4, 0, 0), tile);
            Check(map.TryGetGroundLandingPoint(Offset + new Vector2(2.25f, .25f), .1f, out _),
                "Ground 아래 물은 착지 허용");

            CheckDashStep(scene, map, wall, tile, start);
            CheckWallCollider(scene, start);
            Debug.Log("[CombatMovementCheck] PASS: aim lock 0.5s, bounded homing/no reacquire, Wall/corners/snap/water, fixed-step velocity limits.");
        }
        finally
        {
            MapGenerator.Instance = previous;
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previousScene);
            if (tile != null) Object.DestroyImmediate(tile);
        }
    }

    private static void CheckAim()
    {
        foreach (var type in new[] { typeof(BoneMasterAIPatternSO), typeof(BoneMasterPhase2AIPatternSO), typeof(EliteChargerAIPatternSO) })
        {
            var pattern = (BossAIPatternSO)ScriptableObject.CreateInstance(type);
            try
            {
                Check(pattern.aimLockLeadTime == .5f, type.Name + " 기본 조준 고정 0.5초");
                Check(pattern.CanTrackAim(.49f, 1f) && !pattern.CanTrackAim(.5f, 1f), "잠금 경계");
                Check(!pattern.CanTrackAim(0f, .3f) && !pattern.CanTrackAim(0f, .5f), "짧은 예고 처음부터 고정");
                for (int fps = 30; fps <= 120; fps *= 2)
                for (int i = 0; i <= fps; i++)
                    if (i / (float)fps >= .5f) Check(!pattern.CanTrackAim(i / (float)fps, 1f), "프레임률과 무관한 잠금");
            }
            finally { Object.DestroyImmediate(pattern); }
        }
    }

    private static void CheckHoming(Scene scene)
    {
        var projectile = Make(scene, "Fireball").AddComponent<TrackingFireball>();
        var target = Make(scene, "Target").transform;
        Set(projectile, "_direction", Vector2.right, typeof(Projectile));
        Set(projectile, "_targetTransform", target);
        target.position = new Vector2(10f, 10f);
        Call(projectile, "Steer", .1f);
        var direction = (Vector2)Get(projectile, "_direction", typeof(Projectile));
        Check(Vector2.Angle(Vector2.right, direction) > 5.9f && Vector2.Angle(Vector2.right, direction) < 6.1f,
            "유도탄 최대 선회 60도/초");
        target.position = new Vector2(0f, 10f);
        Call(projectile, "Steer", .1f);
        Check(Get(projectile, "_targetTransform") == null, "옆으로 피하면 유도 해제");
        target.position = new Vector2(10f, 0f);
        Call(projectile, "Steer", 1f);
        Check((Vector2)Get(projectile, "_direction", typeof(Projectile)) == direction, "앞으로 돌아와도 재추적/U턴 없음");
        Set(projectile, "_targetTransform", target);
        target.position = Vector2.left * 10f;
        Call(projectile, "Steer", .1f);
        Check(Get(projectile, "_targetTransform") == null, "뒤로 지나친 대상 추적 불가");
    }

    private static void CheckDashStep(Scene scene, MapGenerator map, Tilemap wall, Tile tile, Vector2 start)
    {
        var playerObject = Make(scene, "Dash Player", typeof(Rigidbody2D));
        var rb = playerObject.GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.position = start;
        var player = playerObject.AddComponent<PlayerController>();
        player.enabled = false; // 입력/저장/UI는 실행하지 않고 실제 공용 물리 스텝만 호출
        Set(player, "_rb", rb);
        Vector2 destination = start + new Vector2(.17f, .23f);
        // 다른 열린 씬의 물리를 진행시키지 않는다. 실제 AdvanceDash가 내보낸 속도를 한 틱 적분한다.
        for (int i = 0; i < 4; i++)
        {
            player.AdvanceDash(destination, 15f);
            rb.position += rb.linearVelocity * Time.fixedDeltaTime;
        }
        Check(Vector2.Distance(rb.position, destination) < .001f, "한 틱보다 짧은 대각선 착지점도 정확히 도달");
        Check(rb.linearVelocity.sqrMagnitude < .000001f, "착지 후 초과 속도 없음");

        rb.position = start;
        wall.SetTile(new Vector3Int(4, 0, 0), tile);
        destination = start + Vector2.right * 5f; // 잘못된 목적지가 넘어와도 스텝 자체가 벽을 차단
        for (int i = 0; i < 30; i++)
        {
            player.AdvanceDash(destination, 15f);
            rb.position += rb.linearVelocity * Time.fixedDeltaTime;
            Check(rb.position.x < Offset.x + 1.72f, "실제 물리 틱에서 Wall 통과 불가");
        }
        Check(rb.position.x > Offset.x + 1.65f && rb.linearVelocity.sqrMagnitude < .000001f,
            "벽 앞 정지 후 종료 가능");
        wall.ClearAllTiles();
    }

    [MenuItem("Tools/Combat/Verify Motion Curves")]
    public static void RunMotionCurves()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        if (Application.isBatchMode)
            EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Single);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousMap = MapGenerator.Instance;
        var tile = ScriptableObject.CreateInstance<Tile>();
        try
        {
            var mapHost = Make(scene, "Curve map");
            mapHost.SetActive(false);
            var map = mapHost.AddComponent<MapGenerator>();
            MapGenerator.Instance = map;
            var grid = Make(scene, "Curve grid", typeof(Grid));
            grid.transform.position = Offset;
            grid.transform.localScale = new Vector3(.5f, .5f, 1f);
            var wall = MakeTilemap(grid, "Wall");
            Set(map, "globalWallTilemap", wall);
            tile.colliderType = Tile.ColliderType.None;
            CheckMotionCurves(scene, wall, tile, Offset + new Vector2(.25f, .25f));
            CheckWallCollider(scene, Offset + new Vector2(.25f, .25f));
        }
        finally
        {
            MapGenerator.Instance = previousMap;
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previousScene);
            Object.DestroyImmediate(tile);
        }
    }

    private static void CheckMotionCurves(Scene scene, Tilemap wall, Tile tile, Vector2 start)
    {
        var host = Make(scene, "Curved movement", typeof(Rigidbody2D));
        var rb = host.GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        var player = host.AddComponent<PlayerController>();
        player.enabled = false;
        Set(player, "_rb", rb);
        var curves = new[]
        {
            AnimationCurve.Linear(0f, 0f, 1f, 1f),
            new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 3f, 3f)),
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.5f, 0f), new Keyframe(1f, 1f)),
            new AnimationCurve(new Keyframe(0f, 0f, 3f, 3f), new Keyframe(1f, 1f, 0f, 0f)),
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.3f, .8f), new Keyframe(.7f, .2f), new Keyframe(1f, 1f))
        };
        float previousDt = Time.fixedDeltaTime;
        try
        {
            Check(Mathf.Abs(SkillCombatUtil.EvaluateMotionProgress(null, .4f) - .4f) < .001f, "곡선 없음 = 등속");
            Check(SkillCombatUtil.EvaluateMotionProgress(curves[1], .5f) < .2f &&
                  SkillCombatUtil.EvaluateMotionProgress(curves[3], .5f) > .8f, "곡선으로 중간 위치 변경");
            foreach (float dt in new[] { .02f, 1f / 30f, 1f / 90f })
            foreach (var curve in curves)
            {
                Time.fixedDeltaTime = dt;
                Set(player, "dashMovementCurve", curve);
                rb.position = start;
                var end = start + new Vector2(.7f, .4f);
                float progress = 0f;
                for (int i = 1; i <= Mathf.CeilToInt(.23f / dt) + 2; i++)
                {
                    Vector2 before = rb.position;
                    bool ended = player.AdvanceDash(start, end, i * dt, .23f, ref progress);
                    Check(!ended || progress >= 1f, "평평한/내려가는 그래프가 대시를 조기 종료하지 않음");
                    rb.position += rb.linearVelocity * dt;
                    Check(Vector2.Dot(rb.position - before, end - start) >= -.0001f, "그래프 역행 구간에도 대시 역주행 없음");
                    Check(Vector2.Distance(start, rb.position) <= Vector2.Distance(start, end) + .001f, "곡선 대시 사거리 제한");
                }
                Check(Vector2.Distance(rb.position, end) < .001f && rb.linearVelocity.sqrMagnitude < .000001f,
                    "물리 간격과 곡선에 무관하게 최종 위치 도달/정지");

                rb.position = start;
                Set(player, "attackDashMovementCurve", curve);
                Set(player, "attackDashDuration", .095f);
                player.ApplyAttackDash(Vector2.right);
                for (int i = 0; i < Mathf.CeilToInt(.095f / dt) + 2; i++)
                {
                    Call(player, "AdvanceAttackDash");
                    rb.position += rb.linearVelocity * dt;
                }
                Check(Mathf.Abs(rb.position.x - start.x - .95f) < .001f && rb.linearVelocity == Vector2.zero,
                    "공격 전진 마지막 물리 틱/거리 보존");
            }
            Time.fixedDeltaTime = .02f;
            wall.SetTile(new Vector3Int(4, 0, 0), tile);
            rb.position = start;
            Set(player, "dashMovementCurve", curves[3]);
            float wallProgress = 0f;
            for (int i = 1; i < 20; i++)
            {
                player.AdvanceDash(start, start + Vector2.right * 5f, i * .02f, .2f, ref wallProgress);
                rb.position += rb.linearVelocity * .02f;
                Check(rb.position.x < Offset.x + 2f, "곡선 대시도 콜라이더 없는 Wall 타일 통과 불가");
            }
            var wallObject = Make(scene, "Curve physical wall", typeof(BoxCollider2D));
            wallObject.layer = LayerMask.NameToLayer("Wall");
            wallObject.transform.position = Offset + new Vector2(2f, .25f);
            wallObject.GetComponent<BoxCollider2D>().size = new Vector2(.01f, 2f);
            Physics2D.SyncTransforms();
            rb.position = start;
            Set(player, "attackDashMovementCurve", curves[3]);
            Set(player, "attackDashDuration", .2f);
            player.ApplyAttackDash(Vector2.right, 4f);
            for (int i = 0; i < 12; i++)
            {
                Call(player, "AdvanceAttackDash");
                rb.position += rb.linearVelocity * .02f;
                Check(rb.position.x < Offset.x + 1.72f, "가속된 공격 전진도 Wall 통과 불가");
            }
            Object.DestroyImmediate(wallObject);
            Physics2D.SyncTransforms();
            Debug.Log("[MotionCurveCheck] PASS: five curves / three physics rates, pauses, no backtracking, endpoints, dash tile walls + lunge physical walls.");
        }
        finally
        {
            Time.fixedDeltaTime = previousDt;
            wall.ClearAllTiles();
        }
    }

    private static void CheckWallCollider(Scene scene, Vector2 start)
    {
        var go = Make(scene, "Thin Wall Collider", typeof(BoxCollider2D));
        go.layer = LayerMask.NameToLayer("Wall");
        go.transform.position = Offset + new Vector2(2.037f, .25f);
        go.GetComponent<BoxCollider2D>().size = new Vector2(.005f, 2f);
        Physics2D.SyncTransforms();
        var end = SkillCombatUtil.ClampToWall(start, Vector2.right, 5f, .1f);
        Check(end.x > Offset.x + 1.9f && end.x < Offset.x + 1.94f, "0.005 폭의 순수 물리 Wall도 통과 불가");
        Vector2 contact = Offset + new Vector2(2f, .25f);
        Check(SkillCombatUtil.ClampToWall(contact, Vector2.right, 2f, .1f) == contact, "물리 Wall 시작 겹침은 관통 불가");
        Check(Vector2.Distance(SkillCombatUtil.ClampToWall(contact, Vector2.left, 2f, .1f), contact) > 1.99f,
            "물리 Wall에서 멀어지는 이동은 허용");
    }

    private static GameObject Make(Scene scene, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    private static Tilemap MakeTilemap(GameObject grid, string name)
    {
        var go = new GameObject(name, typeof(Tilemap));
        go.transform.SetParent(grid.transform, false);
        return go.GetComponent<Tilemap>();
    }

    [MenuItem("Tools/Combat/Verify Repeated Hits In Empty Play Scene")]
    public static void VerifyRepeatedHitsInPlayMode()
    {
        Check(Application.isPlaying && GameManager.Instance == null && Time.timeScale == 1f,
            "GameManager 없는 빈 테스트 씬에서 Play Mode로 실행하세요.");
        var runner = new GameObject("Repeated hit check runner").AddComponent<CharacterHealth>();
        runner.StartCoroutine(RunRepeatedHitTiming(runner));
    }

    public static System.Collections.IEnumerator RunRepeatedHitTiming(MonoBehaviour runner = null)
    {
        Check(Application.isPlaying, "추가 타격 시간 검사는 Play Mode 필요");
        var target = new GameObject("Repeated hit target");
        var effect = new GameObject("Repeated hit feedback probe");
        var stat = target.AddComponent<CharacterStat>();
        var health = target.GetComponent<CharacterHealth>();
        var status = target.GetComponent<CharacterStatus>();
        typeof(CharacterStat).GetProperty("Health").SetValue(stat, health);
        typeof(CharacterStat).GetProperty("Status").SetValue(stat, status);
        health.Init(stat, status);
        var times = new System.Collections.Generic.List<float>();
        health.TakeDamageEvent += (_, __, ___, ____) => times.Add(Time.time);
        BaseHitBox Hit(int count = 2)
        {
            var box = new GameObject("Short lived test hitbox").AddComponent<BaseHitBox>();
            box.HitsPerContact = count;
            box.ContactHitInterval = .08f;
            box.hitEffectPrefab = effect;
            box.Init(new DamageInfo(10f, DamageType.Fixed), 0, .03f);
            Call(box, "DealContactDamage", health);
            return box;
        }
        try
        {
            var weapon = AssetDatabase.LoadAssetAtPath<EquipmentSO>("Assets/SOData/Equipment/1015/Marionette.asset");
            Check(weapon.basicHitsPerContact == 2 && Mathf.Approximately(weapon.basicRepeatHitInterval, .08f), "마리오네트 설정");
            var box = Hit();
            Check(health.CurHP == 90f && times.Count == 1, "첫 타격만 즉시 적용");
            yield return new WaitForSeconds(.14f);
            Check(box == null && health.CurHP == 80f && times.Count == 2 && times[1] - times[0] >= .079f,
                "히트박스 만료 후에도 0.08초 시간차 추가타 1회");
            Check(GameObject.Find(effect.name + "(Clone)") != null, "추가 타격 이펙트 생성");
            Hit(1);
            yield return new WaitForSeconds(.14f);
            Check(health.CurHP == 70f && times.Count == 3, "일반 무기는 한 번만 타격");
            Hit();
            target.SetActive(false);
            yield return new WaitForSeconds(.14f);
            target.SetActive(true);
            Check(health.CurHP == 60f && times.Count == 4, "대상 비활성화/재사용 시 예약타 취소");
            Hit();
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(.14f);
            Check(health.CurHP == 50f && times.Count == 5, "일시정지 동안 추가타 대기");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(.14f);
            Check(health.CurHP == 40f && times.Count == 6, "재개 후 추가타 1회");
            Hit();
            health.SetHP(0f);
            yield return new WaitForSeconds(.14f);
            Check(times.Count == 7, "죽은 대상 추가타 생략");
            health.ResetHP();
            Hit();
            Object.Destroy(target);
            yield return new WaitForSeconds(.14f);
            Check(times.Count == 8, "파괴된 대상 추가타 생략");
            Debug.Log("[RepeatedHitCheck] PASS: real Play Mode timing, hitbox expiry, extra feedback, normal weapon, pause, disabled/dead/destroyed targets.");
        }
        finally
        {
            Time.timeScale = 1f;
            if (target != null) Object.Destroy(target);
            Object.Destroy(effect);
            if (runner != null) Object.Destroy(runner.gameObject);
        }
    }

    private static void Set(object target, string name, object value, Type type = null)
        => (type ?? target.GetType()).GetField(name, Private).SetValue(target, value);
    private static object Get(object target, string name, Type type = null)
        => (type ?? target.GetType()).GetField(name, Private).GetValue(target);
    private static void Call(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[CombatMovementCheck] FAIL: " + message);
    }
}
