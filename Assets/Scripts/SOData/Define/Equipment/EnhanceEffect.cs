using UnityEngine;

/// <summary>
/// 플레이어 스킬이 '장비 강화레벨'에 따라 어떻게 세지는지 한 조각.
/// PlayerSkillSO.enhanceEffects 에 [SerializeReference] 로 담기므로 종류를 자유롭게 늘릴 수 있다 —
/// "강화 시 늘어날 변수가 더 생길 수도 있다"는 요구를 이 구조가 흡수한다(필드 안 늘리고 이펙트 타입만 추가).
///
/// 지금 넣는 건 데미지·타수 둘. 수치/커브는 미정이라 기본값은 전부 '변화 없음'.
/// 스킬별로 다르게 잡으려면 각 PlayerSkillSO 에셋에서 이 리스트를 다르게 채우면 된다.
/// </summary>
[System.Serializable]
public abstract class EnhanceEffect
{
    /// <summary>데미지 배수 기여. 없으면 1(변화 없음).</summary>
    public virtual float DamageMultiplier(int enhanceLevel) => 1f;

    /// <summary>추가 타수 기여. 없으면 0.</summary>
    public virtual int BonusHitCount(int enhanceLevel) => 0;
}

/// <summary>레벨당 데미지 +perLevel(합연산). 예: perLevel 0.1, 레벨 3 → x1.3.</summary>
[System.Serializable]
public class DamageEnhanceEffect : EnhanceEffect
{
    [Tooltip("강화 레벨당 데미지 증가율(합연산). 0.1 = 레벨당 +10%.")]
    public float perLevel = 0f;

    public override float DamageMultiplier(int enhanceLevel)
        => 1f + perLevel * Mathf.Max(0, enhanceLevel);
}

/// <summary>레벨 threshold 마다 타수 +hitsPerStep. 타수를 손볼 몇몇 스킬 전용.</summary>
[System.Serializable]
public class HitCountEnhanceEffect : EnhanceEffect
{
    [Tooltip("몇 레벨마다 타수를 늘릴지. 예: 2 면 2·4·6강에서 한 단계씩.")]
    public int levelsPerHit = 1;

    [Tooltip("한 단계에 몇 대 늘릴지.")]
    public int hitsPerStep = 1;

    public override int BonusHitCount(int enhanceLevel)
        => levelsPerHit <= 0 ? 0 : (Mathf.Max(0, enhanceLevel) / levelsPerHit) * hitsPerStep;
}
