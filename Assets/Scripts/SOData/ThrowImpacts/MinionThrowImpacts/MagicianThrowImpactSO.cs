using UnityEngine;

[CreateAssetMenu(fileName = "MagicianThrowImpact", menuName = "Necromancer/Throw/Basic Impact/Magician")]
public class MagicianThrowImpactSO : BaseThrowImpactSO
{
    [Header("마법 공격 설정")]
    [SerializeField] private float damage = 22f; // 전사보다 약간 높은 기본 피해

    public override void Apply(ImpactContext context)
    {
        if (context.target != null && context.target.TryGetComponent<CharacterStat>(out var stat))
        {
            // --- [향후 구현 예정: 보호막 파괴 기믹] ---
            // 1. 엘리트 몹의 보호막 레이어/컴포넌트 확인
            // 2. 보호막이 존재한다면 1스택 제거 (또는 즉시 파괴)
            // 3. 보호막 파괴 시 특수한 시각 효과 및 사운드 재생
            // ------------------------------------------

            // 현재는 기본 전사와 동일하게 데미지만 입힘
            stat.GetDamage(new DamageInfo(damage, DamageType.Physical, context.attacker));
        }

        Debug.Log($"<color=magenta>[Magician Impact]</color> 마법 투척 명중! (보호막 파괴 기믹 대기 중)");
    }
}
