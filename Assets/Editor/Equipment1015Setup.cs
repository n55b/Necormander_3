using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>1015 장비 에셋 이관/배선. 런타임 생성기가 아니라 에디터에서 저장하는 일회성 작업이다.</summary>
public static class Equipment1015Setup
{
    private const string Folder = "Assets/SOData/Equipment/1015";
    private const string Npc = "Assets/Prefabs/Interactables/EnhanceShopNPC.prefab";
    private const string Picker = "Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab";
    private const string UpgradeUI = "Assets/Prefabs/UI/Reward Selection/EquipmentUpgradeSelectionUI.prefab";
    private const string RewardUI = "Assets/Prefabs/UI/Reward Selection/RewardSelectionUI.prefab";
    private const string RewardCardPrefab = "Assets/Prefabs/UI/Reward Selection/RewardCard 1.prefab";
    private static readonly string[] Keys = { "Gauntlet", "Viper", "ViperSC", "Serendipity", "ViperAS", "Marionette" };

    [MenuItem("Tools/Equipment/1015/Apply assets and prefab wiring")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 종료하세요.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/SOData/Equipment", "1015");
        // 구 에셋 3개는 GUID를 보존하여 이관한다. 씬/프리팹에 Missing 참조를 남기지 않는다.
        Move("GauntletOfFighting", "Gauntlet");
        Move("IceGauntlet", "Viper");
        Move("WindBreaker", "ViperSC");
        var weapons = Keys.Select(LoadOrCreate).ToArray();
        var icon = weapons[0].icon;
        string[] titles = { "건틀렛", "바이퍼", "바이퍼-SC", "세렌디피티", "바이퍼-AS", "마리오네트" };
        int[] stages = { 0, 1, 2, 3, 2, 3 };
        float[] attacks = { 5, 7, 10, 15, 10, 10 };
        const string basic = "기본 공격 3연타 (1 / 1 / 1.5). 보통 공격 속도 · 경직 0.2초.\n";
        const string weakness = "약점 공략: 플레이어 3타 적중 시 취약 10초. 받는 물리 피해 +20% (중첩 없이 갱신).\n";
        for (int i = 0; i < weapons.Length; i++)
        {
            var w = weapons[i]; w.name = Keys[i]; w.equipmentName = titles[i];
            w.icon = icon; // 신규 그림이 없으므로 기존 건틀렛 이미지만 재사용한다.
            w.upgradeOnly = true; w.upgradeStage = stages[i]; w.maxEnhanceLevel = 3;
            w.comboMultipliers = new Vector3(1, 1, 1.5f);
            w.basicHitsPerContact = i == 5 ? 2 : 1;
            w.basicDamagePerHit = i == 5 ? 0.6f : 1f;
            w.minionDashDamageMultiplier = i == 5 ? 1.3f : 1f;
            w.exploitsWeakness = i != 0; w.weaknessDuration = 10f;
            w.serendipity = i == 3; w.resetChance = 0.13f; w.guaranteedProcSeconds = 33f;
            w.moveBuffSeconds = 13f; w.moveBuffPercent = 0.03f;
            w.passives = new List<EquipmentPassive> { Stat(StatType.Attack, attacks[i], false) };
            if (i == 2 || i == 3) w.passives.Add(Stat(StatType.SkillCooldownReduction, 20, false));
            if (i == 4 || i == 5) w.passives.Add(Stat(StatType.AttackSpeed, 0.2f, true));
            w.description = basic + $"공격력 +{attacks[i]}.\n" + (i > 0 ? weakness : "")
                + (i == 2 || i == 3 ? "소환수 Space 스킬 쿨타임 -20%.\n" : "")
                + (i == 4 || i == 5 ? "공격 속도 +20%.\n" : "")
                + (i == 3 ? "행운: 평타 첫 적중마다 13% + Luck 확률로 소환수 스킬 쿨타임 초기화. 33초 동안 미발동 시 확정 발동. 발동 시 이동 속도 +3%, 13초 (중첩 없음)." : "")
                + (i == 5 ? "마리오네트: 플레이어·소환수 평타의 타수 2배, 각 타격 피해 60%. 모션은 유지. 소환수 대쉬 공격 피해 +30%." : "");
            w.upgradeOptions = Array.Empty<EquipmentSO>();
            EditorUtility.SetDirty(w);
        }
        weapons[0].upgradeOptions = new[] { weapons[1] };
        weapons[1].upgradeOptions = new[] { weapons[2], weapons[4] };
        weapons[2].upgradeOptions = new[] { weapons[3] };
        weapons[4].upgradeOptions = new[] { weapons[5] };
        foreach (var registry in AssetsOf<GrowthRegistrySO>())
        {
            registry.equipments = weapons.ToList(); registry.startingEquipment = weapons[0];
            EditorUtility.SetDirty(registry);
        }
        foreach (var shop in AssetsOf<ShopRegistrySO>())
        {
            shop.equipmentPool.RemoveAll(e => e == null || e.upgradeOnly);
            shop.enhanceCost = 250; shop.enhanceCostPerLevel = 25;
            EditorUtility.SetDirty(shop);
        }
        BuildUpgradeUI();
        EditPrefab(Npc, root => {
            var so = new SerializedObject(root.GetComponent<EnhanceShopNPC>());
            so.FindProperty("selectionUIPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RewardSelectionUI>(
                UpgradeUI);
            so.ApplyModifiedPropertiesWithoutUndo();
        });
        // 현재 비교 카드 250x365를 이전 144x222 크기에 가까운 150x219로 돌린다.
        // 하나의 부모 배율만 조정하여 레이아웃, 글자, 홀드바, 비교 상세가 함께 줄어든다.
        EditPrefab(Picker, root => root.transform.Find("MinionSelection").localScale = Vector3.one * 0.6f);
        EditPrefab("Assets/Prefabs/UI/SkillExplainUI.prefab", AddLuckRow);
        WireShopRooms();
        AssetDatabase.SaveAssets();
        Validate();
    }

