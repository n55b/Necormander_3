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

        // [추가] Unsteppable 안전 체크 및 대시 도달 범위 축소
        float originalDist = dashSpeed * dashDuration;
        Vector2 safePos = _player.GetSafeDashPosition(transform.position, _dashDir, originalDist);
        float actualDist = Vector2.Distance(transform.position, safePos);
        _dashTimeLeft = actualDist / dashSpeed; // 동적으로 대시 시간 조절

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
