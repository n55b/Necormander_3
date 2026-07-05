using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 일반적인 미니언의 표준 AI 패턴입니다.
/// 가까운 적을 추적하고 사거리 안에서 공격을 수행합니다.
/// </summary>
[CreateAssetMenu(fileName = "BaseAIPattern", menuName = "Necromancer/AI/BasePattern")]
public class BaseAIPatternSO : AIPatternSO
{
    [Header("패턴 범용 설정")]
    [Tooltip("공격 루틴 시작 시 BaseEntity의 telegraphPrefab(장판)을 자동 생성할지 여부 (궁수, 마법사 등은 false로 설정)")]
    public bool spawnTelegraph = true;

    protected override void UpdateTargeting(BaseEntity entity)
    {
        // 1. 적군(Enemy) 탐색
        Transform nearestEnemy = entity.TargetFinder.FindNearest(entity.detectRange);
        
        if (nearestEnemy != null)
        {
            entity.Target = nearestEnemy;
        }
        else
        {
            // 2. 적이 없을 때: 아군은 플레이어를 타겟으로 삼음
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
        // [상태 잠금] 공격 중일 때는 상태 전이를 무시하고 공격을 끝까지 완수합니다.
        if (entity.CurrentState == AIState.Attack && entity.IsAttacking) return;

        AIState nextState = AIState.Idle;

        if (entity.Target != null)
        {
            float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
            
            // [수정] 아군 미니언이 플레이어를 따라갈 때만 거리 유지, 적군이 플레이어를 잡았을 때는 공격 수행
            if (entity.team == Team.Ally && entity.Target.CompareTag("Player"))
            {
                if (dist > 2.0f) nextState = AIState.Follow;
                else nextState = AIState.Idle;
            }
            else
            {
                // 적인 경우(플레이어 포함) 사거리에 따라 결정
                // 공격 쿨타임이 찼고, 사거리 내에 있을 때만 공격 상태로 전환
                if (dist <= entity.Stats.ATKRANGE - 0.2f && entity.AtkTimer >= entity.Stats.ATKSPD) nextState = AIState.Attack;
                else nextState = AIState.Follow;
            }
        }

        if(nextState != entity.CurrentState)
        {
            entity.CurrentState = nextState;
        }
    }

    protected override void OnIdle(BaseEntity entity)
    {
        StopNavAgent(entity);
    }

    protected override void OnFollow(BaseEntity entity)
    {
        var agent = entity.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled && entity.Target != null)
        {
            agent.isStopped = false;
            agent.speed = entity.Stats.MOVESPEED;

            // 원거리 적(사거리 3.0f 이상, 단 돌진 몬스터 제외)이고 공격 쿨타임이 아직 차지 않은 경우
            bool isCharger = entity.Brain is ChargerAIPatternSO;
            if (!isCharger && entity.Stats.ATKRANGE >= 3.0f && entity.AtkTimer < entity.Stats.ATKSPD)
            {
                // 플레이어가 평타 사거리의 약 2배(거리 7f) 내에 접근했을 때만 도망침
                float fleeTriggerRange = 7.0f;
                if (ShouldFleeFromTarget(entity, fleeTriggerRange))
                {
                    // 매 프레임 목적지를 갱신하여 덜덜덜 흔들리는 것을 막기 위해, 이전 목적지에 근접했을 때만 신규 도망 방향을 연산합니다.
                    if (!agent.pathPending && agent.remainingDistance < 0.5f)
                    {
                        Vector3 fleeDest = CalculateFleeDestination(entity, 3.5f);
                        agent.SetDestination(fleeDest);
                    }
                }
                else
                {
                    // Flee 범위 밖에 있다면 도망치지 않고 정지하여 대기
                    StopNavAgent(entity);
                }
            }
            else
            {
                // 근접 몹이거나 공격 쿨타임이 찼을 때는 타겟에게 붙음
                agent.SetDestination(entity.Target.position);
            }
        }
    }

    protected override void OnAttack(BaseEntity entity)
    {
        StopNavAgent(entity);

        // 단, 이미 공격을 실행 중(애니메이션 재생 중)이라면 중복 실행하지 않습니다.
        if (entity.IsAttacking) return;

        if (entity.AtkTimer >= entity.Stats.ATKSPD)
        {
            entity.AtkTimer = 0f;
            entity.IsAttacking = true; // 코루틴 대기 전 상태 잠금을 위해 선제적 true 처리!
            entity.ActiveAttackCoroutine = entity.StartCoroutine(AttackRoutine(entity));
        }
    }

