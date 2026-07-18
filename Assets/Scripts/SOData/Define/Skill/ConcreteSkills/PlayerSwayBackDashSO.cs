using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스웨이 백 & 대시 스트레이트 (카운터)
///
/// 플레이어가 상체를 뒤로 젖히며 뒤로 물러나 <b>0.5초 무적</b>으로 적의 공격을 회피한다.
/// 회피에 <b>성공했을 때에만</b> 곧바로 앞으로 돌진하며 정권을 질러
/// 기본 공격력의 130% 피해 + 기절(취약 소모)을 부여한다.
///
/// 카운터 판정: 무적(<see cref="CharacterHealth.Invincible"/>)으로 피해를 무효화하되,
/// <see cref="CharacterHealth.OnDamageReceived"/> 는 무적 게이트보다 먼저 발생하므로
/// "회피 성공" 여부를 감지할 수 있다. (CharacterHealth.GetDamage 48행 vs 50행)
/// </summary>
[CreateAssetMenu(fileName = "PlayerSwayBackDash", menuName = "Necromancer/Skills/Player/Physical/SwayBackDash")]
public class PlayerSwayBackDashSO : PlayerSkillSO
{
    [Header("스웨이 백 (회피)")]
    [Tooltip("뒤로 젖히며 무적으로 물러나는 시간(초). 이 시간 동안 적에게 맞으면 회피 성공.")]
    public float swayDuration = 0.5f;
    [Tooltip("뒤로 물러나는 거리(칸).")]
    public float swayBackDistance = 1.0f;

    [Header("대시 스트레이트 (회피 성공 시 돌진 정권)")]
    [Tooltip("Front Skill Hitbox Prefab 재사용. StunSmash / RisingUppercut 과 동일한 방향형 박스.")]
    public BaseHitBox hitBoxPrefab;
    [Tooltip("앞으로 돌진하는 거리(칸).")]
    public float dashDistance = 2.5f;
    public float dashDuration = 0.15f;
    [Tooltip("정권 히트박스가 돌진 거리 너머로 더 뻗는 길이(칸). 총 히트박스 길이 = dashDistance + punchReach 라서 돌진 복도 전체를 덮는다.")]
    public float punchReach = 0.7f;
    public float hitWidth = 2.0f;
    [Tooltip("기본 공격력 대비 배율. 130% = 1.3")]
    public float damageMultiplier = 1.3f;

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);

        // 조준 방향(마우스). 젖힘 = 반대 방향, 돌진/정권 = 이 방향.
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 aimDir = ((Vector2)mousePos - (Vector2)player.transform.position).normalized;
        if (aimDir == Vector2.zero) aimDir = Vector2.right;

        // 공유 SO 가 상태를 갖지 않도록 1회 시전 상태는 런타임 헬퍼가 관리한다.
        var runtime = player.gameObject.GetComponent<SwayBackDashRuntime>();
        if (runtime == null) runtime = player.gameObject.AddComponent<SwayBackDashRuntime>();
        runtime.Begin(this, player, aimDir);
    }
}

/// <summary>
/// 스웨이 백 & 대시 스트레이트의 1회 시전 상태를 관리하는 런타임 헬퍼.
/// 시전 중에는 <see cref="PlayerController.SetInputBlocked"/> 로 입력을 완전히 차단하므로
/// 다른 행동으로 중단되지 않으며, 어떤 경로로 종료되든 OnDisable 에서 상태를 원복한다.
/// </summary>
public class SwayBackDashRuntime : MonoBehaviour
{
    private PlayerSwayBackDashSO _so;
    private PlayerController _player;
    private CharacterHealth _health;
    private Vector2 _aimDir;

    private bool _running;
    private bool _dodged;
    private bool _subscribed;
    private bool _invincibleByUs;

