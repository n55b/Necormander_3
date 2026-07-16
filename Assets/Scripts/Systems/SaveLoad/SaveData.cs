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
    // 에셋 이름으로 저장한다. 예전엔 CommandData(직업) 이름을 저장했는데, 같은 직업의 A/B/C 배리언트가
    // 전부 같은 minionType 을 공유해서 로드 시 레지스트리의 첫 번째 항목으로 붕괴됐다.
    // 플레이어 스킬(equippedPlayerSkillNames)이 이미 쓰던 방식과 동일.
    public string equippedMinionName;
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