    protected virtual System.Collections.IEnumerator AttackRoutine(BaseEntity entity)
    {
        entity.IsAttacking = true;
        entity.HasFiredHitEvent = false;
        entity.HasFiredAttackEndEvent = false;

        bool hasAnimator = entity.Animator != null && entity.Animator.runtimeAnimatorController != null;
        float windupTime = 1.0f; // 기본 선딜레이 fallback (0.6f -> 1.0f 증가)

        if (hasAnimator) 
        {
            // 애니메이션 클립 길이를 동적으로 찾아서, 공격 속도보다 길다면 1.0배속보다 빠르게 틉니다.
            float animLength = 1f;
            var clips = entity.Animator.runtimeAnimatorController.animationClips;
            foreach (var clip in clips)
            {
                if (clip.name.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    animLength = clip.length;
                    break;
                }
            }

            // 여유를 위해 ATKSPD의 90% 시간 안에 애니메이션이 끝나도록 배속을 조절합니다.
            float targetDuration = entity.Stats.ATKSPD * 0.9f;
            if (animLength > targetDuration && targetDuration > 0)
            {
                entity.Animator.speed = animLength / targetDuration;
            }
            else
            {
                entity.Animator.speed = 1f;
            }

            // 애니메이션 처음부터 강제 재시작 (루핑 방지 및 정확한 재생)
            entity.Animator.Play("Attack", -1, 0f);

            // [추가] 애니메이션 이벤트(OnHitEvent)의 정확한 발생 시간(windupTime)을 찾아 배속을 반영합니다.
            float eventTime = 1.0f; // fallback (0.6f -> 1.0f 증가)
            foreach (var clip in clips)
            {
                if (clip.name.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (var evt in clip.events)
                    {
                        if (evt.functionName == "OnHitEvent")
                        {
                            eventTime = evt.time;
                            break;
                        }
                    }
                    break;
                }
            }
            windupTime = eventTime / entity.Animator.speed;
        }

        // [수정] 텔레그래프 등 선딜레이 시작 시점의 처리를 가상 메서드로 분리
        OnWindupStart(entity, windupTime);

        // 애니메이션 이벤트를 우선 대기
        bool hasHitEvent = hasAnimator && entity.HasAnimationEvent("Attack", "OnHitEvent");
        bool hasEndEvent = hasAnimator && entity.HasAnimationEvent("Attack", "OnAttackEndEvent");

        // [1] Windup (선딜레이)
        if (hasHitEvent)
        {
            // 애니메이션 이벤트(OnHitEvent)가 올 때까지 대기 (최대 2초 타임아웃 방어코드)
            float timeout = 2.0f;
            while (!entity.HasFiredHitEvent && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                OnWindupUpdate(entity);
                yield return null;
            }
        }
        else
        {
            // Fallback: 이벤트가 없거나 애니메이터가 없을 경우 임시 시간 대기
            float timeout = 1.0f; // 0.6f -> 1.0f 증가
            while (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                OnWindupUpdate(entity);
                yield return null;
            }
        }

        // [2] Execution (타격)
        ExecuteBasicAttack(entity);
        entity.HasFiredHitEvent = true;

        // [3] Recovery (후딜레이)
        if (hasEndEvent)
        {
            // 애니메이션 이벤트(OnAttackEndEvent)가 올 때까지 대기 (최대 2초 타임아웃 방어코드)
            float timeout = 2.0f;
            while (!entity.HasFiredAttackEndEvent && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            // Fallback: 이벤트가 없거나 애니메이터가 없을 경우 임시 대기 (후딜레이 없음)
            yield return null;
        }

        if (hasAnimator)
        {
            entity.Animator.speed = 1f; // 공격 끝났으므로 원래 속도로 복구
        }

        entity.IsAttacking = false;
        entity.ActiveAttackCoroutine = null;
        // [수정] 공격 중 멈춰있던 애니메이션 상태 추적을 초기화하여, 공격 종료 직후
        // 다음 상태(Idle/Follow 등)가 정상적으로 재생되도록 강제합니다.
        entity.ResetAnimationState();
    }

    protected virtual void OnWindupStart(BaseEntity entity, float windupTime)
    {
        // 기본 근접 몹: 설정에 따라 둥근/네모난 장판 자동 생성
        if (spawnTelegraph)
        {
            entity.StartTelegraph(entity.Target, windupTime);
        }
    }

    protected virtual void OnWindupUpdate(BaseEntity entity)
    {
        // 선딜레이 동안 매 프레임 호출됩니다.
    }

    protected virtual void ExecuteBasicAttack(BaseEntity entity)
    {
        // 기본값: 근접 공격 수행 (HitBox가 아닌 직접 데미지 부여 방식)
        ExecuteAttack(entity, entity.Target);
    }

    /// <summary>
    /// 대상(플레이어 등)이 도망 트리거 범위 내에 들어왔는지 검사합니다.
    /// </summary>
    protected bool ShouldFleeFromTarget(BaseEntity entity, float fleeRangeThreshold)
    {
        if (entity.Target == null) return false;
        float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
        return dist <= fleeRangeThreshold;
    }

    /// <summary>
    /// 대상으로부터 330도 범위 내 무작위 방향으로 fleeDistance 만큼 도망친 목적지(NavMesh 좌표)를 구합니다.
    /// </summary>
    protected Vector3 CalculateFleeDestination(BaseEntity entity, float fleeDistance)
    {
        if (entity.Target == null) return entity.transform.position;

        Vector3 dirToPlayer = (entity.Target.position - entity.transform.position).normalized;
        if (dirToPlayer == Vector3.zero) dirToPlayer = Vector3.right;

        // 플레이어 반대 방향(플레이어 방향 + 180도) 기준으로 +-90도(총 180도) 범위 내에서 무작위 방향 추출
        float angleToPlayer = Mathf.Atan2(dirToPlayer.y, dirToPlayer.x) * Mathf.Rad2Deg;
        float randomOffset = Random.Range(-90f, 90f); // +-90도 편차
        float finalAngle = (angleToPlayer + 180f + randomOffset) * Mathf.Deg2Rad;

        Vector3 randomDir = new Vector3(Mathf.Cos(finalAngle), Mathf.Sin(finalAngle), 0f);
        Vector3 fleeTarget = entity.transform.position + randomDir * fleeDistance;

        if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit hit, fleeDistance, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return fleeTarget;
    }
}
