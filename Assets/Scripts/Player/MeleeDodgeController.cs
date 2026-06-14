using UnityEngine;

public class MeleeDodgeController : MonoBehaviour
{
    [Header("대쉬 설정")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private int maxCharges = 2;
    [SerializeField] private float rechargeTime = 2.0f; // 스택 회복 시간

    private int _currentCharges;
    private float _rechargeTimer;

    private bool _isDashing;
    private float _dashTimeLeft;
    private Vector2 _dashDir;

    private PlayerController _player;
    private Rigidbody2D _rb;

    public bool IsDashing => _isDashing;
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
        _isDashing = true;
        _dashTimeLeft = dashDuration;

        // 입력 방향이 없으면 바라보는 방향으로
        _dashDir = moveInput.normalized;
        if (_dashDir == Vector2.zero)
        {
            _dashDir = new Vector2(-Mathf.Sign(currentFacingSign), 0).normalized;
        }

        if (_player.Stat != null && _player.Stat.Health != null)
        {
            _player.Stat.Health.Invincible = true; // 대쉬 무적
        }

        _player.PlayAllAnim("Dash"); // 애니메이션 이름은 실제 환경에 맞춰 수정
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
        
        _player.PlayAllAnim("Idle");
    }


public int   MaxCharges      => maxCharges;
    public float RechargeTime    => rechargeTime;
    public float RechargeProgress
        => (_currentCharges < maxCharges && rechargeTime > 0f)
            ? 1f - Mathf.Clamp01(_rechargeTimer / rechargeTime)
            : 1f;
}
