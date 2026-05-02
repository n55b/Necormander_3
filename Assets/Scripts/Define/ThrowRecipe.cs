using System.Collections.Generic;
using UnityEngine;

public enum TargetingMode { Self, Target, Area }

/// <summary>
/// 충격 시 발생하는 개별 효과의 유형입니다.
/// </summary>
public enum ImpactActionType
{
    Damage,    // 데미지 (전사, 궁수)
    CC,        // 상태이상/슬로우 (사제)
    Shield,    // 보호막 (방패병)
    Knockback, // 넉백/진형파괴 (창병)
    Area,      // 범위 설정 (궁수 - 반지름)
    Repeat     // 반복 실행 (마법사)
}

/// <summary>
/// 투척 충격 시 실행될 개별 액션 데이터입니다.
/// </summary>
[System.Serializable]
public class ImpactAction
{
    public ImpactActionType type;
    public float baseValue;

    public ImpactAction(ImpactActionType type, float baseValue)
    {
        this.type = type;
        this.baseValue = baseValue;
    }
}

/// <summary>
/// 투척 시 미니언들의 조합을 분석한 결과물(순수 데이터)입니다.
/// </summary>
public class ThrowRecipe
{
    // --- 타겟팅 및 상태 데이터 ---
    public TargetingMode targetingMode = TargetingMode.Self;
    public Team targetTeam = Team.Enemy;
    public GameObject finalTarget; 
    public Vector2 impactPoint;    
    public float chargeRatio;
    public bool isImmediateApplied = false;

    // --- 배율 데이터 ---
    public float modeMultiplier = 1.0f;
    public float chargeMultiplier = 1.0f;
    public float treasurePowerMultiplier = 1.0f; // 기본 1.0 (1.0 + treasurePowerBonus)
    public int treasureRepeatBonus = 0;

    // --- 액션 리스트 ---
    public List<ImpactAction> actions = new List<ImpactAction>();

    // --- 수치 계산 유틸리티 (데이터로부터 파생) ---

    public float GetScaledValue(float baseValue)
    {
        if (baseValue <= 0) return 0;
        // [공식] 기저치 * 모드배율 * 차징배율 * 보물배율
        return baseValue * modeMultiplier * chargeMultiplier * treasurePowerMultiplier;
    }

    public float GetBaseAreaRadius()
    {
        var areaAction = actions.Find(a => a.type == ImpactActionType.Area);
        return areaAction != null ? areaAction.baseValue : 3.0f;
    }

    public float GetScaledRadius()
    {
        float rangeMultiplier = 1.0f; // 추후 보물 등으로 확장 가능
        return GetBaseAreaRadius() * rangeMultiplier;
    }

    public int GetTotalExecutionCount()
    {
        var repeatAction = actions.Find(a => a.type == ImpactActionType.Repeat);
        int baseRepeat = repeatAction != null ? Mathf.FloorToInt(repeatAction.baseValue) : 0;
        return 1 + baseRepeat + treasureRepeatBonus;
    }

    // --- 헬퍼 메서드 ---
    public void AddAction(ImpactActionType type, float value)
    {
        var existing = actions.Find(a => a.type == type);
        if (existing != null)
        {
            // 중첩 가능한 효과(데미지, 보호막 등)는 합산, 설정형(범위 등)은 덮어쓰거나 최대값 선택
            if (type == ImpactActionType.Area) existing.baseValue = Mathf.Max(existing.baseValue, value);
            else existing.baseValue += value;
        }
        else
        {
            actions.Add(new ImpactAction(type, value));
        }
    }

    public bool HasAction(ImpactActionType type) => actions.Exists(a => a.type == type);
    
    public float GetActionValue(ImpactActionType type)
    {
        var action = actions.Find(a => a.type == type);
        return action != null ? action.baseValue : 0f;
    }
}
