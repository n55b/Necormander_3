using UnityEngine;

public class MeleeDodgeController : MonoBehaviour
{
    [Header("대쉬 설정")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private int maxCharges = 2;
    [SerializeField] private float rechargeTime = 2.0f; // 스택 회복 시간
    
    [Header("대쉬 이펙트")]
    [SerializeField] private GameObject dashEffectPrefab; // 대쉬 이펙트 프리팹

    [Header("대쉬 히트박스 (메인 소환수가 대쉬에 피해를 붙일 때 사용)")]
    [Tooltip("BaseHitBox 가 붙은 프리팹. 미지정이면 소환수가 대쉬 피해를 갖고 있어도 발동하지 않는다.")]
    [SerializeField] private BaseHitBox dashHitBoxPrefab;

    private int _currentCharges;
    private float _rechargeTimer;

    private bool _isDashing;
    private float _dashTimeLeft;
    private Vector2 _dashDir;

    private PlayerController _player;
    private Rigidbody2D _rb;

    public bool IsDashing => _isDashing;
    /// <summary>TryDash 성공 시 발생</summary>
    public event System.Action OnDodgeStarted;

    public int CurrentCharges => _currentCharges;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        _rb = GetComponent<Rigidbody2D>();
        _currentCharges = maxCharges;
    }

    private void Update()
    {
        // 쿨타임(스택) 회복
        if (_currentCharges < maxCharges)
        {
            _rechargeTimer -= Time.deltaTime;
            if (_rechargeTimer <= 0f)
            {
                _currentCharges++;
                if (_currentCharges < maxCharges)
                {
                    _rechargeTimer = rechargeTime;
                }
            }
        }

        // 대쉬 중단 체크
        if (_isDashing)
        {
            _dashTimeLeft -= Time.deltaTime;
            if (_dashTimeLeft <= 0f)
            {
                EndDash();
            }
        }
    }

    private void FixedUpdate()
    {
        if (_isDashing && _rb != null)
        {
            // 대쉬 중 물리 속도 강제 덮어쓰기
            _rb.linearVelocity = _dashDir * dashSpeed;
        }
    }

    public bool TryDash(Vector2 moveInput, float currentFacingSign)
    {
        if (_isDashing || _currentCharges <= 0) return false;

        // 스택 차감 및 타이머 시작
        if (_currentCharges == maxCharges)
        {
            _rechargeTimer = rechargeTime;
        }
        _currentCharges--;

        StartDash(moveInput, currentFacingSign);
        return true;
    }

private void StartDash(Vector2 moveInput, float currentFacingSign)
    {
        // Cancel ongoing melee attack, throw charge, and active skill cast
        var meleeCtrl = _player.GetComponent<MeleeCombatController>();
        if (meleeCtrl != null && meleeCtrl.IsAttacking)
        {
            meleeCtrl.CancelAttack();
        }
        var throwCtrl = _player.GetComponentInChildren<ThrowController>();
        if (throwCtrl != null && throwCtrl.IsCharging)
        {
            throwCtrl.InputHandler.ResetCharging();
        }
        _player.CancelActiveSkill();

        _isDashing = true;

        // 입력 방향이 없으면 바라보는 방향으로 (Instantiate보다 먼저 계산해야 이펙트 방향을 알 수 있음)
        _dashDir = moveInput.normalized;
        if (_dashDir == Vector2.zero)
        {
            _dashDir = new Vector2(-Mathf.Sign(currentFacingSign), 0).normalized;
        }

        // [수정] Dash Smog Effect - 대쉬 방향에 따라 좌우 반전
        if (dashEffectPrefab != null)
        {
            GameObject fx = Instantiate(dashEffectPrefab, transform.position, Quaternion.identity);
            Vector3 fxScale = fx.transform.localScale;
            fxScale.x = Mathf.Abs(fxScale.x) * (_dashDir.x < 0f ? 1f : -1f);
            fx.transform.localScale = fxScale;
        }

        // 메인 소환수가 대쉬를 개조한다 (없으면 mod == null → 기본 대쉬 그대로).
        var mod = GetDashModifier();
        float lengthMult = (mod != null) ? Mathf.Max(0.01f, mod.lengthMultiplier) : 1f;

        // [추가] Unsteppable 안전 체크 및 대시 도달 범위 축소
        // 배율은 벽 클램프 '전'에 곱한다 — 클램프가 최종 도달점을 잡아야 벽을 뚫지 않는다.
        float originalDist = dashSpeed * dashDuration * lengthMult;
        Vector2 safePos = _player.GetSafeDashPosition(transform.position, _dashDir, originalDist);
        float actualDist = Vector2.Distance(transform.position, safePos);
        _dashTimeLeft = actualDist / dashSpeed; // 동적으로 대시 시간 조절

        if (mod != null && mod.DealsDamage)
            SpawnDashHitBox(mod, transform.position, _dashDir, actualDist);

        if (_player.Stat != null && _player.Stat.Health != null)
        {
            _player.Stat.Health.Invincible = true; // 대쉬 무적
        }

        _player.SetDashLayer(true); // set dash layer

        // Lock the Idle/Walk auto-transition for the dash duration, otherwise
        // PlayerController.Update() overwrites the Dash pose one frame later.
        _player.LockAnimState(dashDuration);
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Dash", "Idle");
        OnDodgeStarted?.Invoke();
    }

