using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
                // 이 로그가 콘솔에 뜨는지 확인하는 것이 최우선입니다!
                Debug.Log($"<color=cyan>[Card Clicked]</color> {gameObject.name} 클릭됨! Index: {_myIndex}");
                
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
        EnsureButtonLink(); // 혹시 버튼이 늦게 붙었거나 누락된 경우를 위해 재확인
        
        if (nameText != null) nameText.text = candidate.displayData.itemName;
        if (descText != null) descText.text = candidate.displayData.description;
        
        if (iconImage != null)
        {
            iconImage.sprite = candidate.displayData.icon;
            iconImage.gameObject.SetActive(candidate.displayData.icon != null);
        }

        // 보상이 없는 슬롯(rawData == null)인 경우 클릭 버튼을 비활성화합니다.
        if (_cardButton != null)
        {
            _cardButton.interactable = (candidate.rawData != null);
        }
    }
}
