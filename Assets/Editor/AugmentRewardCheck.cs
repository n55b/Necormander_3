using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>격리 batchmode에서 실제 보상 지급 경로와 테이블·아이콘 연결을 확인한다.</summary>
public static class AugmentRewardCheck
{
    private const string Key = "AugmentRewardCheck.Running";
    private static int passed;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("격리 batchmode 전용 검사");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true); EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    private static void Resume() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        int exit = 0;
        try { Check(); }
        catch (Exception e) { Debug.LogException(e); exit = 1; }
        finally
        {
            ActiveAugment.ResetAll(); GameManager.Instance = null; InventoryManager.Instance = null; ItemPouch.Instance = null;
            SessionState.EraseBool(Key); EditorApplication.update -= Tick; EditorApplication.Exit(exit);
        }
    }
    private static void Check()
    {
        var host = new GameObject("Inactive reward fixture"); host.SetActive(false);
        var gm = host.AddComponent<GameManager>(); GameManager.Instance = gm;
        var inventory = host.AddComponent<InventoryManager>(); InventoryManager.Instance = inventory; gm.inventoryManager = inventory;
        var data = host.AddComponent<DataManager>(); gm.dataManager = data;
        var registrySource = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:GrowthRegistrySO")[0]));
        var registry = Object.Instantiate(registrySource); Set(data, "growthRegistry", registry);
        var player = host.AddComponent<PlayerController>(); Set(gm, "playerController", player);
        var stat = new GameObject("Reward HP").AddComponent<CharacterStat>(); Set(player, "stat", stat);
        Set(stat, "setDoneActions", new UnityEngine.Events.UnityEvent());
        stat.Setup();
        var health = stat.GetComponent<CharacterHealth>(); health.Init(stat, null);
        var pouch = host.AddComponent<ItemPouch>(); ItemPouch.Instance = pouch;
        var manager = host.AddComponent<RewardManager>();
        var table = AssetDatabase.LoadAssetAtPath<AugmentTableSO>("Assets/SOData/Augment/AugmentTable.asset");
        Require(table.rewards.Count == 10 && table.allowItemRewards, "새 보상 10행·아이템 후보 활성화");
        Require(table.rewards.Count(r => r.kind == AugmentRewardKind.MaxHpFlat) == 0, "옛 최대 체력 증가 보상 제외");
        var baseOffer = table.RollNoRiskOffer();
        Require(baseOffer.reward.kind == AugmentRewardKind.NormalRoom, "무위험은 일반 방 보상");
        foreach (var reward in table.rewards.Where(r => r.maximumItemTier == ItemTier.Tier4))
        {
            foreach (var drop in Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)) Object.DestroyImmediate(drop.gameObject);
            Set(inventory, "gold", 0); health.SetHP(10);
            ActiveAugment.Apply(new AugmentOffer { reward = reward });
            manager.RequestClearReward(RoomType.Augment);
            int bonus = reward.kind == AugmentRewardKind.Gold ? Mathf.RoundToInt(reward.value) : 0;
            Require(inventory.GOLD == 200 + bonus, reward.tier + "/" + reward.kind + " 기본 200 + 보너스 정확히 한 번");
            float hp = reward.kind == AugmentRewardKind.HealPercent ? Mathf.Min(stat.MAXHP, 10 + stat.MAXHP * reward.value / 100) : 10;
            Require(Mathf.Abs(health.CurHP - hp) < .01f, reward.tier + "/" + reward.kind + " 최대 체력 비례 회복");
            int expectedItems = reward.kind == AugmentRewardKind.NormalRoom ? 1 : reward.kind == AugmentRewardKind.Item ? reward.itemCount : 0;
            Require(Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None).Length == expectedItems, reward.tier + "/" + reward.kind + " 실제 바닥 아이템 " + expectedItems + "개");
            Require(!ActiveAugment.HasPendingReward, "지급 후 대기 보상 소진");
        }
        Require(!RewardProcessor.HasRewardItem(data, ItemTier.Tier2), "현재 고등급 후보 없음");
        var oldRandom = UnityEngine.Random.state; UnityEngine.Random.InitState(91);
        for (int i = 0; i < 100; i++)
        {
            var reward = (AugmentReward)typeof(AugmentTableSO).GetMethod("RollReward", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(table, new object[] { AugmentTier.P3 });
            if (reward.kind == AugmentRewardKind.Item && reward.maximumItemTier != ItemTier.Tier4) throw new Exception("빈 고등급 보상이 노출됨");
        }
        Require(true, "고등급 미구현 보상은 카드 후보에서 제외");
        var rare = ScriptableObject.CreateInstance<ItemSO>(); rare.tier = ItemTier.Tier2; rare.availableAsReward = true;
        registry.items.Add(rare);
        Require(RewardProcessor.HasRewardItem(data, ItemTier.Tier2), "고등급 추가 시 자동으로 후보 발견");
        Require(RewardProcessor.PickRandomItem(data, ItemTier.Tier2) == rare, "고등급 보상에 일반 아이템 혼입 없음");
        bool offered = false;
        for (int i = 0; i < 100; i++)
        {
            var reward = (AugmentReward)typeof(AugmentTableSO).GetMethod("RollReward", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(table, new object[] { AugmentTier.P3 });
            offered |= reward.kind == AugmentRewardKind.Item && reward.maximumItemTier == ItemTier.Tier2;
        }
        Require(offered, "고등급 추가 시 실제 카드 추첨에 포함");
        rare.maxStack = 10; registry.items = new List<ItemSO> { rare };
        var owned = (List<ItemSO>)Field(pouch, "_slots").GetValue(pouch); for (int i = 0; i < 9; i++) owned.Add(rare);
        var reserved = new Dictionary<ItemSO, int>();
        Require(RewardProcessor.PickRandomItem(data, ItemTier.Tier2, reserved) == rare, "9개 보유 시 마지막 한 개는 가능");
        reserved[rare] = 1;
        Require(RewardProcessor.PickRandomItem(data, ItemTier.Tier2, reserved) == null, "한 보상 묶음에서 최대 중첩 초과 방지");
        UnityEngine.Random.state = oldRandom;
        foreach (string name in new[] { "Gauntlet", "Viper", "ViperSC", "ViperAS", "Serendipity", "Marionette" })
        {
            var equipment = AssetDatabase.LoadAssetAtPath<EquipmentSO>($"Assets/SOData/Equipment/1015/{name}.asset");
            Require(equipment.icon != null && AssetDatabase.GetAssetPath(equipment.icon).StartsWith("Assets/Resources/Sprites/UI/Equipment Icons/"), name + " 실제 무기 아이콘 연결");
        }
        Debug.Log($"[AugmentRewardCheck] PASS: {passed} checks; base/bonus gold, max-HP heals, actual drop counts, no-risk, rarity pool gating, stack reservations, six equipment icons.");
    }
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Require(bool success, string message)
    {
        if (!success) throw new Exception("[AugmentRewardCheck] FAIL: " + message);
        passed++; Debug.Log("[AugmentRewardCheck] OK: " + message);
    }
}
