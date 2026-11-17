#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 근접 소환수 진화 계열을 검증한다.
/// (마을 '소환수 선택' NPC 를 처음 배치하던 'Setup Minion Select NPC' 는 끝난 일회성 메뉴라 제거했다 —
///  다시 돌리면 손으로 배치한 NPC 를 지우고 Map/NPC 복제본으로 바꿔 씬을 저장했다.)
///
/// [26/10/22] 외형 테스트(SpriteTests)로 만들던 전사/기사/기사2/무사 외형을 실제 MeleeDoll 계열에 입혔다.
///   전사(Fighter) ─┬─ 1차 트리: 기사(Knight)  → 용사(Champion)   · Breaker 능력
///                  └─ 2차 트리: 무사(Warrior) → 장수(Warlord)    · Whirlwind 능력
/// 옛 테스트 SO 는 SOData/Minion/Main/Deprecated/SpriteTests 에 남아 있으며 레지스트리에는 없다.
/// (예전 'Setup Minion Sprite Tests' 메뉴는 테스트 SO 를 레지스트리에 되살리므로 제거했다.)
/// </summary>
public static class MinionSelectNPCSetup
{
    private const string MainDir = "Assets/SOData/Minion/Main";
    private const string RegistryPath = "Assets/SOData/Registry/Growth Reward Registry.asset";
    private static readonly string[] LineForms = { "Fighter", "Knight", "Champion", "Warrior", "Warlord" };

    private static MainMinionDataSO Form(string form)
        => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>($"{MainDir}/{form} Minion.asset");

    // 소환수 선택 NPC·보상 카드가 실제로 띄우는 아이콘(MinionSelectNPC / RewardProcessor 와 같은 규칙).
    // 근접 5종은 일부러 rewardItemData.icon = 각자 .aseprite 의 Idle 첫 프레임이고, minionIcon(HUD·마을 디버그 목록)은 아이콘 시트라 둘은 다르다.
    private static Sprite CardIcon(MainMinionDataSO m)
        => m.rewardItemData != null && m.rewardItemData.icon != null ? m.rewardItemData.icon : m.minionIcon;

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
            Require(data.minionIcon != null && data.minionIcon.pixelsPerUnit == 32f, form + " HUD 아이콘/PPU");
            Require(CardIcon(data) != null && CardIcon(data).pixelsPerUnit == 32f, form + " 선택 카드 아이콘/PPU");
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

        var warlord = Form("Warlord");
        const string warlordSource = "Assets/Resources/Sprites/Minion/MeleeMinion_Warrior_2.aseprite";
        Require(warlord.basicAnim.visual != Form("Warrior").basicAnim.visual &&
                AssetDatabase.GetAssetPath(CardIcon(warlord)) == warlordSource, "장수 전용 외형/카드 아이콘");
        foreach (var set in new[] { warlord.basicAnim, warlord.skillAnim })
        {
            var controller = (AnimatorController)set.visual.GetComponentInChildren<Animator>().runtimeAnimatorController;
            var clip = (AnimationClip)controller.layers[0].stateMachine.states
                .Single(s => s.state.name == set.SequenceTags().Single()).state.motion;
            Require(AssetDatabase.GetAssetPath(clip) == warlordSource, "장수 원본 애니메이션/이벤트 연결");
        }

        VerifyEvolutionTree();
        VerifyVillageChoices();
        Debug.Log("[MinionLine] PASS: 전사/기사/용사/무사/장수 데이터·외형·능력·진화·저장 키 검증 완료.");
    }

    private static void RequireSameAbility(MainMinionDataSO a, MainMinionDataSO b)
    {
        Require(a.minionSkill == b.minionSkill &&
                AbilityJson(a.finisher) == AbilityJson(b.finisher) &&
                AbilityJson(a.dashModifier) == AbilityJson(b.dashModifier), $"{a.name} ↔ {b.name} 능력 동일");
    }

    // 설명 문구(LocText)는 능력이 아니고 번역 키도 에셋마다 다르므로 비교에서 뺀다.
    private static string AbilityJson(object block)
        => Regex.Replace(JsonUtility.ToJson(block), @"\{""ko"":""(?:[^""\\]|\\.)*"",""key"":""(?:[^""\\]|\\.)*""\}", "{}");

    private static void VerifyEvolutionTree()
    {
        // 열린 씬을 더럽히지 않게 임시 씬을 활성으로 두고 만든다(진화 연출 등 부수 생성물도 임시 씬에만 생긴다).
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
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
        finally
        {
            SceneManager.SetActiveScene(previousScene);
            EditorSceneManager.CloseScene(scene, true);
        }
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
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && MainMinionSelectionUI.Instance == null,
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
                string label = "[미니언] " + data.minionName.ko;
                var button = buttons.Single(b => new SerializedObject(b.GetComponent<SkillTooltipTrigger>())
                    .FindProperty("title.ko").stringValue == label);
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
}
#endif
