using UnityEngine;

[CreateAssetMenu(fileName = "WarriorThrowImpact", menuName = "Necromancer/Throw/Basic Impact/Warrior")]
public class WarriorThrowImpactSO : BaseThrowImpactSO
{
    [SerializeField] private float damage = 20f;
    public override void Apply(ImpactContext context)
    {
        if (context.target != null && context.target.TryGetComponent<CharacterStat>(out var stat))
        {
            stat.GetDamage(new DamageInfo(damage, DamageType.Physical, context.attacker));
        }
    }
}
