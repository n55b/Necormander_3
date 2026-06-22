using UnityEngine;
using UnityEngine.InputSystem;

public class MeleeCombatController : MonoBehaviour
{
    [SerializeField] private GameObject telegraphPrefab; // 인스펙터 할당
    public GameObject TelegraphPrefab => telegraphPrefab;

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
                ExecuteMeleeAttack();
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
        if (_player == null || _player.Stat.Health.IsDead) return;

        var parryCtrl = _player.GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        _lastAttackTime = Time.time;
        OnAttackExecuted?.Invoke(_comboStep);


        float telegraphDuration = (_comboStep == 2) ? mediumTelegraphDuration : lightTelegraphDuration;
        Vector2 hitboxSize = (_comboStep == 2) ? mediumHitboxSize : lightHitboxSize;
        float damageMultiplier = (_comboStep == 2) ? 1.5f : 1.0f;

        // [애니메이션 재생]
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack, 0.3f); // 공격 중 이동 속도 감소
        if (_comboStep == 0) _player.PlayAllAnim("Attack_Light1");
        else if (_comboStep == 1) _player.PlayAllAnim("Attack_Light2");
        else _player.PlayAllAnim("Attack_Medium");

        if (_player.Stat != null && _player.Stat.ATKSPD > 0.0001f)
        {
            float atkAnimSpeed = Mathf.Clamp(1f / _player.Stat.ATKSPD, 0.5f, 3f);
            _player.SetAttackAnimSpeed(atkAnimSpeed);
        }

        if (_player.Stat != null && _player.Stat.ATKSPD > 0.0001f)
        {
            float atkAnimSpeed = Mathf.Clamp(1f / _player.Stat.ATKSPD, 0.5f, 3f);
            _player.SetAttackAnimSpeed(atkAnimSpeed);
        }

        // 공격속도에 맞춰 건 애니모르 재생 속도 조절 (ATKSPD는 주기(조)이단 작을수르 묌단)
        if (_player.Stat != null && _player.Stat.ATKSPD > 0.0001f)
        {
            float atkAnimSpeed = Mathf.Clamp(1f / _player.Stat.ATKSPD, 0.5f, 3f);
            _player.SetAttackAnimSpeed(atkAnimSpeed);
        }

        // [HitBox 소환]
        if (telegraphPrefab != null)
        {
            // 마우스 방향 또는 이동 방향을 공격 방향으로 설정
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            mousePos.z = 0;
            Vector2 dir = (mousePos - transform.position).normalized;

            // 플레이어가 바라보는 방향 동기화
            if (dir.x > 0) transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
            else if (dir.x < 0) transform.localScale = new Vector3(1, transform.localScale.y, transform.localScale.z);

            Vector3 spawnPos = transform.position;
            GameObject go = Instantiate(telegraphPrefab, spawnPos, Quaternion.identity);

            // 범용 BaseHitBox 사용
            _activeHitbox = go.GetComponent<BaseHitBox>();

            if (_activeHitbox != null)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                go.transform.rotation = Quaternion.Euler(0, 0, angle);

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
                    knockbackForce: 2f,
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

    public void CancelAttack()
    {
        if (_activeHitbox != null)
        {
            Destroy(_activeHitbox.gameObject);
            _activeHitbox = null;
        }
        _comboStep = 0;
        _isHoldingAttack = false;
        _player.SetAttackAnimSpeed(1f);
        
        _player.SetAttackAnimSpeed(1f);
        _player.PlayAllAnim("Idle");
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack); // 이동 속도 복구
    }
}
