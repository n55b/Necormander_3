using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SkillExplainUI 내부의 스킬 한 칸(아이콘 + 제목 + 설명)을 표시하는 슬롯.
/// 플레이어 스킬(Q/E/R)과 연계 스킬(미니언) 양쪽에서 공용으로 사용합니다.
/// </summary>
public class SkillExplainSlotUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;

    private const string EMPTY_TITLE = "미보유";
    private const string EMPTY_DESCRIPTION = "장착된 스킬이 없습니다.";

    /// <summary>
    /// 스킬 정보를 슬롯에 채웁니다.
    /// </summary>
    public void SetData(Sprite icon, string title, string description)
    {
        gameObject.SetActive(true);

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (titleText != null) titleText.text = title;
        if (descriptionText != null) descriptionText.text = description;
    }

    /// <summary>
    /// 장착된 스킬이 없을 때 표시할 빈 상태.
    /// </summary>
    public void SetEmpty()
    {
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }

        if (titleText != null) titleText.text = EMPTY_TITLE;
        if (descriptionText != null) descriptionText.text = EMPTY_DESCRIPTION;
    }
}
