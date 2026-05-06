using UnityEngine;

/// <summary>
/// [타이밍 차지] 능력을 구현하는 클래스입니다.
/// 풀차지(100%) 직후 일정 시간 내에 발사하면 효과를 증폭시킵니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowTimingChargeAbility", menuName = "Necromancer/Growth/Throw Ability/TimingCharge")]
public class ThrowTimingChargeAbilitySO : ThrowAbilitySO
{
    [Header("Timing Settings")]
    [SerializeField] private float perfectWindow = 0.3f; // 풀차지 후 0.3초 내에 발사
    [SerializeField] private float powerMultiplier = 1.5f; // 보너스 배율

    // [주의] SO는 런타임 데이터를 직접 저장하면 안 되지만, 
    // 여기서는 간단한 구현을 위해 private 필드를 사용합니다. 
    // 나중에는 ThrowAbilityStateManager 등으로 옮기는 것이 좋습니다.
    private float _fullChargeStartTime = -1f;
    private bool _isPerfectTiming = false;

    public override void OnChargeUpdate(float currentRatio, float deltaTime)
    {
        if (currentRatio >= 0.98f)
        {
            if (_fullChargeStartTime < 0) _fullChargeStartTime = Time.time;
        }
        else
        {
            _fullChargeStartTime = -1f;
        }
    }

    public override void OnThrowLaunch(ThrowController controller, ThrowRecipe recipe, Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float ratio)
    {
        if (_fullChargeStartTime > 0)
        {
            float elapsed = Time.time - _fullChargeStartTime;
            if (elapsed <= perfectWindow)
            {
                _isPerfectTiming = true;
            }
        }

        if (_isPerfectTiming)
        {
            recipe.modifiers.treasurePowerMultiplier *= powerMultiplier;
            Debug.Log($"<color=orange>[Ability: Timing]</color> PERFECT TIMING! Power x{powerMultiplier}");
        }
        
        // 투척 후 상태 리셋
        _fullChargeStartTime = -1f;
        _isPerfectTiming = false;
    }
}
