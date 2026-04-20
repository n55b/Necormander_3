using UnityEngine;

[CreateAssetMenu(fileName = "SpearmanThrowImpact", menuName = "ThrowImpact/Spearman")]
public class SpearmanThrowImpactSO : BaseThrowImpactSO
{
    [Header("가속 설정")]
    [SerializeField] private float damage = 25f; // 전사보다 조금 더 높은 데미지
    [SerializeField] private float minMultiplier = 1.5f;
    [SerializeField] private float maxMultiplier = 2.0f;

    public override void OnPreThrow(ThrowParams p, float chargeRatio)
    {
        // 1. 차징에 따른 배율 계산 (0 -> 1.5, 1 -> 2.0)
        float speedMultiplier = Mathf.Lerp(minMultiplier, maxMultiplier, chargeRatio);
        
        // 2. 물리 수치 수정
        float originalSpeed = p.speed;
        p.speed *= speedMultiplier;

        // 3. 비행 시간(Duration) 재계산 (거리 = 속도 * 시간)
        // 속도가 빨라진 만큼 도달 시간은 짧아져야 함 (t = d / v)
        float distance = originalSpeed * p.duration;
        p.duration = distance / p.speed;

        Debug.Log($"<color=cyan>[Spearman Pre-Throw]</color> 가속 적용: x{speedMultiplier:F2} (속도: {p.speed:F1})");
    }

    public override void Apply(ImpactContext context)
    {
        // 전사와 동일한 로직: 직접 충돌한 대상에게 피해를 입힘
        if (context.target != null && context.target.TryGetComponent<CharacterStat>(out var stat))
        {
            stat.GetDamage(new DamageInfo(damage, DamageType.Physical, context.attacker));
        }

        Debug.Log($"<color=cyan>[Spearman Impact]</color> 가속 투척 명중!");
    }
}
