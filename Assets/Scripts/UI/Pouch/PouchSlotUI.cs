using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>드래그 핸들러를 두지 않아 스크롤 제스처는 부모 ScrollRect에 전달된다.</summary>
public class PouchSlotUI : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Image frameImage;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private GameObject selectionFrame;
    private PouchUI _owner;
    public ItemSO Item { get; private set; }
    public void Bind(PouchUI owner, ItemSO item, int count)
    {
        _owner = owner;
        Item = item;
        if (iconImage != null) { iconImage.sprite = GroundItem.ItemIcon(item); iconImage.enabled = item != null; }
        if (frameImage != null) frameImage.color = Color.white;
        if (countText != null) countText.text = count > 1 ? $"×{count}" : string.Empty;
    }
    private ControlFocusRing selectedOutline;
    public void SetSelected(bool selected)
    {
        if (!selectionFrame) return;
        if (selected && !selectedOutline)
        {
            if (selectionFrame.TryGetComponent<Graphic>(out var oldFrame)) oldFrame.enabled = false;
            selectedOutline = ControlFocusRing.Create((RectTransform)selectionFrame.transform, "Selected item");
            selectedOutline.color = new Color32(255,237,196,255);
        }
        selectionFrame.SetActive(selected);
    }
    public void OnPointerClick(PointerEventData data)
    { if (data.button == PointerEventData.InputButton.Left) _owner?.Select(Item); }
    public void OnSubmit(BaseEventData data) => _owner?.Select(Item);
}
