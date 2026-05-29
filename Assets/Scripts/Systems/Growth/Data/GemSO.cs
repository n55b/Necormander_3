using System;
using System.Collections.Generic;
using UnityEngine;

public enum StatType
{
    Attack,         // 공격력 강화 (배율: 0.1 = 10% 증가)
    Health,         // 체력 강화 (배율: 0.1 = 10% 증가)
    AttackSpeed,    // 공격 속도 강화 (배율: 1.0 = 공격 빈도 100% 증가)
    RespawnTime,    // 부활 시간 단축 (고정치: 1.0 = 1초 단축)
    ThrowEffect     // 던지기 능력 강화 (전사:데미지+, 궁수:범위+, 법사:횟수+, 기타:배율+)
}

public enum GemUniqueType
{
    None = 0,
    LethalPoison = 1,           // 치명적인 독 (중독 스택 2배)
    LethalDose = 2,             // 치사량 (틱 주기 단축)
    AchingBones = 3,            // 시리고 아린 뼈 (동결 후 스택 방어)
    SlowlyFreezingFlower = 4,   // 서서히 얼어붙는 꽃 (한기 최대치 증가)
    ExplodingFlesh = 5,         // 살덩이가 터지는 것 (비폭 전이)
    NoCountryForOldMen = 6,     // 노인을 위한 나라는 없다 (노화 즉사)
    
    // [추가] 신규 중독 유니크 6종
    PoisonHost = 7,             // 숙주 (주기적 광역 중독 전염)
    PoisonFootprint = 8,        // 부식석 발자취 (이동 장판 스폰, 아군 이속 증가)
    PoisonFlask = 9,            // 중독 플라스크 (던지기 적용 스택 증가)
    WoundInfection = 10,        // 상처 감염 (평타 시 독 틱타이머 단축)
    PoisonContagion = 11,       // 중독 전염 (사망 시 주변 전이)
    GreenFluid = 12,            // 초록색 체액 (틱 피해 시 포션 스폰)

    // [추가] 신규 한기 유니크 5종
    ColdBloodedHunter = 13,     // 냉혹한 사냥꾼 (적 이속 추가 감소)
    BitingWind = 14,            // 칼바람 (투척 범위 내 5초마다 한기 스택 부여)
    Frostbreaker = 15,          // 동상 파괴자 (한기 적에게 5% 추가 피해)
    AbsoluteZero = 16,          // 절대영도 (동결 진입 시 주변 50스택 광역, 방 1회)
    ShatterIcicle = 17,         // 고드름 부시기 (투척 적중 시 동결 해제 및 50% 추가 피해)

    // [추가] 처형 유니크 2종
    Fear = 18,                  // 공포 (처형 당한 적 주변 일반 적 1초 공포)
    Guillotine = 19,            // 단두대 (처형 스택 기준치 10% 완화)

    // [추가] 비폭 유니크 6종
    ImprovisedExplosive = 20,   // 급조 폭팔물 (폭발 피해 10% 증가)
    GoreParty = 21,             // 내장 파티 (반경 내 아군 회복)
    BloodArmor = 22,            // 피철갑 (반경 내 아군 쉴드)
    MeltingCorpse = 23,         // 녹아내리는 시체 (폭발 후 장판 생성)
    MutualDestruction = 24,     // 동귀어진 (폭발 전 1.5배 반경 내 적 끌어당김)
    AmIExplodingToo = 25,       // 나도 폭발하는걸까? (피격 적에게 약점 부여, 이후 데미지 20% 증가)
    // [추가] 노화 유니크 3종
    Goryeojang = 26,            // 고려장 (노화 최고스택 1명 주변 둔화 장판)
    DimVision = 27,             // 침침한 시야 (노화 50 이상 적 미스 확률 25% 증가)
    AgingHunter = 28,           // 노화 사냥꾼 (전체 노화 100스택 당 아군 공/이속 10% 증가)
    // [추가] 부식 유니크 4종
    PriestsCantAttack = 29,     // 사제는 공격을 할 수 없어! (부식 시너지 활성화 시 아군 치유량 20% 증가)
    DoubleCorrosion = 30,       // 부식 2배 (부식 시너지 효과 10% 증폭)
    WeaponCorrosion = 31,       // 무기 부식 (부식된 적 공격력 10% 감소)
    RustedArmor = 32,           // 녹슬어 버린 갑옷 (부식된 적 아군 평타 5회 피격 시 체력 5% 고정피해)
    // [추가] 전사 유니크 7종
    WarriorBallistics1 = 33,    // 전사 인형 탄도학 I (포물선 던지기 데미지 10% 증가)
    WarriorBallistics2 = 34,    // 전사 인형 탄도학 II (직구 던지기 데미지 10% 증가)
    WarriorBallistics3 = 35,    // 전사 인형 탄도학 III (던지기 단일 적용 효과 15% 증가)
    CrushingPower = 36,         // 짓눌리는 힘 (오버킬 시 초과 피해량만큼 전사 체력 회복)
    WarriorMedal = 111,         // 전사의 훈장
    WarriorPursuit = 112,       // 추적하는 눈
    WarriorFrenzy = 113,        // 광적인 분노

