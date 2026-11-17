using UnityEngine;

/// <summary>장비의 개별 강화 상태. 플레이어 액티브 스킬은 더 이상 부여하지 않는다.</summary>
[System.Serializable]
public class EquipmentInstance
{
    public EquipmentSO baseData;
    public int enhanceLevel;
    // 강화 전용 장비는 단계가 곧 강화레벨이다(UpgradeTo/LoadFromData 와 같은 규칙). 안 맞추면 세이브-로드 전까지 0강으로 돈다.
    public static EquipmentInstance Roll(EquipmentSO so) => new EquipmentInstance { baseData = so, enhanceLevel = so != null && so.upgradeOnly ? so.upgradeStage : 0 };
}
