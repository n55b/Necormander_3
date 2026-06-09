using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class LeapStrikeSkill : IActiveSkill
{
    public string SkillName => "도약 타격";
    public float Cooldown => 8f;
    
    public bool IsActive { get; private set; }
    public bool IsOnCooldown => Time.time < _lastUsedTime + Cooldown;

    [Header("스킬 설정")]
    [SerializeField] private float jumpDuration = 0.5f;
    [SerializeField] private float jumpHeight = 3f;
    [SerializeField] private float baseDamage = 30f;
    [SerializeField] private float baseStunTime = 1f;
    [SerializeField] private float hitRadius = 2.5f;
    
    // 시너지 스케일링 설정
    [SerializeField] private float damagePerMinion = 15f;
    [SerializeField] private float stunTimePerMinion = 0.2f;

    private PlayerController _player;
    private float _lastUsedTime = -9999f;
    
    private List<AllyController> _synergyMinions = new List<AllyController>();

    public void Initialize(PlayerController player)
    {
        _player = player;
    }

    public void OnInputStart()
    {
        if (IsOnCooldown || IsActive) return;

        IsActive = true;
        _player.SetInputBlocked(true);

        Vector2 targetPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        // 시너지 미니언 탐색 (창병)
        var allyManager = _player.GetComponent<AllyManager>();
        if (allyManager != null)
        {
            _synergyMinions = allyManager.GetAliveAllies(CommandData.SkeletonSpearman);
            foreach (var minion in _synergyMinions)
            {
                minion.EnterSkillState();
                // if (minion.Animator != null) minion.Animator.Play("Attack"); // 도약 모션 임시
            }
        }

        // if (_player.Animator != null) _player.Animator.Play("Dash"); // 임시 도약 모션
        
        Debug.Log($"<color=cyan>[Skill]</color> 도약 타격 발동! 창병 수: {_synergyMinions.Count}");
        _player.StartCoroutine(JumpRoutine(targetPos));
    }

    public void OnInputHold() { }
    public void OnInputRelease() { }

    private IEnumerator JumpRoutine(Vector2 targetPos)
    {
        float elapsed = 0f;
        
        Vector2 pStartPos = _player.transform.position;

        // 미니언 시작 위치 및 도착 위치 계산 (원형 산개)
        List<Vector2> mStartPos = new List<Vector2>();
        List<Vector2> mEndPos = new List<Vector2>();
        
        float radius = 1.5f; // 산개 반경
        float angleStep = _synergyMinions.Count > 0 ? 360f / _synergyMinions.Count : 0f;

        for (int i = 0; i < _synergyMinions.Count; i++)
        {
            mStartPos.Add(_synergyMinions[i].transform.position);
            
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            mEndPos.Add(targetPos + offset);
        }

        while (elapsed < jumpDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / jumpDuration;

            // 포물선 Y 높이
            float height = 4f * jumpHeight * t * (1f - t);

            // 플레이어 포물선 이동
            Vector2 pCur = Vector2.Lerp(pStartPos, targetPos, t);
            _player.transform.position = new Vector3(pCur.x, pCur.y + height, 0f);

            // 미니언 포물선 이동
            for (int i = 0; i < _synergyMinions.Count; i++)
            {
                var m = _synergyMinions[i];
                if (m != null && !m.Stats.Health.IsDead)
                {
                    Vector2 mCur = Vector2.Lerp(mStartPos[i], mEndPos[i], t);
                    m.transform.position = new Vector3(mCur.x, mCur.y + height, 0f);
                }
            }

            yield return null;
        }

        // 착지 시점
        _player.transform.position = targetPos;
        for (int i = 0; i < _synergyMinions.Count; i++)
        {
            var m = _synergyMinions[i];
            if (m != null && !m.Stats.Health.IsDead)
            {
                m.transform.position = mEndPos[i];
                m.ExitSkillState(); // AI 정상화
            }
        }

        // 착지 데미지 계산 및 부여
        ApplyLandingImpact();
        
        _synergyMinions.Clear();
        OnDeactivate();
    }

    private void ApplyLandingImpact()
    {
        // 1. 카메라 흔들림 등 연출
        if (GameManager.Instance != null && GameManager.Instance.cameraManager != null)
        {
            GameManager.Instance.cameraManager.HitShakeCamera();
        }

        // 2. 최종 데미지 및 스턴 시간 산정
        float finalDamage = baseDamage + (_synergyMinions.Count * damagePerMinion);
        float finalStun = baseStunTime + (_synergyMinions.Count * stunTimePerMinion);

        HashSet<int> hitEnemies = new HashSet<int>();

        // 플레이어 착지 반경 판정
        CheckAndDamage(hitEnemies, _player.transform.position, finalDamage, finalStun);

        // 창병 착지 반경 판정
        foreach (var m in _synergyMinions)
        {
            if (m != null && !m.Stats.Health.IsDead)
            {
                CheckAndDamage(hitEnemies, m.transform.position, finalDamage, finalStun);
            }
        }
    }

    private void CheckAndDamage(HashSet<int> hitEnemies, Vector2 checkPos, float damage, float stunTime)
    {
        Collider2D[] cols = Physics2D.OverlapCircleAll(checkPos, hitRadius, LayerMask.GetMask("Enemy"));
        foreach (var col in cols)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health == null) health = col.GetComponentInParent<CharacterHealth>();

            if (health != null && !health.IsDead && !hitEnemies.Contains(health.gameObject.GetInstanceID()))
            {
                hitEnemies.Add(health.gameObject.GetInstanceID());
                
                DamageInfo info = new DamageInfo(damage, DamageType.Physical, _player.gameObject, false, 1f, false, "Leap Strike!");
                
                // 스턴 적용 (CharacterHealth에서 DebuffBoolType.Stunned 활용)
                var stat = health.GetComponent<CharacterStat>();
                if (stat != null && stat.Status != null)
                {
                    stat.Status.SetDebuffBool(DebuffBoolType.Stunned, stunTime);
                }

                health.GetDamage(info);
            }
        }
    }

    public void OnActivate() { }
    public void OnDeactivate()
    {
        IsActive = false;
        _lastUsedTime = Time.time;
        if (_player != null) _player.SetInputBlocked(false);
    }

    public void UpdateSkill() { }
    public bool HandleLeftClick() => false;
}
