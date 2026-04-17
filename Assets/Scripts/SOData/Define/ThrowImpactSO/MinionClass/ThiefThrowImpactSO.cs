using UnityEngine;

[CreateAssetMenu(fileName = "ThiefThrowImpact", menuName = "ThrowImpact/Thief")]
public class ThiefThrowImpactSO : BaseThrowImpactSO
{
    public override void Apply(ImpactContext context)
    {
        // 도적: 착지 시 2명으로 분열 (능력치 50%)
        Debug.Log("<color=purple>[Thief]</color> 도적 분열 실행!");
    }
}
