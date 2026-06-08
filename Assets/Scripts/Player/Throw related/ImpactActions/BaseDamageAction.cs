using UnityEngine;

/// <summary>
/// 모든 공격성 투척에 공통으로 들어가는 플레이어 기본 데미지 처리 액션입니다.
/// </summary>
public class BaseDamageAction : ImpactAction
{
    public override void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe)
    {
        BaseEntity entity = target.GetComponentInChildren<BaseEntity>();
        
        // 아군에게는 기본 데미지를 주지 않음 (방패병이 아군 타겟팅 시 데미지 면제)
        if (entity != null && entity.team == Team.Enemy)
        {
            float finalDamage = recipe.GetFinalDamage();
            // 참고: WarriorAction 등에서 추가 데미지가 있다면 recipe.bonusDamage에 누적되어 여기서 합산(GetFinalDamage)됩니다.

            // 데미지가 0보다 클 때만 데미지 적용
            if (finalDamage > 0)
            {
                entity.Stats.Health.GetDamage(new DamageInfo(finalDamage, DamageType.Physical, null, true));
            }
        }
    }
}
