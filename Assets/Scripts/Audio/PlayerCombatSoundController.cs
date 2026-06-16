using UnityEngine;

/// <summary>
/// 플레이어 전투 사운드 컨트롤러.
/// MeleeCombatController.OnAttackExecuted 이벤트로 콤보 사운드를 재생합니다.
/// 콤보 카운트는 MeleeCombatController가 관리하므로 여기선 중복 추적하지 않습니다.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerCombatSoundController : MonoBehaviour
{
    [Header("사운드 데이터")]
    [SerializeField] private PlayerAttackSoundData soundData;

    // ─── 캐시 ────────────────────────────────────────────────────────
    private MeleeCombatController _melee;
    private MeleeDodgeController  _dodge;

    private CharacterHealth       _health;
    private bool                  _initialized;
    private int                   _playerLayer = -1;

    // ─────────────────────────────────────────────────────────────────
private void Awake()
    {
        _melee       = GetComponent<MeleeCombatController>();
        _dodge       = GetComponent<MeleeDodgeController>();
        _health      = GetComponentInChildren<CharacterHealth>();
        _playerLayer = LayerMask.NameToLayer("Player");
    }

    private void Start() => Initialize();

public void Initialize()
    {
        if (_initialized) return;

        if (_melee  != null) _melee.OnAttackExecuted += OnMeleeAttack;
        if (_dodge  != null) _dodge.OnDodgeStarted  += OnDodge;
        if (_health != null) _health.OnDamageTaken  += OnPlayerHurt;
        DamageEventBus.OnDamageReceived += OnDamageReceived;

        _initialized = true;
        Debug.Log("<color=cyan>[PlayerCombatSound]</color> Initialized.");
    }

private void OnDestroy()
    {
        if (_melee  != null) _melee.OnAttackExecuted -= OnMeleeAttack;
        if (_dodge  != null) _dodge.OnDodgeStarted  -= OnDodge;
        if (_health != null) _health.OnDamageTaken  -= OnPlayerHurt;
        DamageEventBus.OnDamageReceived -= OnDamageReceived;
    }

    // ─── ① 근접 공격 → 콤보 사운드 ──────────────────────────────────
    // comboStep: 0=1타(경타), 1=2타(경타), 2=3타(중타)
    // ─── ① 구르기 → 구르기 사운드 ────────────────────────────────────────────
    private void OnDodge()
    {
        if (soundData == null || SoundManager.Instance == null) return;
        SoundManager.Instance.PlaySFX(soundData.GetDodgeClip(), soundData.dodgeVolume);
    }

    
private void OnMeleeAttack(int comboStep)
    {
        if (soundData == null || SoundManager.Instance == null) return;

        SoundManager.Instance.PlaySFX(
            soundData.GetAttackClip(comboStep),
            soundData.attackVolume,
            soundData.GetPitch(comboStep)
        );
    }

    // ─── ② 적 피격 → 히트 사운드 ────────────────────────────────────
    private void OnDamageReceived(CharacterHealth target, DamageInfo info)
    {
        if (soundData == null || SoundManager.Instance == null) return;
        if (!IsPlayerOrOwned(info.attacker)) return;

        var stat = target.GetComponent<CharacterStat>();
        if (stat != null && !stat.IsEnemy) return;

        SoundManager.Instance.PlaySFX(soundData.GetHitClip(), soundData.hitVolume);
    }

    // ─── ③ 플레이어 피격 → 피격 사운드 ──────────────────────────────
    private void OnPlayerHurt(float damage)
    {
        if (damage <= 0f || soundData == null || SoundManager.Instance == null) return;
        SoundManager.Instance.PlaySFX(soundData.GetPlayerHurtClip(), soundData.playerHurtVolume);
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────
    private bool IsPlayerOrOwned(GameObject attacker)
    {
        if (attacker == null)       return false;
        if (attacker == gameObject) return true;
        if (attacker.transform.IsChildOf(transform)) return true;
        return attacker.layer == _playerLayer && gameObject.layer == _playerLayer;
    }

    // ─── 외부 API ────────────────────────────────────────────────────
    public void SetSoundData(PlayerAttackSoundData data) => soundData = data;
}
