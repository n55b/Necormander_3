using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [관통] 능력을 구현하는 클래스입니다.
/// 직구(풀차징) 투척 시 적을 최대 2회 관통하며, 총 3회의 충격 효과를 발생시킵니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowPierceAbility", menuName = "Necromancer/Growth/Throw Ability/Pierce")]
public class ThrowPierceAbilitySO : ThrowAbilitySO
{
    [Header("Pierce Settings")]
    [Tooltip("관통할 횟수입니다. (2회 관통 시 총 3회 적중)")]
    public int pierceCount = 2;

    public ThrowPierceAbilitySO()
    {
        // 필터 설정: 직구(Straight) 전용, 단일 및 범위 타겟팅 모드에서 활성화
        trajectoryFilter = ThrowTrajectoryFilter.Straight;
        targetFilter = ThrowTargetFilter.Single | ThrowTargetFilter.Area;
    }

    public override void ModifyRecipe(ThrowRecipe recipe, List<IThrowable> heldObjects)
    {
        // 레시피의 최대 관통 횟수를 설정합니다.
        recipe.state.maxPierce = pierceCount;
        
        Debug.Log($"<color=cyan>[Ability: Pierce]</color> MaxPierce set to {pierceCount}");
    }
}
