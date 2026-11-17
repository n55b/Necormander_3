using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Generic hover tooltip trigger for UI icons (skill slots, minion slots, etc).
/// Prefab-authored title/description or SetData() can provide the content.
/// Shows CommonTooltipUI.Instance on hover, hides on exit.
/// </summary>
public class SkillTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private LocText title;
    [SerializeField] private LocText description;

    /// <summary>Call whenever the skill/minion bound to this icon changes.</summary>
    public void SetData(string title, string description)
    {
        // Callers pass already-localized text, so no key (shown as is).
        this.title = new LocText { ko = title };
        this.description = new LocText { ko = description };
    }

    public void Clear()
    {
        title = default;
        description = default;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if ((string.IsNullOrEmpty(title) && string.IsNullOrEmpty(description)) || CommonTooltipUI.Instance == null) return;
        CommonTooltipUI.Instance.Show(new TooltipData(title, description));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (CommonTooltipUI.Instance != null)
            CommonTooltipUI.Instance.Hide();
    }

    private void OnDisable()
    {
        // Make sure the tooltip doesn't get stuck open if this icon is disabled mid-hover
        if (CommonTooltipUI.Instance != null)
            CommonTooltipUI.Instance.Hide();
    }
}
