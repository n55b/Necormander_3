using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 투척과 관련된 사용자 입력을 감지하고 처리하는 클래스입니다.
/// </summary>
public class ThrowInputHandler : MonoBehaviour
{
    private ThrowController _controller;
    private float _chargeTimer;
    private bool _isCharging;
    private Vector2 _rightClickStartPos;
    private bool _isWheelActive;

    public float ChargeRatio => _controller != null ? Mathf.Min(_chargeTimer / _controller.ChargeTime, 1.0f) : 0f;
    private Vector2 CurrentMouseScreenPos => Pointer.current.position.ReadValue();

    public void Init(ThrowController controller)
    {
        _controller = controller;
    }

    private void Update()
    {
        if (_isCharging)
        {
            _chargeTimer = Mathf.Min(_chargeTimer + Time.deltaTime, _controller.ChargeTime);
        }

        if (_isWheelActive && _controller.SelectionWheel != null)
        {
            _controller.SelectionWheel.UpdateHighlight(CurrentMouseScreenPos);
        }
    }

    public void OnRightClickStarted()
    {
        _rightClickStartPos = CurrentMouseScreenPos;
        _isWheelActive = true;
        
        if (_controller.SelectionWheel != null)
        {
            List<bool> availability = new List<bool>();
            foreach (var type in _controller.DirectionMapping)
            {
                availability.Add(_controller.Strategy.CanPickUpType(type, _controller.HeldObjects, _controller.MaxHoldCount));
            }
            _controller.SelectionWheel.Show(_rightClickStartPos, _controller.DirectionMapping, availability);
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
        }
        else if (context.canceled)
        {
            if (_controller.TrajectoryPredictor != null) _controller.TrajectoryPredictor.HideGuide();
            if (_isCharging) _controller.ThrowAll();
            _isCharging = false;
        }
    }

    public void ResetCharging()
    {
        _isCharging = false;
        _chargeTimer = 0f;
    }
}
