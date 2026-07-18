using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebuffIcon : MonoBehaviour
{
    [SerializeField] Image iconImage;
    [SerializeField] TextMeshProUGUI stackText;

    // 풀에서 꺼낼 때 초기화용.
    // [26/07/17] 구 DebuffStackType 인자는 삭제됐다 — 대입만 하고 아무도 안 읽던 필드였다.
    public void Initialize(Sprite sprite, int initialStack)
    {
        iconImage.sprite = sprite;
        UpdateStack(initialStack);
        // 상태이상은 걸린 순간 곧바로 효과가 있으므로 '발동' 모양으로 띄운다.
        // (SetTriggeredState(false) 의 '아직 안 터진 스택' 모양은 구 트리거 구조의 잔재였다.
        //  비폭이 스택을 쌓는 동안 이 구분이 다시 필요해지면 Phase 5 에서 false 를 쓰면 된다.)
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
            outline.effectColor = Color.red;
            outline.effectDistance = new Vector2(4, -4); // 두꺼운 빨간 테두리
            
            // 발동된 디버프는 크기도 20% 키워서 강조
            transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            
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
