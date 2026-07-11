using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DashChargeSkill : IActiveSkill
{
    public string SkillName => "방패 돌진";
    public float Cooldown => 5f;
    
    public bool IsActive { get; private set; }
    public bool IsOnCooldown => Time.time < _lastUsedTime + Cooldown;

    [Header("스킬 설정")]
    [SerializeField] private float maxChargeTime = 1.5f;
    [SerializeField] private float minDashDist = 2f;
    [SerializeField] private float maxDashDist = 8f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float baseDamage = 20f;
    [SerializeField] private float hitRadius = 1.5f;

    private PlayerController _player;
    private float _lastUsedTime = -9999f;
    private bool _isCharging;
    private float _currentChargeTime;
    private Vector2 _dashDirection;

    private List<AllyController> _synergyMinions = new List<AllyController>();

    public void Initialize(PlayerController player)
    {
        _player = player;
    }

    public void OnInputStart()
    {
        if (IsOnCooldown || _isCharging || IsActive) return;

        IsActive = true;
        _isCharging = true;
        _currentChargeTime = 0f;
        _player.SetInputBlocked(true);

        // 시너지 미니언 탐색 (방패병)
        var allyManager = _player.GetComponent<AllyManager>();
        if (allyManager != null)
        {
            _synergyMinions = allyManager.GetAliveAllies(CommandData.SkeletonShieldbearer);
            foreach (var minion in _synergyMinions)
            {
                minion.EnterSkillState();
                // if (minion.Animator != null) minion.Animator.Play("Idle"); // 임시 차징 애니메이션
            }
        }
        
        // if (_player.Animator != null) _player.Animator.Play("Idle"); // 플레이어도 대기 모션
        Debug.Log("<color=cyan>[Skill]</color> 방패 돌진 차징 시작!");
    }

    public void OnInputHold()
    {
        if (!_isCharging) return;

        _currentChargeTime += Time.deltaTime;
        
        // 차징 중 방향 계속 갱신
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        _dashDirection = (mousePos - (Vector2)_player.transform.position).normalized;

        if (_currentChargeTime >= maxChargeTime)
        {
            // 풀차지 자동 발사
            ExecuteDash();
        }
    }

    public void OnInputRelease()
    {
        if (!_isCharging) return;
        ExecuteDash();
    }

    private void ExecuteDash()
    {
        _isCharging = false;
        _lastUsedTime = Time.time;
        float chargeRatio = Mathf.Clamp01(_currentChargeTime / maxChargeTime);
        float actualDist = Mathf.Lerp(minDashDist, maxDashDist, chargeRatio);

        Debug.Log($"<color=cyan>[Skill]</color> 방패 돌진 발사! 거리: {actualDist}");
        _player.StartCoroutine(DashRoutine(actualDist, _dashDirection));
    }

    private IEnumerator DashRoutine(float dist, Vector2 dir)
    {
        if (_player != null) _player.SetDashLayer(true);
        float elapsed = 0f;
        
        Vector2 pStartPos = _player.transform.position;
        // 대시와 동일 판정: 방패 돌진이 벽/낭떠러지를 뚫고 나가지 않도록 목적지 제동.
        Vector2 pEndPos = SkillCombatUtil.GetSafeDestination(pStartPos, dir, dist);

        // 미니언 시작 위치 및 도착 위치 저장 (마우스/목표점 방향으로 수렴 돌진)
        List<Vector2> mStartPos = new List<Vector2>();
        List<Vector2> mEndPos = new List<Vector2>();
        foreach (var m in _synergyMinions)
        {
            mStartPos.Add(m.transform.position);
            // 미니언 위치에서 플레이어의 목표점(마우스 방향)을 향하는 벡터 계산 (벽/낭떠러지 제동 포함)
            Vector2 mDir = (pEndPos - (Vector2)m.transform.position).normalized;
            mEndPos.Add(SkillCombatUtil.GetSafeDestination(m.transform.position, mDir, dist));
        }

        HashSet<int> hitEnemies = new HashSet<int>();

        while (elapsed < dashDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / dashDuration;

            // 플레이어 이동
            _player.transform.position = Vector2.Lerp(pStartPos, pEndPos, t);

            // 미니언 동시 이동
            for (int i = 0; i < _synergyMinions.Count; i++)
            {
                var m = _synergyMinions[i];
                if (m != null && !m.Stats.Health.IsDead)
                {
                    m.transform.position = Vector2.Lerp(mStartPos[i], mEndPos[i], t);
                }
            }

            // 데미지 판정 (플레이어 경로)
            CheckHit(hitEnemies, _player.transform.position, _player.gameObject);

            // 미니언 경로 데미지 판정
            foreach (var m in _synergyMinions)
            {
                if (m != null && !m.Stats.Health.IsDead)
                {
                    CheckHit(hitEnemies, m.transform.position, m.gameObject);
                }
            }

            yield return null;
        }

        // 대쉬 종료
        _player.transform.position = pEndPos;
        for (int i = 0; i < _synergyMinions.Count; i++)
        {
            var m = _synergyMinions[i];
            if (m != null && !m.Stats.Health.IsDead)
            {
                m.transform.position = mEndPos[i];
                m.ExitSkillState();
            }
        }
        _synergyMinions.Clear();

        OnDeactivate();
    }

    private void CheckHit(HashSet<int> hitEnemies, Vector2 checkPos, GameObject attacker)
    {
        Collider2D[] cols = Physics2D.OverlapCircleAll(checkPos, hitRadius, Layers.EnemyMask);
        foreach (var col in cols)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health == null) health = col.GetComponentInParent<CharacterHealth>();

            if (health != null && !health.IsDead && !hitEnemies.Contains(health.gameObject.GetInstanceID()))
            {
                hitEnemies.Add(health.gameObject.GetInstanceID());
                
                DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, attacker, false, 1f, false, "Charge!", false, true, 2f);
                health.GetDamage(info);
            }
        }
    }

    public void OnActivate() { } // 외부에서 즉발로 쓸 때는 미사용
    public void OnDeactivate()
    {
        IsActive = false;
        _isCharging = false;
        if (_player != null)
        {
            _player.SetInputBlocked(false);
            _player.SetDashLayer(false);
        }
    }

    public void UpdateSkill() { }
    public bool HandleLeftClick() => false;
}
