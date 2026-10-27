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

    [Header("손 애니메이션")]
    [Tooltip("선택 사항. Body Animator와 같은 기본 컨트롤러를 사용하는 손 애니메이션 오버라이드. 비어 있으면 기본 외형을 사용한다.")]
    public AnimatorOverrideController handAnimationOverride;

    [Header("패시브 (공격 관련. 유틸은 주머니 아이템 담당)")]
    [Tooltip("장비가 주는 패시브들(1개 이상). 스탯형/상태이상형을 원하는 만큼 담을 수 있다.\n" +
             "인스펙터에서 + 로 종류를 골라 추가한다([SerializeReference]).")]
    [SerializeReference] public List<EquipmentPassive> passives = new List<EquipmentPassive>();

    [Header("강화")]
    [Tooltip("강화 상한 레벨.")]
    public int maxEnhanceLevel = 5;
    [Tooltip("1015 장비는 +N 수치 강화 대신 다음 장비를 선택한다. 빈 배열이면 최종 단계.")]
    public EquipmentSO[] upgradeOptions = new EquipmentSO[0];
    [Min(0)] public int upgradeStage;
    [Tooltip("강화 전용 장비는 무작위 보상/진열 판매에 넣지 않는다.")]
    public bool upgradeOnly;

    [Header("기본 공격")]
    public Vector3 comboMultipliers = new Vector3(1f, 1f, 1.5f);
    [Min(1)] public int basicHitsPerContact = 1;
    [Min(0f), Tooltip("플레이어·소환수 평타의 추가 타격 간격(초). 0이면 동시에 적용한다.")]
    public float basicRepeatHitInterval;
    [Min(0f)] public float basicDamagePerHit = 1f;
    [Min(0f)] public float minionDashDamageMultiplier = 1f;

    [Header("약점 공략 (플레이어 3타 적중)")]
    public bool exploitsWeakness;
    [Min(0f)] public float weaknessDuration = 10f;

    [Header("세렌디피티")]
    public bool serendipity;
    [Range(0f, 1f)] public float resetChance = 0.13f;
    [Min(0.01f)] public float guaranteedProcSeconds = 33f;
    [Min(0f)] public float moveBuffSeconds = 13f;
    [Min(0f)] public float moveBuffPercent = 0.03f;

    public float ComboMultiplier(int index) => comboMultipliers[Mathf.Clamp(index, 0, 2)];
    public float ResetChance(float luckPercent) => Mathf.Clamp01(resetChance + luckPercent / 100f);
    // 강화당 패시브 수치는 각 패시브가 자기 파라미터별로 관리한다
    // (StatEffect.valuePerLevel / EquipmentStatusPassive.*PerLevel / EquipmentBuffPassive.reapplyDelayPerLevel).
    // 예전의 전역 passiveGrowthPerLevel 은 '특정 파라미터만 강화'를 못 해서 제거했다.
}
