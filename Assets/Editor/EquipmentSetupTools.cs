using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>장비 에셋/상점 배선과 회귀 검사. 기존 에셋을 다시 실행으로 덮어쓰지 않는다.</summary>
public static class EquipmentSetupTools
{
    private const string Folder = "Assets/SOData/Equipment/Gauntlets";
    private const string GrowthPath = "Assets/SOData/Registry/Growth Reward Registry.asset";
    private const string ShopPath = "Assets/SOData/Registry/Shop Registry.asset";
    private const string NpcPath = "Assets/Prefabs/Interactables/EnhanceShopNPC.prefab";

    [MenuItem("Tools/Equipment/1. Create Weapons And Wire Shops")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play 모드를 종료한 뒤 실행하세요.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/SOData/Equipment", "Gauntlets");
        var metalIcon = AssetDatabase.LoadAssetAtPath<EquipmentSO>("Assets/SOData/Equipment/GauntletOfFighting.asset").icon;
        var frostIcon = AssetDatabase.LoadAssetAtPath<EquipmentSO>("Assets/SOData/Equipment/IceGauntlet.asset").icon;
        var shadowIcon = AssetDatabase.LoadAssetAtPath<EquipmentSO>("Assets/SOData/Equipment/WindBreaker.asset").icon;
        var mk0 = Make("MK0", "시작형 건틀릿 - MK0", 0, metalIcon, e => { });
        var mk1 = Make("MK1", "강화형 건틀릿 - MK1", 1, metalIcon, Passion);
        var mk2 = Make("MK2", "강화형 건틀릿 - MK2", 2, metalIcon, e => {
            Passion(e); e.passionMaxStacks = 4; e.passionSpeedPerStack = 0.04f;
            e.passionMaxFlatDamage = 2f; e.strongerAtMaxPassion = true;
        });
        var mk02 = Make("MK02", "개수형 건틀릿 - MK02", 2, metalIcon, e => {
            Passion(e); SetCombo(e, 1.1f, 1.1f, 1.6f); e.attackSpeed = EquipmentSO.Speed.Fast;
        });
        var frost = Make("FrostWind", "서리 바람 건틀릿", 1, frostIcon, Frost);
        var burst = Make("FrostBurst", "서리 폭발 건틀릿", 2, frostIcon, e => {
            Frost(e); e.frostMaxHpRatios = new Vector3(0.15f, 0.07f, 0.04f);
            e.frostStackDuration = 8f; e.frostExplosionRatio = 0.4f;
        });
        var core = Make("FrostCore", "서리 핵 건틀릿", 2, frostIcon, e => { Frost(e); e.frostAuraRadius = 1.75f; });
        var shadow = Make("Shadow", "그림자 건틀릿", 1, shadowIcon, Shadow);
        var absorb = Make("ShadowAbsorption", "그림자 흡수 건틀릿", 2, shadowIcon, e => {
            Shadow(e); e.shadowAttackPerStack = 0.6f; e.shadowLossRatio = 0.3f;
        });
        Link(mk0, mk1, frost, shadow); Link(mk1, mk2, mk02); Link(frost, burst, core); Link(shadow, absorb);
        var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(GrowthPath);
        registry.equipments = new System.Collections.Generic.List<EquipmentSO> { mk0, mk1, mk2, mk02, frost, burst, core, shadow, absorb };
        EditorUtility.SetDirty(registry);
        var shop = AssetDatabase.LoadAssetAtPath<ShopRegistrySO>(ShopPath);
        shop.equipmentPool.Clear(); EditorUtility.SetDirty(shop);
        WireShops();
        AssetDatabase.SaveAssets();
        Verify();
    }

