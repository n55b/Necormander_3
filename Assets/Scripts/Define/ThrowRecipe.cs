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
    public float abilityMultiplier = 1.0f; // [추가] 저글링 등 특수 능력 배율
    public int treasureRepeatBonus = 0;
    public int bounceCount = 0; // [추가] 튕기기 능력용 현재 튕긴 횟수
    public bool isBouncing = false; // [추가] 현재 튕기기 중인지 여부
    
    // [관통 및 본체 관리]
    public int pierceCount = 0;    // 현재 관통한 횟수
    public int maxPierce = 0;      // 최대 관통 가능 횟수
    public bool isMaster = true;   // 유닛의 생명주기를 관리하는 메인 투척물인지 여부
    
    public List<GameObject> hitTargets = new List<GameObject>(); // [추가] 튕기기 중복 타격 방지용 리스트
    public List<IThrowable> heldUnits = new List<IThrowable>(); // [추가] 투척에 포함된 유닛 리스트

    public List<ImpactAction> actions = new List<ImpactAction>();

    /// <summary>
    /// 효과의 최종 위력 수치를 계산합니다.
    /// </summary>
    public float GetScaledValue(float baseValue)
    {
        if (baseValue <= 0) return 0;
        return baseValue * modeMultiplier * chargeMultiplier * treasurePowerMultiplier * abilityMultiplier;
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
