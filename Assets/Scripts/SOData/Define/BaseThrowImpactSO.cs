using UnityEngine;

/// <summary>
/// 투척된 물체가 충돌했을 때 발생하는 효과를 정의하는 ScriptableObject 기반 클래스입니다.
/// </summary>
public abstract class BaseThrowImpactSO : ScriptableObject
{
    /// <summary>
    /// 충돌 시 실행될 로직입니다.
    /// </summary>
    /// <param name="self">던져진 유닛 자신</param>
    /// <param name="target">충돌한 대상 (적 등)</param>
    /// <param name="chargeRatio">차징 정도 (0~1)</param>
    public abstract void Apply(GameObject self, GameObject target, float chargeRatio);
}
