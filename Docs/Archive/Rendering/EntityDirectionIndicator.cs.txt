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
    public enum Kind { [UnityEngine.InspectorName("자동")] Auto, [UnityEngine.InspectorName("플레이어")] Player, [UnityEngine.InspectorName("적")] Enemy }

    [Tooltip("Auto = 부모에 PlayerController가 있으면 플레이어, 없으면 적으로 자동 판별.")]
    [SerializeField] private Kind kind = Kind.Auto;
    [Tooltip("스프라이트 기본이 '아래(-Y)'를 봐서 +90 보정. 스프라이트 방향이 다르면 이 값만 바꾸면 됨.")]
    [SerializeField] private float baseAngle = 90f;
    // ─── [TODO] 인디케이터 크기 줄이기 ─────────────────────────────────────────────
    // 크기는 아래 두 가지 중 하나로 조절한다.
    //  (A) 권장: 더 작은 해상도로 새로 그린 스프라이트로 교체
    //      - 가장 간단: Resources/Sprites/UI/Arrow_Indicator.png 의 "Arrow_Indicator_2" 슬라이스 그림을 교체
    //        (각 프리팹 DirectionIndicator 의 SpriteRenderer 가 이 스프라이트를 직접 참조 중)
    //      - 주의: spriteOverride 는 SpriteRenderer 의 Sprite 가 비어 있을 때만 쓰인다(Awake 참고).
    //        새 스프라이트를 별도 에셋으로 만들면 각 프리팹의 SpriteRenderer.Sprite 를 바꾸거나 비워야 함
    //      - PPU 는 다른 아트와 같은 32 를 유지할 것 (PixelArtUpscaleRig 의 assetsPPU 와 일치해야 도트 1:1)
    //      - RotSprite 회전 캐시(RotSpriteCache)는 새 스프라이트로 자동 생성되므로 따로 할 일 없음
    //  (B) indicatorScale 값 변경 — 단, 반드시 정수(1, 2, 3…)만 사용
    //      - 0.5, 1.6 같은 비정수 배율은 픽셀 RT 에서 도트 두께가 들쭉날쭉해져 다시 뭉개진다
    //      - 그래서 '지금보다 작게'는 (B)로는 불가능(1 이 최소) → (A) 로 해야 함
    //  ※ 스프라이트를 바꾸면 회전 중심(피벗)과 baseAngle(기본 방향)도 새 그림에 맞는지 확인할 것.
    // ───────────────────────────────────────────────────────────────────────────────
    [Tooltip("비워두면 Arrow_Indicator.png의 하단 로케이터를 사용합니다.")]
    [SerializeField] private Sprite spriteOverride;
    [Tooltip("로케이터 크기. 이 값이 자식 Transform의 localScale에 그대로 적용된다(여기서만 조절하면 됨). 플레이어는 좀 더 크게.")]
    [Min(0.01f)]
    [SerializeField] private float indicatorScale = 1.6f;
    [Tooltip("플레이어 로케이터를 저해상도 픽셀 RT 대신 Main Camera(화면 해상도)로 그린다. 회전해도 도트가 뭉개지지 않는다.\n대신 월드 그림 전체보다 위에, 다른 UI(UI Sorting Layer·오버레이 캔버스)보다는 아래에 그려진다.")]
    [SerializeField] private bool renderAtScreenResolution = false;
    [Tooltip("픽셀 RT 로 그릴 때 RotSprite 로 미리 회전시켜 둘 방향 수. 클수록 회전이 부드럽다(64 = 5.6° 간격).\n0 이면 예전처럼 transform 을 직접 회전(도트가 뭉개짐).")]
    [Min(0)]
    [SerializeField] private int rotationSteps = 64;
    private Sprite[] _rotSprites;

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
    /// <summary>은신한 본체는 하단 로케이터와 화면 밖 화살표에서도 숨긴다.</summary>
    public bool IsActive => isActiveAndEnabled && IsAlive
        && (_entity == null || _entity.SpriteRenderer == null || _entity.SpriteRenderer.enabled);

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

    /// <summary>인스펙터의 크기 값을 자식 Transform localScale에 적용. 부모 좌우반전(±scale/flipX)과 무관하게 항상 양수 균등 스케일.</summary>
    private void ApplyScale()
    {
        if (indicatorScale > 0f)
            transform.localScale = new Vector3(indicatorScale, indicatorScale, 1f);
    }

