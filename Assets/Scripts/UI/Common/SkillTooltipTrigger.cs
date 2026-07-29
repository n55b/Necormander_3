using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Generic hover tooltip trigger for UI icons (skill slots, minion slots, etc).
/// Attach via AddComponent at runtime and call SetData() whenever the underlying
/// skill/minion changes. Shows CommonTooltipUI.Instance on hover, hides on exit.
/// </summary>
public class SkillTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private string _title;
    private string _description;
    private bool _hasData;

    /// <summary>Call whenever the skill/minion bound to this icon changes.</summary>
    public void SetData(string title, string description)
    {
        _title = title;
        _description = description;
        _hasData = !string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(description);
    }

    public void Clear()
    {
        _hasData = false;
        _title = null;
        _description = null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_hasData || CommonTooltipUI.Instance == null) return;
        CommonTooltipUI.Instance.Show(new TooltipData(_title, _description));
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
