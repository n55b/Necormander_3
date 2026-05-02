using UnityEngine;

/// <summary>
/// 아군을 치유하는 사제 전용 AI 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "PriestAIPattern", menuName = "Necromancer/AI/PriestPattern")]
public class PriestAIPatternSO : BaseAIPatternSO
{
    protected override void UpdateTargeting(BaseEntity entity)
    {
        // 아군 체력 검사
        Collider2D[] teammates = Physics2D.OverlapCircleAll(entity.transform.position, entity.detectRange, entity.myTeamLayer);
        
        CharacterStat lowestHPStat = null;
        float minHPPercent = 1.1f;

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

        if (lowestHPStat != null) target = lowestHPStat.transform;
        else target = null;
    }

    protected override void OnAttack(BaseEntity entity)
    {
        StopNavAgent(entity);

        atkTimer += Time.deltaTime;
        if (atkTimer >= entity.Stats.ATKSPD)
        {
            if (target != null && target.TryGetComponent<CharacterStat>(out var stat))
            {
                stat.Health.Heal(entity.Stats.ATK);
                // Debug.Log($"<color=green>[Priest Pattern]</color> {entity.name} -> {target.name} 치유 완료");
            }
            atkTimer = 0f;
        }
    }

    protected override void UpdateStateTransitions(BaseEntity entity)
    {
        // 치유 대상이 없으면 플레이어 추적 모드로 전환
        if (target == null)
        {
            if (entity.team == Team.Ally && (entity as AllyController)?.player != null)
            {
                target = (entity as AllyController).player;
                currentState = AIState.Follow;
            }
            else currentState = AIState.Idle;
            return;
        }

        base.UpdateStateTransitions(entity);
    }
}