    /// <summary>장착된 메인 소환수의 대쉬 개조안. 없으면 null(= 기본 대쉬).</summary>
    private MinionDashModifier GetDashModifier()
    {
        var skillCtrl = _player != null ? _player.GetComponent<PlayerSkillController>() : null;
        var main = skillCtrl != null ? skillCtrl.MainSummon : null;
        return main != null ? main.dashModifier : null;
    }

    /// <summary>
    /// 대쉬 경로를 덮는 히트박스를 깐다. 대쉬 자체는 원래 순수 이동이라 이 히트박스가 신규다.
    /// 다단히트는 BaseHitBox 의 isContinuousDamage + damageTickRate 로 낸다.
    /// </summary>
    private void SpawnDashHitBox(MinionDashModifier mod, Vector2 origin, Vector2 dir, float dist)
    {
        if (dashHitBoxPrefab == null)
        {
            Debug.LogWarning("<color=orange>[MeleeDodge]</color> 소환수가 대쉬 피해를 갖고 있는데 dashHitBoxPrefab 이 비어 있습니다.");
            return;
        }
        if (dist <= 0.01f) return;

        float travelTime = dist / dashSpeed;

        // 이 프리팹의 콜라이더는 '전방 기준'이다 (m_Offset.x = 0.5, m_Size.x = 1 -> 로컬 x [0,1]).
        // 즉 자기 원점에서 앞으로 자란다. 그래서 대쉬 시작점에 그대로 깔면 [0, dist] 를
        // 정확히 덮는다. ChargerAIPatternSO 도 같은 프리팹을 이렇게 쓴다.
        //
        // 예전엔 여기서 경로 '중앙'에 놓았는데, 그러면 로컬 오프셋 0.5 가 회전+스케일(dist)까지
        // 먹어서 박스가 [0.5d, 1.5d] 로 통째로 밀렸다 — 시작 지점의 적은 안 맞고, 멈춘 자리보다
        // 0.5d 앞까지 때렸다 ("히트박스가 너무 앞에 길게").
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        var box = Instantiate(dashHitBoxPrefab, origin, Quaternion.Euler(0, 0, angle));
        box.transform.localScale = new Vector3(dist, mod.width, 1f);
        box.hitEffectAngle = angle; // 누락돼 있었다 — 없으면 히트 이펙트가 대쉬 방향과 무관하게 오른쪽으로 튄다

        float dmg = (_player.Stat != null ? _player.Stat.ATK : 0f) * mod.damageMultiplier;
        var info = new DamageInfo(dmg, DamageType.Physical, _player.gameObject, false, 1f, true, "Dash",
                                  causesHitstun: true, knockbackForce: mod.pushesEnemies ? mod.pushForce : 0f);

        System.Action<CharacterHealth> onHit = null;
        if (mod.pushesEnemies)
        {
            onHit = (health) =>
            {
                var entity = health.GetComponentInParent<BaseEntity>();
                if (entity != null) entity.ApplyKnockback(dir * mod.pushForce);
            };
        }

        if (mod.hitCount > 1)
        {
            // N타를 이동 시간 안에 균등 배분한다.
            box.isContinuousDamage = true;
            box.damageTickRate = travelTime / mod.hitCount;
        }
        else
        {
            box.isContinuousDamage = false;
        }

        box.Init(info, Layers.EnemyMask, travelTime, 0f, true, onHit);
    }

private void EndDash()
    {
        _isDashing = false;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
        }

        if (_player.Stat != null && _player.Stat.Health != null)
        {
            _player.Stat.Health.Invincible = false;
        }

        _player.SetDashLayer(false); // restore original layer

        _player.CanChangeAnimState(); // unlocks canChangeState and cancels the LockAnimState timeout
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
    }


public int   MaxCharges      => maxCharges;
    public float RechargeTime    => rechargeTime;
    public float RechargeProgress
        => (_currentCharges < maxCharges && rechargeTime > 0f)
            ? 1f - Mathf.Clamp01(_rechargeTimer / rechargeTime)
            : 1f;
}
