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
    public GemTreeNodeSaveData gemTreeRoot;
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
public class GemTreeNodeSaveData
{
    public GemInstanceSaveData gem;
    public List<GemTreeNodeChildSaveData> children = new List<GemTreeNodeChildSaveData>();
}

[System.Serializable]
public class GemTreeNodeChildSaveData
{
    public int slotIndex;
    public GemTreeNodeSaveData childNode;
}
