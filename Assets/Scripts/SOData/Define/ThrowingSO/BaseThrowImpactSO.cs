using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 투척 시 발생하는 모든 데이터를 담는 컨텍스트입니다.
/// </summary>
public struct ImpactContext
{
    public GameObject attacker;      // 던져진 유닛 자신 (또는 시너지 주체)
    public GameObject target;        // 직접 충돌한 대상 (적/벽 등)
    public Vector2 impactPosition;   // 충돌/착지 지점
    public float chargeRatio;        // 플레이어의 차징 비율 (0~1)
    public List<AllyController> supporters; // 시너지 강화에 참여한 나머지 미니언들
}

/// <summary>
/// 투척 물리 수치를 동적으로 수정하기 위한 파라미터입니다.
/// </summary>
public class ThrowParams
{
    public float speed;
    public float maxHeight;
    public float duration;
}

public abstract class BaseThrowImpactSO : ScriptableObject
{
    /// <summary>
    /// 투척 직전 호출됩니다. (예: 창병의 투척 속도 증가 등)
    /// </summary>
    public virtual void OnPreThrow(ThrowParams p, float chargeRatio) { }

    /// <summary>
    /// 충돌 또는 착지 시 호출됩니다. (데미지, 장판 생성, 분열 등 모든 효과)
    /// </summary>
    public abstract void Apply(ImpactContext context);
}
