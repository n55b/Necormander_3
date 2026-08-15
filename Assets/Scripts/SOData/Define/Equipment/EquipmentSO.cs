using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장비 정의(템플릿). 하나의 에셋 = 하나의 장비 종류.
///
/// 플레이어 스킬은 이제 이 장비를 파밍해서만 얻는다(스킬 단독 획득 폐지). 한 번에 한 자루만 착용.
///   - skillPool 에서 skillsToRoll 개(기본 2)를 '획득 시점'에 랜덤으로 뽑아 EquipmentInstance.rolledSkills 로 굳힌다.
///     그 2개가 각각 Q(0)/E(1) 로 들어간다. R 은 소환수 전용이라 장비와 무관.
///   - passives: 여러 개 담을 수 있다([SerializeReference]). 스탯증가형/추가능력형 자유 조합.
///   - 강화는 EquipmentInstance.enhanceLevel(장비 1개당 정수)이 담당. 강화 시 패시브 수치와 바인딩 스킬의
///     데미지/타수가 같이 오른다 — 다만 수치/방식은 아직 미정이라 지금은 '경로'만 뚫어둔 상태다.
/// </summary>
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

    [Header("스킬 풀")]
    [Tooltip("이 장비가 뽑을 수 있는 플레이어 스킬 후보(설계상 4~5개).")]
    public List<PlayerSkillSO> skillPool = new List<PlayerSkillSO>();

    [Tooltip("획득 시 풀에서 몇 개를 뽑아 바인딩할지. 기본 2(Q/E).")]
    public int skillsToRoll = 2;

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
