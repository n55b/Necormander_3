using UnityEngine;
using TMPro;

/// <summary>
/// 엘리트 몬스터 머리 위에 현재 사용 중인 공격/패턴 이름을 한글로 표시하는 라벨입니다.
/// EliteChargerAIPatternSO가 각 행동을 시작/종료할 때 SetText()/Clear()를 호출해 갱신합니다.
/// </summary>
public class EliteBossPatternLabel : MonoBehaviour
{
    private TextMeshPro _text;

    private void Awake()
    {
        _text = GetComponent<TextMeshPro>();
        if (_text == null) _text = gameObject.AddComponent<TextMeshPro>();

        _text.alignment = TextAlignmentOptions.Center;
        _text.fontSize = 3.2f;
        _text.color = Color.white;
        _text.outlineWidth = 0.2f;
        _text.outlineColor = Color.black;
        _text.sortingOrder = 200;
        _text.text = string.Empty;
    }

    /// <summary>
    /// 한글 폰트를 지정합니다. null이면 TMP 프로젝트 기본 폰트를 사용합니다.
    /// </summary>

    /// <summary>텍스트와 색상을 함께 갱신한다. color가 null이면 색은 바꾸지 않는다.</summary>
    public void SetText(string koreanLabel, Color? color)
    {
        if (_text == null) return;
        _text.text = koreanLabel;
        if (color.HasValue) _text.color = color.Value;
    }
    public void SetFont(TMP_FontAsset font)
    {
        if (_text == null) return;
        _text.font = font != null ? font : TMP_Settings.defaultFontAsset;
    }

    public void SetText(string koreanLabel)
    {
        if (_text == null) return;
        _text.text = koreanLabel;
    }

    public void Clear()
    {
        if (_text == null) return;
        _text.text = string.Empty;
    }
}
