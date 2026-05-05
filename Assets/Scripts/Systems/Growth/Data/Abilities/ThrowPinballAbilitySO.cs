using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [핀볼] 능력을 구현하는 클래스입니다.
/// 직구 투척 시 적, 벽, 오브젝트에 부딪히면 반사각으로 튕겨 나갑니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowPinballAbility", menuName = "Necromancer/Growth/Throw Ability/Pinball")]
public class ThrowPinballAbilitySO : ThrowAbilitySO
{
    [Header("Pinball Settings")]
    public int maxBounces = 2;
    public float bounceDuration = 5.0f;

    public ThrowPinballAbilitySO()
    {
        // 필터 설정: 직구(Straight) 전용, 범위(Area) 타겟팅 모드에서만 활성화
        trajectoryFilter = ThrowTrajectoryFilter.Straight;
        targetFilter = ThrowTargetFilter.Area;
    }

    public override void ModifyRecipe(ThrowRecipe recipe, List<IThrowable> heldObjects)
    {
        // 레시피의 최대 튕김 횟수를 설정합니다. (Bouncing과 필드 공유)
        recipe.state.bounceCount = 0;
        // Bouncing 필드를 사용하여 로직 연동
        recipe.state.isBouncing = false; 

        Debug.Log($"<color=cyan>[Ability: Pinball]</color> Ready to bounce up to {maxBounces} times.");
    }

    public override void OnThrowLaunch(ThrowController controller, ThrowRecipe recipe, Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float ratio)
    {
        // 직구의 경우 기본 duration이 매우 짧거나 거리에 비례할 수 있으므로, 
        // 핀볼은 첫 발사부터 충분한 지속 시간을 부여합니다.
        if (controller.ActiveCluster != null)
        {
            var arc = controller.ActiveCluster.GetComponent<ArcMovement>();
            if (arc != null)
            {
                arc.ResetDuration(bounceDuration);
            }
        }
    }
}
