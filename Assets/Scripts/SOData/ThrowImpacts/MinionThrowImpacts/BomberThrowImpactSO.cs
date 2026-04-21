using UnityEngine;

[CreateAssetMenu(fileName = "BomberThrowImpact", menuName = "Necromancer/Throw/Basic Impact/Bomber")]
public class BomberThrowImpactSO : BaseThrowImpactSO
{
    [Header("폭발 설정")]
    [SerializeField] private GameObject explosionPrefab; // BomberThrowEffect 컴포넌트가 붙은 프리팹
    [SerializeField] private float damage = 40f; // 강력한 데미지
    [SerializeField] private float radius = 2.0f; // 방패병과 비슷한 범위
    [SerializeField] private LayerMask enemyLayer;

    public override void Apply(ImpactContext context)
    {
        if (explosionPrefab == null)
        {
            Debug.LogWarning("BomberThrowImpactSO: 폭발 프리팹이 할당되지 않았습니다!");
            return;
        }

        // 효과 소환 (Z축을 강제로 고정하지 않고 context의 위치를 그대로 사용)
        GameObject obj = Instantiate(explosionPrefab, context.impactPosition, Quaternion.identity);

        if (obj.TryGetComponent<BomberThrowEffect>(out var effect))
        {
            effect.Init(damage, radius, enemyLayer, context.attacker);
        }

        Debug.Log($"<color=red>[Bomber Impact]</color> {context.impactPosition} 위치에서 강력한 폭발 발생!");
    }
}
