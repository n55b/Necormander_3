using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>슬라이더의 현재 값을 0~100 숫자로 표시한다. (옵션창 음량 등)</summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI 스킨/슬라이더 수치 표시")]
public class SliderPercentText : MonoBehaviour
{
    [KoLabel("슬라이더")] public Slider slider;
    [KoLabel("수치 글자")] public TMP_Text label;

    private void OnEnable()
    {
        if (slider == null) return;
        slider.onValueChanged.AddListener(Refresh);
        Refresh(slider.value);
    }

    private void OnDisable()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(Refresh);
    }

    private void Refresh(float _)
    {
        if (label != null && slider != null) label.text = Mathf.RoundToInt(slider.normalizedValue * 100f).ToString();
    }
}
