using UnityEngine;

/// <summary>
/// [타이밍 차지] 능력을 구현하는 클래스입니다.
/// 풀차지(100%) 직후 일정 시간 내에 발사하면 효과를 증폭시킵니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowTimingChargeAbility", menuName = "Necromancer/Growth/Throw Ability/Timing Charge")]
public class ThrowTimingChargeAbilitySO : ThrowAbilitySO
{
    [Header("Timing Settings")]
    [SerializeField] private float perfectWindow = 0.25f; // 풀차지 후 0.25초 내에 쏴야 함
    [SerializeField] private float powerMultiplier = 1.5f; // 위력 1.5배 강화

    private float _fullChargeStartTime = -1f;
    private bool _isPerfectTiming = false;

    public override void OnChargeUpdate(float currentRatio, float deltaTime)
    {
        if (currentRatio >= 0.98f)
        {
            if (_fullChargeStartTime < 0)
            {
                _fullChargeStartTime = Time.time;
                Debug.Log("<color=yellow>[Ability: Timing]</color> Full Charge Reached! Release now!");
            }

            // 윈도우 시간 내에 있는지 확인
            _isPerfectTiming = (Time.time - _fullChargeStartTime <= perfectWindow);
        }
        else
        {
            _fullChargeStartTime = -1f;
            _isPerfectTiming = false;
        }
    }

    public override void ModifyRecipe(ThrowRecipe recipe, System.Collections.Generic.List<IThrowable> heldObjects)
    {
        if (_isPerfectTiming)
        {
            recipe.treasurePowerMultiplier *= powerMultiplier;
            Debug.Log($"<color=orange>[Ability: Timing]</color> PERFECT TIMING! Power x{powerMultiplier}");
        }
        
        // 투척 후 상태 리셋
        _fullChargeStartTime = -1f;
        _isPerfectTiming = false;
    }
}
