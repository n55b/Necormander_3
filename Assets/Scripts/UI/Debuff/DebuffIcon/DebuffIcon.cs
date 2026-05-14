using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebuffIcon : MonoBehaviour
{
    [SerializeField] Image iconImage;
    [SerializeField] TextMeshProUGUI stackText;
    [SerializeField] DebuffStackType type;

    // 풀에서 꺼낼 때 초기화용
    public void Initialize(Sprite sprite, int initialStack, DebuffStackType _type)
    {
        iconImage.sprite = sprite;
        type = _type;
        UpdateStack(initialStack);
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
}
