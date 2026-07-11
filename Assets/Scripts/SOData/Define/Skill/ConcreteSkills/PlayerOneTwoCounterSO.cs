using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 원 투 카운터 (카운터)
///
/// 제자리에서 가벼운 잽을 2번 지른다(각 기본 공격력의 70%, 판정 0.5초).
/// 시전 중(0.5초) 적에게 피격당하면 <b>그 피해만 무효화</b>하고 즉시 적의 품으로 파고들어
/// 카운터 훅(어퍼컷)을 꽂아 기본 공격력의 300% 피해 + 강타(취약 소모)를 부여한다.
/// 피격이 없으면 카운터(어퍼컷)는 발동하지 않는다.
///
/// 카운터 판정: 무적을 쓰지 않고 <see cref="DamageEventBus.OnBeforeDamageCalculated"/>(ref DamageInfo)에서
/// 첫 적타의 <c>info.amount</c> 를 0 으로 만들어 해당 피해만 무효화한다.
/// (플레이어는 BaseEntity 가 아니므로 넉백/경직도 함께 무효화됨)
/// </summary>
[CreateAssetMenu(fileName = "PlayerOneTwoCounter", menuName = "Necromancer/Skills/Player/Physical/OneTwoCounter")]
public class PlayerOneTwoCounterSO : PlayerSkillSO
{
    [Header("원 투 (잽 2회)")]
    [Tooltip("Front Skill Hitbox Prefab 재사용.")]
    public BaseHitBox hitBoxPrefab;
    [Tooltip("카운터 판정 창(초) = 잽 시퀀스 길이.")]
    public float counterWindow = 0.5f;
    [Tooltip("1타~2타 간격(초).")]
    public float jabDelay = 0.12f;
    [Tooltip("각 잽 데미지 배율. 70% = 0.7")]
    public float jabMultiplier = 0.7f;
    public float jabDistance = 2.5f;
    public float jabWidth = 1.5f;

    [Header("카운터 훅 (피격 시에만 발동)")]
    [Tooltip("적 품으로 파고드는 시간(초).")]
    public float counterDashDuration = 0.1f;
    [Tooltip("파고든 뒤 적과의 최종 간격(칸).")]
    public float counterCloseOffset = 0.8f;
    public float uppercutDistance = 3.0f;
    public float uppercutWidth = 2.5f;
    [Tooltip("기본 공격력 대비 배율. 300% = 3.0")]
    public float uppercutMultiplier = 3.0f;
    [Tooltip("카운터 명중 시 히트스톱(초). 0 이면 없음.")]
    public float uppercutHitStop = 0.08f;

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 aimDir = ((Vector2)mousePos - (Vector2)player.transform.position).normalized;
        if (aimDir == Vector2.zero) aimDir = Vector2.right;

        var runtime = player.gameObject.GetComponent<OneTwoCounterRuntime>();
        if (runtime == null) runtime = player.gameObject.AddComponent<OneTwoCounterRuntime>();
        runtime.Begin(this, player, aimDir);
    }
}

/// <summary>
/// 원 투 카운터의 1회 시전 상태 + 정적 이벤트 버스 구독 수명을 관리하는 런타임 헬퍼.
/// 정적 버스(<see cref="DamageEventBus.OnBeforeDamageCalculated"/>) 구독은 반드시 해제되어야 하므로
/// 시전 종료/OnDisable 양쪽에서 해제한다.
/// </summary>
public class OneTwoCounterRuntime : MonoBehaviour
{
    private PlayerOneTwoCounterSO _so;
    private PlayerController _player;
    private CharacterHealth _playerHealth;
    private Vector2 _aimDir;

    private bool _running;
    private bool _subscribed;

    private bool _countered;
    private GameObject _counterAttacker;