    // [방패병 전용 유니크 9종]
    ShieldSturdy = 120,         // 든든한 방패
    ShieldWillCourage = 121,    // 방패의 의지 - 용기
    ShieldWillWind = 122,       // 방패의 의지 - 바람
    ShieldWillClash = 123,      // 방패의 의지 - 격돌
    ShieldThornArmor = 124,     // 가시 갑옷
    ShieldHeavyArmor = 125,     // 육중한 갑옷
    ShieldPatienceAura = 126,   // 인내의 오오라
    ShieldOverwhelm = 127,      // 압도
    ShieldTwistedGround = 128,  // 뒤틀리는 지반
    
    // [추가] 궁수 유니크 8종
    ArcherTerrain = 40,         // 선관지형 / 사냥꾼의 몰이법 (범위 내 마리당 던지기 효과 5% 증가)
    ArcherWind = 41,            // 후관풍세 / 확산사격 (던지기 범위 20% 증가)
    ArcherStance = 42,          // 비정비팔 / 정조준 일격 (중앙 적 20% 추가 피해)
    ArcherBreath = 43,          // 흉어복실 / 바람의 방향 (비행 거리에 비례하여 1~33% 던지기 효과 증가)
    ArcherPush = 44,            // 전추태산 / 지원사격 (던지기 광역 피해 15% 증가)
    ArcherTension = 45,         // 발여호미 / 장력의 힘 (기본 공격 빈도 25% 느려짐, 공격력 25% 증가)
    ArcherMiss = 46,            // 발이부중 (반구저기 필요. 공격 빗나감 시 다음 공격 100% 증가)
    ArcherReflect = 47          // 반구저기 (발이부중 필요. 체력 50% 미만 시 공격력 15% 증가, 50% 이상 시 공격속도 15% 증가)
}

public enum SynergyCategory
{
    Common,
    Priest,
    Archer,
    Warrior,
    Shieldbearer,
    Spearman
}

public enum GemSynergyGroup
{
    // [공용 시너지]
    Base = 0,
    Poison = 1,
    BloodPop = 2,
    Execution = 3,

    // [사제 전용 시너지]
    Priest_Chill = 100,
    Priest_Aging = 101,
    Priest_Corrosion = 102,

    // [궁수 전용 시너지]
    Archer_ArcheryPrinciples = 202,

    // [전사 전용 시너지]
    Warrior_Executioner = 300,

    // [방패병 전용 시너지]
    Shield_Guardian = 400
}

/// <summary>
/// 보석의 최상위 클래스입니다. 실제 유형별 효과 리스트를 통해 다양한 기능을 수행합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewGem", menuName = "Necromancer/Growth/Gem - Unified")]
public class GemSO : GrowthItemSO
{
    [Header("시너지 분류")]
    public SynergyCategory category = SynergyCategory.Common;
    public GemSynergyGroup synergyGroup = GemSynergyGroup.Base;

    [Header("트리 구조 설정")]
    [Tooltip("이 보석이 트리에서 제공하는 하위 슬롯 개수입니다.")]
    public int subSlots = 1;

