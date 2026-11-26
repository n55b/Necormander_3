using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerController
{
    public void SetInputBlocked(bool blocked)
    {
        _inputBlocked = blocked;
        moveInput = Vector2.zero;
        if (blocked)
        {
            hurtTime = 0;
            roomWalkScale = 1f;
            visualSquash?.ResetVisual();
            StopAttackDash();
            CancelInteractHold();
            if (_melee != null)
            {
                _melee.CancelAttackPreparation();
                _melee.ReleaseAttackInput();
            }
            PopupSystem.HideInteractionIcon();
            // 차단 중에는 Update의 Idle 전환도 멈춘다. 페이드 시작 전에 걷기를 끝낸다.
            // 공격/대쉬의 애니메이션 잠금이나 사망 모션은 덮어쓰지 않는다.
            if (canChangeState && idleState != null && (stat == null || !stat.IsDead))
            {
                ResetWalkAnimSpeed();
                TransitionToState(idleState);
            }
        }
        else if (stat != null && !stat.IsDead && !IsCCed)
        {
            // 계속 누른 키는 performed가 다시 오지 않는다. 차단 전 값을 복원하지 말고
            // 현재 액션 값을 읽어 암전 중 키 해제/방향 변경까지 반영한다.
            moveInput = ReadCurrentMoveInput();
        }

        // 이전 방의 보간값/속도가 남으면 손을 뗀 뒤에도 새 방에서 미끄러진다.
        ResetLocomotion(moveInput);
        Debug.Log($"<color=yellow>[Player]</color> Input Blocked: {blocked}");
    }

    private bool HasLivingInputTarget => !_inputBlocked && stat != null && stat.Health != null && !stat.Health.IsDead;
    internal bool CanStartGameplayAction => HasLivingInputTarget && !IsGameplayPaused && !IsCCed;
    internal static bool IsCombatPointerBlocked =>
        WorkshopReturn.CapturesPointer || LightingDebugPanel.CapturesPointer || PouchUI.IsOpen || (SkillExplainUI.Instance != null && SkillExplainUI.Instance.IsPointerOverPanel());

    private Vector2 ReadCurrentMoveInput()
    {
        var move = GetComponent<PlayerInput>()?.actions?.FindAction("Player/Move");
        return move != null && move.enabled ? move.ReadValue<Vector2>() : Vector2.zero;
    }

    // These signatures are serialized on the PlayerInput component of Player Melee.
    public void OnMove(InputAction.CallbackContext context)
    {
        if (!HasLivingInputTarget || IsCCed) { moveInput = Vector2.zero; return; }
        if (context.performed || context.canceled) moveInput = context.ReadValue<Vector2>();
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (context.performed && CanStartGameplayAction) TryStartDodge();
    }

    public void OnMinionSkill(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanStartGameplayAction || IsCombatPointerBlocked) return;
        GetComponent<PlayerSkillController>()?.ExecuteMinionSkill(transform);
    }

    public void OnMinionSwap(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanStartGameplayAction ||
            (DialogueUI.Instance != null && DialogueUI.Instance.ConsumesAdvanceInput)) return;
        GetComponent<PlayerSkillController>()?.TrySwapSummon();
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (InteractionBlocked) { CancelInteractHold(); return; }
        if (context.performed) BeginInteraction();
        else if (context.canceled) EndInteraction();
    }

    // Retained for serialized PlayerInput events authored with the old pouch action.
    public void OnPouch(InputAction.CallbackContext context) { }

    public void OnGemTree(InputAction.CallbackContext context)
    {
        if (!context.performed || !HasLivingInputTarget) return;
        CancelInteractHold();
        PouchUI.Instance?.SetOpen(!PouchUI.IsOpen);
    }

    public void OnHandSlot(InputAction.CallbackContext context)
    {
        if (!HasLivingInputTarget) return;

        if (context.performed)
        {
            CancelInteractHold();
            SkillExplainUI.Instance?.Toggle();
        }
    }

    /// <summary>
    /// ESC = 뒤로 가기.
    ///   1) 옵션이 열려 있으면 옵션을 닫는다(게임 재개).
    ///   2) 맨 위 창이 ESC 로 닫을 수 있는 창(주머니/장착 정보/맵)이면 그 창만 닫는다.
    ///   3) 아니면 옵션을 연다(게임 정지). 보상창·대화처럼 ESC 로 안 닫히는 창은 그대로 둔 채 위에 뜬다.
    /// </summary>
    public void OnOption(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        if (LightingDebugPanel.Instance != null && LightingDebugPanel.Instance.IsOpen)
        {
            LightingDebugPanel.Instance.SetOpen(false);
            return;
        }
        var som = SceneOptionManager.Instance;
        if (som == null)
        {
            Debug.LogError("<color=red>[PlayerController]</color> SceneOptionManager.Instance is NULL!");
            return;
        }

        if (som.isOptionOpen) { som.CloseOptionScene(); return; }

        var mgr = UIPopUpManager.Instance;
        if (mgr != null && mgr.CloseTopByEscape()) return;

        som.OpenOptionScene();
    }
}
