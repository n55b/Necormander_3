using UnityEngine;
using System.Collections;

/// <summary>
/// 플레이어의 우클릭을 담당하는 컨트롤러.
///
/// [26/08/10] 패링 전용에서 3종(패링/카운터/가드)으로 확장됐다. 무엇이 나갈지는 장착된 서브
/// 소환수(SubMinionDataSO.rightClick)가 정하고, 아무것도 안 꼈으면 우클릭은 안내 문구만 띄운다.
/// 클래스 이름은 그대로 둔다 — PlayerController/MeleeCombatController/사운드가 이름으로 참조하고
/// 프리팹도 이 스크립트를 물고 있어서, 이름을 바꿔봐야 기능은 그대로인데 배선만 흔들린다.
///
/// 수치는 전부 MinionRightClick(SO)에서 온다. 여기 인스펙터에 같은 값을 또 두면 두 소스가 갈려서
/// 반드시 어긋난다 — 그래서 옛 parryRadius/parryAngle/parryActiveDuration 등은 전부 지웠다.
///
/// 판정 방향이 종류마다 반대다:
///  · 패링       능동 — 창이 열린 동안 매 프레임 주변에서 Projectile 을 찾아 반사한다.
///  · 카운터/가드 수동 — 창이 열린 동안 나에게 들어오는 근접 피해를 기다렸다가 무효화한다.
/// </summary>
public class PlayerParryController : MonoBehaviour
{
    [Header("텔레그래프 (미지정 시 코드로 부채꼴 메쉬 생성)")]
    [SerializeField] private GameObject parryTelegraphPrefab;

    [Header("서브 소환수 미장착 안내")]
    [SerializeField] private string emptyMessage = "장착 항목 없음!";

    private PlayerController _player;
    private bool _isParrying;
    private Coroutine _parryCoroutine;

    // 수동 판정(카운터/가드)이 창 안에서 잡아낸 피격. 핸들러는 '기록 + 무효화'만 하고 실제 반사/보호막은
    // 코루틴이 처리한다 — 핸들러 안에서 반사 피해를 넣으면 OnBeforeDamageCalculated 가 중첩 호출된다.
    private bool _reactHit;
    private GameObject _reactAttacker;
    private float _reactAmount;

    // ── 카운터/가드 판정창 ────────────────────────────────────────────────
    // DamageEventBus 를 쓰지 않는다. 버스 발화(CharacterHealth.GetDamage 안)가 무적 조기 리턴보다
    // '뒤'라서, 버스로는 피격 무적 1초 동안 들어온 공격을 아예 볼 수 없다. 그 1초 동안 카운터가
    // 통째로 죽어버리므로, CharacterHealth 가 무적을 보기 '전'에 여기로 직접 물어보게 했다.
    private static PlayerParryController _activeWindow;
    private MinionRightClick _windowConfig;
    private CharacterHealth _windowSelf;
    private Vector2 _windowAimDir;

    /// <summary>
    /// 열려 있는 카운터/가드 창이 이 피격을 받아냈는가. true 면 그 피격은 통째로 무효다
    /// (피해뿐 아니라 경직·넉백·상태이상까지 — CharacterHealth 가 그 자리에서 return 한다).
    /// </summary>
    public static bool TryBlock(CharacterHealth target, DamageInfo info)
        => _activeWindow != null && _activeWindow.Consume(target, info);

    private bool Consume(CharacterHealth target, DamageInfo info)
    {
        if (_reactHit) return false;                                 // 창 하나당 한 방만 받는다
        if (target == null || target != _windowSelf) return false;
        if (!Qualifies(info)) return false;
        // 각도 판정. 카운터/가드는 angle=360 이라 사실상 전방위 — '내가 맞았으면' 성립한다.
        if (!IsInAimCone(info.attacker.transform.position, _windowAimDir, _windowConfig.angle)) return false;

        Record(info.attacker, info.amount);
        return true;
    }