    [Header("직업 타겟팅")]
    [Tooltip("이 보석을 장착할 수 있는 직업군을 선택하세요.")]
    public MinionJobFlags eligibleJobs = MinionJobFlags.All;

    [Header("보석 효과 목록")]
    [SerializeReference]
    public List<GemEffect> effects = new List<GemEffect>();

    public bool IsEligible(CommandData job)
    {
        if (eligibleJobs == MinionJobFlags.None) return false;
        if (eligibleJobs == MinionJobFlags.All) return true;
        int jobBit = 1 << (int)job;
        return ((int)eligibleJobs & jobBit) != 0;
    }

    public static Color GetSynergyColor(GemSynergyGroup group)
    {
        switch (group)
        {
            case GemSynergyGroup.Poison: return new Color(0.2f, 0.8f, 0.2f); // Lime
            case GemSynergyGroup.Priest_Chill: return new Color(0.3f, 0.6f, 1.0f); // SkyBlue
            case GemSynergyGroup.Execution: return new Color(1.0f, 0.3f, 0.1f); // Orange
            case GemSynergyGroup.BloodPop: return new Color(1.0f, 0.0f, 1.0f); // Magenta
            case GemSynergyGroup.Priest_Aging: return new Color(0.7f, 0.5f, 0.5f); // Brown
            case GemSynergyGroup.Priest_Corrosion: return new Color(1.0f, 0.8f, 0.0f); // Gold
            case GemSynergyGroup.Warrior_Executioner: return new Color(0.8f, 0.1f, 0.1f); // DarkRed
            case GemSynergyGroup.Archer_ArcheryPrinciples: return new Color(0.1f, 0.8f, 0.5f); // SeaGreen
            case GemSynergyGroup.Shield_Guardian: return new Color(0.6f, 0.6f, 0.8f); // LightPurple
            default: return Color.white;
        }
    }

    public GrowthItemData GetDynamicDisplayData(CommandData job)
    {
        // [수정] 실제 직업 이름 대신 보석의 시너지 그룹을 표시합니다.
        string groupName = synergyGroup.ToString().Replace("_", " ");
        string finalDesc = string.IsNullOrEmpty(description) ? "" : description;
        
        if (effects != null && effects.Count > 0)
        {
            finalDesc += "\n<color=yellow>";
            foreach (var effect in effects)
            {
                if (effect != null)
                {
                    string desc = effect.GetDescription();
                    if (!string.IsNullOrEmpty(desc)) finalDesc += $"\n- {desc}";
                }
            }
            finalDesc += "</color>";
        }

        return new GrowthItemData
        {
            itemName = $"{groupName} Gem",
            description = finalDesc,
            icon = icon,
            rarity = rarity
        };
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        string groupStr = synergyGroup.ToString();
        if (groupStr.StartsWith("Priest_")) { category = SynergyCategory.Priest; eligibleJobs = MinionJobFlags.Priest; }
        else if (groupStr.StartsWith("Archer_")) { category = SynergyCategory.Archer; eligibleJobs = MinionJobFlags.Archer; }
        else if (groupStr.StartsWith("Warrior_")) { category = SynergyCategory.Warrior; eligibleJobs = MinionJobFlags.Warrior; }
        else if (groupStr.StartsWith("Shield_")) { category = SynergyCategory.Shieldbearer; eligibleJobs = MinionJobFlags.Shieldbearer; }
        else if (groupStr.StartsWith("Spear_")) { category = SynergyCategory.Spearman; eligibleJobs = MinionJobFlags.Spearman; }
        else { category = SynergyCategory.Common; eligibleJobs = MinionJobFlags.All; }
    }
#endif

    /// <summary>
    /// 하위 호환성을 위해 StatModifier 목록을 반환합니다. (기존 시스템 대응용)
    /// </summary>
    public List<StatModifier> GetStatModifiers()
    {
        List<StatModifier> modifiers = new List<StatModifier>();
        foreach (var effect in effects)
        {
            if (effect is GemStatEffect statEffect)
            {
                modifiers.Add(new StatModifier(statEffect.statType, statEffect.value));
            }
        }
        return modifiers;
    }
}
