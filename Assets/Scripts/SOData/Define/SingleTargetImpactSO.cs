using UnityEngine;

[CreateAssetMenu(fileName = "SingleTargetImpact", menuName = "ThrowImpact/Single")]
public class SingleTargetImpactSO : BaseThrowImpactSO
{
    [Header("데미지 설정")]
    [SerializeField] private float baseDamage = 20f;
    [SerializeField] private DamageType damageType = DamageType.Physical;
    [SerializeField] private float maxChargeBonus = 2.0f;

    public override void Apply(GameObject self, GameObject target, float chargeRatio)
    {
        if (target.TryGetComponent<CharacterStat>(out var targetStat))
        {
            float finalDamage = baseDamage * (1f + (chargeRatio * (maxChargeBonus - 1f)));

            DamageInfo info = new DamageInfo(finalDamage, damageType, self);
            targetStat.GetDamage(info);

            Debug.Log($"<color=cyan>[Impact-Single]</color> {self.name} dealt {finalDamage:F1} {damageType} damage to {target.name}");
        }
    }
}