#if UNITY_EDITOR
    // 에디터에서 슬라이더를 움직이면 곧바로 크기가 반영되도록(플레이 없이도 프리팹에서 바로 확인).
    private void OnValidate() => ApplyScale();
#endif

    private void Awake()
    {
        ApplyScale();
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

        if (renderAtScreenResolution && _resolved == Kind.Player)
            UseScreenResolution();
        else if (rotationSteps > 0)
            _rotSprites = RotSpriteCache.Get(_sr.sprite, rotationSteps); // [크기 수정 시] 여기서 쓰는 _sr.sprite 가 회전 원본. 작은 스프라이트로 바꾸면 자동 반영
    }

    /// <summary>
    /// PixelArtUpscaleRig 는 UI 레이어(5)만 Main Camera 가 화면 해상도로 그리고, 나머지는 아트 픽셀 1:1 RT 로 그린다.
    /// 연속 각도로 회전하는 스프라이트가 RT 격자에 다시 샘플링되면 도트가 각도마다 뭉개지므로 UI 레이어로 옮긴다.
    /// Main Camera 에서는 월드 전체가 BackGround 레이어의 쿼드 한 장이라 Y 정렬은 의미가 없다 →
    /// YSort 를 끄고 Default 레이어 최저 order 로 두어 쿼드보다 위, 다른 UI(UI Sorting Layer 등)보다 아래에 그린다.
    /// (Default Sorting Layer 를 유지하는 건 Light2D 조명을 그대로 받기 위함.)
    /// </summary>
    private void UseScreenResolution()
    {
        gameObject.layer = 5; // UI
        var ySort = GetComponent<YSortableObject>();
        if (ySort != null) ySort.enabled = false;
        _sr.sortingLayerName = "Default";
        _sr.sortingOrder = short.MinValue + 1;
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

        bool show = _sr.sprite != null && _unit != null && IsActive;
        if (_sr.enabled != show) _sr.enabled = show;
        if (!show) return;

        Vector2 aim = ComputeAim();
        if (aim.sqrMagnitude > 0.0001f) _lastAim = aim.normalized;

        float phi = Mathf.Atan2(_lastAim.y, _lastAim.x) * Mathf.Rad2Deg;
        // 부모가 X미러(플레이어 좌향)면 좌우대칭 각도 보정. (아니면 그대로)
        bool flipped = transform.parent != null && transform.parent.lossyScale.x < 0f;
        float r = flipped ? (180f - phi + baseAngle) : (phi + baseAngle);
        if (_rotSprites != null)
        {
            // 미리 회전된 도트 스프라이트로 교체. transform 은 회전시키지 않는다.
            int n = _rotSprites.Length;
            int idx = Mathf.RoundToInt(r / (360f / n)) % n;
            if (idx < 0) idx += n;
            if (_sr.sprite != _rotSprites[idx]) _sr.sprite = _rotSprites[idx];
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            transform.localRotation = Quaternion.Euler(0f, 0f, r);
        }
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
            return _lastAim;
        }

        // 적: (1) AI 조준 오버라이드가 살아있으면 우선 (돌진 충전 중 실시간 재조준 등 — 멈춰 있어도 방향이 바뀐다)
        if (Time.time < _aimOverrideExpiry) return _aimOverride;

        // (2) 공격 대상(타겟)이 있으면 그 방향을 바라본다. 궁수 등 원거리 유닛도 이동하며 타겟을 조준해야
        // 자연스럽기 때문에, 이동 방향보다 타겟 방향을 우선한다. (넉백으로 밀려도 타겟 위치는 그대로라 영향 없음)
        if (_entity != null && _entity.Target != null)
        {
            Vector2 toTarget = (Vector2)_entity.Target.position - (Vector2)_unit.position;
            if (toTarget.sqrMagnitude > 0.0001f) return toTarget;
        }

        // (3) 타겟이 없으면 유닛의 '앞' = 실제 진행(이동) 방향. 스프라이트 좌/우와 무관하게 연속 각도(예: 우상 53°).
        if (_entity != null)
        {
            Vector2 v = _entity.MoveVelocity;
            if (v.sqrMagnitude > 0.01f) return v; // 0.1 유닛/초 이상 움직일 때만 갱신
        }

        // (4) 멈춰 있고 타겟/오버라이드도 없으면 마지막 진행 방향 유지 (일반 공격 중 고정)
        return _lastAim;
    }
}
