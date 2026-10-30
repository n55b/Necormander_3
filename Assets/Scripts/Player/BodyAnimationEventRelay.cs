using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Body의 Animator에서 발생하는 Animation Event(예: CanChangeAnimState)를
/// 상위(부모)에 있는 PlayerController로 전달합니다.
/// Animation Event는 Animator가 붙어있는 GameObject에서만 메서드를 찾기 때문에,
/// PlayerController가 부모(루트)에 있는 구조(Player Melee 등)에서는 이 릴레이가 필요합니다.
/// </summary>
public class BodyAnimationEventRelay : MonoBehaviour
{
    private PlayerController _controller;
    private MeleeCombatController _melee;
    private Tilemap[] _sceneGrounds;

    [Header("걷기 발자국 효과")]
    [SerializeField] private GameObject walkSmogEffectPrefab;
    [SerializeField] private Transform footEffectPoint;

    public GameObject WalkEffectPrefab => walkSmogEffectPrefab;

    private void Awake()
    {
        _controller = GetComponentInParent<PlayerController>();
        _melee = GetComponentInParent<MeleeCombatController>();
        if (_controller == null)
        {
            Debug.LogWarning($"[BodyAnimationEventRelay] {gameObject.name}: 부모에서 PlayerController를 찾지 못했습니다.");
        }
    }

    /// <summary>
    /// Attack 애니메이션 클립의 Animation Event(CanChangeAnimState)에서 호출됩니다.
    /// </summary>
    public void CanChangeAnimState()
    {
        _controller?.CanChangeAnimState();
    }

    /// <summary>
    /// Attack 애니메이션 클립의 실제 타격 프레임(Animation Event)에서 호출됩니다.
    /// </summary>
    public void OnAttackHitFrame()
    {
        _melee?.OnAttackHitFrame();
    }

    /// <summary>
    /// 걷기 애니메이션 클립의 Animation Event(발이 땅에 닿는 프레임)에서 호출됩니다.
    /// </summary>
    public void OnFootstep()
    {
        if (_controller == null || _controller.IsInputBlocked || _controller.IsDashing
            || _controller.IsCCed || _controller.MoveInput.sqrMagnitude < .001f
            || PlayerController.IsGameplayPaused || Time.deltaTime <= 0f) return;
        Vector3 spawnPos = footEffectPoint != null ? footEffectPoint.position : transform.position;
        var settings = PolishFeedback.Catalog;
        if (settings != null)
        {
            var tile = GroundTile(_controller.transform.position);
            bool grass = tile != null && settings.grassFootstepTiles != null
                && System.Array.IndexOf(settings.grassFootstepTiles, tile) >= 0;
            PolishFeedback.PlaySound(grass ? FeedbackCue.GrassFootstep : FeedbackCue.GroundFootstep, spawnPos);
            PolishFeedback.Play(FeedbackCue.FootSurface, spawnPos, Vector2.down,
                tint: MapGenerator.Instance != null ? MapGenerator.Instance.FootstepColor(spawnPos) : (Color?)null);
        }
        if (walkSmogEffectPrefab == null || !PolishFeedback.ExistingEnabled(ExistingFeedbackCue.WalkSmoke)) return;

        var data = GameManager.Instance != null ? GameManager.Instance.dataManager : null;
        GameObject fx = data != null ? data.TakePreparedObject(walkSmogEffectPrefab, spawnPos) : null;
        if (fx == null) fx = Instantiate(walkSmogEffectPrefab, spawnPos, Quaternion.identity);

        // 플레이어 루트(Player Melee)의 localScale.x 부호가 곧 '현재 바라보는 방향'입니다.
        // (PlayerController.Update()에서 이동 방향에 따라 뒤집어서 유지하는 값)
        float rootScaleX = _controller != null ? _controller.transform.localScale.x : 1f;
        float facingSign = rootScaleX < 0f ? -1f : 1f;

        Vector3 scale = fx.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * facingSign;
        fx.transform.localScale = scale;
    }

    private TileBase GroundTile(Vector3 position)
    {
        var tile = MapGenerator.Instance != null ? MapGenerator.Instance.GroundTileAt(position) : null;
        if (tile != null) return tile;
        // 마을은 통합 타일맵을 만들지 않는다. 최초 발걸음 때 한 번만 찾는다.
        if (_sceneGrounds == null)
            _sceneGrounds = System.Array.FindAll(FindObjectsByType<Tilemap>(FindObjectsSortMode.None),
                map => map.name.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0);
        foreach (var map in _sceneGrounds)
            if (map != null && map.isActiveAndEnabled && (tile = map.GetTile(map.WorldToCell(position))) != null) return tile;
        return null;
    }
}
