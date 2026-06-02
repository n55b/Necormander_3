using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 투척과 관련된 사용자 입력을 감지하고 처리하는 클래스입니다.
/// </summary>
public class ThrowInputHandler : MonoBehaviour
{
    private ThrowController _controller;
    [SerializeField] private PlayerController _playerController;
    private float _chargeTimer;
    private bool _isCharging;
    public bool IsCharging => _isCharging; // [추가] 외부 노출용
    private Vector2 _rightClickStartPos;
    private bool _isWheelActive;

    public float ChargeRatio
    {
        get
        {
            if (_controller == null || _controller.ChargeTime <= 0.01f) return 1f;
            return Mathf.Clamp01(_chargeTimer / _controller.ChargeTime);
        }
    }
    private Vector2 CurrentMouseScreenPos => Pointer.current.position.ReadValue();

    public void Init(ThrowController controller)
    {
        _controller = controller;
    }

    private void Start()
    {
        _playerController = GameManager.Instance.PLAYERCONTROLLER;
    }

    private void Update()
    {
        if (_isCharging)
        {
            _chargeTimer = Mathf.Min(_chargeTimer + Time.unscaledDeltaTime, _controller.ChargeTime);
            
            float ratio = ChargeRatio;

            // [복구] 차징 바 UI 업데이트
            if (ThrowChargeBarUI.Instance != null)
            {
                ThrowChargeBarUI.Instance.UpdateCharge(ratio);
            }

            // [추가] 던지기 능력 Hook (OnChargeUpdate)
            if (InventoryManager.Instance != null)
            {
                bool isDirect = ratio >= 0.98f;
                TargetingMode mode = _controller.GetCurrentTargetingMode();

                foreach (var ability in InventoryManager.Instance.ActiveAbilities)
                {
                    if (ability != null && ability.IsApplicable(isDirect, mode))
                    {
                        ability.OnChargeUpdate(ratio, Time.deltaTime);
                    }
                }
            }
        }

        if (_isWheelActive && _controller.SelectionWheel != null)
        {
            _controller.SelectionWheel.UpdateHighlight(CurrentMouseScreenPos);
        }
    }

    public void OnRightClickStarted()
    {
        _rightClickStartPos = CurrentMouseScreenPos;
        
        if (_controller.SelectionWheel != null)
        {
            List<bool> availability = new List<bool>();
            foreach (var type in _controller.DirectionMapping)
            {
                availability.Add(_controller.Strategy.CanPickUpType(type, _controller.HeldObjects, _controller.MaxHoldCount));
            }
            _isWheelActive = _controller.SelectionWheel.Show(_rightClickStartPos, _controller.DirectionMapping, availability);
        }
    }

    public void OnRightClickCanceled()
    {
        if (!_isWheelActive) return;
        _isWheelActive = false;

        float dragDist = Vector2.Distance(_rightClickStartPos, CurrentMouseScreenPos);
        
        if (_controller.SelectionWheel != null)
        {
            int selectedIndex = _controller.SelectionWheel.GetSelectedIndex();
            _controller.SelectionWheel.Hide();

            if (dragDist >= _controller.DragThreshold && selectedIndex != -1)
            {
                CommandData targetType = _controller.DirectionMapping[selectedIndex];
                _controller.TryPickUpByType(targetType);
                return;
            }
        }

        _controller.TryPickUpWithMouse();
    }

    public void OnThrow(InputAction.CallbackContext context)
    {
        if (_controller.HeldObjects.Count == 0) return;

        if (context.started)
        {
            _isCharging = true;
            _chargeTimer = 0f;

            // [추가] 차징 바 표시
            if (ThrowChargeBarUI.Instance != null) ThrowChargeBarUI.Instance.SetVisible(true);
        }
        else if (context.canceled)
        {
            if (_controller.TrajectoryPredictor != null) _controller.TrajectoryPredictor.HideGuide();
            if (_isCharging) _controller.ThrowAll();
            _isCharging = false;

            // [추가] 차징 바 숨김
            if (ThrowChargeBarUI.Instance != null) ThrowChargeBarUI.Instance.SetVisible(false);

            // 애니메이터 불러와서 실행
            _playerController.TransitionToState(_playerController.atkState);
            _playerController.canChangeState = false;
        }
    }

    public void ResetCharging()
    {
        _isCharging = false;
        _chargeTimer = 0f;

        // [추가] 차징 바 숨김
        if (ThrowChargeBarUI.Instance != null) ThrowChargeBarUI.Instance.SetVisible(false);
    }
}
