using System.Collections.Generic;

[System.Serializable]
public class SaveData
{
    public int currentFloor;
    public float playerHP;
    public int gold;
    public List<CoreSlotSaveData> slots = new List<CoreSlotSaveData>();
    public List<TreasureSaveData> treasures = new List<TreasureSaveData>();
    public List<GemInstanceSaveData> availableGems = new List<GemInstanceSaveData>();
    public List<FlatGemTreeNodeSaveData> flatGemTree = new List<FlatGemTreeNodeSaveData>();
    public List<string> equippedPlayerSkillNames = new List<string>(); // index 0-2 = Q/E/R, empty slot = ""
    public List<string> ownedPlayerSkillNames = new List<string>();

}

[System.Serializable]
public class CoreSlotSaveData
{
    public bool isShattered;
    public string equippedLineageJob; // CommandData enum name
    public string equippedThrowAbilityName; // ThrowAbilitySO name or itemName
    public int evolutionIndex;
    public int quantity;
}

[System.Serializable]
public class TreasureSaveData
{
    public string treasureSOAddress; // TreasureSO name or itemName
    public int stackCount;
}

[System.Serializable]
public class GemInstanceSaveData
{
    public string baseGemSOAddress; // GemSO name or itemName
    public string instanceId;
    public int subSlots;
    public List<StatModifier> randomModifiers = new List<StatModifier>();
    public CommandData targetJob;
}

[System.Serializable]
public class FlatGemTreeNodeSaveData
{
    public GemInstanceSaveData gem;
    public string parentInstanceId; // 부모의 인스턴스 ID (루트 노드인 경우 비어있거나 null)
    public int slotIndexInParent;   // 부모의 몇 번째 슬롯에 장착되었는지 (-1이면 루트)
}
