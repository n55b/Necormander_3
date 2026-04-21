using UnityEngine;

[CreateAssetMenu(fileName = "PriestThrowImpact", menuName = "Necromancer/Throw/Basic Impact/Priest")]
public class PriestThrowImpactSO : BaseThrowImpactSO
{
    [Header("회복 설정")]
    [SerializeField] private GameObject healPrefab; // PriestThrowEffect 컴포넌트가 붙은 프리팹
    [SerializeField] private float healAmount = 15f;
    [SerializeField] private float radius = 2.0f;
    [SerializeField] private LayerMask allyLayer;

    public override void Apply(ImpactContext context)
    {
        if (healPrefab == null)
        {
            Debug.LogWarning("PriestThrowImpactSO: 효과 프리팹이 할당되지 않았습니다!");
            return;
        }

        // 효과 소환
        GameObject obj = Instantiate(healPrefab, context.impactPosition, Quaternion.identity);
        if (obj.TryGetComponent<PriestThrowEffect>(out var effect))
        {
            effect.Init(healAmount, radius, allyLayer);
        }

        Debug.Log($"<color=green>[Priest Impact]</color> {context.impactPosition} 위치에 회복 광환 생성");
    }
}
