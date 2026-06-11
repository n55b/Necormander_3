using UnityEngine;

/// <summary>
/// 아군을 치유하는 사제 전용 AI 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "PriestAIPattern", menuName = "Necromancer/AI/PriestPattern")]
public class PriestAIPatternSO : BaseAIPatternSO
{
    protected override void UpdateTargeting(BaseEntity entity)
    {
        // 아군 체력 검사 (힐 타겟 찾기)
        Collider2D[] teammates = Physics2D.OverlapCircleAll(entity.transform.position, entity.detectRange, entity.myTeamLayer);
        
        CharacterStat lowestHPStat = null;
        float minHPPercent = 0.95f; // 95% 미만인 아군만 치료 시도

        foreach (var col in teammates)
        {
            // [수정] 자식 오브젝트에 CharacterStat이 있을 수 있으므로 GetComponentInChildren 사용
            CharacterStat stat = col.GetComponentInParent<CharacterStat>();
            if (stat == null) stat = col.GetComponentInChildren<CharacterStat>();
            if (stat != null)
            {
                // 본인이거나 죽은 대상은 제외
                if (stat.IsDead || stat.gameObject == entity.gameObject || stat.transform.IsChildOf(entity.transform)) continue;

                float hpPercent = stat.CURHP / stat.MAXHP;
                if (hpPercent < minHPPercent)
                {
                    minHPPercent = hpPercent;
                    lowestHPStat = stat;
                }
            }
        }

        if (lowestHPStat != null)
        {
            entity.Target = lowestHPStat.transform;
        }
        else
        {
            // 힐 대상이 없으면 아군은 플레이어를, 적군은 대기
            if (entity.team == Team.Ally)
            {
                var ally = entity as AllyController;
                if (ally != null && ally.player != null) entity.Target = ally.player;
                else entity.Target = null;
            }
            else
            {
                entity.Target = null;
            }
        }
    }

    protected override void UpdateStateTransitions(BaseEntity entity)
    {
        AIState nextState = AIState.Idle;

        if (entity.Target != null)
        {
            float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
            
            // [수정] 타겟이 플레이어인데 체력이 꽉 차있다면 Follow, 아니면 Attack(힐)
            bool isPlayer = entity.Target.CompareTag("Player");
            bool needsHeal = false;
            
            CharacterStat targetStat = entity.Target.GetComponentInParent<CharacterStat>();
            if (targetStat == null) targetStat = entity.Target.GetComponentInChildren<CharacterStat>();
            if (targetStat != null && targetStat.CURHP < targetStat.MAXHP * 0.95f)
            {
                needsHeal = true;
            }

            if (isPlayer && !needsHeal)
            {
                if (dist > 2.0f) nextState = AIState.Follow;
                else nextState = AIState.Idle;
            }
            else
            {
                // 힐 대상이거나, 플레이어인데 치료가 필요한 경우
                if (dist <= entity.Stats.ATKRANGE) nextState = AIState.Attack;
                else nextState = AIState.Follow;
            }
        }

        entity.CurrentState = nextState;
    }

    protected override void OnAttack(BaseEntity entity)
    {
        StopNavAgent(entity);

        entity.AtkTimer += Time.deltaTime;
        if (entity.AtkTimer >= entity.Stats.ATKSPD)
        {
            // [수정] GetComponentInChildren 사용
            if (entity.Target != null)
            {
                CharacterStat stat = entity.Target.GetComponentInParent<CharacterStat>();
                if (stat == null) stat = entity.Target.GetComponentInChildren<CharacterStat>();
                if (stat != null)
                {
                    // 치유 대상의 체력이 이미 가득찼다면 타겟 초기화
                    if (stat.CURHP >= stat.MAXHP)
                    {
                        entity.Target = null;
                    }
                    else
                    {
                        stat.Health.Heal(entity.Stats.ATK);
                        // Debug.Log($"<color=green>[Priest AI]</color> {entity.name} healed {entity.Target.name}");
                    }
                }
            }
            entity.AtkTimer = 0f;
        }
    }
}