    private static void Move(string oldName, string key)
    {
        string old = "Assets/SOData/Equipment/" + oldName + ".asset", next = Folder + "/" + key + ".asset";
        if (AssetDatabase.LoadAssetAtPath<EquipmentSO>(old) == null) return;
        if (AssetDatabase.LoadAssetAtPath<EquipmentSO>(next) != null) throw new Exception("이관 대상이 이미 존재합니다: " + next);
        string error = AssetDatabase.MoveAsset(old, next);
        if (!string.IsNullOrEmpty(error)) throw new Exception(error);
    }

    private static EquipmentSO LoadOrCreate(string key)
    {
        string path = Folder + "/" + key + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<EquipmentSO>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<EquipmentSO>(); AssetDatabase.CreateAsset(asset, path); return asset;
    }

    private static EquipmentPassive Stat(StatType type, float value, bool percent) => new EquipmentStatPassive {
        label = type.ToString(), effect = new StatEffect { statType = type, value = value, isPercent = percent, valuePerLevel = 0 }
    };
    private static IEnumerable<T> AssetsOf<T>() where T : Object => AssetDatabase.FindAssets("t:" + typeof(T).Name)
        .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null);

    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void WireShopRooms()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Map/Room Map Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset.GetComponentInChildren<ShopNPC>(true) == null || asset.GetComponentInChildren<EnhanceShopNPC>(true) != null) continue;
            EditPrefab(path, root => {
                var merchant = root.GetComponentInChildren<ShopNPC>(true);
                var npc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Npc), root.transform);
                npc.transform.position = merchant.transform.position + Vector3.left * 3f;
                Debug.Log("[Equipment1015] 강화 NPC 배치: " + path + " at " + npc.transform.localPosition);
            });
        }
    }

    [MenuItem("Tools/Equipment/1015/Update upgrade card layout")]
    public static void UpdateUpgradeLayout()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 종료하세요.");
        BuildUpgradeUI();
    }

    private static void BuildUpgradeUI()
    {
        // 마을 소환수 선택도 같은 원본 카드를 사용한다. 스크롤은 Variant가 아닌 원본에 저장한다.
        EditPrefab(RewardCardPrefab, root => ConfigureCard(root.GetComponent<RewardCard>()));
        if (AssetDatabase.LoadAssetAtPath<GameObject>(UpgradeUI) == null)
        {
            // 원본 보상창의 프리팹 Variant. 아트/카드 스크립트/버튼 배선을 그대로 상속한다.
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Reward Selection/RewardSelectionUI.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try { PrefabUtility.SaveAsPrefabAsset(instance, UpgradeUI); }
            finally { Object.DestroyImmediate(instance); }
        }
        EditPrefab(UpgradeUI, root => {
            var uiData = new SerializedObject(root.GetComponent<RewardSelectionUI>());
            var skip = (UnityEngine.UI.Button)uiData.FindProperty("skipButton").objectReferenceValue;
            Place((RectTransform)skip.transform, new Vector2(0, -190), new Vector2(90, 24));
            var cards = root.GetComponentsInChildren<RewardCard>(true);
            var group = cards[0].transform.parent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            group.spacing = 30; group.childControlWidth = group.childControlHeight = false;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            Place((RectTransform)group.transform, new Vector2(0, 20), new Vector2(760, 330));
            foreach (var card in cards)
            {
                ((RectTransform)card.transform).sizeDelta = new Vector2(220, 330);
                var data = new SerializedObject(card);
                var title = (TMPro.TextMeshProUGUI)data.FindProperty("nameText").objectReferenceValue;
                var icon = (UnityEngine.UI.Image)data.FindProperty("iconImage").objectReferenceValue;
                title.fontSize = 13; title.enableAutoSizing = false;
                icon.rectTransform.sizeDelta = new Vector2(64, 64);
                ConfigureCard(card);
                card.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true).content.GetComponent<TMPro.TextMeshProUGUI>().fontSize = 10;
            }
        });
    }

    private static void ConfigureCard(RewardCard card)
    {
        var data = new SerializedObject(card);
        var title = (TMPro.TextMeshProUGUI)data.FindProperty("nameText").objectReferenceValue;
        var desc = (TMPro.TextMeshProUGUI)data.FindProperty("descText").objectReferenceValue;
        var icon = (UnityEngine.UI.Image)data.FindProperty("iconImage").objectReferenceValue;
        // 아트의 원형 문양 중심. 카드 크기가 달라도 같은 상대 위치에 놓인다(330 높이에서는 y=59.4).
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, .68f);
        icon.rectTransform.pivot = new Vector2(.5f, .5f);
        icon.rectTransform.anchoredPosition = Vector2.zero;
        icon.preserveAspect = true;
        PlaceInCard(title.rectTransform, new Vector2(0, -28), new Vector2(188, 28));
        var scrolls = card.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true);
        var scroll = scrolls.FirstOrDefault(s => s.name == "DescriptionScroll" && !PrefabUtility.IsAddedGameObjectOverride(s.gameObject));
        if (scroll == null)
        {
            var go = new GameObject("DescriptionScroll", typeof(RectTransform), typeof(UnityEngine.UI.ScrollRect));
            go.layer = card.gameObject.layer; go.transform.SetParent(card.transform, false);
            scroll = go.GetComponent<UnityEngine.UI.ScrollRect>();
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Mask));
            viewport.layer = go.layer; viewport.transform.SetParent(go.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            // 원본에서 기존 텍스트를 이동하므로 참조와 편집 내용을 보존한다.
            desc.transform.SetParent(viewport.transform, false);
            var fitter = desc.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter == null) fitter = desc.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewportRect; scroll.content = desc.rectTransform;
        }
        desc = scroll.content.GetComponent<TMPro.TextMeshProUGUI>();
        desc.gameObject.SetActive(true);
        PlaceInCard((RectTransform)scroll.transform, new Vector2(0, -106), new Vector2(174, 86));
        scroll.horizontal = false; scroll.vertical = true; scroll.inertia = false;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 20f;
        desc.rectTransform.anchorMin = new Vector2(0, 1); desc.rectTransform.anchorMax = Vector2.one;
        desc.rectTransform.pivot = new Vector2(.5f, 1);
        desc.rectTransform.anchoredPosition = Vector2.zero; desc.rectTransform.sizeDelta = Vector2.zero;
        desc.enableAutoSizing = false; desc.raycastTarget = false;
        desc.textWrappingMode = TMPro.TextWrappingModes.Normal;
        desc.overflowMode = TMPro.TextOverflowModes.Overflow;
        desc.alignment = TMPro.TextAlignmentOptions.TopLeft;
        data.FindProperty("descText").objectReferenceValue = desc;
        data.FindProperty("descriptionScroll").objectReferenceValue = scroll;
        data.ApplyModifiedPropertiesWithoutUndo();
        // 앞서 Variant에만 추가했던 사본은 원본 스크롤로 배선을 옮긴 뒤 정리한다.
        foreach (var old in scrolls)
            if (old != scroll && old.name == "DescriptionScroll" && PrefabUtility.IsAddedGameObjectOverride(old.gameObject))
                Object.DestroyImmediate(old.gameObject);
    }

    private static void PlaceInCard(RectTransform rect, Vector2 position, Vector2 size)
    {
        var basis = new Vector2(220, 330);
        rect.anchorMin = new Vector2(.5f + (position.x - size.x * .5f) / basis.x, .5f + (position.y - size.y * .5f) / basis.y);
        rect.anchorMax = new Vector2(.5f + (position.x + size.x * .5f) / basis.x, .5f + (position.y + size.y * .5f) / basis.y);
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void AddLuckRow(GameObject root)
    {
        var panel = root.transform.Find("LoadoutPanel/StatsPanel/PlayerStats");
        if (panel.Find("StatHover17") == null)
        {
            for (int i = 16; i >= 8; i--) panel.Find("StatHover" + i).name = "StatHover" + (i + 1);
            var luck = Object.Instantiate(panel.Find("StatHover7").gameObject, panel, false); luck.name = "StatHover8";
            luck.GetComponent<SkillTooltipTrigger>().SetData("행운 (Luck)", "세렌디피티 등 지정된 효과의 발동 확률에 더합니다. 5%는 확률 +5%p입니다.");
        }
        for (int i = 0; i < 18; i++)
        {
            int line = i < 3 ? i : i + 1;
            Place((RectTransform)panel.Find("StatHover" + i), new Vector2(0, 172 - line * 18 - 9), new Vector2(172, 18));
        }
    }

    private static void Place(RectTransform rect, Vector2 pos, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = pos; rect.sizeDelta = size;
    }

    [MenuItem("Tools/Equipment/1015/Validate")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 종료하세요.");
        var w = Keys.Select(k => AssetDatabase.LoadAssetAtPath<EquipmentSO>(Folder + "/" + k + ".asset")).ToArray();
        Check(w.All(e => e != null), "장비 6종");
        Check(w[0].upgradeOptions.SequenceEqual(new[] { w[1] }) && w[1].upgradeOptions.SequenceEqual(new[] { w[2], w[4] })
            && w[2].upgradeOptions.SequenceEqual(new[] { w[3] }) && w[4].upgradeOptions.SequenceEqual(new[] { w[5] })
            && w[3].upgradeOptions.Length == 0 && w[5].upgradeOptions.Length == 0, "강화 분기/최종 단계");
        Check(w.All(e => e.upgradeOnly && e.ComboMultiplier(0) == 1 && e.ComboMultiplier(1) == 1 && e.ComboMultiplier(2) == 1.5f), "3연타 배율");
        Check(w[5].basicHitsPerContact == 2 && w[5].basicDamagePerHit == 0.6f && w[5].minionDashDamageMultiplier == 1.3f, "마리오네트 배율/타수/대쉬");
        Check(Mathf.Approximately(w[3].ResetChance(5), .18f) && w[3].ResetChance(100) == 1f, "Luck 합연산/상한");
        var token = new PlayerBasicAttackRoll(w[3]);
        var copy = new DamageInfo { playerBasicAttack = token };
        Check(token.TryResolve() && !copy.playerBasicAttack.TryResolve(), "다중 대상/복수 타격 추첨 1회");
        Check(AssetsOf<GrowthRegistrySO>().All(r => r.startingEquipment == w[0] && r.equipments.Count == 6), "시작 무기/레지스트리");
        Check(AssetsOf<ShopRegistrySO>().All(r => !r.equipmentPool.Any(e => e != null && e.upgradeOnly)), "강화 전용 장비 진열 제외");
        var npcData = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(Npc).GetComponent<EnhanceShopNPC>());
        Check(npcData.FindProperty("selectionUIPrefab").objectReferenceValue != null, "강화 선택 프리팹 배선");
        var shopRoom = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/Room Map Prefabs/Shop/Room_Shop.prefab");
        var npcPosition = shopRoom.GetComponentInChildren<EnhanceShopNPC>(true).transform.position;
        var maps = shopRoom.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true);
        var ground = maps.First(t => t.name == "Ground");
        var wall = maps.First(t => t.name == "Wall");
        Check(ground.HasTile(ground.WorldToCell(npcPosition)) && !wall.HasTile(wall.WorldToCell(npcPosition)), "상점 강화 NPC 바닥 배치");
        var picker = AssetDatabase.LoadAssetAtPath<GameObject>(Picker);
        Check(picker.transform.Find("MinionSelection").localScale == Vector3.one * .6f, "습득창 배율");
        Check(picker.GetComponent<HandSlotSelectionUI>().ConfirmHoldSeconds == 1f, "교체 1초 유지");
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Player/Player_AnimClip_Attack_Medium.anim");
        Check(AnimationUtility.GetAnimationEvents(clip).Any(e => e.functionName == "OnAttackHitFrame")
            && AnimationUtility.GetAnimationEvents(clip).Any(e => e.functionName == "CanChangeAnimState"), "기존 3타 타격/종료 이벤트");
        Check(!StatusRules.BlockedBySuperArmor(StatusType.Vulnerable) && !StatusRules.PreventsAction(StatusType.Vulnerable), "취약 슈퍼아머 허용/행동 제한 없음");
        CheckCombat(w);
        Debug.Log("[Equipment1015] ALL CHECKS PASSED");
    }

    private static void CheckCombat(EquipmentSO[] weapons)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var fixture = new GameObject("Equipment1015 isolated check"); fixture.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fixture, scene);
        var oldGm = GameManager.Instance; var oldPsi = PlayerSkillInventoryManager.Instance;
        try
        {
            var gm = fixture.AddComponent<GameManager>(); GameManager.Instance = gm;
            var dm = fixture.AddComponent<DataManager>(); gm.dataManager = dm;
            Field(dm, "growthRegistry", AssetsOf<GrowthRegistrySO>().First());
            Field(dm, "shopRegistry", AssetsOf<ShopRegistrySO>().First());
            var player = fixture.AddComponent<PlayerController>(); Field(gm, "playerController", player);
            var statGo = new GameObject("CharacterStatStuff"); statGo.transform.SetParent(fixture.transform);
            var stat = statGo.AddComponent<CharacterStat>(); Field(player, "stat", stat);
            var health = stat.GetComponent<CharacterHealth>(); var status = stat.GetComponent<CharacterStatus>();
            Field(stat, "<Health>k__BackingField", health); Field(stat, "<Status>k__BackingField", status);
            health.Init(stat, status); status.Init(stat);
            var skills = fixture.AddComponent<PlayerSkillController>();
            var psi = fixture.AddComponent<PlayerSkillInventoryManager>(); PlayerSkillInventoryManager.Instance = psi;
            psi.EquipEquipment(EquipmentInstance.Roll(weapons[0])); Check(stat.ATK == 15f, "기본 10 + 건틀렛 5");
            Check(psi.UpgradeTo(weapons[1]) && stat.ATK == 17f, "공격력 교체 (이전 +5 회수)");
            Check(!psi.UpgradeTo(weapons[5]), "다른 가지/단계 건너뛰기 차단");
            Check(psi.UpgradeTo(weapons[2]) && Mathf.Approximately(stat.ApplySkillCooldown(10), 8), "SC 스킬 쿨감");
            Check(psi.UpgradeTo(weapons[3]) && !psi.CanEnhanceEquipped(), "세렌디피티 최종 단계");
            Field(skills, "_mainSummonCooldownEnd", Time.time + 20f);
            Call(psi, "TickSerendipity", 33f);
            Check(skills.GetMainSummonCooldownRemaining() == 0f && Mathf.Approximately(stat.Mods.Percent(StatType.MoveSpeed), .03f), "33초 확정 발동/루트 스킬 쿨 초기화");
            Call(psi, "TickSerendipity", 13f);
            Check(stat.Mods.Percent(StatType.MoveSpeed) == 0f, "13초 이동 버프 만료");
            Call(psi, "TickSerendipity", 20f);
            Check((float)Read(psi, "_serendipityElapsed") == 0f, "스킬 쿨 0에서도 발동 타이머 초기화");
            var save = new SaveData(); Call(psi, "TickSerendipity", 4f); psi.SaveToData(save);
            var roundTrip = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Check(roundTrip.equipment.serendipityElapsed == 4f && roundTrip.equipment.moveBuffRemaining == 9f, "씬 전환 저장 타이머");
            psi.LoadFromData(roundTrip); psi.ReapplyEquipmentPassives();
            Check((float)Read(psi, "_serendipityElapsed") == 4f && (float)Read(psi, "_moveBuffRemaining") == 9f, "로드/재적용에도 타이머 유지");
            psi.LoadFromData(new SaveData { equipment = new EquipmentSaveData { equipmentSOName = "IceGauntlet" } });
            Check(psi.Weapon == weapons[0], "구 장비 세이브는 시작 장비로 안전 이관");
            psi.EquipEquipment(EquipmentInstance.Roll(weapons[3]));
            stat.Mods.AddFlat(fixture, StatType.Luck, 100);
            var first = new PlayerBasicAttackRoll(weapons[3]);
            var hit = new DamageInfo(1, attacker: fixture, category: DamageCategory.BasicAttack) { playerBasicAttack = first };
            Field(skills, "_mainSummonCooldownEnd", Time.time + 20);
            Call(psi, "OnBasicHit", health, stat, hit, false);
            Check(skills.GetMainSummonCooldownRemaining() == 0, "첫 적중 행운 추첨으로 쿨 초기화");
            Field(skills, "_mainSummonCooldownEnd", Time.time + 20);
            Call(psi, "OnBasicHit", health, stat, hit, false);
            Check(skills.GetMainSummonCooldownRemaining() > 0, "동일 공격 추가 타격은 재추첨 안 함");
            Call(psi, "OnBasicHit", health, stat, new DamageInfo(1, attacker: fixture, category: DamageCategory.BasicAttack), false);
            Check(skills.GetMainSummonCooldownRemaining() > 0, "소환수 평타는 추첨하지 않음");
            stat.Mods.RemoveSource(fixture);
            psi.EquipEquipment(EquipmentInstance.Roll(weapons[5]));
            Check(stat.ATK == 20f && Mathf.Approximately(stat.ATKSPD, 1.2f) && stat.SKILL_CDR == 0f
                && stat.Mods.Percent(StatType.MoveSpeed) == 0f, "장비 교체 시 이전 쿨감/버프 완전 회수");
            stat.Mods.AddFlat(fixture, StatType.Luck, 5); Check(stat.LUCK == 5f, "Luck 스탯 경로");

            var target = new GameObject("Boss target"); target.transform.SetParent(fixture.transform);
            var targetStat = target.AddComponent<CharacterStat>(); var targetStatus = target.GetComponent<CharacterStatus>();
            var targetHp = target.GetComponent<CharacterHealth>();
            Field(targetStat, "<Health>k__BackingField", targetHp); Field(targetStat, "<Status>k__BackingField", targetStatus);
            targetHp.Init(targetStat, targetStatus); targetStatus.Init(targetStat); targetStatus.ApplySuperArmor(999999);
            targetHp.TakeDamage(new DamageInfo(10, applyStatus: StatusType.Vulnerable, statusDuration: 10));
            Check(targetHp.CurHP == 90f && targetStatus.HasStatus(StatusType.Vulnerable), "취약 적용타는 10/슈퍼아머 대상 적용");
            targetHp.TakeDamage(new DamageInfo(10)); Check(targetHp.CurHP == 78f, "취약 물리 피해 +20%");
            targetHp.TakeDamage(new DamageInfo(10, DamageType.Magic)); Check(targetHp.CurHP == 68f, "마법 피해 미증폭");
            targetStatus.ApplyStatus(StatusType.Vulnerable, 10); targetHp.TakeDamage(new DamageInfo(10));
            Check(targetHp.CurHP == 56f, "취약 갱신 시 증폭 비중첩");
            targetStatus.RemoveStatus(StatusType.Vulnerable);
            var box = fixture.AddComponent<BaseHitBox>(); box.HitsPerContact = 2;
            Field(box, "_damageInfo", new DamageInfo(6));
            Call(box, "DealContactDamage", targetHp);
            Check(targetHp.CurHP == 44f, "마리오네트 6 피해 독립 2타");
            CheckUiAndShop(fixture, gm, psi, weapons);
        }
        finally
        {
            Object.DestroyImmediate(fixture);
            GameManager.Instance = oldGm; PlayerSkillInventoryManager.Instance = oldPsi;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CheckUiAndShop(GameObject fixture, GameManager gm, PlayerSkillInventoryManager psi, EquipmentSO[] weapons)
    {
        // 레이아웃 컴포넌트는 비활성 부모 아래에서 동작하지 않으므로 UI만 활성 프리뷰 루트로 검사한다.
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, fixture.scene);
        try
        {
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(UpgradeUI), canvas.transform).GetComponent<RewardSelectionUI>();
            var inv = fixture.AddComponent<InventoryManager>(); gm.inventoryManager = inv; inv.AddGold(1000);
            var shop = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Npc), fixture.transform).GetComponent<EnhanceShopNPC>();
            Field(shop, "_ui", ui);
            psi.EquipEquipment(new EquipmentInstance { baseData = weapons[1], enhanceLevel = 1 });
            Check(shop.Interact(fixture) && shop.CurrentCost() == 275, "분기 선택창/2단계 275G");
            Call(ui, "OnSkipClicked"); Check(inv.GOLD == 1000 && psi.Weapon == weapons[1], "스킵 무과금/장비 유지");
            Check(shop.Interact(fixture), "스킵 후 재선택");
            ui.OnCardClicked(1);
            Check(psi.Weapon == weapons[4] && inv.GOLD == 725 && !shop.Interact(fixture), "AS 선택/1회 과금/같은 상점 재강화 차단");
            // 가장 긴 설명으로 실제 프리팹 텍스트 영역을 검증한다. 기존 세이브나 씬은 수정하지 않는다.
            var candidates = new List<RewardCandidate> { new RewardCandidate { rawData = weapons[3],
                displayData = new GrowthItemData { itemName = weapons[3].equipmentName,
                    description = weapons[3].description + "\n\n강화 비용: 300G (상점당 1회)", icon = weapons[3].icon } } };
            ui.ShowWithCallbacks(candidates, _ => {}, () => {});
            CheckCardScrolling(ui);
            ui.Hide();
            var minionUi = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RewardUI), canvas.transform).GetComponent<RewardSelectionUI>();
            // EditMode 프리뷰에서는 MonoBehaviour.Awake가 자동 실행되지 않는다.
            foreach (var card in minionUi.GetComponentsInChildren<RewardCard>(true)) Call(card, "Awake");
            RewardCandidate picked = default;
            minionUi.ShowWithCallbacks(MinionCandidates(), c => picked = c, () => {});
            CheckCardScrolling(minionUi);
            minionUi.GetComponentsInChildren<RewardCard>()[1].GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Check(picked.rawData is MainMinionDataSO, "미니언 카드 선택 콜백 유지");
            minionUi.Hide();
            var statsUi = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/SkillExplainUI.prefab"), canvas.transform).GetComponent<SkillExplainUI>();
            Call(statsUi, "RefreshPlayerStats");
            var statsText = (TMPro.TextMeshProUGUI)new SerializedObject(statsUi).FindProperty("playerStatsText").objectReferenceValue;
            statsText.ForceMeshUpdate(true);
            Check(statsText.text.Contains("행운") && statsText.preferredHeight <= statsText.rectTransform.rect.height + 1f, "C 스탯 Luck 행/텍스트 영역");
        }
        finally { Object.DestroyImmediate(canvasGo); }
    }

    private static void CheckCardScrolling(RewardSelectionUI ui)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var card in ui.GetComponentsInChildren<RewardCard>(true))
        {
            if (!card.gameObject.activeSelf) continue;
            var data = new SerializedObject(card);
            var text = (TMPro.TextMeshProUGUI)data.FindProperty("descText").objectReferenceValue;
            var icon = (UnityEngine.UI.Image)data.FindProperty("iconImage").objectReferenceValue;
            Check(icon.rectTransform.anchorMin == new Vector2(.5f, .68f) && icon.rectTransform.anchoredPosition == Vector2.zero,
                "아이콘 원형 중심 배치: " + ui.name);
            text.ForceMeshUpdate(true);
            var scroll = card.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            Check(card.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true).Length == 1 &&
                scroll != null && scroll.content == text.rectTransform && scroll.viewport.GetComponent<UnityEngine.UI.Mask>() != null &&
                data.FindProperty("descriptionScroll").objectReferenceValue == scroll,
                "공통 카드 스크롤/마스크 배선: " + ui.name);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Check(text.rectTransform.rect.height > scroll.viewport.rect.height && !text.enableAutoSizing,
                "긴 설명은 축소 없이 하단 칸에서 스크롤");
            float before = scroll.content.anchoredPosition.y;
            scroll.OnScroll(new UnityEngine.EventSystems.PointerEventData(null) { scrollDelta = new Vector2(0, -3) });
            Check(scroll.content.anchoredPosition.y > before, "마우스 휠로 설명 아래로 이동");
            scroll.verticalNormalizedPosition = 0;
            Check(Mathf.Abs(scroll.content.anchoredPosition.y - (scroll.content.rect.height - scroll.viewport.rect.height)) < 1f,
                "설명 끝까지 스크롤 가능");
            // 선택 인덱스는 바꾸지 않고 설명 갱신 경로만 재검사한다.
            Call(card, "SetDescription", text.text);
            Check(Mathf.Abs(scroll.content.anchoredPosition.y) < 1f, "카드 다시 표시 시 스크롤 맨 위로 복원");
        }
    }

    private static List<RewardCandidate> MinionCandidates()
    {
        // 실제 마을 NPC가 만드는 설명을 그대로 써서 길이/타수/스킬 설명을 검사한다.
        var build = typeof(MinionSelectNPC).GetMethod("BuildCandidate", BindingFlags.Static | BindingFlags.NonPublic);
        return AssetsOf<GrowthRegistrySO>().SelectMany(r => r.minionDatas).OfType<MainMinionDataSO>()
            .Where(m => !m.isEvolvedForm).Distinct().Take(3)
            .Select(m => (RewardCandidate)build.Invoke(null, new object[] { m, false })).ToList();
    }

    [MenuItem("Tools/Equipment/1015/Render UI previews")]
    public static void Preview()
    {
        Validate();
        var scene = EditorSceneManager.NewPreviewScene();
        var cameraGo = new GameObject("Equipment UI preview", typeof(Camera));
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, scene);
        var camera = cameraGo.GetComponent<Camera>(); camera.scene = scene;
        camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.12f, .16f, .16f);
        var target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera; canvas.planeDistance = 10;
        var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(960, 540);
        Call(scaler, "Handle");
        var old = RenderTexture.active;
        try
        {
            foreach (string path in new[] { UpgradeUI, RewardUI, Picker })
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), canvas.transform);
                try
                {
                    if (path == UpgradeUI)
                    {
                        var ui = root.GetComponent<RewardSelectionUI>();
                        var choices = new[] { "Serendipity", "Marionette" }.Select(k => {
                            var w = AssetDatabase.LoadAssetAtPath<EquipmentSO>(Folder + "/" + k + ".asset");
                            return new RewardCandidate { rawData = w, displayData = new GrowthItemData {
                                itemName = w.equipmentName, description = w.description + "\n\n강화 비용: 300G (상점당 1회)", icon = w.icon } };
                        }).ToList();
                        ui.ShowWithCallbacks(choices, _ => {}, () => {});
                    }
                    else if (path == RewardUI)
                    {
                        root.GetComponent<RewardSelectionUI>().ShowWithCallbacks(MinionCandidates(), _ => {}, () => {});
                    }
                    else
                    {
                        root.transform.Find("MinionSelection").gameObject.SetActive(true);
                        var minions = AssetsOf<MainMinionDataSO>().Take(2).ToArray();
                        var data = new SerializedObject(root.GetComponent<HandSlotSelectionUI>());
                        for (int i = 0; i < 2; i++)
                        {
                            var card = (SkillExplainSlotUI)data.FindProperty(i == 0 ? "currentCard" : "candidateCard").objectReferenceValue;
                            card.gameObject.SetActive(true); card.SetData(minions[i].minionIcon, minions[i].minionName, i == 0 ? "현재 장착" : "교체 후보");
                        }
                        foreach (string field in new[] { "currentDetails", "candidateDetails" })
                            ((SkillExplainSlotUI)data.FindProperty(field).objectReferenceValue).gameObject.SetActive(false);
                    }
                    Canvas.ForceUpdateCanvases();
                    foreach (var text in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) text.ForceMeshUpdate(true);
                    camera.Render(); RenderTexture.active = target;
                    var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                    System.IO.Directory.CreateDirectory("Logs");
                    string output = "Logs/Equipment1015-" + (path == UpgradeUI ? "upgrade" : path == RewardUI ? "minions" : "pickup") + ".png";
                    System.IO.File.WriteAllBytes(output, image.EncodeToPNG()); Object.DestroyImmediate(image);
                    Debug.Log("[Equipment1015] Preview: " + output);
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
        finally
        {
            RenderTexture.active = old; camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static object Read(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("[Equipment1015] FAIL: " + message);
        Debug.Log("[Equipment1015] PASS: " + message);
    }
}
