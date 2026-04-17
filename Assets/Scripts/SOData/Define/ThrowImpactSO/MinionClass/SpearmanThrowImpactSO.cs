using UnityEngine;

[CreateAssetMenu(fileName = "SpearmanThrowImpact", menuName = "ThrowImpact/Spearman")]
public class SpearmanThrowImpactSO : BaseThrowImpactSO
{
    [SerializeField] private float speedMultiplier = 1.5f;

    public override void OnPreThrow(ThrowParams p, float chargeRatio)
    {
        // 창병: 날아가는 속도 증가
        p.speed *= speedMultiplier;
        Debug.Log($"<color=cyan>[Spearman]</color> 투척 속도 증가: {p.speed}");
    }

    public override void Apply(ImpactContext context)
    {
        // 기본 충격 효과
    }
}
