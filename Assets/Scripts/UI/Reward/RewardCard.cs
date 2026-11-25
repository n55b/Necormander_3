using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;

/// <summary>
/// 개별 보상 카드를 관리하는 클래스입니다.
/// 보상 후보의 아이콘, 이름, 설명을 표시하고 클릭 시 부모 UI에 알립니다.
/// </summary>
public class RewardCard : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;
    [Tooltip("긴 설명을 스크롤하는 카드에서만 연결. 비어 있으면 기존 고정 설명을 사용합니다.")]
    [SerializeField] private ScrollRect descriptionScroll;

    [Header("키워드 하위 설명")]
    [Tooltip("설명에 포함된 키워드(예: 취약)를 감지해 아래에 추가 설명을 붙여줍니다. CommonTooltipUI와 동일한 사전을 연결하세요.")]
    [SerializeField] private AstroNuts.Localization.KeywordDictionary keywordDictionary;
    
    // 카드 자체에 Button 컴포넌트가 있는 경우를 위해 캐싱
    private Button _cardButton;
    private UISparkBurst revealSparks;
    private int _myIndex;
    private RewardSelectionUI _parentUI;
    private LocalizedString boundName, boundDescription;
    private readonly LocalizedString equippedLabel = new("UI Text Table", "Choice_Equipped");
    private string title, equipped;
    private bool markEquipped;
    private string _lastHoveredKeywordId; // 현재 마우스가 올라가 있는 키워드 링크 id (호버 툴팁용)

    private void Awake()
    {
        _parentUI = GetComponentInParent<RewardSelectionUI>();
        EnsureButtonLink();
    }

    private void EnsureButtonLink()
    {
        if (_cardButton == null) _cardButton = GetComponent<Button>();
        
        if (_cardButton != null)
        {
            _cardButton.onClick.RemoveListener(Pick);
            _cardButton.onClick.AddListener(Pick);
        }
        else
        {
            Debug.LogError($"[RewardCard] {gameObject.name}: Button 컴포넌트가 오브젝트에 없습니다!");
        }
    }
    private void Pick() { if (_parentUI != null) _parentUI.OnCardClicked(_myIndex); }

    /// <summary>
    /// 전달받은 보상 데이터를 기반으로 카드의 내용을 갱신합니다.
    /// </summary>
    public void Setup(RewardCandidate candidate, int index)
    {
        _myIndex = index;
        if (iconImage != null) UISparkBurst.Play(ref revealSparks, iconImage.rectTransform);
        EnsureButtonLink();
        
        UnbindStrings();
        equipped = null;
        SetName(candidate.displayData.itemName);
        SetDescription(candidate.displayData.description);
        boundName = candidate.displayData.localizedItemName;
        boundDescription = candidate.displayData.localizedDescription;
        markEquipped = candidate.equipped;
        if (isActiveAndEnabled) BindStrings();
        
        if (iconImage != null)
        {
            iconImage.sprite = candidate.displayData.icon;
            iconImage.gameObject.SetActive(candidate.displayData.icon != null);
        }

        if (_cardButton != null)
        {
            // 데이터가 없는 선택 카드도 클릭할 수 있게 유지한다.
            // '없음'을 클릭했을 때 다음 보상으로 넘어가거나 UI가 닫힐 수 있도록 합니다.
            _cardButton.interactable = true;
        }
    }

    private void SetName(string value)
    {
        title = value;
        if (nameText != null) nameText.text = string.IsNullOrEmpty(equipped) ? title : $"{title} · {equipped}";
    }

    private void SetEquipped(string value) { equipped = value; SetName(title); }

    private void OnEnable() => BindStrings();
    private void BindStrings()
    {
        if (boundName != null && !boundName.IsEmpty) boundName.StringChanged += SetName;
        if (boundDescription != null && !boundDescription.IsEmpty) boundDescription.StringChanged += SetDescription;
        if (markEquipped) equippedLabel.StringChanged += SetEquipped;
    }

    private void UnbindStrings()
    {
        if (boundName != null) boundName.StringChanged -= SetName;
        if (boundDescription != null) boundDescription.StringChanged -= SetDescription;
        equippedLabel.StringChanged -= SetEquipped;
    }

    private void SetDescription(string text)
    {
        if (descText == null) return;
        descText.text = ApplyKeywordHighlighting(text);
        if (descriptionScroll == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(descriptionScroll.content);
        descriptionScroll.StopMovement();
        descriptionScroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>
    /// 설명 텍스트 안에서 키워드 사전에 등록된 단어를 찾아 밑줄 + 사전 색상(badgeColor)의 TMP 링크 태그로 감쌉니다.
    /// 감싸진 키워드는 Update()에서 마우스 호버를 감지해 CommonTooltipUI로 설명을 보여줍니다.
    /// </summary>
    private string ApplyKeywordHighlighting(string text)
    {
        return AstroNuts.Localization.KeywordHighlighter.ApplyTags(text, keywordDictionary);
    }

    /// <summary>
    /// 매 프레임 마우스가 설명 텍스트 안의 키워드 링크 위에 있는지 검사하여 툴팁을 표시/숨김 처리합니다.
    /// </summary>
    private void Update()
    {
        if (descText == null || keywordDictionary == null) return;

        var canvas = descText.canvas;
        var cam = PixelViewport.CanvasCamera(canvas);

        // 마스크 밖으로 스크롤된 키워드는 보이지 않으므로 툴팁도 띄우지 않는다.
        Vector2 pointer = PixelViewport.PointerForCanvas(canvas, Input.mousePosition);
        bool inViewport = descriptionScroll == null || RectTransformUtility.RectangleContainsScreenPoint(
            descriptionScroll.viewport, pointer, cam);
        int linkIndex = inViewport ? TMP_TextUtilities.FindIntersectingLink(descText, pointer, cam) : -1;

        if (linkIndex != -1)
        {
            string keywordId = descText.textInfo.linkInfo[linkIndex].GetLinkID();

            if (keywordId != _lastHoveredKeywordId)
            {
                _lastHoveredKeywordId = keywordId;
                ShowKeywordTooltip(keywordId);
            }
        }
        else if (_lastHoveredKeywordId != null)
        {
            _lastHoveredKeywordId = null;
            if (CommonTooltipUI.Instance != null) CommonTooltipUI.Instance.Hide();
        }
    }

    private void ShowKeywordTooltip(string keywordId)
    {
        var entry = keywordDictionary.GetById(keywordId);
        if (entry == null || CommonTooltipUI.Instance == null) return;

        string keywordName = ResolveLocalizedString(entry.displayName);
        string keywordDesc = ResolveLocalizedString(entry.description);

        string title = $"[{keywordName}]";

        // 사전(KeywordDictionary)에 등록된 badgeColor의 알파값이 0으로 저장되어 있으면
        // 타이틀 텍스트가 완전 투명해져 안 보이는 문제가 있어, 알파를 항상 1로 강제합니다.
        Color titleColor = entry.badgeColor;
        titleColor.a = 1f;

        TooltipData data = new TooltipData(title, keywordDesc);
        data.titleColor = titleColor;
        CommonTooltipUI.Instance.Show(data);
    }

    private string ResolveLocalizedString(UnityEngine.Localization.LocalizedString localizedString)
    {
        if (localizedString == null || localizedString.IsEmpty) return string.Empty;

        var op = localizedString.GetLocalizedStringAsync();
        if (op.IsDone) return op.Result;

        var handle = op;
        handle.WaitForCompletion();
        return handle.Result;
    }

    private void OnDisable()
    {
        UnbindStrings();
        _lastHoveredKeywordId = null;
        if (CommonTooltipUI.Instance != null)
            CommonTooltipUI.Instance.Hide();
    }
}
