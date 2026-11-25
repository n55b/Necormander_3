using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;

/// <summary>
/// SkillExplainUI 내부의 스킬 한 칸(아이콘 + 제목 + 설명)을 표시하는 슬롯.
/// 플레이어 스킬(Q/E/R)과 연계 스킬(미니언) 양쪽에서 공용으로 사용합니다.
/// </summary>
public class SkillExplainSlotUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    private LocalizedString localizedTitle;
    private void UpdateTitle(string value) { if (titleText != null) titleText.text = value; }
    private void OnDisable()
    {
        if (localizedTitle != null) localizedTitle.StringChanged -= UpdateTitle;
    }
    private void OnEnable()
    {
        if (localizedTitle != null && !localizedTitle.IsEmpty) localizedTitle.StringChanged += UpdateTitle;
    }

    internal void EnableScrollableDescription()
    {
        if (!descriptionText || descriptionText.transform.parent.TryGetComponent<ScrollRect>(out _)) return;
        var content = descriptionText.rectTransform;
        var host = new GameObject("Description viewport", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
        host.layer = content.gameObject.layer;
        var viewport = (RectTransform)host.transform;
        viewport.SetParent(content.parent,false);
        viewport.SetSiblingIndex(content.GetSiblingIndex());
        viewport.anchorMin = content.anchorMin; viewport.anchorMax = content.anchorMax;
        viewport.pivot = content.pivot; viewport.sizeDelta = content.sizeDelta;
        viewport.anchoredPosition = content.anchoredPosition;
        content.SetParent(viewport,false);
        content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f,1); content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        descriptionText.alignment = TextAlignmentOptions.TopLeft;
        descriptionText.textWrappingMode = TextWrappingModes.Normal;
        descriptionText.raycastTarget = true;
        content.gameObject.AddComponent<WrappedLabelHeight>().layoutPriority = 2;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = host.GetComponent<ScrollRect>();
        scroll.horizontal = false; scroll.viewport = viewport; scroll.content = content;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 20f;
    }

    private const string EMPTY_TITLE = "미보유";
    private const string EMPTY_DESCRIPTION = "장착된 스킬이 없습니다.";

    /// <summary>
    /// 스킬 정보를 슬롯에 채웁니다.
    /// </summary>
    public void SetData(Sprite icon, string title, string description, LocalizedString translatedTitle = null)
    {
        gameObject.SetActive(true);

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (localizedTitle != null) localizedTitle.StringChanged -= UpdateTitle;
        localizedTitle = translatedTitle;
        UpdateTitle(title);
        if (isActiveAndEnabled && localizedTitle != null && !localizedTitle.IsEmpty) localizedTitle.StringChanged += UpdateTitle;
        if (descriptionText != null) descriptionText.text = description;
    }

    /// <summary>
    /// 장착된 스킬이 없을 때 표시할 빈 상태.
    /// </summary>
    public void SetEmpty()
    {
        if (localizedTitle != null) localizedTitle.StringChanged -= UpdateTitle;
        localizedTitle = null;
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }

        if (titleText != null) titleText.text = EMPTY_TITLE;
        if (descriptionText != null) descriptionText.text = EMPTY_DESCRIPTION;
    }
}
