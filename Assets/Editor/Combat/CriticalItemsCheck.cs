using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>현재 아이템 카탈로그·기본 지급·치명타·저장 검증. 에셋/씬/세이브를 저장하지 않는다.
/// 개수·수치를 박아 두지 않고 지금 에셋에서 계산한다 — 아이템 편집기로 추가/밸런스 조정을 해도 깨지지 않게.</summary>
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
        var charm = growth.startingItem;
        Check(charm != null && charm.MaxStack == 1 && !charm.availableAsReward, "기본 아이템 배선/중첩 1/드롭 제외");
        Check(growth.items.All(i => i != null) && growth.items.Distinct().Count() == growth.items.Count && growth.items.Contains(charm), "카탈로그 빈칸/중복 없음, 기본 아이템 포함");
        Check(!shop.itemPool.Contains(charm), "기본 아이템은 상점에 없음");
        foreach (var item in shop.itemPool)
            Check(item != null && growth.items.Contains(item) && item.availableAsReward, "상점 아이템은 카탈로그 등록 + 보상 등장: " + (item != null ? item.name : "(빈칸)"));
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
            float heal = charm.effects.OfType<ItemRoomClearHealEffect>().Sum(h => h.ResolveAmount(health));
            typeof(ItemPouch).GetMethod("HandleRoomCleared", Private).Invoke(pouch, new object[] { null });
            Check(heal > 0f && Mathf.Approximately(health.CurHP, Mathf.Min(health.MaxHP, 50f + heal)), $"실제 방 클리어 회복 +{heal}");

            var critItems = new[] { AssetDatabase.LoadAssetAtPath<ItemSO>(Folder + "Whetstone.asset"), AssetDatabase.LoadAssetAtPath<ItemSO>(Folder + "CrackedWhetstone.asset") };
            Check(critItems.All(i => i != null), "치명타 아이템 에셋(Whetstone/CrackedWhetstone)");
            float Flat(StatType type) => critItems.SelectMany(i => i.effects).OfType<ItemStatEffect>()
                .Where(e => e.statType == type && !e.isPercent).Sum(e => e.value);
            float critDamageBefore = stat.CRIT_DAMAGE;
            foreach (var item in critItems) pouch.TryAdd(item);
            pouch.Refresh(); pouch.Refresh();
            Check(Mathf.Approximately(stat.CRIT_CHANCE, Flat(StatType.CritChance)) && Mathf.Approximately(stat.CRIT_DAMAGE, critDamageBefore + Flat(StatType.CritDamage)), "치명타 스탯/Refresh 멱등");
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
                Check(reward != null && reward != charm && growth.items.Contains(reward) && reward.availableAsReward, "방 보상은 카탈로그의 보상 등장 아이템만");
            }
            Debug.Log($"[CriticalItemsCheck] PASS: catalog {growth.items.Count} / shop {shop.itemPool.Count} invariants + starting item, fresh/load/reload, room heal, crit stats, 100 reward rolls.");
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
