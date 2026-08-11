using UnityEngine;
using System.Collections;

/// <summary>
/// 플레이어의 우클릭을 담당하는 컨트롤러.
///
/// [26/08/10] 패링 전용에서 3종으로 확장됐다. 무엇이 나갈지는 장착된 서브 소환수
/// (SubMinionDataSO.rightClick)가 정하고, 아무것도 안 꼈으면 우클릭은 안내 문구만 띄운다.
/// 클래스 이름은 그대로 둔다 — PlayerController/MeleeCombatController/사운드가 이름으로 참조하고
/// 프리팹도 이 스크립트를 물고 있어서, 이름을 바꿔봐야 기능은 그대로인데 배선만 흔들린다.
///
/// 세 종류가 성격이 전부 다르다:
///  · 패링   [탭] 판정창 동안 주변 Projectile 을 찾아 반사한다.            (능동 스캔)
///  · 카운터 [탭] 판정창 동안 근접 공격을 받아내 무효화 + 반사 + 범위 경직. (수동 + 영역 스캔)
///  · 가드   [홀드] 누르고 있는 동안 바라보는 방향의 피해를 감소시킨다.     (판정창 없음)
///
/// 수치는 전부 MinionRightClick(SO)에서 온다. 여기 인스펙터에 같은 값을 또 두면 두 소스가 갈려서
/// 반드시 어긋난다 — 그래서 옛 parryRadius/parryAngle/parryActiveDuration 등은 전부 지웠다.
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

    // 카운터가 받아낸 피격. 핸들러는 '기록 + 무효화'만 하고 실제 반사/경직은 코루틴이 처리한다 —
    // 핸들러 안에서 반사 피해를 넣으면 CharacterHealth.GetDamage 가 중첩 호출된다.
    private bool _reactHit;
    private GameObject _reactAttacker;
    private float _reactAmount;

    // 가드가 쓰는 지속 텔레그래프(부채꼴). 홀드가 끝날 때 같이 지운다.
    private GameObject _holdTelegraph;
    private Material _holdTelegraphMat;

    // ── 피격 개입 창구 ────────────────────────────────────────────────────
    // DamageEventBus 를 쓰지 않는다. 버스 발화(CharacterHealth.GetDamage 안)가 무적 조기 리턴보다
    // '뒤'라서, 버스로는 피격 무적 1초 동안 들어온 공격을 아예 볼 수 없다. 그 1초 동안 카운터가
    // 통째로 죽어버리므로, CharacterHealth 가 무적을 보기 '전'에 여기로 직접 물어보게 했다.
    private static PlayerParryController _active;
    private MinionRightClick _activeConfig;
    private CharacterHealth _activeSelf;
    private Vector2 _activeAimDir;

    /// <summary>
    /// 열려 있는 우클릭 판정이 이 피격에 개입한다.
    /// 반환값 true = <b>통째로 무효</b>(카운터가 받아냄). CharacterHealth 가 그 자리에서 return 하므로
    /// 피해뿐 아니라 경직·넉백·상태이상까지 전부 스킵된다.
    /// 가드는 피해를 깎기만 하므로 info.amount 를 줄이고 false 를 돌려준다 — 파이프라인은 계속 흐른다.
    /// </summary>
    public static bool Intercept(CharacterHealth target, ref DamageInfo info)
        => _active != null && _active.InterceptInternal(target, ref info);

    private bool InterceptInternal(CharacterHealth target, ref DamageInfo info)
    {
        if (target == null || target != _activeSelf) return false;
        if (info.attacker == null || info.amount <= 0f) return false;

        // 방향은 '날아온 지점' 기준이다. attacker(쏜 본체)를 쓰면 유도탄에서 어긋난다 —
        // 마법사는 오른쪽에 있는데 파이어볼은 왼쪽에서 날아오는 상황이 실제로 나온다.
        Vector2 srcPos = info.hitFrom ?? (Vector2)info.attacker.transform.position;
        if (!IsInAimCone(srcPos, _activeAimDir, _activeConfig.angle)) return false;

        if (_activeConfig.type == MinionRightClickType.Guard)
        {
            // 가드는 근/원을 안 가리고, 적이 준 피해면 전부 깎는다(함정·자해는 제외 — IsEnemyTier).
            if (!IsEnemyDamage(info)) return false;
            info.amount *= Mathf.Clamp01(1f - _activeConfig.damageReduction);
            return false; // 무효가 아니라 '감소'다. 여기서 return true 하면 피해가 통째로 사라진다.
        }

        if (_reactHit) return false;                 // 창 하나당 한 방만 받는다
        if (!QualifiesForCounter(info)) return false;

        Record(info.attacker, info.amount);
        return true;
    }

    /// <summary>카운터가 받아낼 수 있는 피해인가. 근접 + 일반 몹 전용.</summary>
    private static bool QualifiesForCounter(DamageInfo info)
    {
        if (info.isRanged) return false;
        return ResolveCategory(info) == DamageCategory.EnemyMinion; // 보스·엘리트는 못 막는다
    }

    /// <summary>가드가 깎아줄 수 있는 피해인가. 적이 준 것이면 티어·사거리를 안 가린다.</summary>
    private static bool IsEnemyDamage(DamageInfo info) => DamageRules.IsEnemyTier(ResolveCategory(info));

    /// 갈래는 히트박스 단계에선 아직 None 이다. 판정이 두 벌로 갈리지 않게 CharacterHealth 와 같은
    /// 함수로 유도한다.
    private static DamageCategory ResolveCategory(DamageInfo info)
        => info.category == DamageCategory.None
            ? CharacterHealth.ResolveCategoryFromAttacker(info.attacker)
            : info.category;

    private void Record(GameObject attacker, float amount)
    {
        _reactHit = true;
        _reactAttacker = attacker;
        _reactAmount = amount;
    }

    private void OpenWindow(MinionRightClick rc, Vector2 aimDir)
    {
        _activeConfig = rc;
        _activeSelf = _player.Stat.Health;
        _activeAimDir = aimDir;
        _active = this;
    }

    private void CloseWindow()
    {
        if (_active == this) _active = null;
    }

    public event System.Action OnParryStart;
    public event System.Action OnParrySuccess;
    public event System.Action OnParryFail;

    /// <summary>우클릭 동작 중인가(판정창 또는 가드 홀드). 평타/대쉬/스킬이 이걸 보고 스스로를 막는다.</summary>
    public bool IsParrying => _isParrying;

    private bool _isHoldingGuard;

    /// <summary>
    /// 다른 행동(대쉬/평타/스킬)의 입력이 들어왔을 때 물어보는 관문. <b>true 면 그 행동을 해도 된다.</b>
    ///
    ///  · 우클릭을 안 쓰는 중      → true
    ///  · 가드(홀드) 중            → 가드를 끊고 true. 버튼을 계속 누르고 있어도 다시 안 켜진다 —
    ///                               TryStartParry 는 새 '누름' 이벤트에서만 불리기 때문이다.
    ///  · 패링/카운터 판정창 중    → false. 0.4초짜리 창을 다른 입력이 잡아먹으면 안 된다.
    /// </summary>
    public bool TryInterruptForAction()
    {
        if (!_isParrying) return true;
        if (!_isHoldingGuard) return false;

        CancelGuard();
        return true;
    }

    private void CancelGuard()
    {
        if (_parryCoroutine != null) { StopCoroutine(_parryCoroutine); _parryCoroutine = null; }
        // StopCoroutine 이 finally 를 돌려주긴 하지만, 그걸 믿고 비워두면 나중에 코루틴을 안 거치는
        // 경로가 생겼을 때 조용히 새기 시작한다. 둘 다 멱등이라 한 번 더 불러도 손해가 없다.
        CloseWindow();
        DestroyHoldTelegraph();
        _isHoldingGuard = false;
        EndStance();
    }

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
    }

    // 코루틴의 finally 는 오브젝트가 통째로 파괴될 때(층 이동 등) 실행되지 않는다. 정적 필드에
    // 죽은 플레이어가 남으면 다음 층 내내 그 피격에 개입하려 들므로 여기서도 반드시 닫는다.
    private void OnDisable()
    {
        CloseWindow();
        DestroyHoldTelegraph();
    }

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
        if (_isParrying) return;
        if (_player.Stat.Health.IsDead) return;
        if (_player.IsDashing) return;
        // 다른 입력들은 전부 IsCCed 게이트가 있는데 우클릭만 없었다. 그래서 보스 페이크 카운터의
        // 징벌 경직(0.75초) 동안 나머지가 다 죽은 상태에서 우클릭만 정상 발동했다.
        if (_player.IsCCed) return;

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

        Vector2 aimDir = AimDir();
        FaceAim(aimDir);

        // 성공 모션 꼬리가 아직 돌고 있을 수 있다(성공 시 입력 잠금은 먼저 풀린다). 새 입력이 이긴다.
        if (_parryCoroutine != null) StopCoroutine(_parryCoroutine);
        _parryCoroutine = StartCoroutine(rc.IsHold ? GuardHoldRoutine(rc) : WindowRoutine(rc, aimDir));
    }

    // ──────────────────────────────────────────────────────────────────────
    // 패링 / 카운터 — 한 번 누르면 끝나는 판정창
    // ──────────────────────────────────────────────────────────────────────
    private IEnumerator WindowRoutine(MinionRightClick rc, Vector2 aimDir)
    {
        BeginStance(rc);
        var telegraph = CreateTelegraphSector(aimDir, rc, rc.activeDuration);

        bool success = false;
        _reactHit = false;
        _reactAttacker = null;
        _reactAmount = 0f;

        if (rc.type == MinionRightClickType.Counter) OpenWindow(rc, aimDir);

        // 준비 자세가 통째로 스킵되지 않도록 한 프레임을 보장한다. 반드시 '창을 연 뒤'다 —
        // 앞에 두면 우클릭을 누른 그 프레임에 도착한 피해를 놓친다. 패링은 투사체가 반경 안에
        // 여러 프레임 머물러서 티가 안 났지만, 카운터가 노리는 피격은 딱 한 프레임짜리
        // 사건이라(BaseHitBox 단발) 그 한 프레임이 판정창의 상당 부분이었다.
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
                    //  · _reactHit  = 실제로 피해가 들어왔다 (CharacterHealth 가 Intercept 를 태웠다)
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

        if (telegraph != null) Destroy(telegraph);

        if (!success)
        {
            OnParryFail?.Invoke();
            yield return new WaitForSeconds(rc.recoveryDuration);
            EndStance();
            yield break;
        }

        // ── 성공 ──────────────────────────────────────────────────────────
        if (rc.type == MinionRightClickType.Counter)
        {
            ApplyCounter(rc);
            StunNearbyEnemies(rc);
        }

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

        // 꼬리가 도는 동안 _isParrying 이 이미 풀려 있어서 평타·대쉬·스킬이 정상적으로 시작될 수 있다.
        // 그 상태에서 EndStance 를 부르면 남의 모션을 Idle 로 스냅해버린다(피해는 시간 구동이라
        // 그대로 들어가는 '유령 데미지'가 된다). 애니 주인이 바뀌었으면 손 떼고 나간다 —
        // 그 행동들은 자기 클립 이벤트로 잠금을 푼다.
        var melee = GetComponent<MeleeCombatController>();
        if (_player.IsDashing || _player.IsCastingSkill || _player.IsUsingHandSkill
            || (melee != null && melee.IsInAttackAction))
        {
            _parryCoroutine = null;
            yield break;
        }

        EndStance();
    }

    // ──────────────────────────────────────────────────────────────────────
    // 가드 — 꾹 누르고 있는 동안 유지되는 방어 자세
    // ──────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 버튼 상태를 이벤트가 아니라 <b>직접 폴링</b>한다. PlayerController.OnParry 가 timeScale 0 이나
    /// 입력 차단 상태에서 조기 return 하기 때문에, 그때 손을 떼면 '뗐다'는 이벤트가 통째로 증발해
    /// 가드가 영원히 켜진 채로 남는다. 폴링은 그 구멍이 없고 입력 배선도 안 건드린다.
    /// </summary>
    private IEnumerator GuardHoldRoutine(MinionRightClick rc)
    {
        BeginStance(rc);
        // 가드는 지속형이라 BeginStance 의 기본 3초 안전망이 자세를 풀어버린다(3초 넘게 누르면
        // 경고 로그 1회 + Parry 포즈가 Idle 로 붕괴). 해제는 EndStance 의 CanChangeAnimState 가 한다.
        _player.LockAnimState(9999f);
        _isHoldingGuard = true;

        Vector2 aimDir = AimDir();
        OpenWindow(rc, aimDir);
        _holdTelegraph = CreateTelegraphSector(aimDir, rc, -1f); // -1 = 자동 파괴 안 함

        try
        {
            while (true)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse == null || !mouse.rightButton.isPressed) break;
                if (_player.Stat.Health.IsDead || _player.IsDashing || _player.IsCCed) break;

                // 방향은 매 프레임 마우스를 따라간다 — '바라보는 방향'이 곧 막는 방향이다.
                aimDir = AimDir();
                _activeAimDir = aimDir;
                FaceAim(aimDir);

                if (_holdTelegraph != null)
                {
                    _holdTelegraph.transform.position = transform.position;
                    _holdTelegraph.transform.rotation =
                        Quaternion.Euler(0f, 0f, Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg);
                }

                yield return null;
            }
        }
        finally
        {
            CloseWindow();
            DestroyHoldTelegraph();
            _isHoldingGuard = false;
        }

        EndStance();
    }

    private void DestroyHoldTelegraph()
    {
        if (_holdTelegraph != null) Destroy(_holdTelegraph);
        if (_holdTelegraphMat != null) Destroy(_holdTelegraphMat);
        _holdTelegraph = null;
        _holdTelegraphMat = null;
    }

    // ──────────────────────────────────────────────────────────────────────
    private void BeginStance(MinionRightClick rc)
    {
        _isParrying = true;
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.Parry, rc.moveSpeedMultiplier);

        OnParryStart?.Invoke();

        // 애니메이션은 3종 공통이다 — 종류는 부채꼴 색으로만 구분한다.
        // Idle/Walk 자동 전환을 잠그고 캐시를 비운다(공격 모션과 같은 이유).
        _player.LockAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Parry", "Idle");
    }

    private void EndStance()
    {
        _isParrying = false;
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.Parry);

        _player.CanChangeAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
        _parryCoroutine = null;
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

    /// <summary>카운터 성공 시 판정 반경 안의 적 전체를 경직시킨다(되받아친 그 적만이 아니다).</summary>
    private void StunNearbyEnemies(MinionRightClick rc)
    {
        if (rc.stunDuration <= 0f) return;

        foreach (var col in Physics2D.OverlapCircleAll(transform.position, rc.radius, Layers.EnemyMask))
        {
            if (col == null) continue;
            var stat = col.GetComponent<CharacterStat>() ?? col.GetComponentInParent<CharacterStat>();
            if (stat == null || stat.Status == null) continue;
            if (stat.Health == null || stat.Health.IsDead) continue;

            stat.Status.ApplyStatus(StatusType.Stun, rc.stunDuration);
        }
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

    /// <summary>
    /// [영역 판정] 판정 범위 안에 들어온 '살아 있는' 적 근접 히트박스를 되받아친다.
    /// 이게 없으면 카운터는 "내가 실제로 피해를 입었을 때"만 성립한다 — 판정 영역이라는 개념이
    /// 사실상 없어진다. 되받아친 히트박스는 파괴한다(= 튕겨냈으니 이 공격은 사라진다).
    /// 접촉 피해처럼 히트박스를 안 쓰는 공격(차저 돌진)은 그대로 피해 경로가 잡는다.
    /// </summary>
    private bool CheckAndBlockEnemyHitBoxes(Vector2 aimDir, MinionRightClick rc)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, rc.radius);

        foreach (var col in colliders)
        {
            if (col == null) continue;

            var box = col.GetComponent<BaseHitBox>();
            if (box == null) continue;
            if (!box.IsLive) continue;                 // 윈드업(장판 차오르는) 중인 건 아직 공격이 아니다
            if (box.HasHitAnyone) continue;            // 이미 때리고 지나간 잔해는 받아낼 대상이 아니다
            if (!box.Targets(Layers.Player)) continue; // 나를 노리는 것만 (내 히트박스 되받아치기 방지)
            if (!QualifiesForCounter(box.Info)) continue;
            if (!IsInAimCone(box.transform.position, aimDir, rc.angle)) continue;

            Record(box.Info.attacker, box.Info.amount);
            Destroy(box.gameObject);
            return true;
        }

        return false;
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

    /// <summary>
    /// 판정 범위 부채꼴을 그린다. 메쉬는 항상 <b>+X 방향 기준</b>으로 만들고 방향은 transform 회전으로
    /// 준다 — 가드는 홀드 중 마우스를 따라 계속 돌아야 해서, 방향을 정점에 구워버리면 매 프레임
    /// 메쉬를 다시 만들어야 한다.
    /// </summary>
    /// <param name="lifetime">초 뒤 자동 파괴. 0 이하면 파괴하지 않는다(호출한 쪽이 책임진다).</param>
    private GameObject CreateTelegraphSector(Vector2 dir, MinionRightClick rc, float lifetime)
    {
        float radius = rc.radius;
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
            _holdTelegraphMat = mat; // 홀드는 DestroyHoldTelegraph 가 둘 다 치운다
        }

        return go;
    }
}
