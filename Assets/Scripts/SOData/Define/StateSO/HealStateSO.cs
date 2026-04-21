using UnityEngine;

[CreateAssetMenu(fileName = "HealState", menuName = "Necromancer/FSM/State/Heal")]
public class HealStateSO : FSMStateSO
{
    [Header("힐링 설정")]
    [SerializeField] private float healInterval = 1.0f; // 힐 주기

    public override void Enter(EntityFSM fsm)
    {
        fsm.atkTimer = 0; // 타이머 초기화
    }

    public override void Execute(EntityFSM fsm)
    {
        // 1. BaseEntity 정보 가져오기 (팀 레이어 확인용)
        var entity = fsm.GetComponent<BaseEntity>();
        if (entity == null) return;

        // 2. 쿨타임 체크
        fsm.atkTimer += Time.deltaTime;
        if (fsm.atkTimer < healInterval) return;

        // 3. 주변 내 팀원들 탐색 (myTeamLayer 사용!)
        Collider2D[] teammates = Physics2D.OverlapCircleAll(fsm.transform.position, entity.detectRange, entity.myTeamLayer);
        
        CharacterStat lowestHPStat = null;
        float minHPPercent = 1.1f; // 110% (기준점)

        foreach (var col in teammates)
        {
            if (col.TryGetComponent<CharacterStat>(out var stat))
            {
                if (stat.IsDead) continue;

                float hpPercent = stat.CURHP / stat.MAXHP;
                if (hpPercent < 1.0f && hpPercent < minHPPercent)
                {
                    minHPPercent = hpPercent;
                    lowestHPStat = stat;
                }
            }
        }

        // 4. 힐 실행
        if (lowestHPStat != null)
        {
            lowestHPStat.Heal(fsm.stats.ATK); // 공격력 수치만큼 힐
            fsm.atkTimer = 0;
            Debug.Log($"<color=green>[HealState]</color> {fsm.name}이(가) {lowestHPStat.name}을(를) 치유함!");
        }
        else
        {
            // 주변에 힐할 팀원이 없으면 잠시 대기
            fsm.atkTimer = healInterval * 0.5f; 
        }
    }

    public override void Exit(EntityFSM fsm) { }
}
