using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [묵직한 한방] 능력을 구현하는 클래스입니다.
/// 던지기시 요구되는 스태미너가 최대 스태미너량이 되지만, 
/// 던지기 능력 효율이 사용한 스태미나 10당 4% 증가합니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowHeavyStrikeAbility", menuName = "Necromancer/Growth/Throw Ability/Heavy Strike")]
public class ThrowHeavyStrikeAbilitySO : ThrowAbilitySO
{
    public ThrowHeavyStrikeAbilitySO()
    {
        targetFilter = ThrowTargetFilter.Single;
        trajectoryFilter = ThrowTrajectoryFilter.Parabolic | ThrowTrajectoryFilter.Straight;
    }

    public override void ModifyRecipe(ThrowRecipe recipe, List<IThrowable> heldObjects)
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            // ThrowAll()에서 이미 기본 코스트(예: 15)는 ConsumeStamina()로 깎인 상태입니다.
            // 따라서 현재 남아있는 스태미나가 곧 "추가로 소모할 수 있는 스태미나"가 됩니다.
            float extraStamina = pc.STAMINA.CurrentStamina;

            if (extraStamina > 0)
            {
                // 남은 스태미나 모두 소모 (강제 차감)
                pc.STAMINA.ConsumeRawStamina(extraStamina);
                
                // 추가로 사용한 스태미나 10당 4% (0.04) 배율 증가
                float efficiencyBuff = (extraStamina / 10f) * 0.04f;
                
                // 능력치 보너스를 레시피의 gemPowerMultiplier에 합산
                recipe.modifiers.gemPowerMultiplier += efficiencyBuff;
                
                Debug.Log($"<color=cyan>[Heavy Strike]</color> Extra Stamina Consumed: {extraStamina}, Efficiency Buff: +{efficiencyBuff * 100}%");
            }
            else
            {
                Debug.Log($"<color=cyan>[Heavy Strike]</color> No extra stamina to consume. Current Stamina: {extraStamina}. Efficiency Buff: +0%");
            }
        }
    }
}
