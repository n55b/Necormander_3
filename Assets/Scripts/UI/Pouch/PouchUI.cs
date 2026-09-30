using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// V로 여닫는 아이템 주머니. 전투/시간은 계속 진행된다.
/// 밖에서 놓으면 마우스 방향의 플레이어 주변에 즉시 드랍한다. 슬롯/드랍 테두리는 프리팹에 저작한다.
/// </summary>
public class PouchUI : MonoBehaviour
{
    public static PouchUI Instance;

    /// <summary>주머니가 열려 있는가. 열려 있는 동안엔 평타 입력을 씹는다(드래그하다 주먹이 나가면 안 되니까).</summary>
    public static bool IsOpen { get; private set; }

    [Header("패널")]
    [Tooltip("실제로 켜고 끄는 오브젝트. 복주머니 배경 + 칸들이 이 아래에 있다.")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("드래그를 '주머니 밖'으로 판정하는 기준 영역. 보통 panelRoot 의 RectTransform.")]
    [SerializeField] private RectTransform panelRect;

    [Header("칸 (최대 9개. 순서대로 꽂는다)")]
    [SerializeField] private PouchSlotUI[] slots = new PouchSlotUI[ItemPouch.MAX_SLOTS];

    [Header("드래그 중 커서에 붙는 아이콘")]
    [SerializeField] private Image dragGhost;
    [Header("주머니 밖으로 드래그할 때 표시할 붉은 테두리")]
    [SerializeField] private GameObject dropOutline;
    [Tooltip("버리기 방향은 마우스로 정하고, 거리는 플레이어 기준으로 고정한다. 막히면 주변 바닥으로 보정한다.")]
    [SerializeField, Min(0.1f)] private float dropDistance = 1.25f;

    private PouchSlotUI _dragSource;
    private Canvas _canvas;

