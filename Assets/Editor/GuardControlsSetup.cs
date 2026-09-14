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

    [MenuItem("Tools/Combat/Apply Guard Gauge HUD")]
    public static void ApplyGuardGauge()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        EditPrefab(HudPath, root =>
        {
            var ui = root.GetComponent<PlayerStateUI>();
            var serialized = new SerializedObject(ui);
            if (serialized.FindProperty("guardGaugeFill").objectReferenceValue != null) return;
            var hp = (UnityEngine.UI.Image)serialized.FindProperty("hpSprite").objectReferenceValue;
            var white = (UnityEngine.UI.Image)serialized.FindProperty("shieldSprite").objectReferenceValue;
            var hpText = (TMP_Text)serialized.FindProperty("hpText").objectReferenceValue;
            var hpBackground = hp.transform.parent.GetComponent<UnityEngine.UI.Image>();
            var panel = (RectTransform)hpBackground.transform.parent;
            float hpHeight = panel.sizeDelta.y;
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, hpHeight + 14f);
            var hpRect = hpBackground.rectTransform;
            hpRect.anchorMin = new Vector2(0f, 1f);
            hpRect.anchorMax = Vector2.one;
            hpRect.pivot = new Vector2(.5f, 1f);
            hpRect.sizeDelta = new Vector2(0f, hpHeight);
            hpRect.anchoredPosition = Vector2.zero;

            // 새 아트 없이 기존 HP 프레임/무채색 게이지/폰트만 재사용한다.
            var background = GaugeImage("BG_GuardGauge", panel, hpBackground.sprite);
            background.type = UnityEngine.UI.Image.Type.Sliced;
            var rect = background.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.right;
            rect.pivot = new Vector2(.5f, 0f);
            rect.sizeDelta = new Vector2(0f, 12f);
            rect.anchoredPosition = Vector2.zero;
            var fill = GaugeImage("Field_GuardGauge", rect, white.sprite);
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.color = serialized.FindProperty("guardReadyColor").colorValue;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = new Vector2(4f, 3f);
            fill.rectTransform.offsetMax = new Vector2(-4f, -3f);
            var label = Object.Instantiate(hpText, rect);
            label.name = "Text_GuardGauge";
            label.raycastTarget = false;
            label.text = "100 / 100";
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            serialized.FindProperty("guardGaugeFill").objectReferenceValue = fill;
            serialized.FindProperty("guardGaugeText").objectReferenceValue = label;
            var slot = serialized.FindProperty("guardSlot");
            if (slot.FindPropertyRelative("CooldownFill").objectReferenceValue is UnityEngine.UI.Image cooldown)
                cooldown.gameObject.SetActive(false);
            if (slot.FindPropertyRelative("CooldownText").objectReferenceValue is TMP_Text cooldownText)
                cooldownText.text = "";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        });
        // 수치를 실제 플레이어 프리팹에도 저작해 인스펙터에서 쉽게 조절할 수 있게 한다.
        EditPrefab("Assets/Prefabs/Player Melee.prefab", root =>
        {
            var guard = root.GetComponent<PlayerParryController>();
            Check(guard != null, "기존 플레이어 가드 컴포넌트");
            EditorUtility.SetDirty(guard);
        });
        Debug.Log("[GuardCheck] HUD gauge saved in PlayerStateUI.prefab; reused existing sprites only.");
    }

    private static UnityEngine.UI.Image GaugeImage(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<UnityEngine.UI.Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    // 기존 프리팹을 실제 Unity 카메라로 렌더한다. 결과는 검수용 Logs 파일이고 게임 아트가 아니다.
    [MenuItem("Tools/Combat/Preview Guard Gauge HUD")]
    public static void PreviewGuardGauge()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var cameraObject = new GameObject("HUD preview camera", typeof(Camera));
        var canvasObject = new GameObject("HUD preview", typeof(RectTransform), typeof(Canvas));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
        var camera = cameraObject.GetComponent<Camera>();
        camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.07f, .08f, .1f);
        camera.orthographic = true;
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 10f;
        var scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(960f, 540f);
        var target = new RenderTexture(1920, 1080, 24);
        var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Logs"));
        System.IO.Directory.CreateDirectory(output);
        try
        {
            camera.targetTexture = target;
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath), canvas.transform);
            var serialized = new SerializedObject(root.GetComponent<PlayerStateUI>());
            var gauge = (UnityEngine.UI.Image)serialized.FindProperty("guardGaugeFill").objectReferenceValue;
            var label = (TMP_Text)serialized.FindProperty("guardGaugeText").objectReferenceValue;
            ((UnityEngine.UI.Image)serialized.FindProperty("shieldSprite").objectReferenceValue).enabled = false;
            ((TMP_Text)serialized.FindProperty("hpText").objectReferenceValue).text = "100 / 100";
            for (int state = 0; state < 2; state++)
            {
                gauge.fillAmount = state == 0 ? 1f : .5f;
                gauge.color = serialized.FindProperty(state == 0 ? "guardReadyColor" : "guardBrokenColor").colorValue;
                label.text = state == 0 ? "100 / 100" : "50 / 100";
                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root.GetComponent<RectTransform>());
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                pixels.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(output, $"GuardGauge-{state}.png"), pixels.EncodeToPNG());
            }
            Debug.Log($"[GuardCheck] Preview saved: {output}/GuardGauge-0.png / GuardGauge-1.png");
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
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
            Check(c.CanReflect == (level >= 2), "Lv2부터 투사체 반사");
            Check(Mathf.Approximately(c.EffectiveRadius, level >= 3 ? 3.325f : 2.5f) && c.angle == 160f, "Lv3 반경만 33% 확장");
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
            var stat = host.AddComponent<CharacterStat>();
            var health = host.GetComponent<CharacterHealth>();
            health.Init(stat, null);
            stat.SetCurrentDef(25f);
            Set(guard, "_player", player);
            var dodge = host.AddComponent<MeleeDodgeController>();
            Set(guard, "_dodge", dodge);
            Set(dodge, "_isDashing", true);
            Check((bool)typeof(PlayerParryController).GetProperty("IsDashing", Fields).GetValue(guard),
                "근접 대쉬 상태도 가드 시작 차단에 포함");
            Set(dodge, "_isDashing", false);
            enemy.transform.position = Vector3.right;
            var hit = new DamageInfo(20f, DamageType.Physical, enemy, causesHitstun: true,
                knockbackForce: 20f, applyStatus: StatusType.Freeze, category: DamageCategory.EnemyBoss);
            int successes = 0, damageEvents = 0;
            guard.OnParrySuccess += () => successes++;
            health.TakeDamageEvent += (_, __, ___, ____) => damageEvents++;
            Arm(guard, health, registry.rightClicks[3].config);
            Set(guard, "_raisedAt", Time.time - 60f);
            health.GetDamage(hit);
            Check(health.CurHP == 100f && damageEvents == 0 && successes == 1 && guard.IsParrying
                && !player.canChangeState && Mathf.Approximately(player.SpeedMultiplier, .3f), "오래 유지한 가드도 피해/경직 없이 방어");
            Check(guard.GuardAmount == 85f && !guard.LastBlockWasPerfect, "방어력 25% 적용: 피해 20은 게이지 15 소모");
            health.GetDamage(hit);
            Check(guard.GuardAmount == 70f && successes == 2, "매 타격마다 소모, Lv4 환급 없음");
            Set(guard, "_raisedAt", Time.time);
            health.GetDamage(hit);
            Check(guard.GuardAmount == 62.5f && guard.LastBlockWasPerfect, "0.2초 퍼펙트: 최종 피해 15의 절반 7.5");
            Tick(guard, 10f, Time.time + 10f);
            Check(guard.GuardAmount == 62.5f, "가드를 올린 동안 회복 없음");
            var back = hit; back.hitFrom = Vector2.left;
            Check(!PlayerParryController.Intercept(health, ref back), "후방 방어 불가");
            var ground = hit; ground.bypassGuard = true;
            Check(!PlayerParryController.Intercept(health, ref ground), "장판/돌진 제외");
            var dot = hit; dot.category = DamageCategory.EnemyDebuff;
            var trap = hit; trap.category = DamageCategory.Trap;
            Check(!PlayerParryController.CanGuard(dot) && !PlayerParryController.CanGuard(trap), "DoT/가시 제외");
            typeof(PlayerParryController).GetMethod("UpdateAim", Fields).Invoke(guard, new object[] { Vector2.up });
            Check(!PlayerParryController.Intercept(health, ref hit), "홀드 중 방향 변경: 옛 방향은 방어 안 함");
            var up = hit; up.hitFrom = Vector2.up;
            Check(PlayerParryController.Intercept(health, ref up), "새 조준 방향으로 방어");

            float beforeRecovery = guard.GuardAmount;
            Check(guard.TryInterruptForAction() && !guard.IsParrying && player.canChangeState
                && Mathf.Approximately(player.SpeedMultiplier, 1f), "다른 행동은 후딜 없이 가드 해제");
            float loweredAt = Time.time;
            Tick(guard, .5f, loweredAt + .5f);
            Check(guard.GuardAmount == beforeRecovery, "가드를 내린 뒤 1초는 회복 안 함");
            Tick(guard, .75f, loweredAt + 1.25f);
            Check(Mathf.Approximately(guard.GuardAmount, beforeRecovery + .75f), "지연 경계를 넘긴 0.25초만 3/초 회복");

            Arm(guard, health, registry.rightClicks[0].config);
            Set(guard, "_guard", 5f);
            health.GetDamage(hit);
            Check(guard.GuardBroken && guard.GuardAmount == 0f && !guard.IsParrying && health.CurHP == 100f,
                "부족한 마지막 공격까지 완전 방어 후 소진");
            guard.TryStartParry();
            Check(!guard.IsParrying && !PlayerParryController.Intercept(health, ref hit), "소진 중 재사용/방어 불가");
            Tick(guard, 6f, Time.time + 6f);
            Check(Mathf.Approximately(guard.GuardAmount, 50f) && guard.GuardBroken, "소진 즉시 회복 시작, 6초에 50");
            Tick(guard, 5.9f, Time.time + 11.9f);
            Check(guard.GuardBroken, "100 미만은 계속 소진");
            Tick(guard, .1f, Time.time + 12f);
            Check(!guard.GuardBroken && guard.GuardAmount == 100f, "12초에 100 회복/잠금 해제");
            Tick(guard, 100f, Time.time + 100f);
            Check(guard.GuardAmount == 100f, "최대치 초과 금지");
            Arm(guard, health, registry.rightClicks[0].config);
            guard.SetGuardHeld(false);
            Check(!guard.IsParrying, "버튼을 떼면 즉시 가드 해제");
            var beforeHP = health.CurHP;
            health.GetDamage(hit);
            Check(health.CurHP == beforeHP - 15f, "일반 피격도 동일한 방어력 식");
            var statEnemy = enemy.AddComponent<CharacterStat>();
            Set(statEnemy, "baseCritChance", 100f);
            Set(statEnemy, "baseCritDamage", 200f);
            Check(health.CalculateGuardDamage(hit) == 30f, "치명타도 방어력 이후 가드 소모에 반영");
            Check(health.CalculateGuardDamage(new DamageInfo(0f, attacker: enemy)) == 0f, "0 피해를 1로 올리지 않음");

            var uiObject = Object.Instantiate(hud);
            uiObject.SetActive(false);
            try
            {
                var ui = uiObject.GetComponent<PlayerStateUI>();
                Set(ui, "_guardCtrl", guard);
                var serialized = new SerializedObject(ui);
                var fill = serialized.FindProperty("guardGaugeFill").objectReferenceValue as UnityEngine.UI.Image;
                var label = serialized.FindProperty("guardGaugeText").objectReferenceValue as TMP_Text;
                Check(fill != null && label != null && fill.sprite != null && !fill.raycastTarget,
                    "HUD 프리팹에 게이지/텍스트/기존 스프라이트 실제 연결");
                Set(guard, "_guard", 50f);
                typeof(PlayerStateUI).GetMethod("RefreshGuardGauge", Fields).Invoke(ui, null);
                Check(fill.fillAmount == .5f && fill.color.b > fill.color.r, "사용 가능 게이지는 파랑");
                Set(guard, "_guardBroken", true);
                typeof(PlayerStateUI).GetMethod("RefreshGuardGauge", Fields).Invoke(ui, null);
                Check(fill.fillAmount == .5f && Mathf.Approximately(fill.color.r, fill.color.b), "소진 회복 게이지는 회색");
            }
            finally { Object.DestroyImmediate(uiObject); }
            Debug.Log("[GuardCheck] PASS — hold, damage/defense/critical, perfect, depletion, recovery, interruption, HUD.");

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
        var previousInventory = InventoryManager.Instance;
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
            var stat = host.AddComponent<CharacterStat>();
            var health = host.GetComponent<CharacterHealth>();
            health.Init(stat, null);
            typeof(CharacterStat).GetProperty("Health").SetValue(stat, health);
            Set(player, "stat", stat);
            Set(guard, "_player", player);
            var inventory = host.AddComponent<InventoryManager>();
            inventory.Slots.Add(new InventoryManager.CoreSlot());
            inventory.Slots.Add(new InventoryManager.CoreSlot
            {
                EquippedRightClick = AssetDatabase.LoadAssetAtPath<RightClickDataSO>(LevelPath(1))
            });
            InventoryManager.Instance = inventory;
            guard.SetGuardHeld(true);
            Check(guard.IsParrying && !player.canChangeState, "홀드 입력으로 실제 가드 시작");
            player.CanChangeAnimState();
            Check(!player.canChangeState, "애니메이션 종료가 유지 중인 가드를 해제하지 않음");
            guard.SetGuardHeld(false);
            guard.SetGuardHeld(true);
            Check(guard.IsParrying, "내린 즉시 쿨타임/후딜 없이 다시 올리기");
            player.SetInputBlocked(true);
            Check(!guard.IsParrying, "대화/입력 차단 시 가드 해제");
            guard.SetGuardHeld(true);
            Check(!guard.IsParrying, "입력 차단 중 가드 재시작 금지");
            player.SetInputBlocked(false);
            var dodge = host.AddComponent<MeleeDodgeController>();
            Set(guard, "_dodge", dodge);
            Set(dodge, "_isDashing", true);
            guard.SetGuardHeld(true);
            Check(!guard.IsParrying, "대쉬 도중 가드 시작 금지");
            Set(dodge, "_isDashing", false);
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
                    Check(guard.GuardAmount == 100f - 30f * (shot + 1), "투사체마다 실제 피해량 소모");
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
            // 발사 시각이 아니라 실제 방어 시각으로 퍼펙트를 판정한다.
            for (int late = 0; late < 2; late++)
            {
                Arm(guard, health, AssetDatabase.LoadAssetAtPath<RightClickDataSO>(LevelPath(1)).config);
                var source = new GameObject(probeName);
                source.SetActive(false);
                source.transform.SetParent(host.transform);
                source.transform.position = Vector3.right;
                var projectile = source.AddComponent<TrackingFireball>();
                projectile.Init(host.transform, 30f, Layers.PlayerMask, enemy, 10f, 3f);
                Set(guard, "_raisedAt", Time.time - (late == 0 ? 0f : .3f));
                Check(projectile.GuardInfo.type == DamageType.Magic, "유도탄의 실제 피해와 가드 비용은 같은 마법 속성");
                Check(PlayerParryController.TryBlockProjectile(projectile, health)
                    && guard.GuardAmount == (late == 0 ? 85f : 70f)
                    && guard.LastBlockWasPerfect == (late == 0), "투사체 도착 시점으로 퍼펙트/일반 방어 구분");
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
            Check(successes == beforeScan + 4 && !guard.IsParrying && guard.GuardBroken && guard.GuardAmount == 0f,
                "동시 4공격 방어 후 소진, 중복 콜라이더로 게이지를 더 깎지 않음");
            Debug.Log("[GuardCheck] PASS — repeated and simultaneous attacks, reflection, ownership, duplicate colliders, gauge consumption.");
        }
        finally
        {
            foreach (var p in Resources.FindObjectsOfTypeAll<Projectile>())
                if (p.name == probeName + "(Clone)") Object.DestroyImmediate(p.gameObject);
            Object.DestroyImmediate(scanRoot);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(enemy);
            activeField.SetValue(null, previousGuard);
            InventoryManager.Instance = previousInventory;
        }
    }

    private static void Arm(PlayerParryController guard, CharacterHealth health, RightClickConfig config)
    {
        Set(guard, "_activeConfig", config);
        Set(guard, "_activeSelf", health);
        Set(guard, "_activeAimDir", Vector2.right);
        Set(guard, "_isParrying", true);
        Set(guard, "_guard", 100f);
        Set(guard, "_guardBroken", false);
        Set(guard, "_raisedAt", Time.time - 1f);
        typeof(PlayerParryController).GetField("_active", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, guard);
        var player = (PlayerController)typeof(PlayerParryController).GetField("_player", Fields).GetValue(guard);
        player.SetSpeedModifier(PlayerController.SpeedModifierSource.Parry, config.moveSpeedMultiplier);
        player.canChangeState = false; // 비활성 테스트 객체에서는 LockAnimState의 타임아웃 코루틴을 시작하지 않는다.
    }
    private static void Tick(PlayerParryController guard, float dt, float now) =>
        typeof(PlayerParryController).GetMethod("TickGauge", Fields).Invoke(guard, new object[] { dt, now });

    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Fields).SetValue(obj, value);
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("[GuardCheck] FAIL: " + label); }
}
