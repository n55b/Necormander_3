using UnityEngine;

[CreateAssetMenu(fileName = "BomberThrowImpact", menuName = "ThrowImpact/Bomber")]
public class BomberThrowImpactSO : BaseThrowImpactSO
{
    [SerializeField] private float damage = 40f;
    [SerializeField] private float radius = 2.0f;
    public override void Apply(ImpactContext context)
    {
        // 범위 내 적에게 큰 물리 피해
        Debug.Log("<color=orange>[Bomber]</color> 강력한 폭발 발생!");
    }
}
