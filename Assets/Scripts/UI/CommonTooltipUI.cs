using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

/// <summary>
/// 툴팁에 표시될 공통 데이터 구조입니다.
/// </summary>
public struct TooltipData
{
    public string title;
    public string type;
    public string description;
    public List<string> effects;
    public string footer;
    public Color titleColor;
    
    public LocalizedString localizedTitle;
    public LocalizedString localizedDescription;

    public TooltipData(string title, string description)
    {
        this.title = title;
        this.description = description;
        this.type = "";
        this.effects = new List<string>();
        this.footer = "";
        this.titleColor = Color.white;
        this.localizedTitle = null;
        this.localizedDescription = null;
    }
}

/// <summary>
/// 미니언, 능력, 보석 등 모든 게임 요소를 위한 범용 툴팁 UI입니다.
/// </summary>
public class CommonTooltipUI : MonoBehaviour
{
    public static CommonTooltipUI Instance;

    [Header("UI References")]
    [SerializeField] private RectTransform tooltipPanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI typeText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI effectsText;
    [SerializeField] private TextMeshProUGUI footerText;

    [Header("Settings")]
    [SerializeField] private Vector2 offset = new Vector2(15, -15);

    private Canvas _canvas;

    private void Awake()
    {
        Instance = this;
        _canvas = GetComponentInParent<Canvas>();
        
        var canvasGroup = tooltipPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = tooltipPanel.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        tooltipPanel.pivot = new Vector2(0, 1);

        Hide();
    }

    private void Update()
    {
        if (tooltipPanel.gameObject.activeSelf)
        {
            UpdatePosition();
        }
    }

    public void Show(TooltipData data)
    {
        if (titleText != null)
        {
            if (data.localizedTitle != null && !data.localizedTitle.IsEmpty)
            {
                var locEvent = titleText.GetComponent<LocalizeStringEvent>();
                if (locEvent == null)
                {
                    locEvent = titleText.gameObject.AddComponent<LocalizeStringEvent>();
                    locEvent.OnUpdateString.AddListener((s) => titleText.text = s);
                }
                locEvent.StringReference = data.localizedTitle;
            }
            else
            {
                var locEvent = titleText.GetComponent<LocalizeStringEvent>();
                if (locEvent != null) locEvent.StringReference = null;
                titleText.text = data.title;
            }
            titleText.color = data.titleColor;
        }
        
        if (typeText != null)
        {
            typeText.text = data.type;
            typeText.gameObject.SetActive(!string.IsNullOrEmpty(data.type));
        }
        
        if (descriptionText != null)
        {
            if (data.localizedDescription != null && !data.localizedDescription.IsEmpty)
            {
                var locEvent = descriptionText.GetComponent<LocalizeStringEvent>();
                if (locEvent == null)
                {
                    locEvent = descriptionText.gameObject.AddComponent<LocalizeStringEvent>();
                    locEvent.OnUpdateString.AddListener((s) => descriptionText.text = s);
                }
                locEvent.StringReference = data.localizedDescription;
            }
            else
            {
                var locEvent = descriptionText.GetComponent<LocalizeStringEvent>();
                if (locEvent != null) locEvent.StringReference = null;
                descriptionText.text = data.description;
            }
        }
        
        if (data.effects != null && data.effects.Count > 0)
        {
            effectsText.text = string.Join("\n", data.effects);
            effectsText.gameObject.SetActive(true);
        }
        else
        {
            effectsText.gameObject.SetActive(false);
        }

        if (!string.IsNullOrEmpty(data.footer))
        {
            footerText.text = data.footer;
            footerText.gameObject.SetActive(true);
        }
        else
        {
            footerText.gameObject.SetActive(false);
        }

        tooltipPanel.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipPanel);
        UpdatePosition();
    }

    public void Hide()
    {
        if (tooltipPanel != null)
            tooltipPanel.gameObject.SetActive(false);
    }

    private void UpdatePosition()
    {
        Vector2 mousePos = Input.mousePosition;
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform, 
            mousePos, 
            _canvas.worldCamera, 
            out localPos
        );

        Vector2 finalPos = localPos + offset;
        float canvasWidth = ((RectTransform)_canvas.transform).rect.width;
        float canvasHeight = ((RectTransform)_canvas.transform).rect.height;
        Vector2 tooltipSize = tooltipPanel.sizeDelta;

        if (finalPos.x + tooltipSize.x > canvasWidth / 2f)
            finalPos.x = localPos.x - tooltipSize.x - offset.x;

        if (finalPos.y - tooltipSize.y < -canvasHeight / 2f)
            finalPos.y = localPos.y + tooltipSize.y + Mathf.Abs(offset.y);

        tooltipPanel.anchoredPosition = finalPos;
    }
}
