using System.Collections.Generic;

[System.Serializable]
public class SaveData
{
    // v2 uses asset names for equipment and summon references. Preserve JSON field names.
    public const int CURRENT_VERSION = 2;

    public int saveVersion = CURRENT_VERSION;

    public int currentFloor;
    public float playerHP;
    public int gold;
    public List<CoreSlotSaveData> slots = new List<CoreSlotSaveData>();
    public List<TreasureSaveData> treasures = new List<TreasureSaveData>();

    public EquipmentSaveData equipment; // 착용 장비 한 자루. null = 없음(스킬은 장비로만 얻으므로 초반엔 null).

    // The pouch restores owned copies in order. Earlier v2 files can include empty strings and a slot count.
    public int pouchSlotCount;
    public List<string> pouchItemNames = new List<string>();

    // [증강] 증강 방 보상으로 누적된 최대 체력. 페널티는 방 하나짜리라 저장할 게 없다.
    public float augmentMaxHpBonus;

    // [메인 소환수 강화] 보상방에서 올린 단계(0 ~ MAX_LEVEL). 로드 때 현재 최대치로 잘린다. 필드가 없던 세이브는 0 으로 읽혀 호환된다.
    public int mainSummonEnhanceLevel;

    // [소환수 2슬롯] 2번째 칸의 강화 단계와 활성 슬롯. 필드가 없던 세이브는 0 으로 읽혀 호환된다.
    public int secondSummonEnhanceLevel;
    public int activeSummonSlot;
}

[System.Serializable]
public class EquipmentSaveData
{
    public string equipmentSOName;                          // EquipmentSO 에셋 이름
    public int enhanceLevel;
    public float serendipityElapsed;
    public float moveBuffRemaining;
}

[System.Serializable]
public class CoreSlotSaveData
{
    public bool isShattered;
    // Asset names distinguish variants that share a CommandData value.
    public string equippedMinionName;
    // Kept in the existing v2 payload for compatibility with older files.
    public string equippedThrowAbilityName;
    public int evolutionIndex;
    public int quantity;
}

[System.Serializable]
public class TreasureSaveData
{
    public string treasureSOAddress; // TreasureSO name or itemName
    public int stackCount;
}