    private void Awake()
    {
        Instance = this;
        IsOpen = false;
        if (panelRoot != null) panelRoot.SetActive(false);
        if (dragGhost != null) dragGhost.gameObject.SetActive(false);

        _canvas = GetComponentInParent<Canvas>();

        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) slots[i].Bind(this, i);
        if (dropOutline != null) dropOutline.SetActive(false);
    }

    private void OnDisable()
    {
        if (IsOpen) UIPopUpManager.Instance?.Forget(panelRoot != null ? panelRoot : gameObject);
        IsOpen = false;
        EndDrag(null);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        IsOpen = false;
    }

    private void Update()
    {
        if (!IsOpen) return;

        // 죽거나 씬이 넘어갈 때는 드랍을 취소한다.
        var p = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (p == null || (p.Stat != null && p.Stat.Health != null && p.Stat.Health.IsDead))
        {
            SetOpen(false);
            return;
        }

        // 열려 있는 동안만 매 프레임 다시 그린다. ItemPouch 에 변경 알림 이벤트를 두지 않은 이유는,
        // 이 패널의 초기화와 GameManager 의 ItemPouch.Initialize() 사이 실행 순서가 정해져 있지 않아서
        // 구독 시점에 Instance 가 null 이면 조용히 구독을 놓치고 이후 영원히 안 갱신되기 때문이다.
        // 칸이 9개뿐이고 Image 세터는 값이 같으면 스스로 빠져나가므로 매 프레임 호출이 더 싸다.
        Refresh();
        if (_dragSource != null) MoveGhostToCursor();
    }

    /// <summary>창 닫기/사망/씬 종료는 진행 중 드래그를 취소한다. 놓기 전에는 아이템을 버리지 않는다.</summary>
    /// <summary>
    /// 창 닫기/사망/씬 종료는 진행 중 드래그를 취소한다. 놓기 전에는 아이템을 버리지 않는다.
    /// 팝업 매니저의 Overlay 로 등록한다(시간 정지·입력 차단 없음). 장착 정보와 서로 교체되고,
    /// 맵/옵션/대화 중에는 안 열린다. ESC·보상창 등장으로 매니저가 닫아도 OnPanelClosed 로 온다.
    /// </summary>
    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        var mgr = UIPopUpManager.Instance;
        var root = panelRoot != null ? panelRoot : gameObject;

        if (open)
        {
            if (mgr != null) { if (!mgr.Open(root, UIPopUpManager.Layer.Overlay, OnPanelClosed)) return; }
            else root.SetActive(true);
            IsOpen = true;
            Refresh();
            return;
        }

        if (mgr != null && mgr.IsOpen(root)) mgr.Close(root); // → OnPanelClosed
        else { if (panelRoot != null) panelRoot.SetActive(false); OnPanelClosed(); }
    }

    private void OnPanelClosed()
    {
        IsOpen = false; // EndDrag 는 IsOpen=false 면 드랍하지 않고 취소만 한다
        EndDrag(null);
        CommonTooltipUI.Instance?.Hide();
    }

    /// <summary>
    /// 드래그 고스트를 커서 위로 옮긴다.
    /// rectTransform.position 에 스크린 좌표를 그대로 넣으면 안 된다 — 이 프로젝트 캔버스는
    /// CanvasScaler 가 960x540 기준으로 스케일을 먹여서 월드 좌표와 스크린 픽셀이 1:1 이 아니다.
    /// CommonTooltipUI 가 쓰는 것과 같은 변환을 쓴다(그쪽이 이미 검증된 방식).
    /// </summary>
    private void MoveGhostToCursor()
    {
        if (dragGhost == null || Mouse.current == null) return;

        Vector2 cursor = Mouse.current.position.ReadValue();
        if (dropOutline != null) dropOutline.SetActive(IsOutside(cursor));

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragGhost.rectTransform.parent as RectTransform, cursor, UICamera(), out var local))
        {
            // 부모 로컬 좌표를 사용하므로 화면 해상도/CanvasScaler가 달라도 커서에 붙는다.
            dragGhost.rectTransform.anchoredPosition = local;
        }
    }

    /// <summary>주머니 내용을 칸 UI 에 다시 그린다.</summary>
    private void Refresh()
    {
        var pouch = ItemPouch.Instance;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            bool unlocked = pouch != null && i < pouch.SlotCount;
            slots[i].SetItem(unlocked ? pouch.Get(i) : null, unlocked);
        }
        _dragSource?.SetDimmed(true);
    }

    // ── 드래그 ────────────────────────────────────────────────────────
    /// <summary>칸에서 드래그가 시작됐다. 커서에 아이콘을 붙인다.</summary>
    public void BeginDrag(PouchSlotUI source)
    {
        if (!IsOpen || source == null || source.Item == null) return;
        _dragSource = source;

        if (dragGhost != null)
        {
            dragGhost.sprite = GroundItem.ItemIcon(source.Item);
            dragGhost.color = Color.white;
            dragGhost.gameObject.SetActive(true);
        }
        source.SetDimmed(true);
        MoveGhostToCursor();
    }

    /// <summary>
    /// 드래그가 끝났다. 놓은 자리에 따라 갈린다:
    ///   다른 칸 위       → 두 칸을 맞바꾼다(빈 칸으로 옮기는 것도 같은 처리)
    ///   패널 영역 밖     → 바닥에 버린다
    ///   그 외(같은 칸 등) → 아무 일도 안 한다
    /// </summary>
    public void EndDrag(PouchSlotUI target, Vector2 screenPos = default, bool hasPos = false)
    {
        var source = _dragSource;
        _dragSource = null;

        if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        if (dropOutline != null) dropOutline.SetActive(false);
        if (source != null) source.SetDimmed(false);

        if (source == null || source.Item == null) return;

        if (!IsOpen || source.Index < 0 || source.Index >= slots.Length) return;
        var pouch = ItemPouch.Instance;
        if (pouch == null || pouch.Get(source.Index) != source.Item) return;
        if (target != null && target.Index >= 0 && target.Index < pouch.SlotCount)
        {
            if (source.Index != target.Index)
                pouch.Swap(source.Index, target.Index);
            Refresh();
            return;
        }

        // 취소 호출에는 좌표가 없다. 실제 놓기에서만 생성 성공 후 원본/효과를 제거한다.
        if (hasPos && IsOutside(screenPos) && TryDrop(source.Item, screenPos))
        {
            pouch.RemoveAt(source.Index);
        }
        Refresh();
    }

    private bool IsOutside(Vector2 screenPos) => panelRect != null &&
        !RectTransformUtility.RectangleContainsScreenPoint(panelRect, screenPos, UICamera());

    private bool TryDrop(ItemSO item, Vector2 screenPos)
    {
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        var camera = Camera.main;
        if (player == null || camera == null || !camera.pixelRect.Contains(screenPos)) return false;
        var ray = camera.ScreenPointToRay(screenPos);
        var plane = new Plane(Vector3.forward, player.transform.position);
        if (!plane.Raycast(ray, out float distance)) return false;
        if (!GroundItem.TryFindNearbyDropPoint(player.transform.position, ray.GetPoint(distance), dropDistance, out Vector3 point)) return false;
        return GroundItem.Drop(item, point, scatter: false) != null;
    }

    /// <summary>
    /// 좌표 변환에 넘길 카메라. Screen Space - Overlay 캔버스는 null 을 넘겨야 하고
    /// (카메라를 넘기면 변환이 어긋난다), Camera 모드면 그 카메라를 넘겨야 한다.
    /// </summary>
    private Camera UICamera()
        => _canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : _canvas.worldCamera;
}