    private static EquipmentSO Make(string id, string title, int tier, Sprite icon, Action<EquipmentSO> configure)
    {
        string path = Folder + "/" + id + ".asset";
        var e = AssetDatabase.LoadAssetAtPath<EquipmentSO>(path);
        if (e != null) return e;
        e = ScriptableObject.CreateInstance<EquipmentSO>();
        e.name = id; e.equipmentName = title; e.icon = icon; e.isRunWeapon = true;
        e.upgradeTier = tier; e.maxEnhanceLevel = 2;
        configure(e);
        e.description = Describe(e);
        AssetDatabase.CreateAsset(e, path);
        return e;
    }
    private static void SetCombo(EquipmentSO e, float a, float b, float c, int hits = 1)
        => e.combo = new[] { new EquipmentSO.ComboHit(a, hits), new EquipmentSO.ComboHit(b, hits), new EquipmentSO.ComboHit(c, hits) };
    private static void Passion(EquipmentSO e) => e.effect = EquipmentSO.Effect.Passion;
    private static void Frost(EquipmentSO e)
    { e.effect = EquipmentSO.Effect.Frost; SetCombo(e, 0.9f, 0.9f, 1.1f); e.attackSpeed = EquipmentSO.Speed.Slow; }
    private static void Shadow(EquipmentSO e)
    { e.effect = EquipmentSO.Effect.Shadow; SetCombo(e, 0.6f, 0.6f, 0.8f, 2); e.attackSpeed = EquipmentSO.Speed.Fast; e.hitstun = EquipmentSO.Stagger.None; }
    private static void Link(EquipmentSO e, params EquipmentSO[] next)
    { if (e.upgrades.Length == 0) { e.upgrades = next; EditorUtility.SetDirty(e); } }

    private static string Describe(EquipmentSO e)
    {
        string combo = string.Join(" / ", e.combo.Select(h => h.hits > 1 ? $"{h.multiplier:0.##}×{h.hits}" : $"{h.multiplier:0.##}"));
        string speed = e.attackSpeed == EquipmentSO.Speed.Slow ? "느림" : e.attackSpeed == EquipmentSO.Speed.Fast ? "빠름" : "보통";
        string text = $"기본 공격 3단계: {combo}\n공격 속도: {speed} / 경직: {e.StunDuration():0.##}초\n마지막은 미니언 고유 배율·타수를 유지하고 장비 마지막 배율을 곱합니다.";
        if (e.effect == EquipmentSO.Effect.Passion)
            text += $"\n열정: 명중한 평타 동작마다 공속 +{e.passionSpeedPerStack * 100:0}%, 최대 {e.passionMaxStacks}중첩. 최대 중첩에서 평타 피해 +{e.passionMaxFlatDamage:0}. 5초 미명중 시 해제." + (e.strongerAtMaxPassion ? " 최대 중첩에서 경직 강함." : "");
        if (e.effect == EquipmentSO.Effect.Frost)
            text += $"\n빙결: 5회 명중 시 얼립니다. 다음 직접 타격에 해제되며 최대 체력의 {e.frostMaxHpRatios.x * 100:0}/{e.frostMaxHpRatios.y * 100:0}/{e.frostMaxHpRatios.z * 100:0}% 추가 피해(일반/엘리트/보스). 스택 유지 {e.frostStackDuration:0}초.";
        if (e.frostExplosionRatio > 0) text += "\n해제 추가 피해의 40%로 반경 2 유닛에 폭발. 중심 중복 피해·연쇄 폭발 없음.";
        if (e.frostAuraRadius > 0) text += "\n서리 흉갑: 반경 1.75 유닛 적의 이동·공격 속도와 주는 피해 -15%. 벗어나면 즉시 해제.";
        if (e.effect == EquipmentSO.Effect.Shadow)
            text += $"\n그림자 흡수: 아군이 처치하면 1/10/20중첩(일반/엘리트/보스). 중첩당 공격력 +{e.shadowAttackPerStack:0.#}. 체력 피해를 받으면 {e.shadowLossRatio * 100:0}% 소실(소실량 버림). 경직·밀침 없음.";
        return text;
    }

    private static string[] ShopRooms() => AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Map" })
        .Select(AssetDatabase.GUIDToAssetPath).Where(p => {
            var room = AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponent<RoomInstance>();
            return room != null && (room.roomType == RoomType.Shop || room.roomType == RoomType.EnhanceShop);
        }).ToArray();

