using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 아이템 주머니 패널. B 를 '누르고 있는 동안만' 떠 있고 시간은 멈추지 않는다 —
/// B 를 잡은 채로 마우스로 아이템을 옮기거나 버리는 조작을 한다.
///
/// 키 입력은 다른 키들과 똑같이 InputSystem 을 탄다 — PlayerInputSystem.inputactions 의
/// Player/Pouch 액션(&lt;Keyboard&gt;/b) → 플레이어 프리팹 PlayerInput 이벤트 → PlayerController.OnPouch
/// → 여기 SetOpen(). 이 스크립트는 키를 직접 읽지 않으므로 키를 바꾸려면 액션의 바인딩만 고치면 된다.
///
/// 씬 Canvas 아래에 프리팹 인스턴스로 비활성 배치해두면 된다 — 기획자가 에디터에서 그대로 편집한다.
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

    private PouchSlotUI _dragSource;
    private RectTransform _canvasRect;
    private Canvas _canvas;

    private void Awake()
    {
        Instance = this;
        IsOpen = false;
        if (panelRoot != null) panelRoot.SetActive(false);
        if (dragGhost != null) dragGhost.gameObject.SetActive(false);

        _canvas = GetComponentInParent<Canvas>();
        _canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;

        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) slots[i].Bind(this, i);
    }

    private void OnDisable() => IsOpen = false;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        IsOpen = false;
    }

    private void Update()
    {
        if (!IsOpen) return;

        // B 를 누른 채로 죽거나 씬이 넘어가면 입력 이벤트가 더 안 와서 패널이 화면에 남는다.
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

    /// <summary>
    /// 패널을 켜고 끈다. PlayerController.OnPouch(B 액션)가 누를 때 true, 뗄 때 false 로 부른다.
    /// 시간은 멈추지 않는다 — B 를 잡은 채로 마우스 조작을 하는 UI 다.
    /// </summary>
    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        if (panelRoot != null) panelRoot.SetActive(open);

        if (open) Refresh();
        else EndDrag(null); // B 를 놓으면 들고 있던 것도 취소된다(아무 일도 안 일어남)
    }

    /// <summary>
    /// 드래그 고스트를 커서 위로 옮긴다.
    /// rectTransform.position 에 스크린 좌표를 그대로 넣으면 안 된다 — 이 프로젝트 캔버스는
    /// CanvasScaler 가 1920x1080 기준으로 스케일을 먹여서 월드 좌표와 스크린 픽셀이 1:1 이 아니다.
    /// CommonTooltipUI 가 쓰는 것과 같은 변환을 쓴다(그쪽이 이미 검증된 방식).
    /// </summary>
    private void MoveGhostToCursor()
    {
        if (dragGhost == null || _canvasRect == null || Mouse.current == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, Mouse.current.position.ReadValue(), UICamera(), out var local))
        {
            // 고스트의 부모(이 패널 루트)는 캔버스 중앙에 앵커 0.5 로 붙어 있고 anchoredPosition 이 0 이라
            // 캔버스 로컬 좌표를 그대로 써도 된다.
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
            slots[i].SetItem(unlocked && pouch != null ? pouch.Get(i) : null, unlocked);
        }
    }

    // ── 드래그 ────────────────────────────────────────────────────────
    /// <summary>칸에서 드래그가 시작됐다. 커서에 아이콘을 붙인다.</summary>
    public void BeginDrag(PouchSlotUI source)
    {
        if (source == null || source.Item == null) return;
        _dragSource = source;

        if (dragGhost != null)
        {
            dragGhost.sprite = source.Item.icon;
            dragGhost.color = source.Item.icon != null ? Color.white : source.Item.TierColor;
            dragGhost.gameObject.SetActive(true);
        }
        source.SetDimmed(true);
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
        if (source != null) source.SetDimmed(false);

        if (source == null || source.Item == null) return;

        if (target != null && target != source)
        {
            ItemPouch.Instance?.Swap(source.Index, target.Index);
            return;
        }

        // 패널 밖에 놓았으면 버린다. 좌표를 못 받은 호출(B 를 놓아서 취소된 경우)은 버리지 않는다.
        if (hasPos && panelRect != null
            && !RectTransformUtility.RectangleContainsScreenPoint(panelRect, screenPos, UICamera()))
        {
            DropToGround(source.Index);
        }
    }

    /// <summary>칸의 아이템을 플레이어 발밑 바닥에 버린다. 다시 F 로 주울 수 있다.</summary>
    private void DropToGround(int slotIndex)
    {
        var pouch = ItemPouch.Instance;
        if (pouch == null) return;

        var so = pouch.RemoveAt(slotIndex);
        if (so == null) return;

        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        GroundItem.Drop(so, player != null ? player.transform.position : Vector3.zero);
        Debug.Log($"<color=cyan>[Pouch]</color> '{so.DisplayName}' 를 바닥에 버렸다.");
    }

    /// <summary>
    /// 좌표 변환에 넘길 카메라. Screen Space - Overlay 캔버스는 null 을 넘겨야 하고
    /// (카메라를 넘기면 변환이 어긋난다), Camera 모드면 그 카메라를 넘겨야 한다.
    /// </summary>
    private Camera UICamera()
        => _canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : _canvas.worldCamera;
}
