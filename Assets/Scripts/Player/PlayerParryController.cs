using UnityEngine;
using System.Collections;

/// <summary>한 번 누르면 지속시간 동안 전방 공격을 방어하는 가드. 레벨별 수치는 RightClickDataSO가 관리한다.</summary>
public class PlayerParryController : MonoBehaviour
{
    [SerializeField] private GameObject parryTelegraphPrefab;
    [SerializeField] private string emptyMessage = "우클릭 없음!";
    private PlayerController _player;
    private Coroutine _parryCoroutine;
    private bool _isParrying;
    private bool _blockedAny;
    private float _cooldownEnd;
    private float _windowEnd;
    private static PlayerParryController _active;
    private RightClickConfig _activeConfig;
    private CharacterHealth _activeSelf;
    private Vector2 _activeAimDir;
    private GameObject _telegraph;
    private Material _telegraphMaterial;
    private Mesh _telegraphMesh;

    public event System.Action OnParryStart;
    public event System.Action OnParrySuccess;
    public event System.Action OnParryFail;
    public bool IsParrying => _isParrying;
    public float CooldownRemaining => Mathf.Max(0f, _cooldownEnd - Time.time);
    public bool TryInterruptForAction() => !_isParrying;
    private bool WindowOpen => _active == this && _isParrying && Time.time < _windowEnd;

    private void Awake() => _player = GetComponent<PlayerController>();

    private void OnDisable()
    {
        if (_parryCoroutine != null) StopCoroutine(_parryCoroutine);
        CloseWindow();
        DestroyTelegraph();
        _parryCoroutine = null;
        _isParrying = false;
        _blockedAny = false;
        if (_player != null)
        {
            _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.Parry);
            _player.CanChangeAnimState();
        }
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
        if (guard == null || !guard.WindowOpen || target != guard._activeSelf || !CanGuard(info)) return false;
        Vector2 source = info.hitFrom ?? (Vector2)info.attacker.transform.position;
        if (!guard.IsInAimCone(source)) return false;
        guard.Succeed();
        return true; // 피해뿐 아니라 해당 타격의 경직/넉백/상태이상도 생략한다.
    }

    /// <summary>영역 스캔과 실제 충돌 모두 같은 관문. Update보다 먼저 닿은 투사체도 반사한다.</summary>
    public static bool TryBlockProjectile(Projectile projectile, CharacterHealth target = null)
    {
        var guard = _active;
        if (guard == null || !guard.WindowOpen || projectile == null || projectile.GuardConsumed
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
        // 투사체 소비와 최초 성공을 먼저 기록해 재진입해도 같은 탄/쿨타임 환급은 중복 처리하지 않는다.
        guard.Succeed();
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
                    ? GameManager.Instance.dataManager.GET_GROWTH_REGISTRY()
                    : null;
                so = registry != null ? registry.ResolveDefaultRightClick() : null;
            }

            var rc = so != null ? so.config : null;
            return (rc != null && rc.IsValid) ? rc : null;
        }
    }

    /// <summary>현재 마우스 조준 방향. 실패하면 오른쪽.</summary>
    private Vector2 AimDir()
    {
        var cam = Camera.main;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (cam == null || mouse == null) return Vector2.right;

        Vector3 mousePos = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        mousePos.z = 0;
        Vector2 dir = ((Vector2)(mousePos - transform.position)).normalized;
        return dir.sqrMagnitude < 0.0001f ? Vector2.right : dir;
    }

    private void FaceAim(Vector2 aimDir)
    {
        if (aimDir.x > 0) transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
        else if (aimDir.x < 0) transform.localScale = new Vector3(1, transform.localScale.y, transform.localScale.z);
    }


    public void TryStartParry()
    {
        if (_isParrying || CooldownRemaining > 0f || _player == null || _player.Stat == null
            || _player.Stat.Health.IsDead || _player.IsDashing || _player.IsCCed) return;
        var rc = EquippedRightClick;
        if (rc == null) { Announce(emptyMessage); return; }
        var melee = GetComponent<MeleeCombatController>();
        if (melee != null && melee.IsAttacking) melee.CancelPlayerAttack();
        var aim = AimDir();
        FaceAim(aim);
        _activeConfig = rc;
        _activeSelf = _player.Stat.Health;
        _activeAimDir = aim;
        _cooldownEnd = Time.time + Mathf.Max(0f, rc.cooldownDuration);
        _windowEnd = Time.time + Mathf.Max(0f, rc.activeDuration);
        _active = this;
        _isParrying = true;
        _blockedAny = false;
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.Parry, rc.moveSpeedMultiplier);
        _player.LockAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Parry", "Idle");
        _telegraph = CreateTelegraphSector(aim, rc, -1f);
        OnParryStart?.Invoke();
        _parryCoroutine = StartCoroutine(WindowRoutine(rc));
    }

    private IEnumerator WindowRoutine(RightClickConfig rc)
    {
        while (WindowOpen)
        {
            if (_player.Stat.Health.IsDead || _player.IsCCed) break;
            if (_telegraph != null) _telegraph.transform.position = transform.position;
            ScanIncoming(rc);
            if (!_isParrying) yield break; // 이벤트에서 비활성화된 경우 즉시 정리한다.
            yield return null;
        }
        if (!_isParrying) yield break;
        CloseWindow();
        DestroyTelegraph();
        if (!_blockedAny)
        {
            OnParryFail?.Invoke();
            if (rc.recoveryDuration > 0f) yield return new WaitForSeconds(rc.recoveryDuration);
        }
        EndStance();
    }

    private void ScanIncoming(RightClickConfig rc)
    {
        foreach (var col in Physics2D.OverlapCircleAll(transform.position, rc.EffectiveRadius))
        {
            if (!WindowOpen) break;
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
            var projectile = col.GetComponentInParent<Projectile>();
            if (projectile != null)
            {
                TryBlockProjectile(projectile);
                continue;
            }
            var box = col.GetComponentInParent<BaseHitBox>();
            if (box == null || !box.IsLive || box.HasHitAnyone || !box.Targets(Layers.Player)
                || !CanGuard(box.Info) || !IsInAimCone(box.transform.position)) continue;
            box.gameObject.SetActive(false); // Destroy가 지연되어 같은 프레임에 다시 맞는 것 방지
            Destroy(box.gameObject);
            Succeed();
        }
    }

    private bool IsInAimCone(Vector2 point)
    {
        Vector2 to = point - (Vector2)transform.position;
        return to.sqrMagnitude < 0.0001f || Vector2.Angle(_activeAimDir, to) <= _activeConfig.angle * 0.5f;
    }

    private void Succeed()
    {
        if (!WindowOpen) return;
        if (!_blockedAny)
        {
            _blockedAny = true;
            _cooldownEnd = Mathf.Max(Time.time, _cooldownEnd - _activeConfig.SuccessRefund);
        }
        // 성공해도 시간/자세/인디케이터를 유지한다. 추가 타격은 방어하되 지속시간을 연장하지 않는다.
        OnParrySuccess?.Invoke();
    }

    private void CloseWindow() { if (_active == this) _active = null; }

    private void EndStance()
    {
        _isParrying = false;
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.Parry);
        _player.CanChangeAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
        _parryCoroutine = null;
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
