using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum ThrowTrajectoryFilter
{
    Parabolic = 1 << 0,
    Straight = 1 << 1
}

[System.Flags]
public enum ThrowTargetFilter
{
    Single = 1 << 0,
    Area = 1 << 1,
    Self = 1 << 2
}

/// <summary>
/// 던지기 능력의 베이스 클래스입니다. 
/// 전략 패턴(Strategy Pattern)을 사용하여 각 능력이 투척의 특정 단계에 개입할 수 있도록 합니다.
/// </summary>
public abstract class ThrowAbilitySO : GrowthItemSO
{
    [Header("Ability Filter Settings")]
    public ThrowTrajectoryFilter trajectoryFilter = (ThrowTrajectoryFilter)~0;
    public ThrowTargetFilter targetFilter = (ThrowTargetFilter)~0;

    /// <summary>
    /// 현재 투척 상황이 이 능력의 적용 조건에 부합하는지 확인합니다.
    /// </summary>
    public bool IsApplicable(bool isDirect, TargetingMode mode)
    {
        bool trajMatch = false;
        if (isDirect)
        {
            if ((trajectoryFilter & ThrowTrajectoryFilter.Straight) != 0) trajMatch = true;
        }
        else
        {
            if ((trajectoryFilter & ThrowTrajectoryFilter.Parabolic) != 0) trajMatch = true;
        }

        bool targetMatch = false;
        switch (mode)
        {
            case TargetingMode.Target:
                if ((targetFilter & ThrowTargetFilter.Single) != 0) targetMatch = true;
                break;
            case TargetingMode.Area:
                if ((targetFilter & ThrowTargetFilter.Area) != 0) targetMatch = true;
                break;
            case TargetingMode.Self:
                if ((targetFilter & ThrowTargetFilter.Self) != 0) targetMatch = true;
                break;
        }

        return trajMatch && targetMatch;
    }

    /// <summary>
    /// 던지기 레시피 생성 단계에 개입합니다. (수치 변환, 반복 횟수 추가 등)
    /// </summary>
    public virtual void ModifyRecipe(ThrowRecipe recipe, List<IThrowable> heldObjects) { }
    /// <summary>
    /// 유닛을 집는(PickUp) 단계에 개입합니다. (소모 방지 로직 등)
    /// </summary>
    /// <returns>True를 반환하면 일반적인 집기 로직을 건너뜜니다.</returns>
    public virtual bool OnTryPickUp(IThrowable target, List<IThrowable> heldList) { return false; }

    /// <summary>
    /// 차징 단계에서 매 프레임 호출됩니다.
    /// </summary>
    public virtual void OnChargeUpdate(float currentRatio, float deltaTime) { }

    /// <summary>
    /// 투척이 시작되는 시점에 호출됩니다. 
    /// 비주얼 잔상을 생성하거나 추가적인 물리 효과를 부여할 때 사용합니다.
    /// </summary>
    public virtual void OnThrowLaunch(ThrowController controller, ThrowRecipe recipe, Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float ratio) { }

    /// <summary>
    /// 투척물이 지면에 닿거나 타겟에 적중했을 때 호출됩니다.
    /// </summary>
    public virtual void OnImpact(ThrowRecipe recipe, Vector2 impactPoint, ThrowCluster cluster) { }
}

