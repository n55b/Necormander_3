using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>프리팹 카드/비교 영역/확인 버튼의 포인터 처리. 교체와 스킵만 홀드한다.</summary>
public class HandSlotSelectionItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public enum ActionKind { Current, Candidate, Compare, Replace, Skip }
    [SerializeField] private HandSlotSelectionUI owner;
    [SerializeField] private ActionKind action;
    [SerializeField] private Image holdFill;
    private bool _holding;
    private float _elapsed;
    public void OnPointerEnter(PointerEventData e) => owner?.Hover(action, true);
    public void OnPointerExit(PointerEventData e) { owner?.Hover(action, false); ResetHold(); }
    public void OnPointerDown(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && (action == ActionKind.Replace || action == ActionKind.Skip)) _holding = true;
    }
    public void OnPointerUp(PointerEventData e) => ResetHold();
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && action == ActionKind.Candidate) owner?.Select(action);
    }
    private void Update()
    {
        if (!_holding || owner == null) return;
        _elapsed += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.1f, owner.HoldSeconds(action));
        SetProgress(Mathf.Clamp01(_elapsed / duration));
        if (_elapsed < duration) return;
        ResetHold();
        owner.Select(action);
    }
    private void OnDisable() => ResetHold();
    private void ResetHold() { _holding = false; _elapsed = 0f; SetProgress(0f); }
    private void SetProgress(float progress)
    {
        if (holdFill == null) return;
        // 스프라이트 없는 Image는 fillAmount를 무시한다. RectTransform으로 바 전체 너비를 채운다.
        holdFill.enabled = progress > 0f;
        holdFill.rectTransform.anchorMax = new Vector2(progress, 1f);
    }
}
