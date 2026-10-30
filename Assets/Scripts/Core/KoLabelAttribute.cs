using UnityEngine;

/// <summary>
/// 인스펙터에 한글 이름으로 보이게 하는 표시용 속성.
/// min/max 를 넣으면 슬라이더로 그려진다(float/int). 최솟값만 넣으면 그 아래로 내려가지 않는다.
/// 실제 그리기는 Editor/KoLabelDrawer.cs 가 한다.
/// </summary>
public class KoLabelAttribute : PropertyAttribute
{
    public readonly string label;
    public readonly float min, max;
    public readonly bool hasMin, hasMax;

    public KoLabelAttribute(string label) { this.label = label; }

    public KoLabelAttribute(string label, float min)
    {
        this.label = label; this.min = min; hasMin = true;
    }

    public KoLabelAttribute(string label, float min, float max)
    {
        this.label = label; this.min = min; this.max = max; hasMin = hasMax = true;
    }
}
