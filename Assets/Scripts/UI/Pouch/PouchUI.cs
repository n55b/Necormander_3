using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>V 주머니. 프리팹에 저작된 목록/상세 패널에 아이템 종류별 셀만 채운다.</summary>
public class PouchUI : MonoBehaviour
{
    public static PouchUI Instance;
    public const string UIName = "Pouch"; // UI 소리 설정의 창 이름
    public static bool IsOpen { get; private set; }
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private RectTransform content;
    [SerializeField] private PouchSlotUI slotPrefab;
    [SerializeField] private ScrollRect itemScroll;
    [SerializeField] private ScrollRect detailScroll;
    [SerializeField] private TMP_Text totalsText;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TMP_Text detailTitle;
    [SerializeField] private TMP_Text detailTier;
    [SerializeField] private TMP_Text perCopyText;
    [SerializeField] private TMP_Text stackText;
    [SerializeField] private TMP_Text totalEffectText;
    [SerializeField] private TMP_Text nextEffectText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Button[] sortButtons;
    [SerializeField] private Color activeSortColor = new Color(0.23f, 0.49f, 0.48f);
    [SerializeField] private Color inactiveSortColor = new Color(0.09f, 0.21f, 0.23f);

    private readonly List<PouchSlotUI> _cells = new List<PouchSlotUI>();
    private readonly List<ItemSO> _items = new List<ItemSO>();
    private readonly Dictionary<ItemSO, int> _acquiredOrder = new Dictionary<ItemSO, int>();
    private ItemPouch _pouch;
    private ItemSO _selected;
    private int _revision = -1;
    private int _sort;

    private void Awake()
    {
        Instance = this;
        IsOpen = false;
        if (panelRoot != null) panelRoot.SetActive(false);
    }
    private void OnDisable()
    {
        if (IsOpen) UIPopUpManager.Instance?.Forget(panelRoot != null ? panelRoot : gameObject);
        IsOpen = false;
    }
    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        IsOpen = false;
    }
    private void Update()
    {
        if (!IsOpen) return;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player == null || (player.Stat != null && player.Stat.Health != null && player.Stat.Health.IsDead))
        { SetOpen(false); return; }
        if (_pouch != ItemPouch.Instance || (_pouch != null && _revision != _pouch.Revision)) Refresh();
    }
    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        var manager = UIPopUpManager.Instance;
        var root = panelRoot != null ? panelRoot : gameObject;
        if (open)
        {
            if (manager != null)
            { if (!manager.Open(root, UIPopUpManager.Layer.Overlay, OnPanelClosed, visualRoot: transform)) return; }
            else root.SetActive(true);
            IsOpen = true;
            UIEventBus.NotifyOpen(UIName);
            CommonTooltipUI.Instance?.Hide();
            Refresh();
        }
        else if (manager != null && manager.IsOpen(root)) manager.Close(root);
        else { if (panelRoot != null) panelRoot.SetActive(false); OnPanelClosed(); }
    }
    private void OnPanelClosed()
    {
        if (IsOpen) UIEventBus.NotifyClose(UIName); // UI 소리: 닫힘
        IsOpen = false;
        CommonTooltipUI.Instance?.Hide();
    }
    public void SortByAcquired() => SetSort(0);
    public void SortByCount() => SetSort(1);
    public void SortByTier() => SetSort(2);
    private void SetSort(int sort)
    {
        _sort = sort;
        Refresh();
        if (itemScroll != null) itemScroll.verticalNormalizedPosition = 1f;
    }
    private void Refresh()
    {
        _pouch = ItemPouch.Instance;
        _revision = _pouch != null ? _pouch.Revision : -1;
        _items.Clear();
        if (_pouch != null) _pouch.GetUniqueItems(_items);
        _acquiredOrder.Clear();
        int total = 0;
        for (int i = 0; i < _items.Count; i++)
        { _acquiredOrder[_items[i]] = i; total += _pouch.CountOf(_items[i]); }
        _items.Sort((a, b) =>
        {
            int result = _sort == 1 ? _pouch.CountOf(b).CompareTo(_pouch.CountOf(a))
                : _sort == 2 ? ((int)a.tier).CompareTo((int)b.tier) : 0;
            return result != 0 ? result : _acquiredOrder[a].CompareTo(_acquiredOrder[b]);
        });
        if (_selected == null || !_items.Contains(_selected)) _selected = _items.Count > 0 ? _items[0] : null;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_cells.Count <= i) _cells.Add(Instantiate(slotPrefab, content));
            _cells[i].gameObject.SetActive(true);
            _cells[i].Bind(this, _items[i], _pouch.CountOf(_items[i]));
        }
        for (int i = _items.Count; i < _cells.Count; i++) _cells[i].gameObject.SetActive(false);
        if (totalsText != null) totalsText.text = $"보유 {total}개   종류 {_items.Count}종";
        if (emptyText != null) emptyText.gameObject.SetActive(_items.Count == 0);
        if (sortButtons != null) for (int i = 0; i < sortButtons.Length; i++)
            if (sortButtons[i] != null) sortButtons[i].GetComponent<Image>().color = i == _sort ? activeSortColor : inactiveSortColor;
        Select(_selected);
    }
    public void Select(ItemSO item)
    {
        _selected = item;
        foreach (var cell in _cells) cell.SetSelected(item != null && cell.Item == item);
        int count = item != null && _pouch != null ? _pouch.CountOf(item) : 0;
        if (detailIcon != null) { detailIcon.sprite = GroundItem.ItemIcon(item); detailIcon.enabled = item != null; }
        if (detailTitle != null) detailTitle.text = item != null ? item.DisplayName : "아이템을 선택하세요";
        if (detailTier != null) detailTier.text = item != null ? $"{ItemTierRules.Label(item.tier)} · 보유 {count}개" : "";
        if (perCopyText != null) perCopyText.text = item != null ? "개당 효과\n" + item.EffectSummary(1) : "";
        if (stackText != null) stackText.text = item != null ? $"중첩   ×{count} / {item.MaxStack}" : "";
        if (totalEffectText != null) totalEffectText.text = item != null ? "현재 총 효과\n" + item.EffectSummary(count) : "";
        if (nextEffectText != null) nextEffectText.text = item == null ? "" : count >= item.MaxStack ? "최대 중첩 도달"
            : "1개 더 획득 시\n" + item.EffectSummary(count + 1);
        if (descriptionText != null) descriptionText.text = item != null ? item.description : "방 클리어 보상이나 상점에서 아이템을 획득하세요.";
        if (detailScroll != null) detailScroll.verticalNormalizedPosition = 1f;
    }
}