    private static void WireShops()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPath);
        foreach (string path in ShopRooms())
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentInChildren<EnhanceShopNPC>(true) != null) continue;
                var merchant = root.GetComponentInChildren<ShopNPC>(true);
                var ground = root.GetComponentsInChildren<Tilemap>(true).FirstOrDefault(t => t.name.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0);
                if (ground == null) throw new InvalidOperationException(path + ": Ground 없음");
                Vector3 origin = merchant != null ? merchant.transform.position : root.transform.position;
                var otherTiles = root.GetComponentsInChildren<Tilemap>(true).Where(t => t != ground && t.name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                Vector3? position = null;
                // 왼쪽 상인과 충분히 떨어진 실제 바닥만 선택. 프리팹에 저장하므로 디자이너가 자유롭게 이동 가능.
                foreach (float x in new[] { -4f, 4f, -5f, 5f, -6f, 6f })
                {
                    Vector3 candidate = origin + new Vector3(x, 0f, 0f);
                    bool clear = true;
                    foreach (var d in new[] { Vector3.zero, Vector3.left * 0.5f, Vector3.right * 0.5f, Vector3.up * 0.5f, Vector3.down * 0.5f })
                        if (!ground.HasTile(ground.WorldToCell(candidate + d)) || otherTiles.Any(t => t.HasTile(t.WorldToCell(candidate + d)))) clear = false;
                    if (clear) { position = candidate; break; }
                }
                if (!position.HasValue) throw new InvalidOperationException(path + ": 상인 옆 안전한 배치점 없음");
                var npc = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                npc.transform.position = position.Value;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[Equipment] 강화 NPC 실제 프리팹 배치: " + path + " @ " + npc.transform.localPosition);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    [MenuItem("Tools/Equipment/2. Verify Weapons")]
    public static void Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("검사는 Play 모드 밖에서 실행하세요.");
        void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException("[Equipment] " + what); }
        var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(GrowthPath);
        var weapons = registry.equipments.Where(e => e != null && e.isRunWeapon).ToArray();
        Check(weapons.Length == 9, "장비 9종 등록");
        var mk0 = weapons.Single(e => e.name == "MK0");
        Check(mk0.upgrades.Length == 3, "MK0의 3분기");
        foreach (var e in weapons)
        {
            Check(e.ComboLength == 3 && e.combo.All(h => h.hits >= 1 && h.multiplier > 0), e.name + " 콤보");
            Check(e.speedMultipliers.Length == 5 && e.hitstunSeconds.Length == 5, e.name + " 5단계 설정");
            Check(e.upgrades.All(n => n != null && n.upgradeTier == e.upgradeTier + 1), e.name + " 다음 단계");
            Check(e.upgradeTier < 2 || e.upgrades.Length == 0, e.name + " 2단계 상한");
        }
        var shadow = weapons.Single(e => e.name == "Shadow");
        Check(Mathf.Approximately(10f * 2f * mk0.Hit(2).TotalMultiplier, 30f), "DashDoll MK0 = 30×1");
        Check(Mathf.Approximately(10f * 0.5f * mk0.Hit(2).TotalMultiplier, 7.5f), "MeleeDoll MK0 = 7.5×2");
        Check(Mathf.Approximately(10f * 2f * shadow.Hit(2).TotalMultiplier, 32f), "DashDoll 그림자 = 32×1 (타수 불변)");
        Check(shadow.StunDuration() == 0f, "그림자 무경직");
        Check(PlayerSkillInventoryManager.RemainingShadowStacks(5, 0.5f) == 3, "그림자 손실 버림");
        Check(PlayerSkillInventoryManager.RemainingShadowStacks(5, 0.3f) == 4, "상위 그림자 손실 버림");
        Check(!StatusRules.BlockedBySuperArmor(StatusType.Freeze) && StatusRules.BlockedBySuperArmor(StatusType.Stun), "빙결과 슈퍼아머 분리");
        var saved = new EquipmentSaveData { equipmentSOName = "ShadowAbsorption", enhanceLevel = 2, shadowStacks = 31 };
        Check(JsonUtility.FromJson<EquipmentSaveData>(JsonUtility.ToJson(saved)).shadowStacks == 31, "스택 저장 왕복");
        foreach (string path in ShopRooms()) Check(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<EnhanceShopNPC>(true).Length == 1, path + " 강화 NPC 1개");
        Check(AssetDatabase.LoadAssetAtPath<ShopRegistrySO>(ShopPath).equipmentPool.Count == 0, "교체용 장비 진열 폐지");
        CheckCombat(weapons.Single(e => e.name == "FrostBurst"));
        CheckCards(weapons);
        Debug.Log("[Equipment] PASS: 9 weapons, upgrade tree, finisher multipliers, stagger, save data, shop prefab wiring.");
    }

    private static void CheckCards(EquipmentSO[] weapons)
    {
        var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/Reward Selection/RewardSelectionUI.prefab");
        try
        {
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(960f, 540f);
            ((GameObject)new SerializedObject(root.GetComponent<RewardSelectionUI>()).FindProperty("panel").objectReferenceValue).SetActive(true);
            var card = root.GetComponentInChildren<RewardCard>(true);
            var rect = (RectTransform)card.transform;
            Vector2 original = rect.sizeDelta;
            foreach (var weapon in weapons)
            {
                card.Setup(WeaponCardData(weapon), 0);
                RebuildCardLayouts(root);
                CheckCardRegions(card);
                var scroll = card.GetComponentInChildren<ScrollRect>();
                scroll.verticalNormalizedPosition = 0f;
                card.Setup(WeaponCardData(weapon), 0);
                if (scroll.content.anchoredPosition != Vector2.zero)
                    throw new InvalidOperationException("[Equipment UI] Reopening must reset scroll.");
            }
            card.Setup(new RewardCandidate { displayData = new GrowthItemData { itemName = "기존 보상", description = "복원 검사" } }, 0);
            RebuildCardLayouts(root);
            CheckCardRegions(card);
            if (rect.sizeDelta != original) throw new InvalidOperationException("[Equipment UI] 일반 보상 카드 크기 복원 실패");
            Debug.Log("[Equipment UI] PASS: 9 weapons, art-aligned regions, masked full descriptions, scroll reset, normal size restored.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab");
        try
        {
            var card = root.GetComponentInChildren<RewardCard>(true);
            for (Transform t = card.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            card.Setup(new RewardCandidate { displayData = new GrowthItemData { itemName = "기존 보상", description = "슬롯 선택 카드 검사" } }, -1);
            RebuildCardLayouts(root);
            CheckCardRegions(card);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static RewardCandidate WeaponCardData(EquipmentSO weapon) => new RewardCandidate {
        rawData = weapon, displayData = new GrowthItemData { itemName = weapon.equipmentName,
            description = weapon.description + "\n\n강화 비용: 275G (상점당 1회)", icon = weapon.icon } };

    private static void RebuildCardLayouts(GameObject root)
    {
        // UI 루트에는 LayoutController가 없다. 각 레이아웃 루트에서 직접 갱신해야 자식까지 검사된다.
        Canvas.ForceUpdateCanvases();
        foreach (var group in root.GetComponentsInChildren<LayoutGroup>())
            LayoutRebuilder.ForceRebuildLayoutImmediate(group.GetComponent<RectTransform>());
        foreach (var scroll in root.GetComponentsInChildren<ScrollRect>())
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        Canvas.ForceUpdateCanvases();
    }

    private static void CheckCardRegions(RewardCard card)
    {
        var rect = card.GetComponent<RectTransform>();
        var title = card.transform.Find("Name").GetComponent<TMPro.TextMeshProUGUI>();
        var scroll = card.GetComponentInChildren<ScrollRect>(true);
        if (scroll == null || scroll.viewport.GetComponent<RectMask2D>() == null || scroll.horizontal || !scroll.vertical)
            throw new InvalidOperationException("[Equipment UI] Missing masked vertical scroll viewport.");
        // 글자가 들어갈 Rect만 검사하면 아트의 이름판을 침범해도 통과한다. 실제 아트 영역도 검사한다.
        void Within(RectTransform area, float bottom, float top)
        {
            var corners = new Vector3[4]; area.GetWorldCorners(corners);
            var low = rect.InverseTransformPoint(corners[0]); var high = rect.InverseTransformPoint(corners[2]);
            if (low.y < rect.rect.yMin + rect.rect.height * bottom - 1f ||
                high.y > rect.rect.yMin + rect.rect.height * top + 1f ||
                low.x < rect.rect.xMin || high.x > rect.rect.xMax)
                throw new InvalidOperationException("[Equipment UI] Outside card art region: " + area.name);
        }
        Within(title.rectTransform, 0.35f, 0.47f);
        Within(scroll.viewport, 0.06f, 0.32f);
        title.ForceMeshUpdate(true);
        if (title.isTextOverflowing) throw new InvalidOperationException("[Equipment UI] Title overflow: " + title.text);
        var body = scroll.content.GetComponent<TMPro.TextMeshProUGUI>();
        float required = body.GetPreferredValues(body.text, scroll.content.rect.width, float.PositiveInfinity).y;
        if (scroll.content.rect.height + 1f < required || scroll.content.pivot.y != 1f)
            throw new InvalidOperationException($"[Equipment UI] Description content height {scroll.content.rect.height} < {required}, pivot {scroll.content.pivot.y}.");
    }

    // 실제 프리팹/폰트를 임시 씬에서 렌더한다. 작업 중인 씬이나 게임 데이터는 변경하지 않는다.
    [MenuItem("Tools/Equipment/3. Preview Reward Cards")]
    public static void PreviewCards()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var cameraObject = new GameObject("Card preview camera", typeof(Camera));
        var canvasObject = new GameObject("Card preview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
        var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .08f, .1f); camera.orthographic = true;
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera; canvas.planeDistance = 10f;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        var target = new RenderTexture(960, 540, 24);
        var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Reward Selection/RewardSelectionUI.prefab"), canvas.transform);
            // BattleScene 등 실제 씬에 저장된 RewardSelectionUI 루트 여백과 동일하게 검수한다.
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(-80f, -40f);
            ((GameObject)new SerializedObject(root.GetComponent<RewardSelectionUI>()).FindProperty("panel").objectReferenceValue).SetActive(true);
            var cards = root.GetComponentsInChildren<RewardCard>(true);
            var choices = AssetDatabase.LoadAssetAtPath<EquipmentSO>(Folder + "/MK0.asset").upgrades;
            for (int state = 0; state < 3; state++)
            {
                for (int i = 0; i < cards.Length; i++)
                    cards[i].Setup(state < 2 ? WeaponCardData(choices[i]) : new RewardCandidate {
                        displayData = new GrowthItemData { itemName = "일반 보상", description = "기존 보상 크기와 이름판 위치를 확인합니다.", icon = choices[i].icon } }, i);
                RebuildCardLayouts(root);
                foreach (var card in cards)
                {
                    CheckCardRegions(card);
                    var scroll = card.GetComponentInChildren<ScrollRect>();
                    if (state == 1) scroll.verticalNormalizedPosition = 0f;
                }
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                System.IO.Directory.CreateDirectory("Logs");
                System.IO.File.WriteAllBytes($"Logs/RewardCards-{state}.png", pixels.EncodeToPNG());
            }
            Debug.Log("[Equipment UI] Preview saved: Logs/RewardCards-0.png (top), -1.png (bottom), -2.png (normal).");
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CheckCombat(EquipmentSO frost)
    {
        void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException("[Equipment Combat] " + what); }
        var objects = new System.Collections.Generic.List<GameObject>();
        CharacterStat Target(string name, EnemyTier tier, Vector3 position)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false); go.transform.position = position; go.layer = Layers.Enemy; objects.Add(go);
            var stat = go.AddComponent<CharacterStat>();
            // 셋업용 더미에는 시각 효과 구독을 하지 않는다. 실제 Health/Status/스탯 계산만 검사한다.
            var health = go.GetComponent<CharacterHealth>();
            var status = go.GetComponent<CharacterStatus>();
            typeof(CharacterStat).GetProperty("Health").SetValue(stat, health);
            typeof(CharacterStat).GetProperty("Status").SetValue(stat, status);
            typeof(CharacterStat).GetField("baseMaxHP", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(stat, 1000f);
            status.Init(stat); status.Tier = tier; health.Init(stat, status);
            return stat;
        }
        try
        {
            var main = Target("WeaponCheck target", EnemyTier.Boss, new Vector3(10000, 10000, 0));
            var nearby = Target("WeaponCheck neighbor", EnemyTier.Normal, main.transform.position + Vector3.right);
            main.Status.ApplySuperArmor(999999f);
            main.Status.DamageSuperArmor(1000000f);
            Check(main.Status.HasSuperArmor, "보스 슈퍼아머는 영구");
            for (int i = 0; i < 4; i++) main.Status.AddWeaponFrostStack(frost, null);
            Check(!main.Status.HasStatus(StatusType.Freeze), "4타에는 미빙결");
            main.Status.AddWeaponFrostStack(frost, null);
            Check(main.Status.HasStatus(StatusType.Freeze), "5타에 보스 실제 빙결");
            main.Health.GetDamage(new DamageInfo(1, DamageType.Fixed, category: DamageCategory.Debuff));
            Check(main.Status.HasStatus(StatusType.Freeze), "DoT는 빙결 유지");
            nearby.Status.ApplyStatus(StatusType.Freeze);
            main.Health.GetDamage(new DamageInfo(10, category: DamageCategory.BasicAttack));
            Check(!main.Status.HasStatus(StatusType.Freeze), "다음 직접 타격에 해제");
            Check(Mathf.Approximately(main.Health.CurHP, 949f), "보스 서리폭발 = 최대체력 4% (40) + 직접타격 10 + DoT 1");
            Check(Mathf.Approximately(nearby.Health.CurHP, 984f), "폭발은 40의 40% = 16, 중심 중복 없음");
            Check(nearby.Status.HasStatus(StatusType.Freeze), "폭발로 연쇄 빙결 해제 금지");
            for (int i = 0; i < 5; i++) main.Status.AddWeaponFrostStack(frost, null);
            Check(!main.Status.HasStatus(StatusType.Freeze), "빙결 해제 뒤 내성");
            float speed = main.MOVESPEED;
            main.Status.SetFrostAura(0.15f);
            Check(Mathf.Approximately(main.MOVESPEED, speed * 0.85f), "서리 흉갑 이동 감속");
            main.Status.SetFrostAura(0f);
            Check(Mathf.Approximately(main.MOVESPEED, speed), "서리 흉갑 즉시 회수");

            var entity = main.gameObject.AddComponent<EnemyController>();
            entity.team = Team.Enemy;
            typeof(BaseEntity).GetField("_stats", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(entity, main);
            main.Status.ClearStatus(); main.Status.ApplyStatus(StatusType.Freeze, bypassLimits: true);
            bool advanced = false;
            System.Collections.IEnumerator Inner() { advanced = true; yield return null; }
            var routine = (System.Collections.IEnumerator)typeof(BaseEntity).GetMethod("ActionRoutine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(entity, new object[] { Inner() });
            Check(routine.MoveNext() && !advanced && entity.ActionDeltaTime == 0f, "빙결 동안 패턴 MoveNext 정지");
            main.Status.RemoveStatus(StatusType.Freeze);
            Check(routine.MoveNext() && advanced, "해제 후 같은 패턴 재개");
            (routine as IDisposable)?.Dispose();
            CheckInventory(Target("WeaponCheck player", EnemyTier.Normal, Vector3.zero), main);
            Debug.Log("[Equipment Combat] PASS: frost threshold, boss freeze, DoT, shatter, blast, immunity, aura, pattern pause/resume.");
        }
        finally
        {
            foreach (var go in objects)
            {
                CharacterStatus.ActiveEnemies.Remove(go.GetComponent<CharacterStatus>());
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }

    private static void CheckInventory(CharacterStat player, CharacterStat target)
    {
        void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException("[Equipment Inventory] " + what); }
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Set(object obj, string field, object value) => obj.GetType().GetField(field, flags).SetValue(obj, value);
        void Call(object obj, string method) => obj.GetType().GetMethod(method, flags).Invoke(obj, null);
        var oldGame = GameManager.Instance;
        var oldWeapons = PlayerSkillInventoryManager.Instance;
        var go = new GameObject("WeaponCheck inventory") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        PlayerSkillInventoryManager weapons = null;
        try
        {
            player.gameObject.layer = Layers.Player;
            Set(player, "_isPlayer", true);
            CharacterStatus.ActiveEnemies.Remove(player.Status);
            var controller = player.gameObject.AddComponent<PlayerController>();
            Set(controller, "stat", player);
            var game = go.AddComponent<GameManager>();
            GameManager.Instance = game; Set(game, "playerController", controller);
            game.dataManager = go.AddComponent<DataManager>();
            Set(game.dataManager, "growthRegistry", AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(GrowthPath));
            Set(game.dataManager, "shopRegistry", AssetDatabase.LoadAssetAtPath<ShopRegistrySO>(ShopPath));
            game.inventoryManager = go.AddComponent<InventoryManager>();
            weapons = go.AddComponent<PlayerSkillInventoryManager>();
            Call(weapons, "OnEnable"); weapons.Initialize();
            Check(weapons.Weapon.name == "MK0", "새 런은 MK0");
            var shop1 = go.AddComponent<EnhanceShopNPC>();
            var mk1 = weapons.Weapon.upgrades.Single(e => e.name == "MK1");
            int cost = shop1.CurrentCost();
            Check(!shop1.TryPurchaseUpgrade(mk1) && weapons.Weapon.name == "MK0", "골드 부족은 변경 없음");
            game.inventoryManager.AddGold(cost + 1000);
            Check(shop1.TryPurchaseUpgrade(mk1) && game.inventoryManager.GOLD == 1000, "구매 성공 시에만 차감");
            var mk2 = mk1.upgrades.Single(e => e.name == "MK2");
            Check(!shop1.TryPurchaseUpgrade(mk2) && game.inventoryManager.GOLD == 1000, "한 상점에서는 재강화 금지");
            int action = weapons.BeginWeaponAction();
            var hit = new DamageInfo(10, DamageType.Fixed, player.gameObject, category: DamageCategory.BasicAttack);
            weapons.ConfigureBasicHit(ref hit, action);
            target.Status.ClearStatus();
            target.Health.GetDamage(hit); target.Health.GetDamage(hit);
            Check(weapons.PassionStacks == 1, "다단/광역 동일 동작은 열정 한 번");
            for (int i = 0; i < 2; i++)
            { hit = new DamageInfo(10, DamageType.Fixed, player.gameObject, category: DamageCategory.BasicAttack); weapons.ConfigureBasicHit(ref hit, weapons.BeginWeaponAction()); target.Health.GetDamage(hit); }
            Check(weapons.PassionStacks == 3 && Mathf.Approximately(weapons.BasicSpeedMultiplier, 1.09f), "3중첩 공속 9%");
            hit = new DamageInfo(10); weapons.ConfigureBasicHit(ref hit, weapons.BeginWeaponAction());
            Check(hit.amount == 11f && hit.hitstunDuration == 0.2f, "최대 열정 고정 +1 / 중간 경직 0.2초");
            Set(weapons, "_lastPassionHit", Time.time - 6f); Call(weapons, "UpdateWeapon");
            Check(weapons.PassionStacks == 0, "5초 미명중 열정 만료");
            var shop2 = go.AddComponent<EnhanceShopNPC>();
            Check(shop2.TryPurchaseUpgrade(mk2) && !weapons.CanEnhanceEquipped(), "다른 상점 2단계 가능 / 3단계 불가");

            var shadow = AssetDatabase.LoadAssetAtPath<EquipmentSO>(Folder + "/Shadow.asset");
            weapons.EquipEquipment(EquipmentInstance.Roll(shadow));
            float before = player.ATK;
            DamageEventBus.TriggerEntityDied(target.Health, new DamageInfo(1, attacker: player.gameObject, category: DamageCategory.Skill));
            Check(weapons.ShadowStacks == 20 && Mathf.Approximately(player.ATK, before + 10f), "아군 스킬 보스 처치 20스택 +10공격력");
            player.Health.GetDamage(new DamageInfo(1, DamageType.Fixed, category: DamageCategory.Debuff));
            Check(weapons.ShadowStacks == 10, "실제 체력 피해에 50% 소실");
            player.Health.Invincible = true;
            player.Health.GetDamage(new DamageInfo(1, DamageType.Fixed, category: DamageCategory.Debuff));
            Check(weapons.ShadowStacks == 10, "피해 차단 시 스택 유지");
            Check(weapons.UpgradeTo(shadow.upgrades[0]) && weapons.ShadowStacks == 10, "상위 그림자로 스택 이월");
            var saved = new SaveData(); weapons.SaveToData(saved); weapons.LoadFromData(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(saved)));
            Check(weapons.Weapon.name == "ShadowAbsorption" && weapons.ShadowStacks == 10 && Mathf.Approximately(player.ATK, before + 6f), "층 이동 저장 복원 / 스탯 중복 없음");
            weapons.LoadFromData(new SaveData());
            Check(weapons.Weapon.name == "MK0" && weapons.ShadowStacks == 0, "새 런 장비와 그림자 초기화");
            Debug.Log("[Equipment Inventory] PASS: MK0, gold, once per shop, passion hits/expiry, shadow kills/damage/save/reset.");
        }
        finally
        {
            if (weapons != null) Call(weapons, "OnDisable");
            GameManager.Instance = oldGame; PlayerSkillInventoryManager.Instance = oldWeapons;
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
