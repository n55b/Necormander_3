using UnityEngine;
using UnityEngine.InputSystem;

public class MeleeCombatController : MonoBehaviour
{
    [SerializeField] private GameObject telegraphPrefab; // 인스펙터 할당
    public GameObject TelegraphPrefab => telegraphPrefab;

    [Header("공격 스폰 포인트")]
    [Tooltip("플레이어 공격 히트박스가 스폰될 시작 기준점 트랜스폼입니다. 미지정 시 플레이어 본체 피벗을 사용합니다.")]
    [SerializeField] private Transform attackSpawnPoint;

    [Header("평타 대시 물리력 설정")]
    [Tooltip("평타 타격 순간의 돌진력(가속 배율)입니다. 기본값 1.0f")]
    [SerializeField] private float lightAttackDashMultiplier = 1.0f;

    private PlayerController _player;
    private float _lastAttackTime;
    private int _comboStep = 0; // 0, 1 (+ 메인 소환수가 있으면 2 = 소환수 마무리)

    /// <summary>플레이어 자체 평타는 2타. 메인 소환수가 있으면 3타째에 소환수 마무리가 붙는다.</summary>
    private const int PLAYER_COMBO_LENGTH = 2;

    /// <summary>장착된 메인 소환수의 마무리 일격. 없으면 null.</summary>
    private MinionFinisher Finisher
    {
        get
        {
            // 매 공격마다 읽는다 — SyncWithInventory 가 런 도중 소환수를 갈아끼울 수 있다.
            var skillCtrl = _player != null ? _player.GetComponent<PlayerSkillController>() : null;
            var main = skillCtrl != null ? skillCtrl.MainSummon : null;
            if (main == null) return null;
            return (main.finisher != null && main.finisher.IsValid) ? main.finisher : null;
        }
    }

    /// <summary>현재 콤보 총 타수. 메인 소환수가 있으면 +1.</summary>
    private int ComboLength => Finisher != null ? PLAYER_COMBO_LENGTH + 1 : PLAYER_COMBO_LENGTH;

    /// <summary>이번 스텝이 소환수 마무리 차례인가.</summary>
    private bool IsFinisherStep(int step) => Finisher != null && step == PLAYER_COMBO_LENGTH;

    [Header("콤보 설정")]
    [SerializeField] private float comboResetTime = 1.0f;
    [SerializeField] private float attackCooldown = 0.3f; // 콤보 간 최소 딜레이

    [Header("타격 범위 설정")]
    [SerializeField] private Vector2 lightHitboxSize = new Vector2(2f, 1.5f);
    [SerializeField] private float lightTelegraphDuration = 0.2f;
    // ponytail: medium* (옛 3타 전용) 필드들은 제거됐다. 3타는 소환수 마무리가 대신하고
    // 그 수치는 MinionDataSO.finisher 가 갖는다.

    [Header("평타 범위 표시(텔레그래프) 숨김")]
    [Tooltip("평타(기본 공격)의 범위 표시용 히트박스 시각효과를 숨깁니다. 데미지 판정은 그대로 유지됩니다. (스킬/적 텔레그래프에는 영향 없음)")]
    [SerializeField] private bool hideBasicAttackTelegraph = true;

    [Tooltip("소환수 마무리 일격의 범위 표시도 숨길지. 기본은 '보임' — 마무리는 선딜(HitDelay)이 있어서 " +
             "차오르는 바가 '어디를 언제 치는지'를 알려주는 게 평타보다 중요합니다.")]
    [SerializeField] private bool hideFinisherTelegraph = false;

    private bool _isHoldingAttack = false;
    private BaseHitBox _activeHitbox; // TelegraphHitbox -> BaseHitBox로 변경
    private MinionSkillCaster _activeCaster; // 마무리 타격 소환수 추적 — 연속 공격 시 중복 스폰 방지용

    // 콤보 스텝을 함께 전달하는 공격 시작 이벤트 (int = comboStep 0/1/2)
    public event System.Action<int> OnAttackExecuted;

