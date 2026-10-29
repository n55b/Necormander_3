using UnityEngine;
using UnityEngine.AI;

/// <summary>아이템/미니언 공용 바닥 픽업. F로 습득. 긴 F 분해는 미니언에만 적용한다.</summary>
public class GroundItem : MonoBehaviour, IInteractable, IHoldInteractable
{
    [SerializeField] private SpriteRenderer iconRenderer;
    [Tooltip("아이템이 바닥에 있을 때 공통으로 보여줄 그림. 미니언에는 적용하지 않는다.")]
    [InspectorName("바닥 아이템 공통 그림")]
    [SerializeField] private Sprite itemDropSprite;
    [Tooltip("아이콘을 아직 만들지 않은 아이템의 주머니/상점용 대체 그림.")]
    [InspectorName("아이콘 미지정 시 대체 그림")]
    [SerializeField] private Sprite fallbackItemIcon;
    [SerializeField] private bool alignIconToGround;
    [SerializeField, Min(0f)] private float labelGap = 0.15f;
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
    public float HoldSeconds => _minion != null ? disassembleHoldSeconds : 0f;
    public string InteractionPrompt => _minion != null ? $"F: 미니언 확인 · 길게: 분해 (체력 +{minionRecycleHeal})"
        : _item != null ? "F: 습득" : "";

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
            if (prefab != null) _fallbackItemIcon = prefab.fallbackItemIcon != null ? prefab.fallbackItemIcon : prefab.GetComponent<SpriteRenderer>().sprite;
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

    /// <summary>마우스는 방향만 정한다. 벽 너머/멀리 떨어진 바닥이 아닌 플레이어 주변의 도달 가능한 바닥을 고른다.</summary>
    public static bool TryFindNearbyDropPoint(Vector3 playerPosition, Vector3 mousePosition, float distance, out Vector3 point)
    {
        point = playerPosition;
        distance = Mathf.Max(0.1f, distance);
        if (!TryFindDropPoint(playerPosition, 0.5f, out Vector3 start)) return false;
        Vector2 direction = (Vector2)(mousePosition - playerPosition);
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        Vector3 desired = playerPosition + (Vector3)(direction * distance);

        // 먼저 원래 놓으려던 지점에서 가장 가까운 바닥을 고른다. 얇은 벽 너머 바닥은 제외한다.
        if (TryFindDropPoint(desired, distance, out Vector3 nearest) && Nearby(nearest)
            && !NavMesh.Raycast(start, nearest, out _, NavMesh.AllAreas))
        {
            Vector2 travel = nearest - start;
            if (((Vector2)nearest - SkillCombatUtil.ClampToWall(start, travel, travel.magnitude, 0.1f)).sqrMagnitude < 0.0001f)
            { point = nearest; return true; }
        }

        // 가장 가까운 점이 벽 반대편이면, 플레이어 쪽 벽/물 경계에서 멈춘 지점을 사용한다.
        Vector2 delta = desired - start;
        Vector3 fallback = SkillCombatUtil.ClampToWall(start, delta, delta.magnitude, 0.1f);
        fallback.z = start.z;
        if (NavMesh.Raycast(start, fallback, out var edge, NavMesh.AllAreas)) fallback = edge.position;
        if (TryFindDropPoint(fallback, 0.1f, out Vector3 safe) && Nearby(safe))
        { point = safe; return true; }
        if (!Nearby(start)) return false;
        point = start;
        return true;

        bool Nearby(Vector3 candidate) => ((Vector2)(candidate - playerPosition)).sqrMagnitude <= distance * distance + 0.01f;
    }

    private void Awake()
    {
        _sr = iconRenderer != null ? iconRenderer : GetComponent<SpriteRenderer>();
        if (infoCanvas != null) infoCanvas.SetActive(false);
    }

    public void SetItem(ItemSO item) { _item = item; _minion = null; SetIcon(itemDropSprite != null ? itemDropSprite : ItemIcon(item)); }
    public void SetMinion(MinionDataSO minion) { _minion = minion; _item = null; SetIcon(minion != null ? minion.minionIcon : null); }
    private void SetIcon(Sprite icon)
    {
        if (_sr == null) _sr = iconRenderer != null ? iconRenderer : GetComponent<SpriteRenderer>();
        if (icon != null) _sr.sprite = icon;
        _sr.color = Color.white;
        if (alignIconToGround && _sr.transform != transform && _sr.sprite != null)
        {
            // 전투용 피벗/빈 캔버스와 무관하게 픽업 그림의 실제 발밑을 상호작용 위치에 맞춘다.
            Bounds bounds = _sr.sprite.bounds;
            Vector3 scale = _sr.transform.localScale;
            _sr.transform.localPosition = new Vector3(-bounds.center.x * scale.x, -bounds.min.y * scale.y, 0f);
            if (infoCanvas != null)
                infoCanvas.transform.localPosition = new Vector3(0f, bounds.size.y * scale.y + labelGap, 0f);
        }
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
        if (ItemPouch.Instance == null || !ItemPouch.Instance.TryAdd(_item)) { Announce("최대 중첩에 도달했습니다"); return false; }
        PlayPickupFeedback();
        Consume();
        return true;
    }

    public void PlayPickupFeedback()
    {
        PolishFeedback.Pickup(_sr != null ? _sr.bounds.center : transform.position,
            _item != null ? ItemIcon(_item) : _sr != null ? _sr.sprite : null,
            _sr != null ? _sr.transform.lossyScale : Vector3.one);
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
        if (_minion == null) progress = 0f;
        if (_sr != null) _sr.color = Color.Lerp(Color.white, disassembleTint, progress);
        if (info != null && info.price != null) info.price.text = progress <= 0f ? "" : $"분해 중… {progress:P0}";
    }

    public void OnHoldComplete(GameObject interactor)
    {
        if (!IsAvailable || _minion == null) return;
        if (_minion != null)
        {
            var player = interactor != null ? interactor.GetComponent<PlayerController>() : null;
            if (player == null || player.Stat == null || player.Stat.Health.IsDead) return;
            player.Stat.Health.Heal(minionRecycleHeal);
            Announce($"체력 +{minionRecycleHeal}");
        }
        Consume();
    }

    private void Announce(string message)
        => FloatingTextManager.ShowNotice(message, Color.white, transform); // 아이템이 곧 파괴돼도 텍스트는 끝까지 보인다
}
