#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Aseprite;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// VillageScene 에 '소환수 선택' NPC 를 배치한다. 기존 마을 NPC(Map/NPC)를 복제해 외형/콜라이더 구성을
/// 그대로 쓰고, 상호작용 컴포넌트만 MinionSelectNPC 로 바꾼다. 다시 실행하면 기존 것을 지우고 새로 만든다.
/// </summary>
public static class MinionSelectNPCSetup
{
    private const string UIPrefabPath = "Assets/Prefabs/UI/Reward Selection/RewardSelectionUI.prefab";
    private const string NpcName = "MinionSelectNPC";
    private const string TestDataDir = "Assets/SOData/Minion/Main/SpriteTests";
    private const string TestPrefabDir = "Assets/Prefabs/Minion/SpriteTests";
    private const string TestAnimDir = "Assets/Animations/Minion/SpriteTests";
    private const string RegistryPath = "Assets/SOData/Registry/Growth Reward Registry.asset";
    private static readonly string[] TestForms = { "Fighter", "Knight", "Knight_2" };

    [MenuItem("Tools/Village/Setup Minion Sprite Tests")]
    public static void SetupSpriteTests()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        var source = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>("Assets/SOData/Minion/Main/MeleeDoll Minion.asset");
        var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(RegistryPath);
        Require(source != null && registry != null, "MeleeDoll/레지스트리 누락");
        EnsureTestFolder(TestDataDir);
        EnsureTestFolder(TestPrefabDir);
        EnsureTestFolder(TestAnimDir);

