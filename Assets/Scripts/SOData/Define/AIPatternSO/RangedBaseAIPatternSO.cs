using UnityEngine;

/// <summary>
/// 원거리 계열 AI 패턴들의 공통 기본 클래스입니다.
/// 가시선 검사 시 Unsteppable 레이어를 무시하는 원거리 공통 성격을 보장합니다.
/// </summary>
public abstract class RangedBaseAIPatternSO : BaseAIPatternSO
{
    protected override bool IgnoreUnsteppableForLOS => true;
}
