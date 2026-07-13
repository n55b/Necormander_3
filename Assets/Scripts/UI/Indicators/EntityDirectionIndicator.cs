using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 유닛(플레이어/적)의 '공격할(바라보는) 방향'을 가리키는 로케이터.
/// <b>프리팹의 자식 노드("DirectionIndicator", SpriteRenderer 포함)에 직접 붙여</b> 쓴다.
///  - 플레이어: 마우스 조준 방향
///  - 적:       실제 진행(이동) 방향 = 유닛의 '앞'. 연속 각도(스프라이트 좌우와 무관), 멈추면 마지막 방향 유지.
///
/// 부모(유닛)의 좌우 반전 방식이 서로 다르다:
///  - 플레이어: 루트 transform.localScale.x = ±1 (자식도 함께 X미러링됨)
///  - 적:       SpriteRenderer.flipX (자식 transform엔 영향 없음)
/// 그래서 위치·크기는 프리팹에 authoring한 값(부모를 따라감)을 그대로 두고, 이 컴포넌트는
/// 오직 localRotation만 세팅한다. 부모가 X미러(플레이어)일 땐 MeleeCombatController와 동일한
/// 좌우대칭 각도 보정을 적용한다(로케이터는 좌우대칭 도형이라 미러링돼도 같은 그림).
///
/// 적 로케이터는 <see cref="EnemyIndicators"/> 정적 레지스트리에 등록되어
/// <see cref="OffscreenEnemyArrowManager"/>가 '화면 밖 화살표' 대상을 순회하게 한다.
/// kind=Auto면 부모에 PlayerController가 있으면 플레이어, 아니면 적으로 자동 판별하므로
/// (보스=BoneMasterController:BaseEntity 포함) team 세팅 타이밍과 무관하게 항상 올바르게 붙는다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class EntityDirectionIndicator : MonoBehaviour
{
    public enum Kind { Auto, Player, Enemy }

    [Tooltip("Auto = 부모에 PlayerController가 있으면 플레이어, 없으면 적으로 자동 판별.")]
    [SerializeField] private Kind kind = Kind.Auto;
    [Tooltip("스프라이트 기본이 '아래(-Y)'를 봐서 +90 보정. 스프라이트 방향이 다르면 이 값만 바꾸면 됨.")]
    [SerializeField] private float baseAngle = 90f;
    [Tooltip("비워두면 kind에 맞는 Arrow.aseprite 로케이터(Ally/Enemy_Locator)를 런타임에 자동 로드.")]
    [SerializeField] private Sprite spriteOverride;

    /// <summary>화면 밖 화살표가 순회할 적군 로케이터 레지스트리.</summary>
    public static readonly List<EntityDirectionIndicator> EnemyIndicators = new List<EntityDirectionIndicator>();

    private Kind _resolved;
    private Transform _unit;             // 방향/좌표를 읽어올 유닛 루트
    private BaseEntity _entity;
    private PlayerController _player;
    private CharacterHealth _health;
    private SpriteRenderer _sr;
    private Vector2 _lastAim = Vector2.right;
    private bool _registered;
    private bool _healthResolved;
    private Vector2 _aimOverride;
    private float _aimOverrideExpiry;
    private const float AimOverrideHold = 0.25f; // 매 프레임 갱신을 멈추면 이만큼 뒤 자동 해제

    public bool IsEnemy => _resolved == Kind.Enemy;
    public Vector2 WorldPosition => _unit != null ? (Vector2)_unit.position : (Vector2)transform.position;
    public bool IsAlive { get { EnsureHealth(); return _health == null || !_health.IsDead; } }
    /// <summary>화면 밖 화살표 대상 여부(활성 + 생존).</summary>
    public bool IsActive => isActiveAndEnabled && IsAlive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => EnemyIndicators.Clear();

    /// <summary>
    /// AI가 특정 상태(예: 돌진 충전 중 실시간 재조준)에서 인디케이터 방향을 강제한다. 이동 방향보다 우선.
    /// 매 프레임 갱신을 멈추면 <see cref="AimOverrideHold"/>초 뒤 자동 해제되어 이동 방향으로 복귀한다(별도 정리 불필요).
    /// </summary>
    public void SetAimOverride(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        _aimOverride = dir.normalized;
        _aimOverrideExpiry = Time.time + AimOverrideHold;
    }

    public void ClearAimOverride() => _aimOverrideExpiry = 0f;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _player = GetComponentInParent<PlayerController>();
        _entity = GetComponentInParent<BaseEntity>();

        _resolved = kind;
        if (_resolved == Kind.Auto)
            _resolved = (_player != null) ? Kind.Player : Kind.Enemy;

        _unit = (_resolved == Kind.Player && _player != null) ? _player.transform
              : (_entity != null ? _entity.transform : (transform.parent != null ? transform.parent : transform));

        if (_sr.sprite == null)
        {
            if (spriteOverride != null) _sr.sprite = spriteOverride;
            else _sr.sprite = (_resolved == Kind.Enemy) ? ArrowIndicatorSprites.EnemyLocator : ArrowIndicatorSprites.AllyLocator;
        }
    }

    private void OnEnable()
    {
        if (_resolved == Kind.Enemy && !_registered)
        {
            EnemyIndicators.Add(this);
            _registered = true;
        }
    }

    private void OnDisable()
    {
        if (_registered)
        {
            EnemyIndicators.Remove(this);
            _registered = false;
        }
    }

    private void EnsureHealth()
    {
        if (_healthResolved) return;
        // BaseEntity.Stats 는 부모 Awake에서 캐싱되므로, 자식 Awake 순서와 무관하도록 지연 해석.
        _healthResolved = true;
        if (_resolved == Kind.Player)
            _health = (_player != null && _player.Stat != null) ? _player.Stat.Health : null;
        else
            _health = (_entity != null && _entity.Stats != null) ? _entity.Stats.Health : null;
    }

    private void LateUpdate()
    {
        if (_sr == null) return;

        bool show = _sr.sprite != null && _unit != null && IsAlive;
        if (_sr.enabled != show) _sr.enabled = show;
        if (!show) return;

        Vector2 aim = ComputeAim();
        if (aim.sqrMagnitude > 0.0001f) _lastAim = aim.normalized;

        float phi = Mathf.Atan2(_lastAim.y, _lastAim.x) * Mathf.Rad2Deg;
        // 부모가 X미러(플레이어 좌향)면 좌우대칭 각도 보정. (아니면 그대로)
        bool flipped = transform.parent != null && transform.parent.lossyScale.x < 0f;
        float r = flipped ? (180f - phi + baseAngle) : (phi + baseAngle);
        transform.localRotation = Quaternion.Euler(0f, 0f, r);
    }

    private Vector2 ComputeAim()
    {
        if (_resolved == Kind.Player)
        {
            var cam = Camera.main;
            if (cam != null && Mouse.current != null)
            {
                Vector3 mw = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
                Vector2 d = (Vector2)mw - (Vector2)_unit.position;
                if (d.sqrMagnitude > 0.0001f) return d;
            }
            if (_player != null && _player.CurrentSkillAimDir.sqrMagnitude > 0.0001f)
                return _player.CurrentSkillAimDir;
            return _lastAim;
        }

        // 적: (1) AI 조준 오버라이드가 살아있으면 우선 (돌진 충전 중 실시간 재조준 등 — 멈춰 있어도 방향이 바뀐다)
        if (Time.time < _aimOverrideExpiry) return _aimOverride;

        // (2) 아니면 유닛의 '앞' = 실제 진행(이동) 방향. 스프라이트 좌/우와 무관하게 연속 각도(예: 우상 53°).
        if (_entity != null)
        {
            Vector2 v = _entity.MoveVelocity;
            if (v.sqrMagnitude > 0.01f) return v; // 0.1 유닛/초 이상 움직일 때만 갱신
        }

        // (3) 멈춰 있고 오버라이드도 없으면 마지막 진행 방향 유지 (일반 공격 중 고정)
        return _lastAim;
    }
}
