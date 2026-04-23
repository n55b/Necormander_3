using UnityEngine;

/// <summary>
/// 주변 아군을 치유하는 행동을 정의합니다.
/// 1. detectRange로 치유 대상을 탐색합니다.
/// 2. attackRange 안에 대상이 들어오면 이동을 멈추고 치유를 시작합니다.
/// 3. 치유가 완료될 때까지 제자리에 고정됩니다.
/// </summary>
[CreateAssetMenu(fileName = "HealState", menuName = "Necromancer/Attack States/Heal")]
public class HealStateSO : AttackStateSO
{
    private CharacterStat _currentHealTarget;

    public override void Execute(EntityFSM fsm)
    {
        // 1. BaseEntity 정보 가져오기 (범위 확인용)
        var entity = fsm.GetComponent<BaseEntity>();
        if (entity == null) return;

        // 2. 치유 대상 탐색 및 결정 (매 프레임 최적의 대상 확인)
        FindBestHealTarget(fsm, entity);

        // 3. 대상과의 거리에 따른 이동 및 공격(힐) 제어
        if (_currentHealTarget != null)
        {
            fsm.target = _currentHealTarget.transform;
            float dist = Vector2.Distance(fsm.transform.position, _currentHealTarget.transform.position);

            // [핵심] 사거리 안에 들어왔을 때만 타이머 진행 및 이동 정지
            if (dist <= fsm.stats.ATKRANGE)
            {
                // 이동 정지 (힐 집중)
                if (fsm.agent != null) fsm.agent.isStopped = true;

                // 부모의 타이머 로직 실행
                fsm.atkTimer += Time.deltaTime;
                if (fsm.atkTimer >= fsm.stats.ATKSPD)
                {
                    PerformAction(fsm);
                    fsm.atkTimer = 0.0f;
                    
                    // 힐을 한 번 넣었으므로 다음 프레임부터 다시 이동 가능 여부 판단
                    if (fsm.agent != null) fsm.agent.isStopped = false;
                }
            }
            else
            {
                // 사거리 밖이면 타이머 초기화 및 이동 재개 (대상을 쫓아감)
                fsm.atkTimer = 0.0f;
                if (fsm.agent != null) fsm.agent.isStopped = false;
            }
        }
        else
        {
            // 치유할 대상이 전혀 없으면 이동 재개 및 타이머 리셋
            fsm.atkTimer = 0.0f;
            if (fsm.agent != null) fsm.agent.isStopped = false;
        }
    }

    private void FindBestHealTarget(EntityFSM fsm, BaseEntity entity)
    {
        // detectRange 내의 팀원들을 찾습니다.
        Collider2D[] teammates = Physics2D.OverlapCircleAll(fsm.transform.position, entity.detectRange, entity.myTeamLayer);
        
        CharacterStat lowestHPStat = null;
        float minHPPercent = 1.1f;

        foreach (var col in teammates)
        {
            if (col.TryGetComponent<CharacterStat>(out var stat))
            {
                if (stat.IsDead) continue;

                float hpPercent = stat.CURHP / stat.MAXHP;
                // 체력이 100% 미만인 아군 중 가장 체력 비율이 낮은 대상 선택
                if (hpPercent < 1.0f && hpPercent < minHPPercent)
                {
                    minHPPercent = hpPercent;
                    lowestHPStat = stat;
                }
            }
        }

        _currentHealTarget = lowestHPStat;
    }

    protected override void PerformAction(EntityFSM fsm)
    {
        if (_currentHealTarget != null)
        {
            _currentHealTarget.Heal(fsm.stats.ATK);
            Debug.Log($"<color=green>[HealState]</color> {fsm.name} -> {_currentHealTarget.name} 치유 완료 (양: {fsm.stats.ATK})");
        }
    }

    public override void Exit(EntityFSM fsm)
    {
        base.Exit(fsm);
        // 상태를 나갈 때 혹시 멈춰있다면 다시 풀어줌
        if (fsm.agent != null) fsm.agent.isStopped = false;
    }
}
