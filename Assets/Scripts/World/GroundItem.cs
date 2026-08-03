using UnityEngine;

/// <summary>
/// 바닥에 떨어져 있는 물건. 획득 경로가 뭐였든(상점 구매/엘리트 드랍/주머니에서 버리기/
/// 장착하다 밀려난 것) 전부 일단 바닥에 떨어진다 — 자동으로 장착되는 경로는 없다.
///
///   F 짧게 누름  → 되찾기(주머니 습득 / 슬롯 장착 / 장비 착용).
///   F 길게 누름  → 분해. 골드를 받고 사라진다.
///
/// 담을 수 있는 건 세 가지다. 상자를 세 개 만들지 않고 한 프리팹이 셋 다 처리한다 —
/// 바닥에서 하는 일(줍기/분해/정보창)이 똑같고, 다른 건 '되찾을 때 어디로 가느냐'뿐이라서다.
///   · ItemSO            = 주머니 아이템 → 주머니로
///   · MinionDataSO      = 소환수 카드   → 자기 역할 슬롯으로(메인/서브)
///   · EquipmentInstance = 장비 한 자루  → 착용 슬롯으로(굴린 스킬·강화레벨까지 그대로 보존)
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

    [Tooltip("소환수·장비를 분해할 때 판매가의 몇 배를 돌려줄지.\n" +
             "주머니 아이템은 이 값을 안 쓰고 티어별 고정 환급표(ItemTierRules.Refund)를 그대로 쓴다.")]
    [SerializeField] private float salvageRatio = 0.45f;

    [Tooltip("분해가 진행될 때 아이콘이 이 색으로 물든다.")]
    [SerializeField] private Color disassembleTint = new Color(1f, 0.35f, 0.25f);

    // 셋 중 정확히 하나만 채워진다.
    private ItemSO _item;
    private MinionDataSO _minion;
    private EquipmentInstance _equip;

    private SpriteRenderer _sr;
    private Tooltip _info;

    public ItemSO Item => _item;

    private static GroundItem _prefab;

    // ── 드랍 ──────────────────────────────────────────────────────────
    // pos 를 비우면 플레이어 발밑이다. 장착하다 밀려난 물건은 전부 그 경우라 호출부가 짧아진다.

    /// <summary>주머니 아이템을 바닥에 떨어뜨린다.</summary>
    public static GroundItem Drop(ItemSO so, Vector3? pos = null)
    {
        if (so == null) return null;
        var inst = Spawn(pos);
        if (inst != null) inst.SetItem(so);
        return inst;
    }

    /// <summary>소환수 카드를 바닥에 떨어뜨린다(슬롯에서 밀려났을 때).</summary>
    public static GroundItem Drop(MinionDataSO so, Vector3? pos = null)
    {
        if (so == null) return null;
        var inst = Spawn(pos);
        if (inst != null) inst.SetMinion(so);
        return inst;
    }

    /// <summary>장비 한 자루를 바닥에 떨어뜨린다. 인스턴스째 들고 있어서 굴린 스킬과 강화레벨이 보존된다.</summary>
    public static GroundItem Drop(EquipmentInstance equip, Vector3? pos = null)
    {
        if (equip == null || equip.baseData == null) return null;
        var inst = Spawn(pos);
        if (inst != null) inst.SetEquipment(equip);
        return inst;
    }

    /// <summary>
    /// 프리팹을 집어와 pos 주변으로 살짝 흩어서 놓는다(여러 개가 완전히 겹치지 않게).
    /// pos 는 보통 플레이어나 상점 진열대 위치라 벽 밖으로 튀어나갈 걱정은 없다.
    /// </summary>
    private static GroundItem Spawn(Vector3? pos)
    {
        if (_prefab == null) _prefab = Resources.Load<GroundItem>(PREFAB_RESOURCE);
        if (_prefab == null)
        {
            Debug.LogError($"[GroundItem] Resources 에서 '{PREFAB_RESOURCE}' 프리팹을 못 찾았다. " +
                            "아무것도 떨어뜨릴 수 없다.");
            return null;
        }

        Vector3 at = pos ?? PlayerPos();
        Vector2 jitter = Random.insideUnitCircle * 0.4f;
        return Instantiate(_prefab, at + new Vector3(jitter.x, jitter.y, 0f), Quaternion.identity);
    }

    private static Vector3 PlayerPos()
        => GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null
            ? GameManager.Instance.PLAYERCONTROLLER.transform.position
            : Vector3.zero;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (infoCanvas != null) infoCanvas.SetActive(false);
    }

    // ── 내용물 설정 ───────────────────────────────────────────────────
    /// <summary>어떤 아이템인지 정한다. 아이콘이 없으면 프리팹의 기본 스프라이트를 그대로 쓴다.</summary>
    public void SetItem(ItemSO so)
    {
        _item = so; _minion = null; _equip = null;
        ApplyIcon(so != null ? so.icon : null);
    }

    /// <summary>어떤 소환수 카드인지 정한다.</summary>
    public void SetMinion(MinionDataSO so)
    {
        _minion = so; _item = null; _equip = null;
        ApplyIcon(so != null ? so.ResolveIcon() : null);
    }

    /// <summary>어떤 장비인지 정한다.</summary>
    public void SetEquipment(EquipmentInstance inst)
    {
        _equip = inst; _item = null; _minion = null;
        ApplyIcon(inst != null && inst.baseData != null ? inst.baseData.icon : null);
    }

    private void ApplyIcon(Sprite icon)
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) return;
        if (icon != null) _sr.sprite = icon;
        _sr.color = Color.white;
    }

    private bool IsEmpty => _item == null && _minion == null && _equip == null;

    /// <summary>정보창 제목.</summary>
    private string DisplayName()
    {
        if (_item != null) return _item.DisplayName;
        if (_minion != null) return _minion.ResolveTitle();
        if (_equip != null && _equip.baseData != null)
            return _equip.enhanceLevel > 0
                ? $"{_equip.baseData.equipmentName} +{_equip.enhanceLevel}"
                : _equip.baseData.equipmentName;
        return "";
    }

    /// <summary>되찾으면 어디로 가는지(프롬프트 문구용).</summary>
    private string RetrieveVerb()
        => _item != null ? "습득" : _equip != null ? "착용" : "장착";

    /// <summary>
    /// 분해 시 돌려받는 골드.
    /// 주머니 아이템은 티어별 고정 환급표를 쓰고, 소환수·장비는 판매가 × salvageRatio 다.
    /// 장비는 강화에 부은 골드까지 원금에 얹는다 — 안 그러면 5강 장비가 밀려났을 때 1500G 를
    /// 통째로 날리게 되고, 그러면 장비를 바꾸는 선택 자체를 아무도 안 하게 된다.
    /// </summary>
    private int SalvageValue()
    {
        if (_item != null) return _item.Refund;
        if (_minion != null) return Mathf.RoundToInt(_minion.shopCost * salvageRatio);

        if (_equip != null && _equip.baseData != null)
        {
            int principal = _equip.baseData.shopCost + SpentOnEnhance(_equip.enhanceLevel);
            return Mathf.RoundToInt(principal * salvageRatio);
        }
        return 0;
    }

    /// <summary>0강에서 level 강까지 올리는 데 든 총 골드(강화 상점 가격표 그대로).</summary>
    private static int SpentOnEnhance(int level)
    {
        if (level <= 0) return 0;

        var dm = GameManager.Instance != null ? GameManager.Instance.dataManager : null;
        var reg = dm != null ? dm.SHOP_REGISTRY : null;
        if (reg == null) return 0;

        int sum = 0;
        for (int i = 0; i < level; i++) sum += reg.enhanceCost + i * reg.enhanceCostPerLevel;
        return sum;
    }

    // ── IInteractable ─────────────────────────────────────────────────
    public string InteractionPrompt
        => IsEmpty ? "" : $"F 짧게: {RetrieveVerb()} · 길게: 분해 (+{SalvageValue()}G)";

    /// <summary>F 짧게 = 되찾기.</summary>
    public bool Interact(GameObject interactor)
    {
        if (IsEmpty) return false;

        if (_item != null)
        {
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
        }
        else if (_minion != null)
        {
            // 슬롯이 차 있으면 EquipMinion 이 알아서 그 자리 것을 바닥에 뱉는다 → 자연스러운 맞교환.
            if (InventoryManager.Instance == null || !InventoryManager.Instance.EquipMinion(_minion))
            {
                Announce("장착할 수 없다");
                return false;
            }
        }
        else
        {
            // 장비도 마찬가지 — 착용 중이던 자루가 이 자리에 떨어진다.
            if (PlayerSkillInventoryManager.Instance == null)
            {
                Announce("착용할 수 없다");
                return false;
            }
            PlayerSkillInventoryManager.Instance.EquipEquipment(_equip);
        }

        Destroy(gameObject);
        return true;
    }

    public void OnFocused(GameObject interactor)
    {
        if (IsEmpty) return;
        if (infoCanvas == null || infoPrefab == null)
        {
            Debug.LogWarning($"[GroundItem] {name}: infoCanvas/infoPrefab 이 비어 있어 정보창이 안 뜬다.");
            return;
        }

        infoCanvas.SetActive(true);
        if (_info == null) _info = Instantiate(infoPrefab, infoCanvas.transform).GetComponent<Tooltip>();
        if (_info == null) return;

        if (_info.name != null) _info.name.text = DisplayName();

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

    /// <summary>F 길게 = 분해. 골드를 주고 사라진다.</summary>
    public void OnHoldComplete(GameObject interactor)
    {
        if (IsEmpty) return;

        int refund = SalvageValue();
        InventoryManager.Instance?.AddGold(refund);
        Announce($"+{refund}G");
        Debug.Log($"<color=cyan>[GroundItem]</color> '{DisplayName()}' 분해 → +{refund}G");

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
