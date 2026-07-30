using UnityEngine;

/// <summary>
/// 바닥에 떨어져 있는 아이템. 아이템은 획득 경로가 뭐였든(상점 구매/엘리트 드랍/주머니에서 버리기)
/// 전부 일단 바닥에 떨어진다 — 자동으로 장착되는 경로는 없다.
///
///   F 짧게 누름  → 주머니에 습득. 꽉 찼으면 안내만 뜨고 아이템은 바닥에 남는다.
///   F 3초 누름   → 분해. 티어별 환급 골드를 받고 사라진다(ItemTierRules.Refund).
///
/// 홀드 타이머는 PlayerController 가 굴린다(IHoldInteractable).
///
/// 프리팹은 Resources 에 두고 Drop() 이 알아서 집어 온다 — 드랍하는 쪽(상점/보상상자/주머니 UI)이
/// 전부 프리팹 참조를 인스펙터로 물고 있으면 한 곳만 빠뜨려도 조용히 아무것도 안 떨어진다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class GroundItem : MonoBehaviour, IInteractable, IHoldInteractable
{
    /// <summary>Resources 안의 프리팹 이름. 폴더 위치는 Resources 아래 아무 곳이나 상관없다.</summary>
    private const string PREFAB_RESOURCE = "GroundItem";

    [Header("정보 표시 (상점 SellItem 과 같은 구조)")]
    [Tooltip("자식 World Space Canvas. 플레이어가 근처에 오면 켜진다.")]
    [SerializeField] private GameObject infoCanvas;
    // [주의] 타입은 GameObject 여야 한다. Tooltip(컴포넌트)으로 두면 인스펙터/YAML 의 프리팹 참조가
    // 프리팹 '루트 GameObject' 를 가리켜서 조용히 null 이 되고, 정보창이 아예 안 뜬다(한 번 그랬다).
    // 상점 SellItem.explainPrefab 도 같은 이유로 GameObject 다.
    [Tooltip("이름/설명 두 줄짜리 툴팁 프리팹. 상점이 쓰는 것과 같은 걸 쓰면 된다.")]
    [SerializeField] private GameObject infoPrefab;

    [Header("분해")]
    [Tooltip("F 를 이만큼 누르고 있으면 분해된다.")]
    [SerializeField] private float disassembleHoldSeconds = 1f;

    [Tooltip("분해가 진행될 때 아이콘이 이 색으로 물든다.")]
    [SerializeField] private Color disassembleTint = new Color(1f, 0.35f, 0.25f);

    private ItemSO _item;
    private SpriteRenderer _sr;
    private Tooltip _info;

    public ItemSO Item => _item;

    private static GroundItem _prefab;

    // ── 드랍 ──────────────────────────────────────────────────────────
    /// <summary>
    /// 아이템을 바닥에 떨어뜨린다. pos 주변으로 살짝 흩어서 여러 개가 완전히 겹치지 않게 한다.
    /// pos 는 보통 플레이어나 상점 진열대 위치라 벽 밖으로 튀어나갈 걱정은 없다.
    /// </summary>
    public static GroundItem Drop(ItemSO so, Vector3 pos)
    {
        if (so == null) return null;

        if (_prefab == null) _prefab = Resources.Load<GroundItem>(PREFAB_RESOURCE);
        if (_prefab == null)
        {
            Debug.LogError($"[GroundItem] Resources 에서 '{PREFAB_RESOURCE}' 프리팹을 못 찾았다. " +
                            "아이템을 떨어뜨릴 수 없다.");
            return null;
        }

        Vector2 jitter = Random.insideUnitCircle * 0.4f;
        var inst = Instantiate(_prefab, pos + new Vector3(jitter.x, jitter.y, 0f), Quaternion.identity);
        inst.SetItem(so);
        return inst;
    }

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (infoCanvas != null) infoCanvas.SetActive(false);
    }

    /// <summary>어떤 아이템인지 정한다. 아이콘이 없으면 프리팹의 기본 스프라이트를 그대로 쓴다.</summary>
    public void SetItem(ItemSO so)
    {
        _item = so;
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr != null)
        {
            if (so != null && so.icon != null) _sr.sprite = so.icon;
            _sr.color = Color.white;
        }
    }

    // ── IInteractable ─────────────────────────────────────────────────
    public string InteractionPrompt
        => _item == null ? "" : $"F 짧게: 습득 · 길게: 분해 (+{_item.Refund}G)";

    /// <summary>F 짧게 = 습득.</summary>
    public bool Interact(GameObject interactor)
    {
        if (_item == null) return false;

        var pouch = ItemPouch.Instance;
        if (pouch == null)
        {
            Debug.LogWarning("[GroundItem] ItemPouch.Instance 가 없어 습득할 수 없다.");
            return false;
        }

        if (!pouch.TryAdd(_item))
        {
            // 꽉 찼으면 아이템은 그대로 바닥에 남긴다. 주머니를 비우고 다시 줍거나, 길게 눌러 분해하면 된다.
            Announce("주머니가 꽉 찼다");
            return false;
        }

        Destroy(gameObject);
        return true;
    }

    public void OnFocused(GameObject interactor)
    {
        if (_item == null) return;
        if (infoCanvas == null || infoPrefab == null)
        {
            Debug.LogWarning($"[GroundItem] {name}: infoCanvas/infoPrefab 이 비어 있어 정보창이 안 뜬다.");
            return;
        }

        infoCanvas.SetActive(true);
        if (_info == null) _info = Instantiate(infoPrefab, infoCanvas.transform).GetComponent<Tooltip>();
        if (_info == null) return;

        if (_info.name != null) _info.name.text = _item.DisplayName;

        // 둘째 줄은 비워둔다. 조작법("F 짧게: 습득 …")을 상시로 띄우면 잔소리가 된다 —
        // 분해를 누르고 있는 동안에만 OnHoldProgress 가 여기에 진행률을 쓴다.
        if (_info.price != null) _info.price.text = "";
    }

    public void OnLostFocus(GameObject interactor)
    {
        if (_info != null) { Destroy(_info.gameObject); _info = null; }
        if (infoCanvas != null) infoCanvas.SetActive(false);
        ResetHoldVisual();
    }

    // ── IHoldInteractable (분해) ───────────────────────────────────────
    public float HoldSeconds => disassembleHoldSeconds;

    public void OnHoldProgress(float t01)
    {
        if (_sr != null) _sr.color = Color.Lerp(Color.white, disassembleTint, t01);

        if (_info != null && _info.price != null)
            _info.price.text = t01 <= 0f ? "" : $"분해 중… {Mathf.RoundToInt(t01 * 100f)}%";
    }

    /// <summary>F 길게 = 분해. 티어별 환급 골드를 주고 사라진다.</summary>
    public void OnHoldComplete(GameObject interactor)
    {
        if (_item == null) return;

        int refund = _item.Refund;
        InventoryManager.Instance?.AddGold(refund);
        Announce($"+{refund}G");
        Debug.Log($"<color=cyan>[GroundItem]</color> '{_item.DisplayName}'({ItemTierRules.Label(_item.tier)}) 분해 → +{refund}G");

        Destroy(gameObject);
    }

    private void ResetHoldVisual()
    {
        if (_sr != null) _sr.color = Color.white;
    }

    /// <summary>짧은 안내 문구를 아이템 위에 띄운다(데미지 팝업과 같은 풀).</summary>
    private void Announce(string msg)
    {
        var mgr = FloatingTextManager.Instance;
        if (mgr == null) return;
        var t = mgr.GetFromPool();
        if (t != null) t.SetUp(msg, Color.white, transform);
    }
}