    public bool IsAttacking => _activeHitbox != null || (Time.time - _lastAttackTime) < attackCooldown;
    public Vector2 CurrentAttackDir { get; private set; } = Vector2.right;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
    }

    private void Update()
    {
        // 콤보 리셋
        if (!IsAttacking)
        {
            if (_player != null)
            {
                _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack); // 공격 끝났으므로 이속 복구
            }

            if (Time.time - _lastAttackTime > comboResetTime)
            {
                _comboStep = 0;
            }

            // 홀드 공격 유지 (현재 텔레그래프가 끝났고, 쿨다운이 지났다면 다음 콤보 발동)
            if (_isHoldingAttack)
            {
                // 플레이어가 대시 중, 패리 중, 또는 스킬 시전 중일 때는 꾹 누르고 있어도 자동 공격 제한
                bool canAttack = true;
                if (_player != null)
                {
                    var parryCtrl = _player.GetComponent<PlayerParryController>();
                    bool isParrying = parryCtrl != null && parryCtrl.IsParrying;
                    if (_player.IsDashing || isParrying || _player.IsCastingSkill || _player.IsUsingHandSkill)
                    {
                        canAttack = false;
                    }
                }

                if (canAttack)
                {
                    ExecuteMeleeAttack();
                }
            }
        }
    }

    public void OnAttackInput(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            _isHoldingAttack = true;
            if (!IsAttacking)
            {
                ExecuteMeleeAttack();
            }
        }
        else if (context.canceled)
        {
            _isHoldingAttack = false;
        }
    }

    private void ExecuteMeleeAttack()
    {
        if (Time.timeScale == 0f) return; // [추가] 시간 일시정지 중 공격 차단
        if (_player == null || _player.Stat.Health.IsDead) return;
        if (_player.IsCCed) return; // [26/07/17] 기절/빙결 중에는 평타 차단
        if (_player.IsCastingSkill || _player.IsUsingHandSkill) return; // [추가] 스킬 사용 중에는 평타 차단

        var parryCtrl = _player.GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        _lastAttackTime = Time.time;
        OnAttackExecuted?.Invoke(_comboStep);

        // 마무리 타이밍이면 플레이어는 아무것도 하지 않고, 소환수가 나와서 때린다.
        if (IsFinisherStep(_comboStep))
        {
            ExecuteFinisher();
            _comboStep = (_comboStep + 1) % ComboLength;
            return;
        }

        float telegraphDuration = lightTelegraphDuration;
        Vector2 hitboxSize = lightHitboxSize;
        // 평타 1·2타 배율. 예전엔 1.0 하드코딩이었는데, "평타 데미지만 높이는 증감 요소"를
        // 나중에 넣을 수 있게 스탯으로 뺐다. 기본값은 그대로 1.0 이라 동작은 같다.
        // (3타는 소환수 마무리라 여기 안 온다 — 소환수 고유 배율을 쓴다.)
        float damageMultiplier = _player.Stat != null ? _player.Stat.BASIC_ATK_MULT : 1.0f;

        // [애니메이션 재생]
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack, 0f); // [수정] 평타 모션 중 키보드 수동 이동 차단

        // 공격 애니메이션 재생 중에는 Update()의 Idle/Walk 자동 전환을 잠그고,
        // 캐시를 초기화해서 공격이 끝난 뒤 Idle로 제대로 복귀하도록 합니다.
        // (실제 복귀는 애니메이션 클립의 'CanChangeAnimState' 이벤트가 canChangeState를 다시 true로 바꿔줄 때 일어납니다)
        _player.LockAnimState(); // canChangeState lock with timeout safety net
        _player.ResetAnimStateCache();

        // ponytail: Attack_Medium(옛 3타) 은 이제 재생하지 않는다. 3타는 소환수 마무리가 대신하고
        // 플레이어는 Idle 로 있는다. 클립 자체는 남겨둠 — 되살릴 때 다시 연결하면 된다.
        if (_comboStep == 0) _player.PlayAllAnim("Attack_Light1", "Attack");
        else _player.PlayAllAnim("Attack_Light2", "Attack");

        // 공속(회/초)이 곧 애니 배속이다. 1회/초 = 1배속, 2회/초 = 2배속.
        if (_player.Stat != null)
        {
            float atkAnimSpeed = Mathf.Clamp(_player.Stat.ATKSPD, 0.5f, 3f);
            _player.SetAttackAnimSpeed(atkAnimSpeed);
        }

        // [HitBox 소환]
        if (telegraphPrefab != null)
        {
            // 마우스 방향 또는 이동 방향을 공격 방향으로 설정 (지정된 스폰포인트 기준으로 에이밍 방향 산출)
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            mousePos.z = 0;
            Vector3 startOrigin = attackSpawnPoint != null ? attackSpawnPoint.position : transform.position;
            Vector2 dir = (mousePos - startOrigin).normalized;
            CurrentAttackDir = dir;

            // 플레이어가 바라보는 방향 동기화
            if (dir.x > 0) transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
            else if (dir.x < 0) transform.localScale = new Vector3(1, transform.localScale.y, transform.localScale.z);

            Vector3 spawnPos = attackSpawnPoint != null ? attackSpawnPoint.position : transform.position;
            GameObject go = Instantiate(telegraphPrefab, spawnPos, Quaternion.identity, transform); // [수정] 월드가 아닌 시전자(플레이어)의 하위 자식으로 붙여 이동 궤적 동기화

            // [평타 텔레그래프 숨김] 범위 표시용 시각(SpriteRenderer)만 끄고 콜라이더/데미지 판정은 그대로 둔다.
            if (hideBasicAttackTelegraph)
            {
                foreach (var vis in go.GetComponentsInChildren<SpriteRenderer>(true))
                    vis.enabled = false;
                // [중요] SpriteMask도 반드시 꺼야 한다. 텔레그래프 프리팹엔 안쪽을 도려내는 SpriteMask가 들어있는데,
                // SpriteRenderer만 끄면 눈엔 안 보여도 '마스크'는 살아있어서, 겹치는 적 텔레그래프의
                // 외곽선(MaskInteraction=Visible Outside Mask)을 마스크 영역만큼 잘라내(덮어써) 경계선이 사라진다.
                foreach (var mask in go.GetComponentsInChildren<SpriteMask>(true))
                    mask.enabled = false;
            }

            // 범용 BaseHitBox 사용
            _activeHitbox = go.GetComponent<BaseHitBox>();

            if (_activeHitbox != null)
            {
                // 부모의 scale.x 반전에 맞물려 대칭 정렬되도록 로컬 오프셋 유지
                go.transform.localPosition = attackSpawnPoint != null ? attackSpawnPoint.localPosition : Vector3.zero;

                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                // [수정] 부모(플레이어)의 localScale.x 반전(-1) 상태에 따른 로컬 회전 각도 기하학적 보정 (마우스 좌우 대칭 오류 해결)
                float localAngle = (transform.localScale.x < 0f) ? (180f - angle) : angle;
                go.transform.localRotation = Quaternion.Euler(0, 0, localAngle);

            // [추가] 히트 이펙트는 부모(플레이어)의 미러링 보정 없이 스폰되므로, 보정 전의 실제 타격 방향(angle)을 그대로 전달
            if (_activeHitbox != null) _activeHitbox.hitEffectAngle = angle;

                // 크기 설정 (TelegraphHitbox 로직 대체)
                go.transform.localScale = new Vector3(hitboxSize.x, hitboxSize.y, 1f);

                // 플레이어 공격은 적에게 경직과 약간의 넉백을 유발
                float saDmg = 20f;
                // 서브 소환수의 평타 고정 추가 피해
                var subPassive = _player.GetComponent<SubSummonPassiveController>();
                float flatBonus = subPassive != null ? subPassive.BasicAttackDamageBonus : 0f;

                DamageInfo info = new DamageInfo(_player.Stat.ATK * damageMultiplier + flatBonus, DamageType.Physical, this.gameObject, 1f, "", false, causesHitstun: true, knockbackForce: 2f, superArmorDamage: saDmg, category: DamageCategory.BasicAttack);

                LayerMask enemyLayer = Layers.EnemyMask;
                // duration은 0.2f(타격유지시간), startDelay는 telegraphDuration(선딜레이)
                _activeHitbox.Init(info, enemyLayer, 0.2f, telegraphDuration);
            }
            else
            {
                Debug.LogError("[MeleeCombat] telegraphPrefab에 BaseHitBox 스크립트가 없습니다!");
            }
        }
        else
        {
            Debug.LogError("[MeleeCombat] telegraphPrefab이 인스펙터에 할당되지 않았습니다.");
        }

        // 콤보 진행 (메인 소환수가 있으면 3타, 없으면 2타 반복)
        _comboStep = (_comboStep + 1) % ComboLength;
    }

    /// <summary>
    /// Attack 애니메이션 클립의 실제 타격 프레임(Animation Event)에서 호출됩니다.
    /// 텔레그래프 선딜레이를 강제로 끝내고 즉시 데미지 판정을 시작합니다.
    /// (BodyAnimationEventRelay -> 여기로 전달됨)
    /// </summary>
    public void OnAttackHitFrame()
    {
        _activeHitbox?.ForceActivate();

        if (_player != null)
        {
            // 1. 방향키 입력 방향 (MoveInput) 가져오기
            Vector2 inputDir = _player.MoveInput;
            
            // 2. [수정] 방향키 입력이 있는 경우에만 가속 전진을 수행 (가만히 서서 때릴 때는 제자리 타격)
            if (inputDir.sqrMagnitude > 0.001f)
            {
                Vector2 dashDir = inputDir.normalized;
                
                // 플레이어 평타는 이제 1/2타뿐이라 전부 light 배율을 쓴다.
                // (3타 = 소환수 마무리는 플레이어가 움직이지 않으므로 이 경로를 타지 않는다)
                float forceMultiplier = lightAttackDashMultiplier;

                _player.ApplyAttackDash(dashDir, forceMultiplier);
            }
        }
    }

    /// <summary>
    /// 콤보 마지막 타이밍: 플레이어는 아무것도 하지 않고, 메인 소환수가 실체화해 마무리 일격을 넣는다.
    /// (설계 3.3 "마지막에 소환수의 마무리 일격이 발동" — 플레이어는 Idle)
    /// </summary>
    private void ExecuteFinisher()
    {
        var skillCtrl = _player.GetComponent<PlayerSkillController>();
        var main = skillCtrl != null ? skillCtrl.MainSummon : null;
        var fin = Finisher;
        if (main == null || fin == null) return;

        // ── 마무리 일격의 시전 시간은 공속을 따라간다 ───────────────────────────────
        // 마무리는 '평타 콤보의 3타'다. 1·2타만 공속으로 빨라지고 3타가 원래 속도로 남으면
        // 콤보 중간에 속도가 뚝 떨어진다. 그래서 여기만 공속을 먹인다.
        //
        // [유의사항 — 나중에 물어볼 것]
        // 소환수 R 액티브(MinionSkillSO.skillAnimDuration)는 일부러 공속을 '안' 받는다.
        // R 은 평타 콤보의 일부가 아니라 독립 스킬이라 분리해 둔 것이다(기획 확정, 26/07/17).
        // 로직상 마음에 안 드는 결정이라고 하셨으니, 연결하고 싶어지면 MinionActionSkillSO 의
        // animDuration 계산에 같은 나눗셈만 얹으면 된다 — 애니와 타격 시점이 전부 비율로
        // 묶여 있어서 castDuration/skillAnimDuration 하나만 줄이면 나머지가 알아서 따라온다.
        float atkSpd = (_player.Stat != null) ? Mathf.Max(0.05f, _player.Stat.ATKSPD) : 1f;
        float castDuration = fin.castDuration / atkSpd;
        float hitWindow = fin.EventHitWindow / atkSpd;

        // 조준 방향은 평타와 동일하게 마우스 기준.
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mousePos.z = 0;
        Vector3 origin = attackSpawnPoint != null ? attackSpawnPoint.position : transform.position;
        Vector2 dir = ((Vector2)(mousePos - origin)).normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = CurrentAttackDir;
        CurrentAttackDir = dir;

        // 플레이어 외형만 방향 동기화하고, 모션은 재생하지 않는다 (Idle 유지).
        if (dir.x > 0) transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
        else if (dir.x < 0) transform.localScale = new Vector3(1, transform.localScale.y, transform.localScale.z);

        // [수정] 히트박스는 평타처럼 조준 각도(dir)에 맞춰 자유 회전시킨다.
        // (소환수 스프라이트 자체는 좌우로만 뒤집히지만, 판정 박스는 실제 조준 방향을 그대로 따라간다.)
        bool faceRight = dir.x >= 0f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector3 spawnPos = origin + (Vector3)(dir * fin.spawnOffset);

        // 이전 마무리 소환수가 아직 안 사라졌으면 먼저 정리 (연속 공격 시 여러 마리 남는 문제 방지)
        if (_activeCaster != null)
        {
            Destroy(_activeCaster.gameObject);
            _activeCaster = null;
        }

        var caster = MinionSkillCaster.Spawn(main, spawnPos);
        _activeCaster = caster;

        // 이펙트는 같은 애니메이터의 다른 상태라 한 오브젝트로 동시 재생이 안 된다.
        // 하나 더 띄워서 겹친다 (예: DashDoll 은 Attack + Effect 가 별도 태그다).
        if (!string.IsNullOrEmpty(fin.effectState))
            caster.AttachVisual(fin.visual, fin.effectState, castDuration, faceRight);

        if (telegraphPrefab == null) return;

        GameObject go = Instantiate(telegraphPrefab, spawnPos, Quaternion.identity, caster.transform);
        if (hideFinisherTelegraph)
        {
            foreach (var vis in go.GetComponentsInChildren<SpriteRenderer>(true)) vis.enabled = false;
            foreach (var mask in go.GetComponentsInChildren<SpriteMask>(true)) mask.enabled = false;
        }

        var box = go.GetComponent<BaseHitBox>();
        if (box == null) return;

        go.transform.localRotation = Quaternion.Euler(0, 0, angle);
        go.transform.localScale = new Vector3(fin.hitBoxSize.x, fin.hitBoxSize.y, 1f);
        box.hitEffectAngle = angle;

        // 피해는 '플레이어의 ATK * 소환수 고유 배율'.
        // [26/07/17] 예전엔 소환수 SO 자신의 attack 을 썼는데, 이제 베이스 ATK 를 공유한다.
        // 그래야 "아군 공격력 증가" 같은 버프를 플레이어 ATK 하나에만 걸어도
        // 주먹과 소환수 마무리에 동시에 먹는다. 소환수의 개성은 배율이 유지한다.
        var info = new DamageInfo(_player.Stat.ATK * fin.damageMultiplier, DamageType.Physical, _player.gameObject, 1f, !string.IsNullOrEmpty(main.minionName) ? $"{main.minionName} 마무리" : "Finisher", false, causesHitstun: fin.causesHitstun, knockbackForce: fin.knockbackForce, superArmorDamage: fin.superArmorDamage, category: DamageCategory.BasicAttack); // 소환수 마무리 일격도 평타 갈래

        // 판정은 '언제 열지'를 애니메이션이 정한다 — 초로 박지 않는다.
        //  · damageState 를 쓰면 그 태그가 재생되는 동안만 열린다 (MeleeDoll: Slash).
        //  · hitEvent 를 쓰면 Aseprite 셀에 심어둔 event: 프레임에 열린다 (DashDoll).
        // 그때까지는 콜라이더를 꺼둔 채로 기다린다. 히트박스를 미리 만들어두는 이유는
        // 텔레그래프(차오르는 바)가 '어디를 칠지'를 그동안 보여줘야 하기 때문이다.
        var col = go.GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        caster.PlaySequenced(
            fin.visual, fin.animSequence, fin.damageState, fin.hitEvent,
            castDuration, hitWindow, faceRight,
            // 판정 열기
            window =>
            {
                if (box == null) return;
                if (fin.hitCount > 1)
                {
                    // 다단히트: 판정창(window) 동안 hitCount 를 균등 배분한다. window 는 PlaySequenced 가
                    // 방식에 따라 정해준다 — 이벤트 창(OnHit~OnAttackEnd) / 태그 길이 / 시퀀스 전체.
                    box.isContinuousDamage = true;
                    box.damageTickRate = window / fin.hitCount;
                }
                else
                {
                    // 단타: 판정이 열리는 순간 1회.
                    box.isContinuousDamage = false;
                }
                if (col != null) col.enabled = true;
                box.Init(info, Layers.EnemyMask, window, 0f, true);
            },
            // OnHitEvent 마다: '이미 때린 대상' 기록을 지워서 다음 물리 스텝에 한 번 더 때린다.
            // 적 공격 클립이 2타면 OnHitEvent 를 2번 박는 것과 같은 계약이다.
            onHitPulse: () => { if (box != null) box.ResetHitTargets(); },
            // OnAttackEndEvent: 후딜까지 끝났으니 판정을 닫는다.
            onAttackEnd: () => { if (col != null) col.enabled = false; });
    }

    public void CancelAttack()
    {
        if (_activeHitbox != null)
        {
            Destroy(_activeHitbox.gameObject);
            _activeHitbox = null;
        }
        if (_activeCaster != null)
        {
            Destroy(_activeCaster.gameObject);
            _activeCaster = null;
        }
        _comboStep = 0;
        // _isHoldingAttack = false; // 대시/패리 후에도 꾹 누르고 있으면 이어서 공격하도록 주석 처리
        _player.SetAttackAnimSpeed(1f);

        // 공격이 중간에 취소되더도 Idle/Walk 전환이 영원히 잠겨있지 않도록 해제합니다.
        _player.CanChangeAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack); // 이동 속도 복구
    }
}