    public void Begin(PlayerOneTwoCounterSO so, PlayerController player, Vector2 aimDir)
    {
        if (_running) return;
        _so = so;
        _player = player;
        _aimDir = aimDir;
        _playerHealth = (player.Stat != null) ? player.Stat.Health : null;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        _running = true;
        _countered = false;
        _counterAttacker = null;

        _player.SetInputBlocked(true);

        // 카운터 판정 창 시작: 정적 버스 구독 (무적 없이, 첫 적타의 피해만 0 처리)
        DamageEventBus.OnBeforeDamageCalculated += HandleBeforeDamage;
        _subscribed = true;

        // === 원 투: 잽 2회, 판정 창 동안 피격 감지 ===
        const int totalJabs = 2;
        float elapsed = 0f;
        int jabsThrown = 0;
        float nextJabAt = 0f;

        while (elapsed < _so.counterWindow)
        {
            if (_countered) break;

            if (jabsThrown < totalJabs && elapsed >= nextJabAt)
            {
                SpawnJab(jabsThrown + 1);
                jabsThrown++;
                nextJabAt = elapsed + _so.jabDelay;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 판정 창 종료 → 구독 해제 (카운터 발동 여부와 무관하게 반드시)
        Unsubscribe();

        // === 카운터 성공 시에만 어퍼컷 ===
        if (_countered)
            yield return DoCounterHook();

        _player.SetInputBlocked(false);
        _running = false;
        Destroy(this);
    }

    private void HandleBeforeDamage(CharacterHealth target, ref DamageInfo info)
    {
        if (_countered) return;
        if (_playerHealth == null || target != _playerHealth) return;
        if (info.amount <= 0f) return;
        if (!SkillCombatUtil.IsEnemyAttacker(info.attacker)) return;

        // 그 피해만 무효화 + 카운터 예약
        info.amount = 0f;
        _countered = true;
        _counterAttacker = info.attacker;
    }

    private void SpawnJab(int index)
    {
        if (_so.hitBoxPrefab == null) return;

        // 제자리 잽: 매 타 마우스 방향 재조준
        Vector2 pos = _player.transform.position;
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = ((Vector2)mousePos - pos).normalized;
        if (dir == Vector2.zero) dir = _aimDir;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        BaseHitBox box = Instantiate(_so.hitBoxPrefab, pos, Quaternion.Euler(0, 0, angle));
        box.transform.localScale = new Vector3(_so.jabDistance, _so.jabWidth, 1f);

        float dmg = _player.Stat.ATK * _so.jabMultiplier;
        DamageInfo info = new DamageInfo(dmg, DamageType.Physical, _player.gameObject, false, 1f, false, $"Jab {index}!");
        box.Init(info, Layers.EnemyMask, 0.15f, 0f, true, null);
    }

    private IEnumerator DoCounterHook()
    {
        _player.PlayAllAnim("Attack", "Idle");

        // 적 품으로 파고들기
        Vector2 start = _player.transform.position;
        Vector2 dir = _aimDir;
        if (_counterAttacker != null)
        {
            Vector2 toEnemy = (Vector2)_counterAttacker.transform.position - start;
            if (toEnemy.sqrMagnitude > 0.0001f) dir = toEnemy.normalized;
            Vector2 target = (Vector2)_counterAttacker.transform.position - dir * _so.counterCloseOffset;
            // 대시와 동일 판정(벽+낭떠러지)으로 파고들기 목적지 제동.
            target = SkillCombatUtil.GetSafeDestination(start, dir, Vector2.Distance(start, target));
            yield return SkillCombatUtil.MoveOverTime(_player.transform, start, target, _so.counterDashDuration);
        }

        // 어퍼컷 히트박스
        Vector2 pos = _player.transform.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (_so.hitBoxPrefab != null)
        {
            BaseHitBox box = Instantiate(_so.hitBoxPrefab, pos, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(_so.uppercutDistance, _so.uppercutWidth, 1f);

            float dmg = _player.Stat.ATK * _so.uppercutMultiplier;
            DamageInfo info = new DamageInfo(dmg, DamageType.Physical, _player.gameObject, false, 1f, false, "Counter Hook!");
            GameObject attacker = _player.gameObject;
            box.Init(info, Layers.EnemyMask, 0.2f, 0f, true, (health) =>
            {
                var stat = health.GetComponent<CharacterStat>() ?? health.GetComponentInParent<CharacterStat>();
                if (stat != null && stat.Status != null)
                    stat.Status.ConsumeVulnerability(SkillKeyword.Smash, attacker, true); // 소모(강타)
            });
        }

        // 카운터 타격감
        if (_so.uppercutHitStop > 0f && HitStopManager.Instance != null)
            HitStopManager.Instance.DoHitStop(_so.uppercutHitStop);
        if (CameraManager.Instance != null)
            CameraManager.Instance.HitShakeCamera(_so.shakeForce);
    }

    private void Unsubscribe()
    {
        if (_subscribed)
            DamageEventBus.OnBeforeDamageCalculated -= HandleBeforeDamage;
        _subscribed = false;
    }

    private void OnDisable()
    {
        // 안전장치: 정적 버스 구독은 반드시 해제 (누수/파괴된 플레이어 참조 방지)
        Unsubscribe();
        if (_player != null) _player.SetInputBlocked(false);
    }
}
