using UnityEngine;

[CreateAssetMenu(fileName = "ArcherThrowImpact", menuName = "Necromancer/Throw/Basic Impact/Archer")]
public class ArcherThrowImpactSO : BaseThrowImpactSO
{
    [Header("화살 포화 설정")]
    [SerializeField] private GameObject volleyPrefab; // ArcherThrowEffect 컴포넌트가 붙은 프리팹
    [SerializeField] private float damage = 10f;
    [SerializeField] private float radius = 4.0f;
    [SerializeField] private LayerMask enemyLayer;

    public override void Apply(ImpactContext context)
    {
        if (volleyPrefab == null)
        {
            Debug.LogWarning("ArcherThrowImpactSO: 효과 프리팹이 할당되지 않았습니다!");
            return;
        }

        // 효과 소환
        GameObject obj = Instantiate(volleyPrefab, context.impactPosition, Quaternion.identity);
        if (obj.TryGetComponent<ArcherThrowEffect>(out var effect))
        {
            effect.Init(damage, radius, enemyLayer, context.attacker);
        }

        Debug.Log($"<color=yellow>[Archer Impact]</color> {context.impactPosition} 위치에 화살 포화 생성");
    }
}
