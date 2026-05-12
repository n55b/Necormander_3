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

/// <summary>
/// 보석의 최상위 클래스입니다. 이제 다형성 효과 리스트를 통해 다양한 기능을 수행합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewGem", menuName = "Necromancer/Growth/Gem - Unified")]
public class GemSO : GrowthItemSO
{
    [Header("트리 구조 설정")]
    [Tooltip("이 보석이 트리에서 제공하는 하위 슬롯 개수입니다.")]
    public int subSlots = 1;

    [Header("직업 타겟팅")]
    [Tooltip("이 보석을 장착할 수 있는 직업들을 선택하세요.")]
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

    public GrowthItemData GetDynamicDisplayData(CommandData job)
    {
        string jobName = job.ToString().Replace("Skeleton", "");
        string finalDesc = description;
        
        if (effects.Count > 0)
        {
            finalDesc += "\n<color=yellow>";
            foreach (var effect in effects)
            {
                if (effect != null) finalDesc += $"\n- {effect.GetDescription()}";
            }
            finalDesc += "</color>";
        }

        return new GrowthItemData {
            itemName = $"[{jobName}] {itemName}",
            description = finalDesc,
            icon = this.icon,
            rarity = this.rarity
        };
    }

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
