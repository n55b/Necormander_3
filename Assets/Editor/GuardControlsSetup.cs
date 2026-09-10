using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Object = UnityEngine.Object;

/// <summary>가드/Space 전환 에셋 배선과 재실행 가능한 회귀 검사. 런타임 자동 배선은 하지 않는다.</summary>
public static class GuardControlsSetup
{
    private const string HudPath = "Assets/Prefabs/UI/Player State/PlayerStateUI.prefab";
    private const string ExplainPath = "Assets/Prefabs/UI/SkillExplainUI.prefab";
    private const string RegistryPath = "Assets/SOData/Registry/Growth Reward Registry.asset";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string BatchProjectileCheck = "GuardControlsSetup.ProjectileCheck";
    private static string LevelPath(int level) => level == 1 ? "Assets/SOData/RightClick/Guard.asset"
        : $"Assets/SOData/RightClick/GuardLevel{level}.asset";

    [MenuItem("Tools/Combat/Apply Guard And Space Controls")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var levels = new RightClickDataSO[4];
        for (int i = 0; i < levels.Length; i++)
        {
            string path = LevelPath(i + 1);
            var so = AssetDatabase.LoadAssetAtPath<RightClickDataSO>(path);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<RightClickDataSO>();
                so.config = new RightClickConfig { level = i + 1 };
                AssetDatabase.CreateAsset(so, path);
            }
            so.config.type = RightClickType.Guard;
            so.config.level = i + 1;
            so.displayName = $"가드 Lv{i + 1}";
            EditorUtility.SetDirty(so);
            levels[i] = so;
        }
        var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(RegistryPath);
        registry.rightClicks = levels.ToList();
        registry.defaultRightClick = levels[0];
        EditorUtility.SetDirty(registry);
        // 기존 Parry/Counter 에셋은 보존하되 레지스트리/런타임 선택에서는 제외한다.
        // 삭제된 Q/E 스킬 풀의 직렬화 키도 실제 에셋에서 정리한다. 패시브/강화는 그대로 저장.
        foreach (var equipment in registry.equipments) if (equipment != null) EditorUtility.SetDirty(equipment);
        AssetDatabase.SaveAssets();

        EditPrefab(HudPath, root =>
        {
            var ui = root.GetComponentInChildren<PlayerStateUI>(true);
            var serialized = new SerializedObject(ui);
            var guard = serialized.FindProperty("guardSlot");
            var minion = serialized.FindProperty("minionSkillSlot");
            ConfigureSlot(guard, "Icon_Guard", "Q", "RMB");
            ConfigureSlot(minion, "Icon_MinionSkill", "R", "SPACE");
            // 이름이 확인된 옛 E 슬롯 하나만 삭제. Q 자리는 우클릭 쿨타임으로 재사용한다.
            var oldE = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Icon_E_Skill");
            if (oldE != null) Object.DestroyImmediate(oldE.gameObject);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        });
        EditPrefab(ExplainPath, root =>
        {
            var ui = root.GetComponentInChildren<SkillExplainUI>(true);
            var serialized = new SerializedObject(ui);
            var slots = serialized.FindProperty("equipmentSlots");
            if (slots.arraySize > 1)
            {
                var keep = slots.GetArrayElementAtIndex(slots.arraySize - 1).objectReferenceValue;
                for (int i = 0; i < slots.arraySize - 1; i++)
                {
                    var old = slots.GetArrayElementAtIndex(i).objectReferenceValue as Component;
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                }
                slots.arraySize = 1;
                slots.GetArrayElementAtIndex(0).objectReferenceValue = keep;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        });
        EditPrefab("Assets/Prefabs/Player Melee.prefab", root =>
        {
            var input = root.GetComponent<PlayerInput>();
            input.defaultActionMap = "Player";
            var action = input.actions.FindAction("Player/MinionSkill", true);
            var pouch = input.actions.FindAction("Player/Pouch", true);
            var events = input.actionEvents.Where(e => e.actionId != action.id.ToString()
                && e.actionId != pouch.id.ToString()).ToList();
            var minionEvent = new PlayerInput.ActionEvent(action);
            UnityEditor.Events.UnityEventTools.AddPersistentListener<InputAction.CallbackContext>(
                minionEvent, root.GetComponent<PlayerController>().OnMinionSkill);
            events.Add(minionEvent);
            var pouchEvent = new PlayerInput.ActionEvent(pouch);
            UnityEditor.Events.UnityEventTools.AddPersistentListener<InputAction.CallbackContext>(
                pouchEvent, root.GetComponent<PlayerController>().OnPouch);
            events.Add(pouchEvent);
            input.actionEvents = events.ToArray();
            foreach (var anim in root.GetComponentsInChildren<Animator>(true))
                if (anim.runtimeAnimatorController != null &&
                    anim.runtimeAnimatorController.name.IndexOf("HandSkill", StringComparison.OrdinalIgnoreCase) >= 0)
                    Object.DestroyImmediate(anim);
        });
        Verify();
    }

