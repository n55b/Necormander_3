#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// VillageScene 에 '소환수 선택' NPC 를 배치하고, 근접 소환수 진화 계열을 검증한다.
///
/// [26/10/22] 외형 테스트(SpriteTests)로 만들던 전사/기사/기사2/무사 외형을 실제 MeleeDoll 계열에 입혔다.
///   전사(Fighter) ─┬─ 1차 트리: 기사(Knight)  → 용사(Champion)   · Breaker 능력
///                  └─ 2차 트리: 무사(Warrior) → 장수(Warlord)    · Whirlwind 능력
/// 옛 테스트 SO 는 SOData/Minion/Main/Deprecated/SpriteTests 에 남아 있으며 레지스트리에는 없다.
/// (예전 'Setup Minion Sprite Tests' 메뉴는 테스트 SO 를 레지스트리에 되살리므로 제거했다.)
/// </summary>
public static class MinionSelectNPCSetup
{
    private const string UIPrefabPath = "Assets/Prefabs/UI/Reward Selection/RewardSelectionUI.prefab";
    private const string NpcName = "MinionSelectNPC";
    private const string MainDir = "Assets/SOData/Minion/Main";
    private const string RegistryPath = "Assets/SOData/Registry/Growth Reward Registry.asset";
    private static readonly string[] LineForms = { "Fighter", "Knight", "Champion", "Warrior", "Warlord" };

    private static MainMinionDataSO Form(string form)
        => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>($"{MainDir}/{form} Minion.asset");

    [MenuItem("Tools/Village/Verify Minion Evolution Line")]
    public static void VerifySpriteTests()
    {
        var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(RegistryPath);
        Require(registry != null, "레지스트리 누락");
        foreach (string form in LineForms)
        {
            var data = Form(form);
            Require(data != null, form + " 에셋 누락");
            Require(registry.minionDatas.Count(m => m == data) == 1, form + " 등록/중복");
            Require(registry.minionDatas.Count(m => m != null && m.name == data.name) == 1, form + " 저장 키 중복");
            Require(data.isEvolvedForm == (form != "Fighter"), form + " 시작/진화형 구분");
            Require(data.minionIcon != null && data.rewardItemData.icon == data.minionIcon &&
                    data.minionIcon.pixelsPerUnit == 32f, form + " 아이콘/PPU");
            Require(data.dashAnim.visual == data.basicAnim.visual && data.dashAnim.hitBoxClip == data.basicAnim.SequenceTags().Single(), form + " 대쉬 배선");
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
            }
        }
        // 같은 트리는 같은 능력(원래 Breaker / Whirlwind 능력)을 쓴다.
        RequireSameAbility(Form("Knight"), Form("Champion"));
        RequireSameAbility(Form("Warrior"), Form("Warlord"));
        Require(Form("Knight").minionSkill.name == "MeleeDoll_Breaker_Active", "기사 = Breaker 스킬");
        Require(Form("Warrior").minionSkill.name == "MeleeDoll_Whirlwind_Active", "무사 = Whirlwind 스킬");

        VerifyEvolutionTree();
        VerifyVillageChoices();
        Debug.Log("[MinionLine] PASS: 전사/기사/용사/무사/장수 데이터·외형·능력·진화·저장 키 검증 완료.");
    }

    private static void RequireSameAbility(MainMinionDataSO a, MainMinionDataSO b)
    {
        Require(a.minionSkill == b.minionSkill &&
                JsonUtility.ToJson(a.finisher) == JsonUtility.ToJson(b.finisher) &&
                JsonUtility.ToJson(a.dashModifier) == JsonUtility.ToJson(b.dashModifier), $"{a.name} ↔ {b.name} 능력 동일");
    }

    private static void VerifyEvolutionTree()
    {
        var go = new GameObject("Minion evolution validation");
        try
        {
            var inventory = go.AddComponent<InventoryManager>();
            inventory.Slots.Clear();
            for (int i = 0; i < InventoryManager.SLOT_COUNT; i++) inventory.Slots.Add(new InventoryManager.CoreSlot());
            foreach (var (branch, final) in new[] { ("Knight", "Champion"), ("Warrior", "Warlord") })
            {
                inventory.EquipMinion(Form("Fighter"));
                inventory.ResetMainSummonEnhance();
                // 강화 없이 첫 보상방에서 바로 진화 갈래가 뜬다.
                Require(inventory.CanEvolveMainSummon && RewardProcessor.GenerateMinionEvolveRewards(inventory).Count == 2, "전사 첫 보상 = 기사/무사 선택지");
                Require(!inventory.EvolveMainSummon(Form(final)), "진화 단계 건너뛰기 금지");
                Require(inventory.EvolveMainSummon(Form(branch)) && inventory.MainSummonEnhanceLevel == 0, branch + " 진화/강화 초기화");
                Require(inventory.CanEvolveMainSummon && inventory.EvolveMainSummon(Form(final)) && !inventory.CanEvolveMainSummon, final + " 최종 진화");
                Require(inventory.EnhanceMainSummon() && !inventory.CanEnhanceMainSummon, final + " 최종형 강화");
            }

            // 예전 에셋 이름으로 저장된 세이브도 새 에셋으로 불러온다.
            foreach (string form in LineForms)
            {
                var data = Form(form);
                foreach (var oldName in data.formerAssetNames ?? new string[0])
                    Require(data.MatchesSaveKey(oldName), $"{form}: 옛 세이브 키 '{oldName}'");
            }
        }
        finally { Object.DestroyImmediate(go); }
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
            foreach (string form in LineForms)
            {
                var data = Form(form);
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
            Debug.Log("[MinionLine] PASS: 마을 NPC 선택 버튼 5종 → 장착/저장, 씬 원본 변경 없음.");
        }
        finally
        {
            GameManager.Instance = previousManager;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[MinionLine] " + message);
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