    public void Begin(PlayerSwayBackDashSO so, PlayerController player, Vector2 aimDir)
    {
        if (_running) return; // 재진입 방지(입력은 이미 차단되지만 안전장치)
        _so = so;
        _player = player;
        _aimDir = aimDir;
        _health = (player.Stat != null) ? player.Stat.Health : null;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        _running = true;
        _dodged = false;

        _player.SetInputBlocked(true);

        // === Phase A: 스웨이 백 (0.5초 무적 + 뒤로 이동, 회피 감지) ===
        if (_health != null)
        {
            _health.Invincible = true;
            _invincibleByUs = true;
            _health.OnDamageReceived += HandleDamageReceived;
            _subscribed = true;
        }
        _player.PlayAllAnim("Dash", "Idle"); // 젖히는 모션(Dash 클립 재사용, 없으면 Idle)

        Vector2 startPos = _player.transform.position;
        // 대시와 동일 판정(벽+낭떠러지): 뒤로 젖히는 이동도 못 밟는 곳으로 밀려나지 않게 제동.
        Vector2 backTarget = SkillCombatUtil.GetSafeDestination(startPos, -_aimDir, _so.swayBackDistance);
        yield return SkillCombatUtil.MoveOverTime(_player.transform, startPos, backTarget, _so.swayDuration);

        // 무적 종료 + 감지 해제
        if (_health != null) _health.Invincible = false;
        _invincibleByUs = false;
        Unsubscribe();

        // === Phase B: 회피 성공 시에만 돌진 정권 ===
        if (_dodged)
        {
            Vector2 dashStart = _player.transform.position;
            Vector2 dashTarget = SkillCombatUtil.GetSafeDestination(dashStart, _aimDir, _so.dashDistance);
            _player.PlayAllAnim("Attack", "Idle");
            SpawnStraightPunch(); // 히트박스는 즉시 전방 생성, 돌진과 함께 판정
            yield return SkillCombatUtil.MoveOverTime(_player.transform, dashStart, dashTarget, _so.dashDuration);
        }

        _player.SetInputBlocked(false);
        _running = false;
        Destroy(this);
    }

    private void SpawnStraightPunch()
    {
        if (_so.hitBoxPrefab == null) return;

        Vector2 pos = _player.transform.position;
        float angle = Mathf.Atan2(_aimDir.y, _aimDir.x) * Mathf.Rad2Deg;

        BaseHitBox box = Instantiate(_so.hitBoxPrefab, pos, Quaternion.Euler(0, 0, angle));
        // Front 프리팹은 피벗이 뒤쪽(원점)이라 +X(조준 방향)로 뻗는다. 돌진 시작 지점에서 스폰하고
        // 길이 = dashDistance + punchReach 로 잡으면, 히트박스가 돌진 복도 전체 + 착지 지점 약간 너머까지 덮는다.
        float length = _so.dashDistance + _so.punchReach;
        box.transform.localScale = new Vector3(length, _so.hitWidth, 1f);

        float dmg = _so.GetBaseDamage(_player.Stat) * _so.damageMultiplier;
        DamageInfo info = new DamageInfo(dmg, _so.ResolveDamageType(), _player.gameObject, false, 1f, false, "Dash Straight!");
        box.Init(info, Layers.EnemyMask, 0.2f, 0f, true);
    }

    // 무적 상태라 실제 피해는 무효화되지만, OnDamageReceived 는 무적 게이트보다 먼저 발생한다.
    private void HandleDamageReceived(DamageInfo info)
    {
        if (_dodged) return;
        if (info.amount <= 0f) return;
        if (!SkillCombatUtil.IsEnemyAttacker(info.attacker)) return;
        _dodged = true;
    }

    private void Unsubscribe()
    {
        if (_subscribed && _health != null)
            _health.OnDamageReceived -= HandleDamageReceived;
        _subscribed = false;
    }

    private void OnDisable()
    {
        // 안전장치: 씬 전환/파괴 등 어떤 경로로 중단되어도 상태 원복.
        Unsubscribe();
        if (_invincibleByUs && _health != null) _health.Invincible = false; // 우리가 켠 무적만 되돌림
        _invincibleByUs = false;
        if (_player != null) _player.SetInputBlocked(false);
    }
}
