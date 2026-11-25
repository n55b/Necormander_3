using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebuffIcon : MonoBehaviour
{
    [Header("기획 조절 · 동작과 타이밍")]
    [SerializeField, Min(0.01f), Tooltip("상태이상 아이콘을 표시할 때 적용하는 크기 배율입니다.")]
    private float triggeredScale = 1.2f;
    [SerializeField, Tooltip("아이콘의 빨간 외곽선 두께입니다. X와 Y는 UI 픽셀 단위입니다.")]
    private Vector2 triggeredOutlineDistance = new Vector2(4f, -4f);
    [SerializeField, ColorUsage(true), Tooltip("상태이상이 활성화되었을 때 아이콘에 표시할 외곽선 색입니다.")]
    private Color triggeredOutlineColor = Color.red;

    [SerializeField] Image iconImage;
    [SerializeField] TextMeshProUGUI stackText;

    public void Initialize(Sprite sprite, int initialStack)
    {
        iconImage.sprite = sprite;
        UpdateStack(initialStack);
        // 상태이상 효과는 적용 즉시 시작되므로 풀에서 꺼낸 아이콘도 발동 상태로 표시한다.
        SetTriggeredState(true);
    }

    // 숫자(스택)만 갱신할 때 호출
    public void UpdateStack(int amount)
    {
        if (amount > 1)
        {
            stackText.gameObject.SetActive(true);
            stackText.text = amount.ToString();
        }
        else
        {
            stackText.gameObject.SetActive(false);
        }
    }

    public void SetTriggeredState(bool isTriggered)
    {
        var outline = GetComponent<Outline>();
        if (outline == null)
        {
            outline = gameObject.AddComponent<Outline>();
        }
        
        if (isTriggered)
        {
            outline.enabled = true;
            outline.effectColor = triggeredOutlineColor;
            outline.effectDistance = triggeredOutlineDistance; // 두꺼운 빨간 테두리
            
            // 발동된 디버프는 크기도 20% 키워서 강조
            transform.localScale = Vector3.one * triggeredScale;
            
            // 색상을 약간 더 눈에 띄게 (필요시)
            iconImage.color = Color.white;
        }
        else
        {
            outline.enabled = true;
            outline.effectColor = Color.black; // 스택 중일 때는 얇은 검은 테두리
            outline.effectDistance = new Vector2(1, -1);
            
            // 원래 크기 유지
            transform.localScale = Vector3.one;
            
            // 스택 중일 때는 사아아알짝 어둡게 (발동 전 상태 강조)
            iconImage.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        }
    }
}
