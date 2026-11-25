using System;
using UnityEngine;

public partial class PlayerController
{
    private bool InteractionBlocked => _inputBlocked || IsGameplayPaused || PouchUI.IsOpen ||
        (SkillExplainUI.Instance != null && SkillExplainUI.Instance.IsOpen);

    private void InteractWithFocusedTarget()
    {
        if (_closestInteractable is NPCBase npc) npc.InteractWithGreeting(gameObject);
        else _closestInteractable?.Interact(gameObject);
    }

    private void BeginInteraction()
    {
        _holdConsumed = false;
        _holdTimer = 0f;
        _holdTarget = _closestInteractable is IHoldInteractable target && target.HoldSeconds > 0f ? target : null;
        if (_holdTarget == null) InteractWithFocusedTarget();
    }

    private void EndInteraction()
    {
        bool wasHolding = _holdTarget != null;
        CancelInteractHold();
        if (wasHolding && !_holdConsumed) InteractWithFocusedTarget();
        _holdConsumed = false;
    }

    private void CheckForInteractable()
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, interactRange, interactableLayer);

        IInteractable nearest = null;
        Collider2D nearestCollider = null;
        float minDist = float.MaxValue;

        foreach (var col in colliders)
        {
            if (col.TryGetComponent<IInteractable>(out var interactable))
            {
                float dist = Vector2.SqrMagnitude(col.transform.position - transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = interactable;
                    nearestCollider = col;
                }
            }
        }

        // 포커스 변경 시 OnFocused / OnLostFocus 호출
        if (!ReferenceEquals(nearest, _closestInteractable))
        {
            _closestInteractable?.OnLostFocus(gameObject);
            nearest?.OnFocused(gameObject);
            _closestInteractable = nearest;

            // 누르고 있는 채로 대상에서 멀어졌으면 홀드는 취소된다.
            if (_holdTarget != null && !ReferenceEquals(nearest, _holdTarget)) CancelInteractHold();
        }

        // 포커스 처리(툴팁 켜기) 뒤에 배치해야 첫 프레임부터 툴팁 위에 F가 붙는다.
        // 대상에 툴팁 같은 월드 UI가 있으면 그 위에, 없으면 콜라이더 위에 뜬다(PopupSystem 참고).
        PopupSystem.ShowInteractionIcon(nearestCollider, interactIconOffset, uiIconGap);
    }

    // ── F 홀드 ────────────────────────────────────────────────────────
    /// <summary>홀드 타이머를 굴린다. 다 차면 OnHoldComplete 를 부르고 그 입력을 소모 처리한다.</summary>
    private void TickInteractHold()
    {
        if (_holdTarget == null || _holdConsumed) return;
        if (InteractionBlocked) { CancelInteractHold(); return; }

        float need = _holdTarget.HoldSeconds;
        if (need <= 0f) { CancelInteractHold(); return; }

        _holdTimer += Time.deltaTime;
        _holdTarget.OnHoldProgress(Mathf.Clamp01(_holdTimer / need));

        if (_holdTimer >= need)
        {
            _holdConsumed = true;              // 손을 떼도 Interact 가 안 불리게
            var target = _holdTarget;
            _holdTarget = null;
            _holdTimer = 0f;
            target.OnHoldProgress(0f);
            target.OnHoldComplete(gameObject);
        }
    }

    /// <summary>진행 중인 홀드를 취소하고 게이지를 되돌린다.</summary>
    private void CancelInteractHold()
    {
        _holdTarget?.OnHoldProgress(0f);
        _holdTarget = null;
        _holdTimer = 0f;
    }
}
