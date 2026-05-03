using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 던지기 능력의 베이스 클래스입니다. 
/// 전략 패턴(Strategy Pattern)을 사용하여 각 능력이 투척의 특정 단계에 개입할 수 있도록 합니다.
/// </summary>
public abstract class ThrowAbilitySO : GrowthItemSO
{
    [Header("Ability Info")]
    public ThrowAbilityType abilityType;

    /// <summary>
    /// 던지기 레시피 생성 단계에 개입합니다. (수치 변환, 반복 횟수 추가 등)
    /// </summary>
    public virtual void ModifyRecipe(ThrowRecipe recipe, List<IThrowable> heldObjects) { }

    /// <summary>
    /// 유닛을 집는(PickUp) 단계에 개입합니다. (소모 방지 로직 등)
    /// </summary>
    /// <returns>True를 반환하면 일반적인 집기 로직을 건너뜁니다.</returns>
    public virtual bool OnTryPickUp(IThrowable target, List<IThrowable> heldList) { return false; }

    /// <summary>
    /// 차징 단계에서 매 프레임 호출됩니다.
    /// </summary>
    public virtual void OnChargeUpdate(float currentRatio, float deltaTime) { }

    /// <summary>
    /// 투척이 시작되는 시점에 호출됩니다.
    /// </summary>
    public virtual void OnThrowLaunch(ThrowRecipe recipe, ThrowCluster cluster) { }

    /// <summary>
    /// 투척물이 지면에 닿거나 타겟에 적중했을 때 호출됩니다.
    /// </summary>
    public virtual void OnImpact(ThrowRecipe recipe, Vector2 impactPoint) { }
}

public enum ThrowAbilityType
{
    Repeat,         // 반복: 추가 실행
    Phantom,        // 안날라가기: 미니언 소모 방지
    Pinball,        // 핀볼: 벽 반사
    TimingCharge,   // 타이밍 차지: 적기 발사 시 강화
    Juggling        // 저글링: 회수 및 콤보
}
