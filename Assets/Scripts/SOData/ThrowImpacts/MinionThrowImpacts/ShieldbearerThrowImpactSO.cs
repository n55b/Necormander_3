using UnityEngine;

[CreateAssetMenu(fileName = "ShieldbearerThrowImpact", menuName = "Necromancer/Throw/Basic Impact/Shieldbearer")]
public class ShieldbearerThrowImpactSO : BaseThrowImpactSO
{
    [Header("여진 설정")]
    [SerializeField] private GameObject quakePrefab; // ShieldbearerThrowEffect 컴포넌트가 붙은 프리팹
    [SerializeField] private float damage = 5f;
    [SerializeField] private float duration = 1.5f;
    [SerializeField] private float radius = 1.5f;
    [SerializeField] private float slowAmount = 0.5f;
    [SerializeField] private LayerMask enemyLayer;

    public override void Apply(ImpactContext context)
    {
        if (quakePrefab == null)
        {
            Debug.LogWarning("ShieldbearerThrowImpactSO: 효과 프리팹이 할당되지 않았습니다!");
            return;
        }

        // 효과 소환
        GameObject obj = Instantiate(quakePrefab, context.impactPosition, Quaternion.identity);
        if (obj.TryGetComponent<ShieldbearerThrowEffect>(out var effect))
        {
            effect.Init(damage, duration, slowAmount, radius, enemyLayer, context.attacker);
        }

        Debug.Log($"<color=white>[Shieldbearer Impact]</color> {context.impactPosition} 위치에 전용 효과 생성");
    }
}