        foreach (string form in TestForms)
        {
            string artPath = $"Assets/Resources/Sprites/Minion/MeleeMinion_{form}.aseprite";
            var importer = AssetImporter.GetAtPath(artPath) as AsepriteImporter;
            Require(importer != null, artPath);
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, 32f))
            {
                importer.spritePixelsPerUnit = 32f;
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(artPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(artPath).OfType<AnimationClip>().ToArray();
            var attack = clips.Single(c => c.name == "Attack");
            var skill = clips.Single(c => c.name == "Skill");
            Require(attack.events.Any(e => e.functionName == "OnHitEvent") &&
                    skill.events.Any(e => e.functionName == "OnHitEvent"), form + " 타격 이벤트 누락");

            string controllerPath = $"{TestAnimDir}/{form}.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var states = controller.layers[0].stateMachine;
                var complete = states.AddState("Complete");
                complete.writeDefaultValues = false;
                states.defaultState = complete;
                foreach (var clip in new[] { attack, skill })
                {
                    var state = states.AddState(clip.name);
                    state.motion = clip;
                    state.writeDefaultValues = false;
                    // 원본 반복 태그는 보존하고, 소환수 소멸 대기 중 재타격만 막는다.
                    var end = state.AddTransition(complete);
                    end.hasExitTime = true;
                    end.exitTime = 1f;
                    end.duration = 0f;
                }
            }

            string prefabPath = $"{TestPrefabDir}/{form}.prefab";
            var visual = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (visual == null)
            {
                var root = new GameObject(form + " Visual");
                try
                {
                    var body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    body.GetComponentInChildren<Animator>().runtimeAnimatorController = controller;
                    var sprite = body.GetComponentInChildren<SpriteRenderer>();
                    // 256px 캔버스의 빈 하단 여백 대신 첫 자세의 발을 소환 위치에 맞춘다.
                    body.transform.localPosition = new Vector3(0f, -Mathf.Round(sprite.bounds.min.y * 32f) / 32f, 0f);
                    visual = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { Object.DestroyImmediate(root); }
            }

            string dataPath = $"{TestDataDir}/{form} Test Minion.asset";
            var data = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(dataPath);
            if (data == null)
            {
                data = Object.Instantiate(source);
                data.name = form + " Test Minion";
                data.minionName = "[테스트] " + form.Replace('_', ' ');
                data.minionIcon = model.GetComponentInChildren<SpriteRenderer>().sprite;
                data.rewardItemData.itemName = data.minionName;
                data.rewardItemData.icon = data.minionIcon;
                data.rewardItemData.description = "MeleeDoll과 동일한 능력의 외형 테스트용 미니언.";
                data.basicAnim = TestAnimation(visual, attack);
                data.skillAnim = TestAnimation(visual, skill);
                data.dashAnim = new MinionDashAnim { visual = visual, hitBoxClip = "Attack" };
                // 기존 진화형 필터로 일반 보상/시작 3종에서 제외. 마을 지급 NPC는 전부 표시한다.
                data.isEvolvedForm = true;
                data.evolutions = Array.Empty<MainMinionDataSO>();
                AssetDatabase.CreateAsset(data, dataPath);
            }
            if (!registry.minionDatas.Contains(data))
            {
                Undo.RecordObject(registry, "Register minion sprite test");
                registry.minionDatas.Add(data);
                EditorUtility.SetDirty(registry);
            }
        }
        AssetDatabase.SaveAssets();
        VerifySpriteTests();
    }

    private static MinionAnimSet TestAnimation(GameObject visual, AnimationClip clip)
        => new MinionAnimSet
        {
            visual = visual, duration = clip.length, hitEvent = "OnHitEvent",
            sequence = new System.Collections.Generic.List<AnimPhase> { new AnimPhase { tag = clip.name } }
        };

    private static void EnsureTestFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureTestFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    [MenuItem("Tools/Village/Verify Minion Sprite Tests")]
    public static void VerifySpriteTests()
    {
        var source = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>("Assets/SOData/Minion/Main/MeleeDoll Minion.asset");
        var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(RegistryPath);
        foreach (string form in TestForms)
        {
            var data = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>($"{TestDataDir}/{form} Test Minion.asset");
            Require(data != null && registry.minionDatas.Count(m => m == data) == 1, form + " 등록/중복");
            Require(registry.minionDatas.Single(m => m.name == data.name) == data, form + " 씬 전환 저장 키");
            Require(data.minionSkill == source.minionSkill && data.isEvolvedForm && !data.CanEvolve, form + " 스킬/보상 격리");
            Require(JsonUtility.ToJson(data.finisher) == JsonUtility.ToJson(source.finisher) &&
                    JsonUtility.ToJson(data.dashModifier) == JsonUtility.ToJson(source.dashModifier), form + " 기존 능력 유지");
            Require(data.minionIcon != null && data.rewardItemData.icon == data.minionIcon &&
                    data.minionIcon.pixelsPerUnit == 32f, form + " 아이콘/PPU");
            Require(data.dashAnim.visual == data.basicAnim.visual && data.dashAnim.hitBoxClip == "Attack" &&
                    string.IsNullOrEmpty(data.dashAnim.startClip) && string.IsNullOrEmpty(data.dashAnim.endClip), form + " 대쉬 배선");
            foreach (var set in new[] { data.basicAnim, data.skillAnim })
            {
                var animator = set.visual.GetComponentInChildren<Animator>();
                var controller = animator.runtimeAnimatorController as AnimatorController;
                string tag = set.SequenceTags().Single();
                var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == tag).state;
                var clip = state.motion as AnimationClip;
                Require(clip != null && clip.events.Any(e => e.functionName == set.hitEvent), form + "/" + tag + " 이벤트");
                Require(state.transitions.Any(t => t.hasExitTime && t.exitTime == 1f && t.duration == 0f &&
                        t.destinationState.name == "Complete"), form + "/" + tag + " 재타격 방지");
                VerifyOneShot(set.visual, clip);
                Debug.Log($"[MinionSpriteTest] {form}/{tag}: {set.duration:F3}s, hit events={clip.events.Count(e => e.functionName == set.hitEvent)}");
            }
        }
        VerifyVillageChoices();
        Debug.Log("[MinionSpriteTest] PASS: 3종 데이터/외형/이벤트/PPU/능력/보상 제외/저장 키 검증 완료.");
    }

    private static void VerifyOneShot(GameObject prefab, AnimationClip clip)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        try
        {
            var animator = instance.GetComponentInChildren<Animator>();
            animator.gameObject.AddComponent<MinionAnimEventRelay>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Play(clip.name, 0, 0f);
            animator.Update(0f);
            for (float time = 0f; time < clip.length * 2f + 0.5f; time += 0.01f) animator.Update(0.01f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Complete"), prefab.name + "/" + clip.name + " 종료");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void VerifyVillageChoices()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && HandSlotSelectionUI.Instance == null,
            "마을 검사는 플레이를 끈 상태에서 실행하세요.");
        var previousManager = GameManager.Instance;
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/VillageScene.unity");
        try
        {
            var roots = scene.GetRootGameObjects();
            var ui = roots.SelectMany(r => r.GetComponentsInChildren<VillageDebugLoadout>(true)).Single();
            var manager = roots.SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            manager.dataManager = manager.GetComponentInChildren<DataManager>(true);
            manager.inventoryManager = manager.GetComponentInChildren<InventoryManager>(true);
            GameManager.Instance = manager;
            var inventory = manager.inventoryManager;
            inventory.Slots.Clear();
            for (int i = 0; i < InventoryManager.SLOT_COUNT; i++) inventory.Slots.Add(new InventoryManager.CoreSlot());
            var settings = new SerializedObject(ui);
            Require(settings.FindProperty("includeMinions").boolValue, "마을 NPC 미니언 목록 활성화");
            foreach (string field in new[] { "includeItems", "includeEquipments", "includeRightClicks" })
                settings.FindProperty(field).boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(VillageDebugLoadout).GetMethod("Populate", flags).Invoke(ui, null);
            var buttons = (System.Collections.Generic.List<GameObject>)typeof(VillageDebugLoadout).GetField("_spawned", flags).GetValue(ui);
            foreach (string form in TestForms)
            {
                var data = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>($"{TestDataDir}/{form} Test Minion.asset");
                string label = "[미니언] " + data.minionName;
                var button = buttons.Single(b => new SerializedObject(b.GetComponent<SkillTooltipTrigger>())
                    .FindProperty("title").stringValue == label);
                Require(button.GetComponentsInChildren<UnityEngine.UI.Image>(true).Any(i => i.sprite == data.minionIcon),
                    form + " 마을 선택 아이콘");
                button.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Require(inventory.MainSummon == data, form + " NPC 선택 버튼 → 장착");
                var saved = new SaveData();
                inventory.SaveToData(saved);
                Require(saved.slots[InventoryManager.SLOT_MAIN].equippedMinionName == data.name, form + " 실제 저장 키");
            }
            Debug.Log("[MinionSpriteTest] PASS: 마을 NPC의 실제 선택 버튼 3종 → 장착/저장, 씬 원본 변경 없음.");
        }
        finally
        {
            GameManager.Instance = previousManager;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[MinionSpriteTest] " + message);
    }

    [MenuItem("Tools/Village/Setup Minion Select NPC")]
    public static void Setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "VillageScene") { Debug.LogError("[Setup] VillageScene 을 열고 실행하세요."); return; }

        var source = GameObject.Find("Map/NPC");
        if (source == null) { Debug.LogError("[Setup] Map/NPC 를 찾지 못했습니다."); return; }

        var old = GameObject.Find("Map/" + NpcName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var npc = Object.Instantiate(source, source.transform.parent);
        Undo.RegisterCreatedObjectUndo(npc, "Create Minion Select NPC");
        npc.name = NpcName;
        npc.transform.position = new Vector3(-4f, 2f, 0f); // 기존 NPC(4,2)의 맞은편

        var sr = npc.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = new Color(0.8f, 0.7f, 1f, 1f); // 구분용 옅은 보라 틴트(외형 교체 전까지)

        var range = npc.transform.Find("InteractRange");
        if (range == null) { Debug.LogError("[Setup] InteractRange 자식이 없습니다."); return; }

        foreach (var oldNpc in range.GetComponents<NPCBase>()) Object.DestroyImmediate(oldNpc);
        foreach (var popup in range.GetComponents<PopupSystem>()) Object.DestroyImmediate(popup);

        var select = range.gameObject.AddComponent<MinionSelectNPC>();
        var so = new SerializedObject(select);
        so.FindProperty("selectionUIPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<RewardSelectionUI>(UIPrefabPath);
        var nameProp = so.FindProperty("name");
        if (nameProp != null && nameProp.propertyType == SerializedPropertyType.String) nameProp.stringValue = "소환수 선택";
        so.ApplyModifiedPropertiesWithoutUndo();

        // 머리 위 이름표가 있으면 바꾼다.
        foreach (var t in npc.GetComponentsInChildren<TMPro.TMP_Text>(true)) t.text = "소환수 선택";
        foreach (var t in npc.GetComponentsInChildren<UnityEngine.UI.Text>(true)) t.text = "소환수 선택";

        // 메인 소환수 에셋에 강화 기본값(1.2/1.45/1.75)을 실제로 기록해 둔다(인스펙터에서 바로 조정 가능하게).
        foreach (var g in AssetDatabase.FindAssets("t:MainMinionDataSO"))
        {
            var m = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g));
            if (m != null) EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = npc;
        Debug.Log("[Setup] MinionSelectNPC 배치 완료: " + npc.transform.position);
    }
}
#endif
