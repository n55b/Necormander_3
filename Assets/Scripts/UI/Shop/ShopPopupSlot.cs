using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>상점 창의 상품 카드 한 장.</summary>
public class ShopPopupSlot : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private Button buyButton;
    [SerializeField] private TextMeshProUGUI buyLabel;
    [SerializeField] private CanvasGroup canvasGroup;

    private ShopOffer _offer;
    private ShopPopupUI _owner;
    private Coroutine _shake;

    // ─── 공개 API ────────────────────────────────────────────────────
    /// <summary>카드에 상품 정보를 채우고 구매 버튼을 연결한다.</summary>
    public void Setup(ShopOffer offer, ShopPopupUI owner)
    {
        _offer = offer;
        _owner = owner;

        if (icon != null)
        {
            icon.sprite = offer.Icon;
            icon.enabled = icon.sprite != null;
        }
        if (nameText != null) nameText.text = offer.GetTitle();
        if (descText != null) descText.text = offer.GetDescription();
        if (priceText != null) priceText.text = $"{offer.Price:N0} G";

        if (buyButton != null)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(() => _owner.Purchase(this, _offer));
        }
    }

    /// <summary>판매 완료 / 구매 가능 / 골드 부족 상태를 표시한다.</summary>
    public void Refresh(int gold)
    {
        if (_offer == null) return;

        if (_offer.Purchased)
        {
            if (priceText != null) priceText.color = ShopPopupUI.SoldColor;
            if (buyButton != null) buyButton.interactable = false;
            if (buyLabel != null) buyLabel.text = "판매 완료";
            if (canvasGroup != null) canvasGroup.alpha = 0.5f;
            return;
        }

        bool affordable = gold >= _offer.Price;
        if (priceText != null) priceText.color = affordable ? ShopPopupUI.GoldColor : ShopPopupUI.PoorColor;
        if (buyButton != null) buyButton.interactable = affordable;
        if (buyLabel != null) buyLabel.text = affordable ? "구매" : "골드 부족";
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    /// <summary>구매 실패 시 카드 내용을 살짝 흔든다.</summary>
    public void PlayFail()
    {
        if (!isActiveAndEnabled || canvasGroup == null) return;
        if (_shake != null) StopCoroutine(_shake);
        _shake = StartCoroutine(Shake((RectTransform)canvasGroup.transform));
    }

    // ─── 내부 전용 ───────────────────────────────────────────────────
    // 레이아웃 그룹이 카드 위치를 잡으므로 카드 자체 대신 안쪽 Content 를 흔든다.
    private IEnumerator Shake(RectTransform rt)
    {
        float t = 0f;
        while (t < 0.25f)
        {
            t += Time.unscaledDeltaTime;
            rt.anchoredPosition = new Vector2(Mathf.Sin(t * 80f) * 8f * (1f - t / 0.25f), 0f);
            yield return null;
        }
        rt.anchoredPosition = Vector2.zero;
        _shake = null;
    }
}
