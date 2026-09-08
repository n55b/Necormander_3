using System.Collections.Generic;
using UnityEngine;

/// <summary>장비 정의. 패시브와 강화만 부여한다.</summary>
[CreateAssetMenu(fileName = "Equipment", menuName = "Necromancer/Equipment/Equipment")]
public class EquipmentSO : ScriptableObject
{
    [Header("표시")]
    public string equipmentName;
    [TextArea] public string description;
    public Sprite icon;
    [Tooltip("장비 등급.")]
    public ItemRarity rarity = ItemRarity.Common;
    [Tooltip("상점 판매가(골드).")]
    public int shopCost = 150;

    [Header("패시브 (공격 관련. 유틸은 주머니 아이템 담당)")]
    [Tooltip("장비가 주는 패시브들(1개 이상). 스탯형/상태이상형을 원하는 만큼 담을 수 있다.\n" +
             "인스펙터에서 + 로 종류를 골라 추가한다([SerializeReference]).")]
    [SerializeReference] public List<EquipmentPassive> passives = new List<EquipmentPassive>();

    [Header("강화")]
    [Tooltip("강화 상한 레벨.")]
    public int maxEnhanceLevel = 5;
    // 강화당 패시브 수치는 각 패시브가 자기 파라미터별로 관리한다
    // (StatEffect.valuePerLevel / EquipmentStatusPassive.*PerLevel / EquipmentBuffPassive.reapplyDelayPerLevel).
    // 예전의 전역 passiveGrowthPerLevel 은 '특정 파라미터만 강화'를 못 해서 제거했다.
}
