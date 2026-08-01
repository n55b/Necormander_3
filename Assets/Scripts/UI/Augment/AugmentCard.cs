using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 증강 카드 한 장. 상단 카드 4장과 하단 P0 버튼이 전부 이 스크립트를 쓴다 —
/// 둘의 차이는 프리팹 안의 배치와 크기뿐이라 스크립트를 나눌 이유가 없다.
///
/// 텍스트 필드는 전부 선택 사항이다. 프리팹에서 빼면 그 줄만 안 나온다.
/// </summary>
public class AugmentCard : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("등급 색이 칠해질 테두리/배경. 비워두면 색을 안 바꾼다.")]
    [SerializeField] private Image frame;
    [Tooltip("'P3 · 가혹' 같은 등급 표기.")]
    [SerializeField] private TextMeshProUGUI tierText;
    [Tooltip("페널티 이름. P0 카드에선 '무위험'.")]
    [SerializeField] private TextMeshProUGUI nameText;
    [Tooltip("표시 문구(분위기 글). 수치는 안 적는다.")]
    [SerializeField] private TextMeshProUGUI flavorText;
    [Tooltip("실제 수치('받는 피해 +25%'). P0 카드는 페널티가 없어 빈 줄이 된다.")]
    [SerializeField] private TextMeshProUGUI effectText;
    [Tooltip("클리어 시 받을 보상.")]
    [SerializeField] private TextMeshProUGUI rewardText;

    private Button _button;
    private AugmentOffer _offer;
    private AugmentSelectionUI _owner;

    /// <summary>이 카드가 들고 있는 선택지. 비어 있으면 null.</summary>
    public AugmentOffer Offer => _offer;

    /// <summary>카드 내용을 채운다. offer 가 null 이면 카드 자체를 끈다(테이블 cardCount 가 슬롯보다 적을 때).</summary>
    public void Setup(AugmentOffer offer, AugmentSelectionUI owner)
    {
        _offer = offer;
        _owner = owner;

        if (offer == null) { gameObject.SetActive(false); return; }
        gameObject.SetActive(true);

        Color tint = AugmentTableSO.TierColor(offer.Tier);
        if (frame != null) frame.color = tint;
        if (tierText != null) { tierText.text = AugmentTableSO.TierLabel(offer.Tier); tierText.color = tint; }
        if (nameText != null) nameText.text = offer.Title;
        if (flavorText != null) flavorText.text = offer.Flavor;
        if (effectText != null) effectText.text = offer.EffectText;
        if (rewardText != null) rewardText.text = offer.RewardText;

        // 리스너를 매번 비우고 다시 단다. 방마다 Setup 이 다시 불려서, 안 지우면 클릭 한 번에
        // 예전 방의 선택지까지 같이 발화한다.
        if (_button == null) _button = GetComponent<Button>();
        if (_button != null)
        {
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => _owner?.Pick(_offer));
            _button.interactable = true;
        }
        else
        {
            Debug.LogError($"[AugmentCard] {name}: Button 컴포넌트가 없어서 선택할 수 없다.");
        }
    }
}