    private static void ConfigureSlot(SerializedProperty slot, string name, string oldKey, string newKey)
    {
        var root = slot.FindPropertyRelative("SlotRoot").objectReferenceValue as GameObject;
        if (root == null) throw new InvalidOperationException("HUD 슬롯 루트 연결 누락");
        root.name = name;
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.text != oldKey) continue;
            text.text = newKey;
            text.enableAutoSizing = true;
            text.fontSizeMin = 8f;
            text.fontSizeMax = 18f;
            var size = text.rectTransform.sizeDelta;
            text.rectTransform.sizeDelta = new Vector2(Mathf.Max(55f, size.x), size.y);
        }
        var arrow = slot.FindPropertyRelative("ArrowImage");
        var button = slot.FindPropertyRelative("SkillChangeButton");
        if (arrow.objectReferenceValue is GameObject arrowGo) Object.DestroyImmediate(arrowGo);
        if (button.objectReferenceValue is Component buttonComponent) Object.DestroyImmediate(buttonComponent);
        arrow.objectReferenceValue = null;
        button.objectReferenceValue = null;
    }

    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Combat/Verify Guard And Space Controls")]
    public static void Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/PlayerInputSystem.inputactions");
        Check(input.FindAction("Player/SkillQ") == null && input.FindAction("Player/SkillE") == null
            && input.FindAction("Player/SkillR") == null, "Q/E/R 액션 제거");
        var skill = input.FindAction("Player/MinionSkill", true);
        Check(skill.bindings.Any(b => b.path == "<Keyboard>/space"), "Space 미니언 스킬");
        Check(input.FindAction("Player/Dash", true).bindings.Any(b => b.path == "<Keyboard>/leftShift"), "Shift 대쉬 유지");
        Check(!input.bindings.Any(b => b.path == "<Keyboard>/q" || b.path == "<Keyboard>/e" || b.path == "<Keyboard>/r"), "옛 키 바인딩 없음");

        var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(RegistryPath);
        Check(registry.rightClicks.Count == 4 && registry.ResolveDefaultRightClick().config.level == 1, "마을 NPC 4레벨/기본 Lv1");
        for (int level = 1; level <= 4; level++)
        {
            var c = registry.rightClicks[level - 1].config;
            Check(c.level == level && c.IsValid, $"Lv{level} 유효");
            Check(Mathf.Approximately(c.activeDuration, .4f) && Mathf.Approximately(c.recoveryDuration, .3f)
                && Mathf.Approximately(c.cooldownDuration, 3f), "가드 .4초/실패 .3초/쿨타임 3초");
            Check(c.CanReflect == (level >= 2), "Lv2부터 투사체 반사");
            Check(Mathf.Approximately(c.EffectiveRadius, level >= 3 ? 3.325f : 2.5f) && c.angle == 160f, "Lv3 반경만 33% 확장");
            Check(Mathf.Approximately(c.SuccessRefund, level == 4 ? .75f : 0f), "Lv4 기본 쿨타임의 25% 환급");
        }
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        Check(!hud.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Icon_E_Skill" || t.name == "Icon_Q_Skill" || t.name == "Icon_R_Skill"), "HUD 옛 슬롯 제거/교체");
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab");
        Check(playerPrefab.GetComponent<PlayerInput>().defaultActionMap == "Player", "플레이어 입력맵 자동 활성화");
        var events = playerPrefab.GetComponent<PlayerInput>().actionEvents;
        Check(!events.Any(e => e.actionName.Contains("SkillQ") || e.actionName.Contains("SkillE") || e.actionName.Contains("SkillR")), "프리팹 입력 이벤트 교체");
        Check(events.Any(e => e.actionName.Contains("MinionSkill") &&
            Enumerable.Range(0, e.GetPersistentEventCount()).Any(i => e.GetPersistentMethodName(i) == "OnMinionSkill")), "Space 콜백 실제 연결");
        foreach (string method in new[] { "OnMove", "OnAttackInput", "OnParry", "OnHandSlot", "OnGemTree", "OnInteract", "OnOption", "OnDash", "OnPouch" })
            Check(events.Any(e => Enumerable.Range(0, e.GetPersistentEventCount())
                .Any(i => e.GetPersistentMethodName(i) == method)), "기존 입력 유지: " + method);

        var host = new GameObject("Guard regression (inactive)");
        host.SetActive(false); // 실제 플레이어 Awake/Start나 인벤토리를 건드리지 않는다.
        var enemy = new GameObject("Guard attacker");
        enemy.SetActive(false);
        enemy.layer = Layers.Enemy;
        try
        {
            var player = host.AddComponent<PlayerController>();
            var guard = host.AddComponent<PlayerParryController>();
            var health = host.AddComponent<CharacterHealth>();
            Set(guard, "_player", player);
            Set(health, "curHP", 100f);
            enemy.transform.position = Vector3.right;
            var hit = new DamageInfo(30f, DamageType.Physical, enemy, causesHitstun: true,
                knockbackForce: 20f, applyStatus: StatusType.Freeze, category: DamageCategory.EnemyBoss);
            int successes = 0, failures = 0, damageEvents = 0;
            guard.OnParrySuccess += () => successes++;
            guard.OnParryFail += () => failures++;
            health.TakeDamageEvent += (_, __, ___, ____) => damageEvents++;
            Arm(guard, health, registry.rightClicks[3].config);
            float windowEnd = (float)typeof(PlayerParryController).GetField("_windowEnd", Fields).GetValue(guard);
            health.GetDamage(hit);
            Check(health.CurHP == 100f && damageEvents == 0 && successes == 1 && guard.IsParrying
                && !player.canChangeState && Mathf.Approximately(player.SpeedMultiplier, .3f), "실제 피해 차단 후 가드 자세/이속 유지");
            Check(Mathf.Abs(guard.CooldownRemaining - 2.25f) < .05f, "성공 1회 0.75초 환급");
            health.GetDamage(hit);
            var rangedHit = hit; rangedHit.isRanged = true;
            health.GetDamage(rangedHit);
            Check(health.CurHP == 100f && damageEvents == 0 && successes == 3 && guard.IsParrying,
                "같은 가드에서 근접/원거리 연속 타격 전부 차단");
            Check(Mathf.Abs(guard.CooldownRemaining - 2.25f) < .05f &&
                (float)typeof(PlayerParryController).GetField("_windowEnd", Fields).GetValue(guard) == windowEnd,
                "추가 방어는 환급 중첩/시간 연장 없음");
            guard.TryStartParry();
            Check((float)typeof(PlayerParryController).GetField("_windowEnd", Fields).GetValue(guard) == windowEnd,
                "활성 중 재입력으로 시간 연장 금지");
            Set(guard, "_windowEnd", Time.time - .01f);
            Check(!PlayerParryController.Intercept(health, ref hit), "성공했어도 지속시간 종료 뒤 방어 불가");
            var successRoutine = (IEnumerator)typeof(PlayerParryController).GetMethod("WindowRoutine", Fields)
                .Invoke(guard, new object[] { registry.rightClicks[3].config });
            Check(!successRoutine.MoveNext() && !guard.IsParrying && failures == 0 && player.canChangeState
                && Mathf.Approximately(player.SpeedMultiplier, 1f), "성공한 가드는 시간 종료 시 실패 후딜 없이 Idle");
            guard.TryStartParry();
            Check(!guard.IsParrying, "쿨타임 중 재입력 금지");

            Arm(guard, health, registry.rightClicks[0].config);
            var back = hit; back.hitFrom = Vector2.left;
            Check(!PlayerParryController.Intercept(health, ref back), "뒤쪽 공격은 가드 불가");
            var ground = hit; ground.bypassGuard = true;
            Check(!PlayerParryController.Intercept(health, ref ground) && guard.IsParrying, "장판/돌진은 가드 소비 없이 통과");
            var dot = hit; dot.category = DamageCategory.EnemyDebuff;
            Check(!PlayerParryController.CanGuard(dot), "DoT 가드 불가");
            var trap = hit; trap.category = DamageCategory.Trap;
            Check(!PlayerParryController.CanGuard(trap), "가시/환경 피해 가드 불가");
            Check(PlayerParryController.CanGuard(hit), "보스 찌르기처럼 이동을 동반한 일반 근접은 가드 가능");
            var ranged = hit; ranged.isRanged = true;
            Check(PlayerParryController.CanGuard(ranged), "Lv1도 원거리 피해 완전 방어");

            Set(guard, "_windowEnd", Time.time - .01f);
            Check(!PlayerParryController.Intercept(health, ref hit), "0.4초 창 종료 후 피격 통과");
            var routine = (IEnumerator)typeof(PlayerParryController).GetMethod("WindowRoutine", Fields)
                .Invoke(guard, new object[] { registry.rightClicks[0].config });
            Check(routine.MoveNext() && routine.Current is WaitForSeconds && failures == 1 && guard.IsParrying, "실패만 후딜 시작");
            float recovery = (float)typeof(WaitForSeconds).GetField("m_Seconds", Fields).GetValue(routine.Current);
            Check(Mathf.Approximately(recovery, .3f), "실패 후딜 0.3초");
            Check(!routine.MoveNext() && !guard.IsParrying, "실패 후딜 종료 후 Idle 복귀");
            Debug.Log("[GuardCheck] PASS — input, assets, levels, repeated blocks, fixed duration, one refund, success/failure recovery.");
        }
        finally { Object.DestroyImmediate(host); Object.DestroyImmediate(enemy); }
    }

    // 배치 실행 전용: 실제 게임/저장 데이터를 시작하지 않고 빈 씬에서 투사체까지 검사한다.
    public static void VerifyBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("배치 모드 전용 검사입니다.");
        Verify();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        SessionState.SetBool(BatchProjectileCheck, true);
        ResumeBatchCheck();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void ResumeBatchCheck()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(BatchProjectileCheck, false)) return;
        EditorApplication.playModeStateChanged -= RunBatchProjectiles;
        EditorApplication.playModeStateChanged += RunBatchProjectiles;
    }

    private static void RunBatchProjectiles(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.EraseBool(BatchProjectileCheck);
        EditorApplication.playModeStateChanged -= RunBatchProjectiles;
        try { VerifyProjectiles(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Combat/Verify Guard Projectiles In Play Mode")]
    public static void VerifyProjectiles()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행하세요.");
        var activeField = typeof(PlayerParryController).GetField("_active", BindingFlags.Static | BindingFlags.NonPublic);
        var previousGuard = activeField.GetValue(null);
        var host = new GameObject("Guard projectile check") { hideFlags = HideFlags.HideAndDontSave };
        host.SetActive(false);
        var enemy = new GameObject("Guard projectile attacker") { hideFlags = HideFlags.HideAndDontSave };
        enemy.SetActive(false);
        enemy.layer = Layers.Enemy;
        enemy.transform.position = Vector3.right * 5f;
        string probeName = "Guard probe " + Guid.NewGuid();
        var scanRoot = new GameObject(probeName + " scan") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var player = host.AddComponent<PlayerController>();
            var guard = host.AddComponent<PlayerParryController>();
            var health = host.AddComponent<CharacterHealth>();
            Set(guard, "_player", player);
            int successes = 0;
            guard.OnParrySuccess += () => successes++;
            for (int level = 1; level <= 2; level++)
            {
                var config = AssetDatabase.LoadAssetAtPath<RightClickDataSO>(LevelPath(level)).config;
                Arm(guard, health, config);
                for (int shot = 0; shot < 2; shot++)
                {
                    var source = new GameObject(probeName) { hideFlags = HideFlags.HideAndDontSave };
                    source.SetActive(false);
                    source.transform.SetParent(host.transform);
                    source.transform.position = Vector3.right;
                    var projectile = source.AddComponent<Projectile>();
                    projectile.Init(Vector2.zero, 30f, Layers.PlayerMask, enemy, 10f, 3f);
                    Check(PlayerParryController.TryBlockProjectile(projectile, health), "실제 투사체 방어");
                    Check(projectile.GuardConsumed && successes == (level - 1) * 2 + shot + 1 && guard.IsParrying,
                        "연속 투사체 소비 후에도 가드 유지");
                    Check(!PlayerParryController.TryBlockProjectile(projectile, health), "소비된 투사체 재타격 금지");
                    var clones = Resources.FindObjectsOfTypeAll<Projectile>()
                        .Where(p => p.name == probeName + "(Clone)").ToArray();
                    Check(clones.Length == (level == 1 ? 0 : shot + 1), "Lv1 소멸 / Lv2 매 투사체 반사");
                    if (level == 2)
                    {
                        var reflected = clones[0];
                        Check(reflected.Shooter == host && reflected.TargetLayer.value == Layers.EnemyMask
                            && !reflected.GuardConsumed, "반사체 소유자/적 타격 레이어");
                        Check(Vector2.Dot(reflected.Direction, Vector2.right) > .99f
                            && Mathf.Approximately(reflected.GuardInfo.amount, 45f), "발사자 방향 반사/기존 반사 피해 보존");
                    }
                }
            }
            // 한 번의 스캔에서 여러 공격을 처리하고, 한 공격의 중복 콜라이더는 다시 세지 않는다.
            host.transform.position = new Vector3(10000f, 10000f, 0f);
            enemy.transform.position = host.transform.position + Vector3.right * 5f;
            var scanConfig = AssetDatabase.LoadAssetAtPath<RightClickDataSO>(LevelPath(4)).config;
            Arm(guard, health, scanConfig);
            int beforeScan = successes;
            for (int i = 0; i < 4; i++)
            {
                var source = new GameObject(probeName);
                source.SetActive(false);
                source.transform.SetParent(scanRoot.transform);
                source.transform.position = host.transform.position + new Vector3(1f + i * .4f, 0f, 0f);
                if (i < 2)
                    source.AddComponent<Projectile>().Init(host.transform.position, 30f, Layers.PlayerMask, enemy, 10f, 3f);
                source.AddComponent<CircleCollider2D>().isTrigger = true;
                source.AddComponent<BoxCollider2D>().isTrigger = true;
                if (i >= 2)
                    source.AddComponent<BaseHitBox>().Init(new DamageInfo(30f, DamageType.Physical, enemy), Layers.PlayerMask);
                source.SetActive(true);
            }
            Physics2D.SyncTransforms();
            typeof(PlayerParryController).GetMethod("ScanIncoming", Fields).Invoke(guard, new object[] { scanConfig });
            Check(successes == beforeScan + 4 && guard.IsParrying, "동시 투사체 2개/근접 2개 모두 방어, 중복 콜라이더 무시");
            Check(Mathf.Abs(guard.CooldownRemaining - 2.25f) < .05f, "동시 방어도 환급은 사용당 한 번");
            Debug.Log("[GuardCheck] PASS — repeated and simultaneous attacks, reflection, ownership, duplicate colliders, one refund.");
        }
        finally
        {
            foreach (var p in Resources.FindObjectsOfTypeAll<Projectile>())
                if (p.name == probeName + "(Clone)") Object.DestroyImmediate(p.gameObject);
            Object.DestroyImmediate(scanRoot);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(enemy);
            activeField.SetValue(null, previousGuard);
        }
    }

    private static void Arm(PlayerParryController guard, CharacterHealth health, RightClickConfig config)
    {
        Set(guard, "_activeConfig", config);
        Set(guard, "_activeSelf", health);
        Set(guard, "_activeAimDir", Vector2.right);
        Set(guard, "_isParrying", true);
        Set(guard, "_blockedAny", false);
        Set(guard, "_windowEnd", Time.time + config.activeDuration);
        Set(guard, "_cooldownEnd", Time.time + config.cooldownDuration);
        typeof(PlayerParryController).GetField("_active", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, guard);
        var player = (PlayerController)typeof(PlayerParryController).GetField("_player", Fields).GetValue(guard);
        player.SetSpeedModifier(PlayerController.SpeedModifierSource.Parry, config.moveSpeedMultiplier);
        player.canChangeState = false; // 비활성 테스트 객체에서는 LockAnimState의 타임아웃 코루틴을 시작하지 않는다.
    }
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Fields).SetValue(obj, value);
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("[GuardCheck] FAIL: " + label); }
}
