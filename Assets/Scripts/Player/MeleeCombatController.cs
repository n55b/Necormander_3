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
    [Tooltip("평타 1, 2타 시의 순간 돌진력(가속 배율)입니다. 기본값 1.0f")]
    [SerializeField] private float lightAttackDashMultiplier = 1.0f;
    [Tooltip("평타 3타(피니시) 시의 순간 돌진력(가속 배율)입니다. 기본값 1.5f")]
    [SerializeField] private float mediumAttackDashMultiplier = 1.5f;

    private PlayerController _player;
    private float _lastAttackTime;
    private int _comboStep = 0; // 0, 1, 2

    [Header("콤보 설정")]
    [SerializeField] private float comboResetTime = 1.0f;
    [SerializeField] private float attackCooldown = 0.3f; // 콤보 간 최소 딜레이

    [Header("타격 범위 설정")]
    [SerializeField] private Vector2 lightHitboxSize = new Vector2(2f, 1.5f);
    [SerializeField] private Vector2 mediumHitboxSize = new Vector2(3f, 2f);
    [SerializeField] private float lightTelegraphDuration = 0.2f;
    [SerializeField] private float mediumTelegraphDuration = 0.4f;

    private bool _isHoldingAttack = false;
    private BaseHitBox _activeHitbox; // TelegraphHitbox -> BaseHitBox로 변경

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
                    if (_player.IsDashing || isParrying || _player.IsCastingSkill)
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

        var parryCtrl = _player.GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        _lastAttackTime = Time.time;
        OnAttackExecuted?.Invoke(_comboStep);


        float telegraphDuration = (_comboStep == 2) ? mediumTelegraphDuration : lightTelegraphDuration;
        Vector2 hitboxSize = (_comboStep == 2) ? mediumHitboxSize : lightHitboxSize;
        float damageMultiplier = (_comboStep == 2) ? 1.5f : 1.0f;

        // [애니메이션 재생]
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack, 0f); // [수정] 평타 모션 중 키보드 수동 이동 차단

        // 공격 애니메이션 재생 중에는 Update()의 Idle/Walk 자동 전환을 잠그고,
        // 캐시를 초기화해서 공격이 끝난 뒤 Idle로 제대로 복귀하도록 합니다.
        // (실제 복귀는 애니메이션 클립의 'CanChangeAnimState' 이벤트가 canChangeState를 다시 true로 바꿔줄 때 일어납니다)
        _player.LockAnimState(); // canChangeState lock with timeout safety net
        _player.ResetAnimStateCache();

        if (_comboStep == 0) _player.PlayAllAnim("Attack_Light1", "Attack");
        else if (_comboStep == 1) _player.PlayAllAnim("Attack_Light2", "Attack");
        else _player.PlayAllAnim("Attack_Medium", "Attack");

        // Higher ATKSPD (lower stat value) plays the attack animation faster.
        if (_player.Stat != null && _player.Stat.ATKSPD > 0.0001f)
        {
            float atkAnimSpeed = Mathf.Clamp(1f / _player.Stat.ATKSPD, 0.5f, 3f);
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
                float saDmg = (_comboStep == 2) ? 30f : 20f; // 1,2타는 20f, 3타는 30f
                DamageInfo info = new DamageInfo(
                    _player.Stat.ATK * damageMultiplier,
                    DamageType.Physical,
                    this.gameObject,
                    false, 1f, true, "", false,
                    causesHitstun: true,
                    knockbackForce: _comboStep == 2 ? 6f : 2f,
                    superArmorDamage: saDmg
                );

                LayerMask enemyLayer = LayerMask.GetMask("Enemy");
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

        // 콤보 진행
        _comboStep = (_comboStep + 1) % 3;
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
                
                // 3. 콤보 피니시(3타) 및 1/2타 타격 순간 대시 가속력 배율 적용
                // ExecuteMeleeAttack에서 _comboStep이 이미 (스텝+1)%3 으로 갱신되어 있으므로:
                // 1타 타격 시점: _comboStep == 1
                // 2타 타격 시점: _comboStep == 2
                // 3타 타격 시점: _comboStep == 0
                float forceMultiplier = (_comboStep == 0) ? mediumAttackDashMultiplier : lightAttackDashMultiplier;

                _player.ApplyAttackDash(dashDir, forceMultiplier);
            }
        }
    }

    public void CancelAttack()
    {
        if (_activeHitbox != null)
        {
            Destroy(_activeHitbox.gameObject);
            _activeHitbox = null;
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
