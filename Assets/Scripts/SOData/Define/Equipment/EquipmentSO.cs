using System.Collections.Generic;
using UnityEngine;

/// <summary>장비 콤보·공격 속도·경직·고유 효과·런 강화 분기 정의.</summary>
[CreateAssetMenu(fileName = "Equipment", menuName = "Necromancer/Equipment/Equipment")]
public class EquipmentSO : ScriptableObject
{
    public enum Speed { VerySlow, Slow, Normal, Fast, VeryFast }
    public enum Stagger { None, Low, Medium, Strong, VeryStrong }
    public enum Effect { None, Passion, Frost, Shadow }

    [System.Serializable]
    public class ComboHit
    {
        [Min(0f)] public float multiplier = 1f;
        [Min(1)] public int hits = 1;
        public ComboHit(float multiplier, int hits = 1) { this.multiplier = multiplier; this.hits = hits; }
        public float TotalMultiplier => multiplier * Mathf.Max(1, hits);
    }

    [Header("기본 공격 (마지막 단계는 미니언 마무리)")]
    public bool isRunWeapon;
    public ComboHit[] combo = { new ComboHit(1f), new ComboHit(1f), new ComboHit(1.5f) };
    public Speed attackSpeed = Speed.Normal;
    public Stagger hitstun = Stagger.Medium;
    [Tooltip("5단계 속도 배율. Space 스킬에는 적용하지 않는다.")]
    public float[] speedMultipliers = { 0.7f, 0.85f, 1f, 1.15f, 1.3f };
    public float[] hitstunSeconds = { 0f, 0.1f, 0.2f, 0.3f, 0.4f };
    [Min(0.01f)] public float multiHitInterval = 0.08f;
    public int ComboLength => Mathf.Clamp(combo != null ? combo.Length : 3, 3, 5);
    public ComboHit Hit(int step) => combo != null && combo.Length > 0
        ? combo[Mathf.Clamp(step, 0, combo.Length - 1)] : new ComboHit(1f);
    public float SpeedMultiplier => Mathf.Max(0.05f, TierValue(speedMultipliers, (int)attackSpeed, 1f));
    public float StunDuration(bool maximumPassion = false) =>
        Mathf.Max(0f, TierValue(hitstunSeconds, (int)(maximumPassion && strongerAtMaxPassion ? Stagger.Strong : hitstun), 0.2f));
    private static float TierValue(float[] values, int index, float fallback)
        => values != null && index >= 0 && index < values.Length ? values[index] : fallback;

    [Header("런 강화 분기 (MK0 → 1단계 → 2단계)")]
    [Range(0, 2)] public int upgradeTier;
    public EquipmentSO[] upgrades = new EquipmentSO[0];

    [Header("무기 특수 효과")]
    public Effect effect;
    [Header("열정 — 명중한 공격 동작당 한 번")]
    [Min(1)] public int passionMaxStacks = 3;
    public float passionSpeedPerStack = 0.03f;
    public float passionMaxFlatDamage = 1f;
    public float passionDuration = 5f;
    public bool strongerAtMaxPassion;

    [Header("빙결 — 대상별 누적 / 일반·엘리트·보스 순")]
    [Min(1)] public int frostHits = 5;
    public float frostStackDuration = 5f;
    public float freezeDuration = 2.5f;
    public Vector3 frostMaxHpRatios = new Vector3(0.1f, 0.05f, 0.02f);
    [Range(0f, 1f)] public float frostExplosionRatio;
    public float frostExplosionRadius = 2f;
    public float frostAuraRadius;
    [Range(0f, 1f)] public float frostAuraReduction = 0.15f;

    [Header("그림자 — 아군 처치 공유 / 실제 체력 피해에만 손실")]
    public float shadowAttackPerStack = 0.5f;
    [Range(0f, 1f)] public float shadowLossRatio = 0.5f;
    public Vector3Int shadowKillStacks = new Vector3Int(1, 10, 20);
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
