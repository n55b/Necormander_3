using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A vertical layout measures after horizontal layout assigns the wrap width.
[RequireComponent(typeof(TMP_Text))]
public sealed class WrappedLabelHeight : LayoutElement
{
    private TMP_Text label;
    public override float preferredHeight
    {
        get
        {
            if (!label) label = GetComponent<TMP_Text>();
            float width = label.rectTransform.rect.width;
            return width > 0 ? Mathf.Ceil(label.GetPreferredValues(label.text, width, Mathf.Infinity).y) : 0;
        }
        set => base.preferredHeight = value;
    }
}
