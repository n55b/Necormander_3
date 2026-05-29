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
    LethalDose = 2,             // 독의 치사량 (틱 주기 단축)
    AchingBones = 3,            // 시리고 아린 뼈 (동결 중 스택 방지 등)
    SlowlyFreezingFlower = 4,   // 서서히 얼어붙는 꽃 (한기 최대치 증가)
    ExplodingFlesh = 5,         // 살덩이가 폭발하는 것 (비폭 전이)
    NoCountryForOldMen = 6      // 노인을 위한 나라는 없다 (노화 즉사)
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
    Archer_Bullseye = 200,
    Archer_CarpetBomb = 201,

    // [전사 전용 시너지]
    Warrior_FatalStrike = 300,
    Warrior_Duel = 301,

    // [방패병 전용 시너지]
    Shield_Vitality = 400,
    Shield_Frenzy = 401,

    // [창병 전용 시너지]
    Spear_Sprint = 500,
    Spear_Collision = 501
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
