using UnityEngine;

/// <summary>우클릭을 누르는 동안 게이지로 전방 공격을 방어한다. 기존 입력/성공 이벤트 배선을 유지한다.</summary>
public class PlayerParryController : MonoBehaviour
{
    [SerializeField] private GameObject parryTelegraphPrefab;
    [SerializeField] private string emptyMessage = "우클릭 없음!";
    [Header("가드 게이지")]
    [SerializeField, Min(1f)] private float maxGuard = 100f;
    [SerializeField, Min(0f), Tooltip("가드를 올린 뒤 실제 방어까지 이 시간 이내면 소모량이 절반이다.")]
    private float perfectGuardWindow = 0.2f;
    [SerializeField, Min(0f)] private float guardRegenPerSecond = 3f;
    [SerializeField, Min(0f), Tooltip("가드를 내린 뒤 자연 회복을 기다리는 시간.")]
    private float guardRegenDelay = 1f;
    [SerializeField, Min(0.01f), Tooltip("소진 즉시 회복을 시작하고 이 시간 후 100%가 되어 다시 사용할 수 있다.")]
    private float guardBreakRecoveryDuration = 12f;

    private PlayerController _player;
    private MeleeDodgeController _dodge;
    private bool _isParrying;
    private float _guard = 100f;
    private bool _guardBroken;
    private float _raisedAt;
    private float _recoverAt;
    private static PlayerParryController _active;
    private RightClickConfig _activeConfig;
    private CharacterHealth _activeSelf;
    private Vector2 _activeAimDir;
    private GameObject _telegraph;
    private Material _telegraphMaterial;
    private Mesh _telegraphMesh;

