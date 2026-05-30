using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

public static class SaveLoadTest
{
    [MenuItem("Tools/Verify Save-Load System")]
    public static void RunTest()
    {
        Debug.Log("<b>[SaveLoadTest]</b> Starting verification test...");

        // 1. 테스트용 SaveData 구성
        SaveData data = new SaveData();
        data.currentFloor = 3;
        data.playerHP = 8.5f;
        data.gold = 350;

        // 슬롯 테스트
        data.slots.Clear();
        data.slots.Add(new CoreSlotSaveData { isShattered = false, equippedLineageJob = "SkeletonWarrior", equippedThrowAbilityName = "", evolutionIndex = 1, quantity = 5 });
        data.slots.Add(new CoreSlotSaveData { isShattered = false, equippedLineageJob = "", equippedThrowAbilityName = "Fireball", evolutionIndex = 0, quantity = 0 });
        data.slots.Add(new CoreSlotSaveData { isShattered = true, equippedLineageJob = "", equippedThrowAbilityName = "", evolutionIndex = 0, quantity = 0 });

        // 보물 테스트
        data.treasures.Add(new TreasureSaveData { treasureSOAddress = "GoldCrown", stackCount = 2 });
        data.treasures.Add(new TreasureSaveData { treasureSOAddress = "IronShield", stackCount = 1 });

        // 보관 보석 테스트
        GemInstanceSaveData gem1 = new GemInstanceSaveData();
        gem1.baseGemSOAddress = "RubyGem";
        gem1.instanceId = "gem-12345";
        gem1.subSlots = 2;
        gem1.targetJob = CommandData.SkeletonWarrior;
        gem1.randomModifiers.Add(new StatModifier(StatType.Attack, 1.5f));
        data.availableGems.Add(gem1);

        // 보석 트리 테스트
        data.flatGemTree.Add(new FlatGemTreeNodeSaveData
        {
            gem = new GemInstanceSaveData { baseGemSOAddress = "RootGem", instanceId = "root-id", subSlots = 3, targetJob = CommandData.SkeletonWarrior },
            parentInstanceId = null,
            slotIndexInParent = -1
        });
        
        data.flatGemTree.Add(new FlatGemTreeNodeSaveData
        {
            gem = new GemInstanceSaveData { baseGemSOAddress = "EmeraldGem", instanceId = "child-id-1", subSlots = 1, targetJob = CommandData.SkeletonWarrior },
            parentInstanceId = "root-id",
            slotIndexInParent = 0
        });

        // 2. 직렬화 테스트
        string json = "";
        try
        {
            json = JsonUtility.ToJson(data, true);
            Debug.Log("<b>[SaveLoadTest]</b> Successfully serialized SaveData to JSON:\n" + json);
        }
        catch (System.Exception e)
        {
            Debug.LogError("<b>[SaveLoadTest]</b> Failed to serialize SaveData: " + e.Message);
            return;
        }

        // 3. 역직렬화 테스트
        SaveData restored = null;
        try
        {
            restored = JsonUtility.FromJson<SaveData>(json);
            Debug.Log("<b>[SaveLoadTest]</b> Successfully deserialized SaveData from JSON.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("<b>[SaveLoadTest]</b> Failed to deserialize SaveData: " + e.Message);
            return;
        }

        // 4. 정합성 검증
        bool success = true;
        if (restored.currentFloor != data.currentFloor) { Debug.LogError($"Mismatch currentFloor: {restored.currentFloor} vs {data.currentFloor}"); success = false; }
        if (restored.playerHP != data.playerHP) { Debug.LogError($"Mismatch playerHP: {restored.playerHP} vs {data.playerHP}"); success = false; }
        if (restored.gold != data.gold) { Debug.LogError($"Mismatch gold: {restored.gold} vs {data.gold}"); success = false; }

        if (restored.slots.Count != data.slots.Count) { Debug.LogError("Mismatch slots count"); success = false; }
        else
        {
            for (int i = 0; i < data.slots.Count; i++)
            {
                if (restored.slots[i].isShattered != data.slots[i].isShattered ||
                    restored.slots[i].equippedLineageJob != data.slots[i].equippedLineageJob ||
                    restored.slots[i].equippedThrowAbilityName != data.slots[i].equippedThrowAbilityName ||
                    restored.slots[i].evolutionIndex != data.slots[i].evolutionIndex ||
                    restored.slots[i].quantity != data.slots[i].quantity)
                {
                    Debug.LogError($"Mismatch at Slot {i}");
                    success = false;
                }
            }
        }

        if (restored.treasures.Count != data.treasures.Count) { Debug.LogError("Mismatch treasures count"); success = false; }
        if (restored.availableGems.Count != data.availableGems.Count) { Debug.LogError("Mismatch available gems count"); success = false; }
        else if (restored.availableGems.Count > 0)
        {
            if (restored.availableGems[0].randomModifiers.Count != data.availableGems[0].randomModifiers.Count ||
                restored.availableGems[0].randomModifiers[0].Type != data.availableGems[0].randomModifiers[0].Type ||
                restored.availableGems[0].randomModifiers[0].Value != data.availableGems[0].randomModifiers[0].Value)
            {
                Debug.LogError("Mismatch available gem modifiers");
                success = false;
            }
        }

        if (restored.flatGemTree == null || restored.flatGemTree.Count == 0) { Debug.LogError("Restored flatGemTree is empty"); success = false; }
        else
        {
            var rootNode = restored.flatGemTree.Find(x => string.IsNullOrEmpty(x.parentInstanceId));
            var childNode = restored.flatGemTree.Find(x => x.parentInstanceId == "root-id");
            
            if (rootNode == null) { Debug.LogError("Root node not found in flatGemTree"); success = false; }
            else if (rootNode.gem.instanceId != "root-id") { Debug.LogError("Mismatch root-id"); success = false; }
            
            if (childNode == null) { Debug.LogError("Child node not found in flatGemTree"); success = false; }
            else
            {
                if (childNode.slotIndexInParent != 0) { Debug.LogError($"Mismatch child slotIndex: {childNode.slotIndexInParent}"); success = false; }
                if (childNode.gem.instanceId != "child-id-1") { Debug.LogError($"Mismatch child gem instance id: {childNode.gem.instanceId}"); success = false; }
            }
        }

        if (success)
        {
            Debug.Log("<color=green><b>[SaveLoadTest]</b> ALL TESTS PASSED! SaveData format is fully JSON compatible and consistent.</color>");
        }
        else
        {
            Debug.LogError("<b>[SaveLoadTest]</b> Verification FAILED due to data mismatches.");
        }
    }
}
