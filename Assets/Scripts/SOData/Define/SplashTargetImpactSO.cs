using UnityEngine;

[CreateAssetMenu(fileName = "SplashTargetImpact", menuName = "ThrowImpact/Splash")]
public class SplashTargetImpactSO : BaseThrowImpactSO
{
    [Header("데미지 설정")]
    [SerializeField] private float baseDamage = 15f;
    [SerializeField] private DamageType damageType = DamageType.Physical;
    [SerializeField] private float maxChargeBonus = 1.5f;

    [Header("스플래시 설정")]
    [SerializeField] private float splashRadius = 2.5f;
    [SerializeField] private LayerMask enemyLayer;

    public override void Apply(GameObject self, GameObject target, float chargeRatio)
    {
        float finalDamage = baseDamage * (1f + (chargeRatio * (maxChargeBonus - 1f)));

        Vector2 impactPos = target.transform.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(impactPos, splashRadius, enemyLayer);

        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<CharacterStat>(out var targetStat))
            {
                DamageInfo info = new DamageInfo(finalDamage, damageType, self);
                targetStat.GetDamage(info);
            }
        }

        Debug.Log($"<color=orange>[Impact-Splash]</color> {self.name} dealt {finalDamage:F1} {damageType} splash damage!");
    }
}
