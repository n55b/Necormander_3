using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>현재 아이템 카탈로그·기본 지급·치명타·저장 검증. 에셋/씬/세이브를 저장하지 않는다.</summary>
public static class CriticalItemsCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Folder = "Assets/SOData/Items/";

    [MenuItem("Tools/Items/Verify Current Item Catalog")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var growth = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>("Assets/SOData/Registry/Growth Reward Registry.asset");
        var shop = AssetDatabase.LoadAssetAtPath<ShopRegistrySO>("Assets/SOData/Registry/Shop Registry.asset");
        var names = new[] { "Whetstone", "CrackedWhetstone", "ClothArmor", "LightBoots", "WindFeather",
            "PortableShield", "GaleDashRecovery", "GaleDashLeap", "HermesContract" };
        var charm = AssetDatabase.LoadAssetAtPath<ItemSO>(Folder + "HealingCharm.asset");
        Check(charm != null && growth.startingItem == charm && charm.MaxStack == 1 && !charm.availableAsReward, "기본 부적 배선/중첩/드롭 제외");
        Check(growth.items.Count == 10 && growth.items.Distinct().Count() == 10 && growth.items.Contains(charm), "카탈로그 10종");
        Check(shop.itemPool.Count == 9 && shop.itemPool.All(i => i != null && names.Contains(i.name)), "상점은 스택형 9종만");
        foreach (var name in names)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemSO>(Folder + name + ".asset");
            Check(item != null && growth.items.Contains(item) && shop.itemPool.Contains(item) && item.availableAsReward,
                "카탈로그/보상/상점: " + name);
            Check(item.MaxStack == (name == "HermesContract" ? 9 : 10), "최대 중첩: " + name);
        }
        foreach (var name in new[] { "BalanceWeight", "DeadlyCounter", "FrostKnuckle", "LifeCharm", "LuckyCoin", "SeventeenToOne", "VitalStrike" })
            Check(AssetDatabase.LoadAssetAtPath<ItemSO>(Folder + name + ".asset") == null, "구 아이템 제거: " + name);

        var scene = EditorSceneManager.NewPreviewScene();
        var oldManager = GameManager.Instance;
        var oldPouch = ItemPouch.Instance;
        var oldRandom = UnityEngine.Random.state;
        ItemPouch pouch = null;
        try
        {
            var host = new GameObject("Item catalog check");
            host.SetActive(false);
            SceneManager.MoveGameObjectToScene(host, scene);
            var stat = host.AddComponent<CharacterStat>();
            Set(stat, "_isPlayer", true);
            Set(stat, "baseMaxHP", 100f);
            Set(stat, "baseCritChance", 0f);
            var health = host.GetComponent<CharacterHealth>();
            var status = host.GetComponent<CharacterStatus>();
            typeof(CharacterStat).GetProperty("Health").SetValue(stat, health);
            typeof(CharacterStat).GetProperty("Status").SetValue(stat, status);
            health.Init(stat, status);
            var player = host.AddComponent<PlayerController>();
            Set(player, "stat", stat);
            var manager = host.AddComponent<GameManager>();
            Set(manager, "playerController", player);
            var data = host.AddComponent<DataManager>();
            Set(data, "growthRegistry", growth);
            manager.dataManager = data;
            GameManager.Instance = manager;
            pouch = host.AddComponent<ItemPouch>();
            Set(pouch, "useDebugStartingItems", false);
            pouch.Initialize(false);
            pouch.Initialize(false);
            Check(pouch.CountOf(charm) == 1, "새 게임/재초기화 기본 1개");
            health.SetHP(50f);
            typeof(ItemPouch).GetMethod("HandleRoomCleared", Private).Invoke(pouch, new object[] { null });
            Check(Mathf.Approximately(health.CurHP, 60f), "실제 방 클리어 회복 +10");

            pouch.TryAdd(AssetDatabase.LoadAssetAtPath<ItemSO>(Folder + "Whetstone.asset"));
            pouch.TryAdd(AssetDatabase.LoadAssetAtPath<ItemSO>(Folder + "CrackedWhetstone.asset"));
            pouch.Refresh(); pouch.Refresh();
            Check(Mathf.Approximately(stat.CRIT_CHANCE, 7f) && Mathf.Approximately(stat.CRIT_DAMAGE, 165f), "치명타 스탯/Refresh 멱등");
            var save = new SaveData(); pouch.SaveToData(save);
            pouch.LoadFromData(save); pouch.LoadFromData(save);
            var restored = new SaveData(); pouch.SaveToData(restored);
            Check(save.pouchItemNames.SequenceEqual(restored.pouchItemNames) && pouch.CountOf(charm) == 1, "층 이동/이어하기 중복 지급 없음");
            pouch.LoadFromData(new SaveData());
            Check(pouch.CountOf(charm) == 1 && pouch.SlotCount == 1, "부적 없는 이전 세이브 보완");

            ItemPouch.Instance = null;
            Check(!RewardProcessor.CanOfferItem(charm), "주머니 초기화 전에도 부적 추첨 제외");
            UnityEngine.Random.InitState(25);
            for (int i = 0; i < 100; i++)
            {
                var reward = RewardProcessor.PickRandomItem(data);
                Check(reward != null && names.Contains(reward.name), "방 보상은 스택형 9종만");
            }
            Debug.Log("[CriticalItemsCheck] PASS: 9 drop items + starting charm, fresh/load/reload, room heal, crit stats, 100 reward rolls.");
        }
        finally
        {
            if (pouch != null) typeof(ItemPouch).GetMethod("OnDisable", Private).Invoke(pouch, null);
            EditorSceneManager.ClosePreviewScene(scene);
            GameManager.Instance = oldManager;
            ItemPouch.Instance = oldPouch;
            UnityEngine.Random.state = oldRandom;
        }
    }

    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[CriticalItemsCheck] FAIL: " + message);
    }
}
