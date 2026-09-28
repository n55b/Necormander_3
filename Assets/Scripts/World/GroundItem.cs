using UnityEngine;
using UnityEngine.AI;

/// <summary>아이템/미니언 공용 바닥 픽업. 짧은 F는 습득, 긴 F는 분해한다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class GroundItem : MonoBehaviour, IInteractable, IHoldInteractable
{
    [SerializeField] private GameObject infoCanvas;
    [SerializeField] private Tooltip info;
    [Header("분해")]
    [SerializeField, Min(0.1f)] private float disassembleHoldSeconds = 1f;
    [SerializeField] private Color disassembleTint = new Color(1f, 0.35f, 0.25f);
    [SerializeField, Min(0f)] private float minionRecycleHeal = 10f;
    private ItemSO _item;
    private MinionDataSO _minion;
    private SpriteRenderer _sr;
    private bool _consumed;
    private static Sprite _fallbackItemIcon;
    public ItemSO Item => _item;
    public MinionDataSO Minion => _minion;
    public bool IsAvailable => !_consumed && (_item != null || _minion != null);
    public float HoldSeconds => disassembleHoldSeconds;
    public string InteractionPrompt => _minion != null ? $"F: 미니언 확인 · 길게: 분해 (체력 +{minionRecycleHeal})"
        : _item != null ? $"F: 습득 · 길게: 분해 (+{_item.Refund}G)" : "";

    public static GroundItem Drop(ItemSO item, Vector3 pos, bool scatter = true)
    {
        if (item == null) return null;
        var result = Spawn("GroundItem", pos, scatter);
        if (result != null) result.SetItem(item);
        return result;
    }

    public static GroundItem Drop(MinionDataSO minion, Vector3 pos)
    {
        if (!(minion is MainMinionDataSO)) return null;
        var result = Spawn("GroundMinion", pos);
        if (result != null) result.SetMinion(minion);
        return result;
    }

    private static GroundItem Spawn(string resource, Vector3 pos, bool scatter = true)
    {
        var prefab = Resources.Load<GroundItem>(resource);
        if (prefab == null) { Debug.LogError($"[GroundItem] Resources/{resource} 프리팹 누락. 원본은 소비하지 않습니다."); return null; }
        return Instantiate(prefab, scatter ? pos + (Vector3)(Random.insideUnitCircle * 0.4f) : pos, Quaternion.identity);
    }

    // 주머니/드래그/월드 픽업이 같은 그림을 쓴다. 미저작 아이템도 픽업 프리팹의 기존 그림으로 통일한다.
    public static Sprite ItemIcon(ItemSO item)
    {
        if (item == null) return null;
        if (item.icon != null) return item.icon;
        if (_fallbackItemIcon == null)
        {
            var prefab = Resources.Load<GroundItem>("GroundItem");
            if (prefab != null) _fallbackItemIcon = prefab.GetComponent<SpriteRenderer>().sprite;
        }
        return _fallbackItemIcon;
    }

    /// <summary>클릭 위치에서 가장 가까운 실제 이동 가능 바닥. 맵 준비 전에는 실패하여 소지품을 보존한다.</summary>
    public static bool TryFindDropPoint(Vector3 desired, float searchRadius, out Vector3 point)
    {
        point = desired;
        if (!NavMesh.SamplePosition(desired, out var hit, Mathf.Max(0.1f, searchRadius), NavMesh.AllAreas)) return false;
        point = hit.position;
        if (MapGenerator.Instance != null && !MapGenerator.Instance.TryGetGroundLandingPoint(point, 0f, out _)) return false;
        return !Physics2D.OverlapCircle(point, 0.05f, Layers.WallMask);
    }

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (infoCanvas != null) infoCanvas.SetActive(false);
    }

    public void SetItem(ItemSO item) { _item = item; _minion = null; SetIcon(ItemIcon(item)); }
    public void SetMinion(MinionDataSO minion) { _minion = minion; _item = null; SetIcon(minion != null ? minion.minionIcon : null); }
    private void SetIcon(Sprite icon)
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (icon != null) _sr.sprite = icon;
        _sr.color = Color.white;
    }

    public bool Interact(GameObject interactor)
    {
        if (!IsAvailable) return false;
        if (_minion != null)
        {
            if (HandSlotSelectionUI.Instance == null) return false;
            HandSlotSelectionUI.Instance.Show(this);
            return true;
        }
        if (ItemPouch.Instance == null || !ItemPouch.Instance.TryAdd(_item)) { Announce("주머니가 꽉 찼다"); return false; }
        Consume();
        return true;
    }

    // Destroy는 프레임 끝이므로 즉시 소비 표시를 남겨 중복 지급/회복을 막는다.
    public void Consume()
    {
        if (_consumed) return;
        _consumed = true;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    public void OnFocused(GameObject interactor)
    {
        if (!IsAvailable || infoCanvas == null || info == null) return;
        infoCanvas.SetActive(true);
        if (info.name != null) info.name.text = _minion != null ? _minion.minionName : _item.DisplayName;
        if (info.price != null) info.price.text = "";
    }

    public void OnLostFocus(GameObject interactor)
    {
        if (infoCanvas != null) infoCanvas.SetActive(false);
        OnHoldProgress(0f);
    }

    public void OnHoldProgress(float progress)
    {
        if (_sr != null) _sr.color = Color.Lerp(Color.white, disassembleTint, progress);
        if (info != null && info.price != null) info.price.text = progress <= 0f ? "" : $"분해 중… {progress:P0}";
    }

    public void OnHoldComplete(GameObject interactor)
    {
        if (!IsAvailable) return;
        if (_minion != null)
        {
            var player = interactor != null ? interactor.GetComponent<PlayerController>() : null;
            if (player == null || player.Stat == null || player.Stat.Health.IsDead) return;
            player.Stat.Health.Heal(minionRecycleHeal);
            Announce($"체력 +{minionRecycleHeal}");
        }
        else
        {
            if (InventoryManager.Instance == null) return;
            InventoryManager.Instance.AddGold(_item.Refund);
            Announce($"+{_item.Refund}G");
        }
        Consume();
    }

    private void Announce(string message)
    {
        var text = FloatingTextManager.Instance != null ? FloatingTextManager.Instance.GetFromPool() : null;
        if (text != null) text.SetUp(message, Color.white, transform);
    }
}
