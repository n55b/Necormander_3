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
            if (candidate.displayData.localizedDescription != null && !candidate.displayData.localizedDescription.IsEmpty)
            {
                var locEvent = descText.GetComponent<LocalizeStringEvent>();
                if (locEvent == null)
                {
                    locEvent = descText.gameObject.AddComponent<LocalizeStringEvent>();
                    locEvent.OnUpdateString.AddListener((s) => descText.text = s);
                }
                locEvent.StringReference = candidate.displayData.localizedDescription;
            }
            else
            {
                var locEvent = descText.GetComponent<LocalizeStringEvent>();
                if (locEvent != null) locEvent.StringReference = null;
                descText.text = candidate.displayData.description;
            }

            // [추가] 설명 안에 키워드 사전(예: "취약")에 등록된 단어가 있으면 아래에 추가 효과 설명을 붙여줍니다.
            // (CommonTooltipUI의 스킬 툴팁과 동일한 방식)
            if (keywordDictionary != null && !string.IsNullOrEmpty(candidate.displayData.description))
            {
                foreach (var entry in keywordDictionary.entries)
                {
                    string keyword = entry.displayName.GetLocalizedString();

                    if (candidate.displayData.description.Contains(keyword))
                    {
                        string kTitle = entry.displayName.GetLocalizedString();
                        string kDesc = entry.description.GetLocalizedString();

                        descText.text += $"\n\n<b><color=#E24B4A>[{kTitle}]</color></b>\n{kDesc}";
                        break;
                    }
                }
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
}
