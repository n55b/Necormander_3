using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 주머니 칸 하나. 아이콘 표시 + 호버 툴팁 + 드래그(칸 교환 / 밖으로 버리기).
/// 실제 데이터는 ItemPouch 가 갖고 있고, 이 컴포넌트는 화면만 담당한다.
///
/// 잠긴 칸(주머니 칸 수 밖의 칸)은 UI 상 회색으로 죽어 있고 드래그·툴팁이 전부 막힌다.
/// 지금은 4칸으로 시작해 최대 9칸까지 열 수 있고, 여는 수단(구매)은 아직 없다 —
/// ItemPouch.slotCount 를 인스펙터에서 직접 올리면 그만큼 열린다.
/// </summary>
public class PouchSlotUI : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [Header("표시")]
    [Tooltip("아이템 아이콘. 아이템이 없으면 꺼진다.")]
    [SerializeField] private Image iconImage;

    [Tooltip("칸 테두리/배경. 아이템 등급 색으로 물든다.")]
    [SerializeField] private Image frameImage;

    [Tooltip("잠긴 칸에 덮이는 것(선택). 없으면 frame 색만 어두워진다.")]
    [SerializeField] private GameObject lockedOverlay;

    [Header("색")]
    [SerializeField] private Color emptyFrameColor  = new Color(1f, 1f, 1f, 0.20f);
    [SerializeField] private Color lockedFrameColor = new Color(0f, 0f, 0f, 0.45f);

    private PouchUI _owner;
    private int _index = -1;
    private ItemSO _item;
    private bool _unlocked;

    public int Index => _index;
    public ItemSO Item => _item;

    /// <summary>PouchUI 가 Awake 에서 칸 번호를 알려준다.</summary>
    public void Bind(PouchUI owner, int index)
    {
        _owner = owner;
        _index = index;
    }

    /// <summary>이 칸이 뭘 담고 있는지 다시 그린다.</summary>
    public void SetItem(ItemSO so, bool unlocked)
    {
        _item = so;
        _unlocked = unlocked;

        if (lockedOverlay != null) lockedOverlay.SetActive(!unlocked);

        if (iconImage != null)
        {
            iconImage.gameObject.SetActive(unlocked && so != null);
            if (so != null)
            {
                // 아이콘 스프라이트가 아직 없는 아이템은 등급 색 사각으로 대신 보여준다.
                iconImage.sprite = so.icon;
                iconImage.color = so.icon != null ? Color.white : so.TierColor;
            }
        }

        if (frameImage != null)
            frameImage.color = !unlocked ? lockedFrameColor
                             : so != null ? so.TierColor
                             : emptyFrameColor;

        SetDimmed(false);
    }

    /// <summary>드래그로 '들려 있는' 동안 원래 칸을 반투명하게.</summary>
    public void SetDimmed(bool dimmed)
    {
        if (iconImage == null) return;
        var c = iconImage.color;
        c.a = dimmed ? 0.35f : 1f;
        iconImage.color = c;
    }

    // ── 호버 툴팁 ─────────────────────────────────────────────────────
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_unlocked || _item == null || CommonTooltipUI.Instance == null) return;
        CommonTooltipUI.Instance.Show(new TooltipData(_item.DisplayName, _item.TooltipBody));
    }

    public void OnPointerExit(PointerEventData eventData) => HideTooltip();

    private void OnDisable() => HideTooltip();

    private static void HideTooltip()
    {
        if (CommonTooltipUI.Instance != null) CommonTooltipUI.Instance.Hide();
    }

    // ── 드래그 ────────────────────────────────────────────────────────
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!_unlocked || _item == null) return;
        HideTooltip();
        _owner?.BeginDrag(this);
    }

    // 커서 추적은 PouchUI 가 매 프레임 하므로 여기선 할 일이 없다.
    // 다만 이 핸들러가 없으면 Unity 가 드래그 이벤트를 시작조차 안 한다.
    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 놓은 자리 아래에 있는 칸을 찾는다. 칸이 아니면(배경/화면 밖) null 이고,
        // PouchUI 가 좌표로 '패널 밖인가'를 판정해 버리기로 넘긴다.
        PouchSlotUI target = null;
        var hit = eventData.pointerCurrentRaycast.gameObject;
        if (hit != null) target = hit.GetComponentInParent<PouchSlotUI>();
        if (target != null && !target._unlocked) target = null; // 잠긴 칸으로는 못 옮긴다

        _owner?.EndDrag(target, eventData.position, true);
    }
}