    public event System.Action OnParryStart;
    public event System.Action OnParrySuccess;
    public bool IsParrying => _isParrying;
    public float GuardAmount => _guard;
    public float GuardCapacity => Mathf.Max(1f, maxGuard);
    public float GuardFraction => Mathf.Clamp01(_guard / GuardCapacity);
    public bool GuardBroken => _guardBroken;
    public bool LastBlockWasPerfect { get; private set; }
    private bool WindowOpen => _active == this && _isParrying && !_guardBroken && _guard > 0f;
    private bool IsDashing => _player.IsDashing || (_dodge != null && _dodge.IsDashing);

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        _dodge = GetComponent<MeleeDodgeController>();
        _guard = GuardCapacity;
    }

    private void OnDisable() => StopGuard();
    private void OnApplicationFocus(bool focused) { if (!focused) StopGuard(); }

    private void Update()
    {
        if (_player == null || _player.Stat == null || _player.Stat.Health.IsDead
            || _player.IsInputBlocked || Time.timeScale == 0f)
        {
            StopGuard();
            return;
        }
        if (_player.IsCCed || IsDashing) StopGuard();
        if (_isParrying)
        {
            UpdateAim(AimDir());
            ScanIncoming(_activeConfig);
        }
        else TickGauge(Time.deltaTime, Time.time);
    }

    private void TickGauge(float deltaTime, float now)
    {
        if (_isParrying || deltaTime <= 0f) return;
        // 일반 회복은 지연을 넘긴 프레임의 일부만, 소진 회복은 지연 없이 12초 전체를 쓴다.
        float elapsed = _guardBroken ? deltaTime : Mathf.Clamp(now - _recoverAt, 0f, deltaTime);
        float rate = _guardBroken ? GuardCapacity / Mathf.Max(0.01f, guardBreakRecoveryDuration)
                                 : Mathf.Max(0f, guardRegenPerSecond);
        _guard = Mathf.Min(GuardCapacity, _guard + elapsed * rate);
        if (_guard >= GuardCapacity - 0.0001f) { _guard = GuardCapacity; _guardBroken = false; }
    }

    public void SetGuardHeld(bool held)
    {
        if (held) TryStartParry();
        else StopGuard();
    }

    public bool TryInterruptForAction()
    {
        StopGuard();
        return true;
    }

    /// <summary>장판/돌진은 발생 지점에서 bypassGuard. 찌르기는 이동하더라도 일반 공격이다.</summary>
    public static bool CanGuard(DamageInfo info)
    {
        if (info.bypassGuard || info.amount <= 0f || info.attacker == null) return false;
        if (info.type != DamageType.Physical && info.type != DamageType.Magic) return false;
        var category = info.category == DamageCategory.None
            ? CharacterHealth.ResolveCategoryFromAttacker(info.attacker) : info.category;
        return category == DamageCategory.EnemyMinion || category == DamageCategory.EnemyElite
            || category == DamageCategory.EnemyBoss;
    }

    public static bool Intercept(CharacterHealth target, ref DamageInfo info)
    {
        var guard = _active;
        if (guard == null || !guard.WindowOpen || target == null || target.IsDead
            || target != guard._activeSelf || !CanGuard(info)) return false;
        Vector2 source = info.hitFrom ?? (Vector2)info.attacker.transform.position;
        if (!guard.IsInAimCone(source)) return false;
        guard.Block(info);
        return true; // 소진시키는 마지막 타격까지 피해/경직/넉백/상태이상을 완전히 막는다.
    }

    /// <summary>영역 스캔과 실제 충돌이 같은 방어/소모 경로를 쓴다. 같은 탄은 한 번만 소모한다.</summary>
    public static bool TryBlockProjectile(Projectile projectile, CharacterHealth target = null)
    {
        var guard = _active;
        if (guard == null || !guard.WindowOpen || guard._activeSelf == null || guard._activeSelf.IsDead
            || projectile == null || projectile.GuardConsumed
            || (target != null && target != guard._activeSelf)
            || (projectile.TargetLayer.value & Layers.PlayerMask) == 0
            || !CanGuard(projectile.GuardInfo)) return false;
        Vector2 point = target != null
            ? projectile.GuardInfo.hitFrom.Value : (Vector2)projectile.transform.position;
        if (!guard.IsInAimCone(point)) return false;

        var shooter = projectile.Shooter;
        Vector2 returnDir = shooter != null
            ? (Vector2)(shooter.transform.position - projectile.transform.position) : -projectile.Direction;
        bool reflect = guard._activeConfig.CanReflect;
        projectile.GuardConsumed = true;
        guard.Block(projectile.GuardInfo);
        if (reflect) projectile.Deflect(guard.gameObject, Layers.EnemyMask, returnDir);
        else Destroy(projectile.gameObject);
        return true;
    }

    private static RightClickConfig EquippedRightClick
    {
        get
        {
            var inven = InventoryManager.Instance;
            var so = inven != null ? inven.EquippedRightClick : null;
            if (so == null)
            {
                var registry = GameManager.Instance != null && GameManager.Instance.dataManager != null
                    ? GameManager.Instance.dataManager.GET_GROWTH_REGISTRY() : null;
                so = registry != null ? registry.ResolveDefaultRightClick() : null;
            }
            var rc = so != null ? so.config : null;
            return (rc != null && rc.IsValid) ? rc : null;
        }
    }

    private Vector2 AimDir()
    {
        var cam = Camera.main;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (cam == null || mouse == null) return Vector2.right;
        Vector3 mousePos = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        Vector2 dir = ((Vector2)(mousePos - transform.position)).normalized;
        return dir.sqrMagnitude < 0.0001f ? Vector2.right : dir;
    }

    private void UpdateAim(Vector2 dir)
    {
        _activeAimDir = dir;
        if (dir.x != 0f)
            transform.localScale = new Vector3(dir.x > 0f ? -1f : 1f, transform.localScale.y, transform.localScale.z);
        if (_telegraph == null) return;
        _telegraph.transform.SetPositionAndRotation(transform.position,
            Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg));
    }

    public void TryStartParry()
    {
        if (_isParrying || _guardBroken || _guard <= 0f || _player == null || _player.Stat == null
            || _player.IsInputBlocked || Time.timeScale == 0f || _player.Stat.Health.IsDead
            || IsDashing || _player.IsCCed) return;
        var rc = EquippedRightClick;
        if (rc == null) { Announce(emptyMessage); return; }
        var melee = GetComponent<MeleeCombatController>();
        if (melee != null && melee.IsInAttackAction) melee.CancelPlayerAttack();
        _activeConfig = rc;
        _activeSelf = _player.Stat.Health;
        _raisedAt = Time.time;
        _active = this;
        _isParrying = true;
        LastBlockWasPerfect = false;
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.Parry, rc.moveSpeedMultiplier);
        _player.LockAnimState(0f); // 가드를 내릴 때 직접 해제. 3초 타임아웃으로 홀드 자세가 풀리면 안 된다.
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Parry", "Idle");
        _telegraph = CreateTelegraphSector(AimDir(), rc, -1f);
        UpdateAim(AimDir());
        OnParryStart?.Invoke();
    }

    public void StopGuard()
    {
        if (_active == this) _active = null;
        if (!_isParrying) return;
        _isParrying = false;
        _recoverAt = Time.time + Mathf.Max(0f, guardRegenDelay);
        DestroyTelegraph();
        if (_player == null) return;
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.Parry);
        _player.CanChangeAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
    }

    private void ScanIncoming(RightClickConfig rc)
    {
        foreach (var col in Physics2D.OverlapCircleAll(transform.position, rc.EffectiveRadius))
        {
            if (!WindowOpen) break;
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
            var projectile = col.GetComponentInParent<Projectile>();
            if (projectile != null) { TryBlockProjectile(projectile); continue; }
            var box = col.GetComponentInParent<BaseHitBox>();
            if (box == null || !box.IsLive || box.HasHitAnyone || !box.Targets(Layers.Player)
                || !CanGuard(box.Info) || !IsInAimCone(box.transform.position)) continue;
            box.gameObject.SetActive(false);
            Destroy(box.gameObject);
            Block(box.Info);
        }
    }

    private bool IsInAimCone(Vector2 point)
    {
        Vector2 to = point - (Vector2)transform.position;
        return to.sqrMagnitude < 0.0001f || Vector2.Angle(_activeAimDir, to) <= _activeConfig.angle * 0.5f;
    }

    private void Block(DamageInfo info)
    {
        float damage = _activeSelf.CalculateGuardDamage(info);
        LastBlockWasPerfect = Time.time - _raisedAt <= Mathf.Max(0f, perfectGuardWindow);
        _guard = Mathf.Max(0f, _guard - damage * (LastBlockWasPerfect ? 0.5f : 1f));
        _player?.RecordCombatAction();
        if (_guard <= 0f)
        {
            _guardBroken = true;
            StopGuard();
        }
        OnParrySuccess?.Invoke();
    }

    private void DestroyTelegraph()
    {
        if (_telegraph != null) Destroy(_telegraph);
        if (_telegraphMaterial != null) Destroy(_telegraphMaterial);
        if (_telegraphMesh != null) Destroy(_telegraphMesh);
        _telegraph = null;
        _telegraphMaterial = null;
        _telegraphMesh = null;
    }

    private void Announce(string msg)
    {
        var mgr = FloatingTextManager.Instance;
        if (mgr == null || string.IsNullOrEmpty(msg)) return;
        var text = mgr.GetFromPool();
        if (text != null) text.SetUp(msg, Color.white, transform);
    }

    private GameObject CreateTelegraphSector(Vector2 dir, RightClickConfig rc, float lifetime)
    {
        float radius = rc.EffectiveRadius;
        float angleSpan = Mathf.Min(360f, rc.angle);
        Color fill = new Color(rc.sectorColor.r, rc.sectorColor.g, rc.sectorColor.b, 0.4f);
        Color edge = new Color(rc.sectorColor.r, rc.sectorColor.g, rc.sectorColor.b, 0.8f);
        Quaternion rot = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

        // 커스텀 프리팹이 있다면 그것을 사용
        if (parryTelegraphPrefab != null)
        {
            GameObject customGo = Instantiate(parryTelegraphPrefab, transform.position, rot);
            customGo.transform.localScale = new Vector3(radius, radius, 1f);

            // 프리팹에 콜라이더가 있다면 적의 길을 막지 않게 isTrigger로 강제 변경
            foreach (var col in customGo.GetComponentsInChildren<Collider2D>()) col.isTrigger = true;

            // 종류별 색은 프리팹 경로에서도 먹어야 한다(알파는 프리팹 것을 존중).
            foreach (var sr in customGo.GetComponentsInChildren<SpriteRenderer>(true))
                sr.color = new Color(rc.sectorColor.r, rc.sectorColor.g, rc.sectorColor.b, sr.color.a);

            if (lifetime > 0f) Destroy(customGo, lifetime);
            return customGo;
        }

        // 프리팹이 없다면 코드로 직접 부채꼴 2D 메쉬 및 라인을 렌더링
        GameObject go = new GameObject("RightClickTelegraphSector");
        go.transform.position = transform.position;
        go.transform.rotation = rot;

        MeshFilter meshFilter = go.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();

        // 빌드에서도 안전하게 핑크색 에러 없이 렌더링되게 Sprites/Default 셰이더 적용
        Shader spriteShader = Shader.Find("Sprites/Default");
        Material mat = new Material(spriteShader != null ? spriteShader : Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        mat.color = fill;
        meshRenderer.material = mat;

        // 부채꼴 메쉬 작성 (+X 를 중심으로 좌우 대칭)
        Mesh mesh = new Mesh();
        _telegraphMesh = mesh;
        int segments = 20;
        int vertexCount = segments + 2;
        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero; // 중심점

        float startAngle = -(angleSpan / 2f);
        float angleStep = angleSpan / segments;

        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = (startAngle + i * angleStep) * Mathf.Deg2Rad;
            vertices[i + 1] = new Vector3(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle), 0f) * radius;
        }

        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshFilter.mesh = mesh;

        // 테두리 외곽선 LineRenderer
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.startWidth = 0.05f;
        lr.endWidth = 0.05f;
        lr.material = mat;
        lr.startColor = edge;
        lr.endColor = edge;

        // 전방위(360)면 중심으로 돌아오는 반지름 선을 빼고 테두리만 그린다 — 안 그러면 원 한가운데에
        // 스포크가 하나 그어진다. 부채꼴일 때는 중심-테두리-중심이 맞다.
        if (angleSpan >= 360f)
        {
            lr.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++) lr.SetPosition(i, vertices[i + 1]);
        }
        else
        {
            lr.positionCount = vertexCount + 1;
            Vector3[] linePositions = new Vector3[vertexCount + 1];
            linePositions[0] = Vector3.zero;
            for (int i = 0; i <= segments; i++) linePositions[i + 1] = vertices[i + 1];
            linePositions[vertexCount] = Vector3.zero;
            lr.SetPositions(linePositions);
        }

        // new Material 은 렌더러가 치워주지 않는다. 오브젝트와 같이 명시적으로 지운다
        // (예전엔 우클릭 한 번마다 머티리얼이 하나씩 새서 런 내내 쌓였다).
        if (lifetime > 0f)
        {
            Destroy(go, lifetime);
            Destroy(mat, lifetime);
        }
        else
        {
            _telegraphMaterial = mat;
        }

        return go;
    }
}
