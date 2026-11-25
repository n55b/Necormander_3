using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Focus changes only the outline. The label, control geometry and hit target stay in place.
[DisallowMultipleComponent, AddComponentMenu("UI 스킨/포커스 표시")]
public sealed class UIHoverMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    public RectTransform target;
    private Selectable control;
    private ControlFocusRing ring;
    private VectorUiImage face;
    private RoundedPlate plate;
    private float focus, inset;
    private bool hovered, selected, pressed;

    private void Awake()
    {
        control = GetComponent<Selectable>();
        if (control == null) return;
        if (control is Slider slider)
        {
            if (slider.handleRect) RoundedPlate.ApplyFlat(slider.handleRect.GetComponent<Image>());
            if (slider.fillRect) RoundedPlate.ApplyFlat(slider.fillRect.GetComponent<Image>());
            var rail = slider.transform.Find("Background/Skin_Track");
            if (rail) RoundedPlate.ApplyFlat(rail.GetComponent<Image>());
        }
        if (control.transition == Selectable.Transition.ColorTint)
        {
            var colors = control.colors;
            colors.highlightedColor = colors.selectedColor = colors.pressedColor = colors.normalColor;
            control.colors = colors;
        }
        else if (control.transition == Selectable.Transition.SpriteSwap)
        {
            var sprites = control.spriteState;
            sprites.highlightedSprite = sprites.selectedSprite = sprites.pressedSprite = null;
            control.spriteState = sprites;
        }
    }

    public void OnPointerEnter(PointerEventData e) { hovered = true; Refresh(); }
    public void OnPointerExit(PointerEventData e) { hovered = false; Refresh(); }
    public void OnSelect(BaseEventData e) { selected = true; Refresh(); }
    public void OnDeselect(BaseEventData e) { selected = false; Refresh(); }
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(); } }
    public void OnPointerUp(PointerEventData e) { pressed = false; Refresh(); }

    private void OnEnable()
    {
        selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        Refresh();
    }

    private void OnDisable()
    {
        hovered = selected = pressed = false;
        if (face != null) face.SetFocus(0);
        if (plate != null) plate.SetInteraction(0, 0);
        focus = inset = 0;
        if (ring != null) ring.canvasRenderer.SetAlpha(0);
    }

    private void LateUpdate()
    {
        if (plate != null)
        {
            float dt = UISkinSettings.MotionOn ? Time.unscaledDeltaTime : 1;
            bool enabledControl = control.IsInteractable();
            focus = Mathf.MoveTowards(focus, enabledControl && (hovered || selected) ? 1 : 0, dt * 9);
            inset = Mathf.MoveTowards(inset, enabledControl && pressed ? 1 : 0, dt * 17);
            plate.SetInteraction(focus, inset);
        }
        if (face != null)
        {
            float to = control.IsInteractable() && (hovered || selected) ? (pressed ? .88f : .48f) : 0f;
            focus = Mathf.MoveTowards(focus,to,UISkinSettings.MotionOn ? Time.unscaledDeltaTime*8 : 1);
            face.SetFocus(focus);
        }
        if (ring != null && ring.gameObject.activeSelf != control.IsInteractable())
            ring.gameObject.SetActive(control.IsInteractable());
    }

    private void Refresh()
    {
        if (control == null || control is RewardSkipButton) return;
        if (TryGetComponent<RoundedPlate>(out plate)) return;
        face = control.targetGraphic as VectorUiImage;
        if (face != null && face.HasFrame) return;
        face = null;
        bool visible = hovered || selected;
        if (ring == null)
        {
            if (!visible || !control.IsInteractable()) return;
            var parent = target != null ? target : control.targetGraphic != null ? control.targetGraphic.rectTransform : (RectTransform)transform;
            ring = ControlFocusRing.Create(parent, "Focus outline");
            ring.canvasRenderer.SetAlpha(0);
        }
        ring.CrossFadeAlpha(visible ? (pressed ? .95f : .68f) : 0f, UISkinSettings.MotionOn ? .075f : 0f, true);
    }
}
