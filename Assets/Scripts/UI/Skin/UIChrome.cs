using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(menuName = "UI/Panel and control art")]
public sealed class UIChrome : ScriptableObject
{
    [SerializeField] private Sprite panel, button, focus, selected;
    private static UIChrome current;
    private static UIChrome Current => current != null ? current : current = Resources.Load<UIChrome>("UI/Chrome");

    private static VectorUiImage Face(RectTransform rect, Sprite sprite)
    {
        var image = rect.gameObject.AddComponent<VectorUiImage>();
        image.type = Image.Type.Sliced;
        image.sprite = sprite;
        var canvas = rect.GetComponentInParent<Canvas>(true);
        if (canvas != null && sprite != null)
            image.pixelsPerUnitMultiplier = canvas.referencePixelsPerUnit / sprite.pixelsPerUnit;
        return image;
    }

    public static Image Panel(RectTransform rect) => Face(rect, Current.panel);

    public static Button Button(RectTransform rect, bool chosen = false)
    {
        var image = Face(rect, chosen ? Current.selected : Current.button);
        var control = rect.gameObject.AddComponent<Button>();
        control.targetGraphic = image;
        // A selected tab keeps dark lettering. Its light face must survive keyboard focus.
        if (!chosen)
        {
            control.transition = Selectable.Transition.SpriteSwap;
            control.spriteState = new SpriteState { highlightedSprite = Current.focus, selectedSprite = Current.focus,
                pressedSprite = Current.panel, disabledSprite = Current.panel };
        }
        return control;
    }
}
