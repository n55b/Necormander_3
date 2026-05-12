using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 보석의 상세 정보를 보여주는 툴팁 UI 클래스입니다.
/// </summary>
public class GemTooltipUI : MonoBehaviour
{
    public static GemTooltipUI Instance;

    [Header("UI References")]
    [SerializeField] private RectTransform tooltipPanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI typeText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI effectsText;
    [SerializeField] private TextMeshProUGUI subSlotsText;

    [Header("Settings")]
    [SerializeField] private Vector2 offset = new Vector2(15, -15);

    private Canvas _canvas;

    private void Awake()
    {
        Instance = this;
        _canvas = GetComponentInParent<Canvas>();
        
        // [추가] 툴팁 자체가 마우스를 가려 깜빡이는 현상 방지
        var canvasGroup = tooltipPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = tooltipPanel.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        // [추가] 피벗을 좌측 상단(0, 1)으로 강제 고정
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

    public void Show(GemInstance gem)
    {
        if (gem == null || gem.BaseData == null) return;

        GemSO data = gem.BaseData;
        
        titleText.text = data.itemName;
        typeText.text = $"<color={GetGroupColorTag(data.synergyGroup)}>[{data.synergyGroup}]</color>";
        descriptionText.text = data.description;
        
        string effectsStr = "";
        foreach (var effect in data.effects)
        {
            if (effect != null)
                effectsStr += $"- {effect.GetDescription()}\n";
        }
        
        if (gem.RandomModifiers != null && gem.RandomModifiers.Count > 0)
        {
            effectsStr += "<color=#ADD8E6>\n[Bonus Modifiers]</color>\n";
            foreach (var mod in gem.RandomModifiers)
            {
                effectsStr += $"- {mod.Type}: +{mod.Value * 100}%\n";
            }
        }
        effectsText.text = effectsStr.TrimEnd();

        subSlotsText.text = $"Tree Expansion: <b>+{data.subSlots} Slots</b>";

        tooltipPanel.gameObject.SetActive(true);
        
        // 레이아웃 즉시 갱신 (크기 확정)
        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipPanel);
        
        // 크기 확정 후 위치 업데이트
        UpdatePosition();
    }

    public void Hide()
    {
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

        // 기본 위치: 마우스 우측 하단 (탑-레프트 피벗 기준)
        Vector2 finalPos = localPos + offset;

        // [화면 밖 방지 로직]
        float canvasWidth = ((RectTransform)_canvas.transform).rect.width;
        float canvasHeight = ((RectTransform)_canvas.transform).rect.height;
        Vector2 tooltipSize = tooltipPanel.sizeDelta;

        // 오른쪽 화면 밖으로 나갈 때 -> 왼쪽으로 이동
        if (finalPos.x + tooltipSize.x > canvasWidth / 2f)
        {
            finalPos.x = localPos.x - tooltipSize.x - offset.x;
        }

        // 아래쪽 화면 밖으로 나갈 때 -> 위쪽으로 이동
        if (finalPos.y - tooltipSize.y < -canvasHeight / 2f)
        {
            finalPos.y = localPos.y + tooltipSize.y + Mathf.Abs(offset.y);
        }

        tooltipPanel.anchoredPosition = finalPos;
    }

    private string GetGroupColorTag(GemSynergyGroup group)
    {
        switch (group)
        {
            case GemSynergyGroup.Poison: return "#32CD32";
            case GemSynergyGroup.Chill: return "#00BFFF";
            case GemSynergyGroup.BloodPop: return "#FF00FF";
            case GemSynergyGroup.Aging: return "#BC8F8F";
            case GemSynergyGroup.Corrosion: return "#FFD700";
            default: return "#FFFFFF";
        }
    }
}
