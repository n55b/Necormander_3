using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 상점 창. 상점 주인(ShopNPC)에게 F 로 말을 걸면 열린다. 진열 상품 카드 + 보유 골드를 보여준다.
/// UIPopUpManager 의 Window 레이어로 열려서 ESC 로 닫히고, 열려 있는 동안 게임플레이 입력이 막힌다.
/// Screen Space Overlay + Scale With Screen Size(1920x1080) 라 해상도가 달라도 비율이 유지된다.
/// 프리팹: Resources/UI/ShopPopupUI.prefab (Tools/Shop/Build Shop Popup Prefab 로 생성)
/// </summary>
public class ShopPopupUI : MonoBehaviour
{
    private const string ResourcePath = "UI/ShopPopupUI";
    private const string UIName = "Shop";

    private static ShopPopupUI _instance;

    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private Button closeButton;
    [SerializeField] private RectTransform slotContainer;
    [SerializeField] private ShopPopupSlot slotTemplate;
    [SerializeField] private GameObject emptyLabel;

    public static readonly Color GoldColor = new Color(1f, 0.82f, 0.25f, 1f);
    public static readonly Color PoorColor = new Color(0.95f, 0.35f, 0.35f, 1f);
    public static readonly Color SoldColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    private readonly List<ShopPopupSlot> _slots = new List<ShopPopupSlot>();
    private ShopNPC _npc;
    private int _shownGold = int.MinValue;
    private int _openedFrame = -1;
    private int _closedFrame = -1;

    // ─── 공개 API ────────────────────────────────────────────────────
    public static bool IsOpen => _instance != null && _instance.panel != null && _instance.panel.activeSelf;

    /// <summary>씬에 없으면 Resources 프리팹을 하나 띄운다. 프리팹이 없으면 null.</summary>
    public static ShopPopupUI Ensure()
    {
        if (_instance != null) return _instance;
        var prefab = Resources.Load<ShopPopupUI>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[ShopPopupUI] Resources/{ResourcePath}.prefab 이 없습니다. Tools/Shop/Build Shop Popup Prefab 을 실행하세요.");
            return null;
        }
        Instantiate(prefab).name = "ShopPopupUI";
        return _instance; // Awake 에서 등록됨
    }

    /// <summary>해당 NPC 의 재고로 상점 창을 연다. 규칙상 못 열면 false.</summary>
    public static bool Open(ShopNPC npc)
    {
        var ui = Ensure();
        return ui != null && npc != null && ui.OpenInternal(npc);
    }

    /// <summary>상점 창을 닫는다. (X 버튼, F 키)</summary>
    public void Close()
    {
        if (panel == null) return;
        if (UIPopUpManager.Instance != null && UIPopUpManager.Instance.IsOpen(panel)) UIPopUpManager.Instance.Close(panel); // onClosed 가 불린다
        else if (panel.activeSelf) { panel.SetActive(false); OnClosed(); }
    }

    /// <summary>카드의 구매 버튼에서 호출. 실패하면 카드를 흔든다.</summary>
    internal void Purchase(ShopPopupSlot slot, ShopOffer offer)
    {
        if (_npc != null && _npc.TryPurchase(offer)) slot.Refresh(CurrentGold());
        else slot.PlayFail();
        RefreshGold(force: true);
    }

    // ─── 내부 전용 ───────────────────────────────────────────────────
    // 싱글톤 등록. 템플릿/패널은 꺼둔 상태로 시작한다.
    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        if (slotTemplate != null) slotTemplate.gameObject.SetActive(false);
        if (panel != null) panel.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    // 씬이 내려갈 때 UIPopUpManager 목록에 남지 않도록 빼둔다.
    private void OnDestroy()
    {
        if (_instance != this) return;
        if (UIPopUpManager.Instance != null && panel != null) UIPopUpManager.Instance.Forget(panel);
        _instance = null;
    }

    // 열려 있는 동안 골드 표시를 갱신하고, F 로 닫는다(여는 F 와 같은 프레임은 무시).
    // ESC 는 UIPopUpManager.CloseTopByEscape 쪽에서 처리된다.
    private void Update()
    {
        if (!IsOpen) return;
        RefreshGold();

        var kb = Keyboard.current;
        if (kb != null && kb.fKey.wasPressedThisFrame && Time.frameCount != _openedFrame) Close();
    }

    // Window 레이어로 연다. 닫는 F 와 같은 프레임에 다시 열리는 것을 막는다.
    private bool OpenInternal(ShopNPC npc)
    {
        if (Time.frameCount == _closedFrame) return false;
        if (IsOpen) return true;

        bool opened;
        if (UIPopUpManager.Instance != null) opened = UIPopUpManager.Instance.Open(panel, UIPopUpManager.Layer.Window, OnClosed);
        else { panel.SetActive(true); opened = true; }
        if (!opened) return false;

        _npc = npc;
        _openedFrame = Time.frameCount;
        UIEventBus.NotifyOpen(UIName);
        Rebuild();
        return true;
    }

    // 어떤 경로로 닫히든(직접/ESC/매니저) 한 번 불린다.
    private void OnClosed()
    {
        _closedFrame = Time.frameCount;
        _npc = null;
        UIEventBus.NotifyClose(UIName);
    }

    // 재고 수만큼 카드를 맞추고(모자라면 템플릿에서 복제) 내용을 채운다.
    private void Rebuild()
    {
        var stock = _npc != null ? _npc.Stock : null;
        int count = stock != null ? stock.Count : 0;

        while (_slots.Count < count) _slots.Add(Instantiate(slotTemplate, slotContainer));

        for (int i = 0; i < _slots.Count; i++)
        {
            bool used = i < count;
            _slots[i].gameObject.SetActive(used);
            if (used) _slots[i].Setup(stock[i], this);
        }

        if (emptyLabel != null) emptyLabel.SetActive(count == 0);
        RefreshGold(force: true);
    }

    // 골드가 바뀌었을 때만 보유 골드 텍스트와 카드 상태(구매 가능/부족)를 갱신한다.
    private void RefreshGold(bool force = false)
    {
        int gold = CurrentGold();
        if (!force && gold == _shownGold) return;
        _shownGold = gold;

        if (goldText != null) goldText.text = $"보유 골드  <color=#{ColorUtility.ToHtmlStringRGB(GoldColor)}>{gold:N0} G</color>";
        foreach (var s in _slots) if (s.gameObject.activeSelf) s.Refresh(gold);
    }

    private static int CurrentGold() => InventoryManager.Instance != null ? InventoryManager.Instance.GOLD : 0;
}
