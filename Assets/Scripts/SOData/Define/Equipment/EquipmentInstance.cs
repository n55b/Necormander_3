using UnityEngine;

/// <summary>장비의 개별 강화 상태. 플레이어 액티브 스킬은 더 이상 부여하지 않는다.</summary>
[System.Serializable]
public class EquipmentInstance
{
    public EquipmentSO baseData;
    public int enhanceLevel;
    public static EquipmentInstance Roll(EquipmentSO so) => new EquipmentInstance { baseData = so };
}
