using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Components;

/// <summary>
/// 개별 보상 카드를 관리하는 클래스입니다.
/// 보상 후보의 아이콘, 이름, 설명을 표시하고 클릭 시 부모 UI에 알립니다.
/// </summary>
public class RewardCard : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;

    [Header("Keyword Sub Tooltip")]
    [Tooltip("설명에 포함된 키워드(예: 취약)를 감지해 아래에 추가 설명을 붙여줍니다. CommonTooltipUI와 동일한 사전을 연결하세요.")]
    [SerializeField] private AstroNuts.Localization.KeywordDictionary keywordDictionary;
    
    // 카드 자체에 Button 컴포넌트가 있는 경우를 위해 캐싱
    private Button _cardButton;
    private int _myIndex;
    private RewardSelectionUI _parentUI;
    private string _lastHoveredKeywordId; // [추가] 현재 마우스가 올라가 있는 키워드 링크 id (호버 툴팁용)

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
            _cardButton.onClick.RemoveAllListeners();
            _cardButton.onClick.AddListener(() => {
                if (_parentUI != null)
                {
                    _parentUI.OnCardClicked(_myIndex);
                }
                else
                {
                    Debug.LogError($"[RewardCard] {gameObject.name}: 부모 UI(RewardSelectionUI)를 찾을 수 없습니다!");
                }
            });
        }
        else
        {
            Debug.LogError($"[RewardCard] {gameObject.name}: Button 컴포넌트가 오브젝트에 없습니다!");
        }
    }

    /// <summary>
    /// 전달받은 보상 데이터를 기반으로 카드의 내용을 갱신합니다.
    /// </summary>
    public void Setup(RewardCandidate candidate, int index)
    {
        _myIndex = index;
        EnsureButtonLink();
        
        if (nameText != null)
        {
            if (candidate.displayData.localizedItemName != null && !candidate.displayData.localizedItemName.IsEmpty)
            {
                var locEvent = nameText.GetComponent<LocalizeStringEvent>();
                if (locEvent == null)
                {
                    locEvent = nameText.gameObject.AddComponent<LocalizeStringEvent>();
                    locEvent.OnUpdateString.AddListener((s) => nameText.text = s);
                }
                locEvent.StringReference = candidate.displayData.localizedItemName;
            }
            else
            {
                // 로컬라이즈 이벤트가 있으면 참조 해제
                var locEvent = nameText.GetComponent<LocalizeStringEvent>();
                if (locEvent != null) locEvent.StringReference = null;
                nameText.text = candidate.displayData.itemName;
            }
        }

        if (descText != null)
        {
            // [수정] 키워드(예: "취약")는 아래에 텍스트를 덧붙이는 대신,
            // 설명 안에서 밑줄 + 사전에 등록된 색상(badgeColor)으로 강조 표시하고,
            // 마우스를 올렸을 때 CommonTooltipUI로 효과 설명을 보여줍니다.
            if (candidate.displayData.localizedDescription != null && !candidate.displayData.localizedDescription.IsEmpty)
            {
                var locEvent = descText.GetComponent<LocalizeStringEvent>();
                if (locEvent == null)
                {
                    locEvent = descText.gameObject.AddComponent<LocalizeStringEvent>();
                }
                locEvent.OnUpdateString.RemoveAllListeners();
                locEvent.OnUpdateString.AddListener((s) => descText.text = ApplyKeywordHighlighting(s));
                locEvent.StringReference = candidate.displayData.localizedDescription;
            }
            else
            {
                var locEvent = descText.GetComponent<LocalizeStringEvent>();
                if (locEvent != null) locEvent.StringReference = null;
                descText.text = ApplyKeywordHighlighting(candidate.displayData.description);
            }
        }
        
        if (iconImage != null)
        {
            iconImage.sprite = candidate.displayData.icon;
            iconImage.gameObject.SetActive(candidate.displayData.icon != null);
        }

        if (_cardButton != null)
        {
            // [수정] 데이터가 없어도(rawData == null) 버튼은 항상 활성화하여 
            // '없음'을 클릭했을 때 다음 보상으로 넘어가거나 UI가 닫힐 수 있도록 합니다.
            _cardButton.interactable = true;
        }
    }

    /// <summary>
    /// [추가] 설명 텍스트 안에서 키워드 사전에 등록된 단어를 찾아 밑줄 + 사전 색상(badgeColor)의 TMP 링크 태그로 감쌉니다.
    /// 감싸진 키워드는 Update()에서 마우스 호버를 감지해 CommonTooltipUI로 설명을 보여줍니다.
    /// </summary>
    private string ApplyKeywordHighlighting(string text)
    {
        if (keywordDictionary == null || string.IsNullOrEmpty(text)) return text;

        foreach (var entry in keywordDictionary.entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

            string keyword = ResolveLocalizedString(entry.displayName);
            if (string.IsNullOrEmpty(keyword) || !text.Contains(keyword)) continue;

            string colorHex = ColorUtility.ToHtmlStringRGB(entry.badgeColor);
            string wrapped = $"<u><color=#{colorHex}><link=\"{entry.id}\">{keyword}</link></color></u>";
            text = text.Replace(keyword, wrapped);
        }

        return text;
    }

    /// <summary>
    /// [추가] 매 프레임 마우스가 설명 텍스트 안의 키워드 링크 위에 있는지 검사하여 툴팁을 표시/숨김 처리합니다.
    /// </summary>
    private void Update()
    {
        if (descText == null || keywordDictionary == null) return;

        Camera cam = null;
        var canvas = descText.canvas;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        int linkIndex = TMP_TextUtilities.FindIntersectingLink(descText, Input.mousePosition, cam);

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

        // [수정] 사전(KeywordDictionary)에 등록된 badgeColor의 알파값이 0으로 저장되어 있으면
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
        _lastHoveredKeywordId = null;
        if (CommonTooltipUI.Instance != null)
            CommonTooltipUI.Instance.Hide();
    }
}