    /// <summary>
    /// 이 피해가 카운터/가드로 받아낼 수 있는 종류인가. 피해 경로와 영역 경로가 같은 규칙을 쓰도록
    /// 한곳에 모아둔다 — 갈래는 히트박스 단계에선 아직 None 이라 여기서 같은 함수로 유도한다.
    /// </summary>
    private static bool Qualifies(DamageInfo info)
    {
        if (info.isRanged) return false;                             // 근접 전용
        if (info.attacker == null || info.amount <= 0f) return false;

        DamageCategory cat = info.category == DamageCategory.None
            ? CharacterHealth.ResolveCategoryFromAttacker(info.attacker)
            : info.category;
        return cat == DamageCategory.EnemyMinion;                    // 보스·엘리트는 못 막는다
    }

    private void Record(GameObject attacker, float amount)
    {
        _reactHit = true;
        _reactAttacker = attacker;
        _reactAmount = amount;
    }

    /// <summary>
    /// [영역 판정] 판정 범위 안에 들어온 '살아 있는' 적 근접 히트박스를 되받아친다.
    /// 이게 없으면 카운터는 "내가 실제로 피해를 입었을 때"만 성립한다 — 판정 영역이라는 개념이
    /// 사실상 없어진다. 되받아친 히트박스는 파괴한다(= 튕겨냈으니 이 공격은 사라진다).
    /// 접촉 피해처럼 히트박스를 안 쓰는 공격(차저 돌진)은 그대로 피해 경로(Consume)가 잡는다.
    /// </summary>
    private bool CheckAndBlockEnemyHitBoxes(Vector2 aimDir, MinionRightClick rc)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, rc.radius);

        foreach (var col in colliders)
        {
            if (col == null) continue;

            var box = col.GetComponent<BaseHitBox>();
            if (box == null) continue;
            if (!box.IsLive) continue;               // 윈드업(장판 차오르는) 중인 건 아직 공격이 아니다
            if (!box.Targets(Layers.Player)) continue; // 나를 노리는 것만 (내 히트박스 되받아치기 방지)
            if (!Qualifies(box.Info)) continue;
            if (!IsInAimCone(box.transform.position, aimDir, rc.angle)) continue;

            Record(box.Info.attacker, box.Info.amount);
            Destroy(box.gameObject);
            return true;
        }

        return false;
    }

    private void CloseWindow()
    {
        if (_activeWindow == this) _activeWindow = null;
    }

    public event System.Action OnParryStart;
    public event System.Action OnParrySuccess;
    public event System.Action OnParryFail;
    public bool IsParrying => _isParrying;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
    }

    // 코루틴의 finally 는 오브젝트가 통째로 파괴될 때(층 이동 등) 실행되지 않는다. 정적 필드에
    // 죽은 플레이어가 남으면 다음 층 내내 그 피격을 받아내려 들므로 여기서도 반드시 닫는다.
    private void OnDisable() => CloseWindow();

    /// <summary>장착된 서브 소환수가 부여한 우클릭. 없으면 null.</summary>
    private static MinionRightClick EquippedRightClick
    {
        get
        {
            var sub = InventoryManager.Instance != null ? InventoryManager.Instance.SubSummon : null;
            var rc = sub != null ? sub.rightClick : null;
            return (rc != null && rc.IsValid) ? rc : null;
        }
    }

    public void TryStartParry()
    {
        if (_isParrying) return;
        if (_player.Stat.Health.IsDead) return;
        if (_player.IsDashing) return;

        var rc = EquippedRightClick;
        if (rc == null)
        {
            // 서브를 안 꼈으면 우클릭은 아무 일도 안 한다. 나중에 여기에 '빈손' 모션을 붙이면 된다.
            Announce(emptyMessage);
            return;
        }

        // 공격 중일 때는 플레이어 평타만 캔슬하고 시도(미니언 마무리는 남김 — R끼리만 회수)
        var meleeCtrl = GetComponent<MeleeCombatController>();
        if (meleeCtrl != null && meleeCtrl.IsAttacking) meleeCtrl.CancelPlayerAttack();

        // 플레이어 액티브 스킬 시전 중일 때도 캔슬하고 시도
        if (_player.IsCastingSkill) _player.CancelActiveSkill();

        // 마우스 조준 방향 벡터 획득
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
        mousePos.z = 0;
        Vector2 aimDir = ((Vector2)(mousePos - transform.position)).normalized;
        if (aimDir.sqrMagnitude < 0.0001f) aimDir = Vector2.right;

        // 플레이어 바라보는 방향 조준선에 동기화
        if (aimDir.x > 0) transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
        else if (aimDir.x < 0) transform.localScale = new Vector3(1, transform.localScale.y, transform.localScale.z);

        // 성공 모션 꼬리가 아직 돌고 있을 수 있다(성공 시 입력 잠금은 먼저 풀린다). 새 입력이 이긴다.
        if (_parryCoroutine != null) StopCoroutine(_parryCoroutine);
        _parryCoroutine = StartCoroutine(RightClickSequence(rc, aimDir));
    }

    private IEnumerator RightClickSequence(MinionRightClick rc, Vector2 aimDir)
    {
        _isParrying = true;
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.Parry, rc.moveSpeedMultiplier);

        OnParryStart?.Invoke();

        // 애니메이션은 3종 공통이다 — 종류는 부채꼴 색으로만 구분한다.
        // Idle/Walk 자동 전환을 잠그고 캐시를 비운다(공격 모션과 같은 이유).
        _player.LockAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Parry", "Idle");

        CreateTelegraphSector(aimDir, rc);

        // ── 판정창 ────────────────────────────────────────────────────────
        bool success = false;
        _reactHit = false;
        _reactAttacker = null;
        _reactAmount = 0f;

        if (rc.IsReactive)
        {
            _windowConfig = rc;
            _windowSelf = _player.Stat.Health;
            _windowAimDir = aimDir;
            _activeWindow = this;
        }

        // 준비 자세가 통째로 스킵되지 않도록 한 프레임을 보장한다. 반드시 '구독 뒤'다 —
        // 앞에 두면 우클릭을 누른 그 프레임에 도착한 피해를 놓친다. 패링은 투사체가 반경 안에
        // 여러 프레임 머물러서 티가 안 났지만, 카운터/가드가 노리는 피격은 딱 한 프레임짜리
        // 사건이라(BaseHitBox 단발) 그 한 프레임이 판정창의 1/3이었다.
        yield return null;

        try
        {
            float elapsed = 0f;
            while (elapsed < rc.activeDuration)
            {
                if (rc.type == MinionRightClickType.Parry)
                {
                    if (CheckAndDeflectProjectiles(aimDir, rc)) { success = true; break; }
                }
                else
                {
                    // 두 경로 모두 성공으로 친다:
                    //  · _reactHit  = 실제로 피해가 들어왔다 (CharacterHealth 가 Consume 을 태웠다)
                    //  · 영역 스캔  = 적 히트박스가 판정 범위에 들어왔다 (아직 안 맞았어도)
                    if (_reactHit || CheckAndBlockEnemyHitBoxes(aimDir, rc)) { success = true; break; }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        finally
        {
            // 창이 열린 채로 코루틴이 잘릴 수 있다(성공 꼬리 중 재입력 → StopCoroutine).
            // 안 닫으면 플레이어가 받는 근접 피해를 영원히 무효로 만든다.
            CloseWindow();
        }

        // 창이 닫히는 마지막 프레임에 들어온 피격도 인정한다(위 루프는 검사 후 대기라 한 틱 샐 수 있다).
        if (!success && _reactHit) success = true;

        if (!success)
        {
            OnParryFail?.Invoke();
            yield return new WaitForSeconds(rc.recoveryDuration);
            EndParry();
            yield break;
        }

        // ── 성공 ──────────────────────────────────────────────────────────
        if (rc.type == MinionRightClickType.Counter) ApplyCounter(rc);
        else if (rc.type == MinionRightClickType.Guard) ApplyGuard(rc);

        OnParrySuccess?.Invoke();

        // 성공하면 후딜이 없다 — 연속으로 들어오는 공격을 연타로 받아칠 수 있어야 한다.
        // 입력 잠금(_isParrying)과 감속만 먼저 풀고, 성공 모션은 뒤에서 마저 재생한다.
        // 이 꼬리 중에 우클릭이 다시 들어오면 TryStartParry 가 StopCoroutine 으로 잘라간다.
        _isParrying = false;
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.Parry);

        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Parry_Success", "Parry");

        float successClipLength = GetAnimationClipLength("Parry_Success");
        if (successClipLength > 0f) yield return new WaitForSeconds(successClipLength);

        EndParry();
    }

    /// <summary>카운터: 막아낸 피해량만큼 때린 그 적에게만 되돌린다.</summary>
    private void ApplyCounter(MinionRightClick rc)
    {
        if (_reactAttacker == null) return;

        var stat = _reactAttacker.GetComponent<CharacterStat>()
                ?? _reactAttacker.GetComponentInParent<CharacterStat>()
                ?? _reactAttacker.GetComponentInChildren<CharacterStat>(true);
        if (stat == null || stat.Health == null || stat.Health.IsDead) return;

        // 갈래는 패링(반사) — IsPlayerSourced 에 들어 있어서 플레이어의 치명타·물리 증폭을 그대로 탄다.
        var info = new DamageInfo(_reactAmount * rc.reflectMultiplier, DamageType.Physical, gameObject, 1f,
                                  "카운터!", category: DamageCategory.Parry);
        stat.Health.GetDamage(info);
    }

    /// <summary>가드: 막아낸 피해량만큼 보호막. 상한은 최대 체력의 shieldMaxHpRatio.</summary>
    private void ApplyGuard(MinionRightClick rc)
    {
        var status = _player.GetComponentInChildren<CharacterStatus>();
        if (status == null) return;

        float cap = _player.Stat.MAXHP * rc.shieldMaxHpRatio;
        status.AddShieldCapped(_reactAmount, rc.shieldDuration, cap);
    }

    private void EndParry()
    {
        _isParrying = false;
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.Parry);

        _player.CanChangeAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
        _parryCoroutine = null;
    }

    /// <summary>조준 방향 기준 부채꼴 안에 있는가. angle 이 360 이상이면 전방위.</summary>
    private bool IsInAimCone(Vector2 worldPos, Vector2 aimDir, float angle)
    {
        if (angle >= 360f) return true;
        Vector2 to = worldPos - (Vector2)transform.position;
        if (to.sqrMagnitude < 0.0001f) return true;
        return Vector2.Angle(aimDir, to.normalized) <= angle / 2f;
    }

    private bool CheckAndDeflectProjectiles(Vector2 aimDir, MinionRightClick rc)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, rc.radius);
        bool deflectedAny = false;
        LayerMask enemyLayer = Layers.EnemyMask;

        foreach (var col in colliders)
        {
            if (col == null || col.isTrigger == false) continue;

            Projectile proj = col.GetComponent<Projectile>();
            if (proj == null) continue;

            // 이미 적군을 대상으로 하는 투사체(반사됨)는 다시 반사하지 않음
            if ((proj.TargetLayer.value & (1 << Layers.Enemy)) != 0) continue;

            if (!IsInAimCone(col.transform.position, aimDir, rc.angle)) continue;

            proj.Deflect(gameObject, enemyLayer, aimDir);
            deflectedAny = true;
        }

        return deflectedAny;
    }

    /// <summary>플레이어 머리 위에 안내 문구를 띄운다. (ActiveAugment.Announce 와 같은 경로)</summary>
    private void Announce(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        var mgr = FloatingTextManager.Instance;
        if (mgr == null) return;
        var t = mgr.GetFromPool();
        if (t != null) t.SetUp(msg, Color.white, transform);
    }

    // state name suffix ("Parry" or "Parry_Success") -> matching clip length, or -1 if not found.
    // Uses EndsWith so that "Parry" never accidentally matches the "Parry_Success" clip.
    private float GetAnimationClipLength(string stateNameSuffix)
    {
        Animator anim = GetComponentInChildren<Animator>();
        if (anim == null) anim = GetComponent<Animator>();

        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (var clip in anim.runtimeAnimatorController.animationClips)
            {
                if (clip.name.EndsWith(stateNameSuffix, System.StringComparison.OrdinalIgnoreCase))
                    return clip.length;
            }
        }
        return -1f;
    }

    private void CreateTelegraphSector(Vector2 dir, MinionRightClick rc)
    {
        float radius = rc.radius;
        float angleSpan = Mathf.Min(360f, rc.angle);
        Color fill = new Color(rc.sectorColor.r, rc.sectorColor.g, rc.sectorColor.b, 0.4f);
        Color edge = new Color(rc.sectorColor.r, rc.sectorColor.g, rc.sectorColor.b, 0.8f);

        // 커스텀 프리팹이 있다면 그것을 사용
        if (parryTelegraphPrefab != null)
        {
            GameObject customGo = Instantiate(parryTelegraphPrefab, transform.position, Quaternion.identity);
            float angleCustom = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            customGo.transform.rotation = Quaternion.Euler(0, 0, angleCustom);
            customGo.transform.localScale = new Vector3(radius, radius, 1f);

            // 프리팹에 콜라이더가 있다면 적의 길을 막지 않게 isTrigger로 강제 변경
            foreach (var col in customGo.GetComponentsInChildren<Collider2D>()) col.isTrigger = true;

            // 종류별 색은 프리팹 경로에서도 먹어야 한다(알파는 프리팹 것을 존중).
            foreach (var sr in customGo.GetComponentsInChildren<SpriteRenderer>(true))
                sr.color = new Color(rc.sectorColor.r, rc.sectorColor.g, rc.sectorColor.b, sr.color.a);

            Destroy(customGo, rc.activeDuration);
            return;
        }

        // 프리팹이 없다면 코드로 직접 부채꼴 2D 메쉬 및 라인을 렌더링
        GameObject go = new GameObject("RightClickTelegraphSector");
        go.transform.position = transform.position;
        go.transform.rotation = Quaternion.identity;

        MeshFilter meshFilter = go.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();

        // 빌드에서도 안전하게 핑크색 에러 없이 렌더링되게 Sprites/Default 셰이더 적용
        Shader spriteShader = Shader.Find("Sprites/Default");
        Material mat = new Material(spriteShader != null ? spriteShader : Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        mat.color = fill;
        meshRenderer.material = mat;

        // 부채꼴 메쉬 작성
        Mesh mesh = new Mesh();
        int segments = 20;
        int vertexCount = segments + 2;
        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero; // 중심점

        float centerAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float startAngle = centerAngle - (angleSpan / 2f);
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
        lr.positionCount = vertexCount + 1;
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
            Vector3[] linePositions = new Vector3[vertexCount + 1];
            linePositions[0] = Vector3.zero;
            for (int i = 0; i <= segments; i++) linePositions[i + 1] = vertices[i + 1];
            linePositions[vertexCount] = Vector3.zero;
            lr.SetPositions(linePositions);
        }

        Destroy(go, rc.activeDuration);
        // new Material 은 렌더러가 치워주지 않는다. 오브젝트와 같이 명시적으로 지운다
        // (예전엔 우클릭 한 번마다 머티리얼이 하나씩 새서 런 내내 쌓였다).
        Destroy(mat, rc.activeDuration);
    }
}
