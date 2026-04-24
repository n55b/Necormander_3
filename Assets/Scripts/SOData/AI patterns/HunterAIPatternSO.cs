using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 플레이어를 최우선으로 사냥하는 특수 AI 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "HunterAIPattern", menuName = "Necromancer/AI/HunterPattern")]
public class HunterAIPatternSO : BaseAIPatternSO
{
    protected override void UpdateTargeting(BaseEntity entity)
    {
        // 1. [최우선] 플레이어 도달 가능성 체크
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            float distToPlayer = Vector2.Distance(entity.transform.position, player.transform.position);
            
            if (distToPlayer <= entity.detectRange)
            {
                var agent = entity.GetComponent<NavMeshAgent>();
                if (agent != null && agent.isActiveAndEnabled)
                {
                    agent.CalculatePath(player.transform.position, testPath);
                    
                    if (testPath.status == NavMeshPathStatus.PathComplete)
                    {
                        target = player.transform;
                        return;
                    }
                    else if (testPath.status == NavMeshPathStatus.PathPartial)
                    {
                        Vector3 lastPoint = testPath.corners[testPath.corners.Length - 1];
                        if (Vector2.Distance(lastPoint, player.transform.position) <= entity.Stats.ATKRANGE)
                        {
                            target = player.transform;
                            return;
                        }
                    }
                }
            }
        }

        // 2. [차선] 플레이어 불가 시 주변 미니언
        base.UpdateTargeting(entity);
    }
}
