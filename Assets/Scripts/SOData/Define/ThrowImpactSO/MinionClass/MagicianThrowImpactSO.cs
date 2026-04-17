using UnityEngine;

[CreateAssetMenu(fileName = "MagicianThrowImpact", menuName = "ThrowImpact/Magician")]
public class MagicianThrowImpactSO : BaseThrowImpactSO
{
    public override void Apply(ImpactContext context)
    {
        // 마법사: 보호막 깨트리는 공격 (나중에 보호막 시스템 추가 시 구현)
        // if(context.target.HasShield) { BreakShield(); }
        Debug.Log("<color=blue>[Magician]</color> 마법 충격 발생 (보호막 파괴 예정)");
    }
}
