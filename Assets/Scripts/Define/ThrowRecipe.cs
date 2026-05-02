using System.Collections.Generic;
using UnityEngine;

public enum TargetingMode { Self, Target, Area }

/// <summary>
/// 투척 시 미니언들의 조합을 분석한 결과물(순수 데이터 및 액션 묶음)입니다.
/// </summary>
public class ThrowRecipe
{
    public TargetingMode targetingMode = TargetingMode.Self;
    public Team targetTeam = Team.Enemy;
    public GameObject finalTarget; 
    public Vector2 impactPoint;    
    public float chargeRatio;
    public bool isImmediateApplied = false;

    public float modeMultiplier = 1.0f;
    public float chargeMultiplier = 1.0f;
    public float treasurePowerMultiplier = 1.0f; 
    public int treasureRepeatBonus = 0;

    public List<ImpactAction> actions = new List<ImpactAction>();

    /// <summary>
    /// 효과의 최종 위력 수치를 계산합니다.
    /// </summary>
    public float GetScaledValue(float baseValue)
    {
        if (baseValue <= 0) return 0;
        return baseValue * modeMultiplier * chargeMultiplier * treasurePowerMultiplier;
    }

    /// <summary>
    /// 광역 모드일 때의 최종 범위를 계산합니다.
    /// </summary>
    public float GetScaledRadius()
    {
        float radius = 3.0f;
        foreach (var a in actions)
        {
            if (a is ArcherAction archer) radius = archer.radius;
        }
        return radius;
    }

    /// <summary>
    /// 효과를 총 몇 번 실행할지 결정합니다.
    /// </summary>
    public int GetTotalExecutionCount()
    {
        int bonus = 0;
        foreach (var a in actions)
        {
            if (a is MagicianAction magi) bonus += magi.repeatCount;
        }
        return 1 + bonus + treasureRepeatBonus;
    }

    /// <summary>
    /// 특정 타입의 액션이 포함되어 있는지 확인합니다.
    /// </summary>
    public bool HasAction<T>() where T : ImpactAction
    {
        return actions.Exists(a => a is T);
    }
}
