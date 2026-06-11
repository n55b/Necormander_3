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
            target = nearestEnemy;
        }
        else
        {
            // 2. 적이 없을 때: 아군은 플레이어를 타겟으로 삼음
            if (entity.team == Team.Ally)
            {
                var ally = entity as AllyController;
                if (ally != null && ally.player != null) target = ally.player;
                else target = null;
            }
            else
            {
                target = null;
            }
        }
    }

    protected override void UpdateStateTransitions(BaseEntity entity)
    {
        // [상태 잠금] 공격 중일 때는 상태 전이를 무시하고 공격을 끝까지 완수합니다.
        if (currentState == AIState.Attack && entity.IsAttacking) return;

        AIState nextState = AIState.Idle;

        if (target != null)
        {
            float dist = Vector2.Distance(entity.transform.position, target.position);
            
            // [수정] 아군 미니언이 플레이어를 따라갈 때만 거리 유지, 적군이 플레이어를 잡았을 때는 공격 수행
            if (entity.team == Team.Ally && target.CompareTag("Player"))
            {
                if (dist > 2.0f) nextState = AIState.Follow;
                else nextState = AIState.Idle;
            }
            else
            {
                // 적인 경우(플레이어 포함) 사거리에 따라 결정
                if (dist <= entity.Stats.ATKRANGE - 0.2f) nextState = AIState.Attack;
                else nextState = AIState.Follow;
            }
        }

        if(nextState != currentState)
        {
            currentState = nextState;
        }
    }

    protected override void OnIdle(BaseEntity entity)
    {
        StopNavAgent(entity);
        atkTimer = 1000f; // 적을 만나면 즉시 첫 공격 발동을 위해 큰 값으로 세팅
    }

    protected override void OnFollow(BaseEntity entity)
    {
        var agent = entity.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.isStopped = false;
            agent.speed = entity.Stats.MOVESPEED;
            agent.SetDestination(target.position);
        }
        atkTimer = 1000f; // 이동 중에는 게이지를 가득 채워두어 접근 즉시 타격
    }

    protected override void OnAttack(BaseEntity entity)
    {
        StopNavAgent(entity);

        // 타이머는 공격 중이든 아니든 무조건 돕니다. (초당 공격 횟수 정확히 보장)
        atkTimer += Time.deltaTime;

        // 단, 이미 공격을 실행 중(애니메이션 재생 중)이라면 중복 실행하지 않습니다.
        if (entity.IsAttacking) return;

        if (atkTimer >= entity.Stats.ATKSPD)
        {
            atkTimer = 0f;
            entity.StartCoroutine(AttackRoutine(entity));
        }
    }

    protected virtual System.Collections.IEnumerator AttackRoutine(BaseEntity entity)
    {
        entity.IsAttacking = true;
        entity.HasFiredHitEvent = false;
        entity.HasFiredAttackEndEvent = false;

        bool hasAnimator = entity.Animator != null;
        float windupTime = 0.3f; // 기본 선딜레이 fallback

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
            float eventTime = 0.5f; // fallback
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
            float timeout = 0.3f;
            while (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                OnWindupUpdate(entity);
                yield return null;
            }
        }

        // [2] Execution (타격)
        ExecuteBasicAttack(entity);

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
            // Fallback: 이벤트가 없거나 애니메이터가 없을 경우 임시 시간 대기 (0.5초)
            yield return new WaitForSeconds(0.5f);
        }

        if (hasAnimator)
        {
            entity.Animator.speed = 1f; // 공격 끝났으므로 원래 속도로 복구
        }

        entity.IsAttacking = false;
    }

    protected virtual void OnWindupStart(BaseEntity entity, float windupTime)
    {
        // 기본 근접 몹: 설정에 따라 둥근/네모난 장판 자동 생성
        if (spawnTelegraph)
        {
            entity.StartTelegraph(target, windupTime);
        }
    }

    protected virtual void OnWindupUpdate(BaseEntity entity)
    {
        // 선딜레이 동안 매 프레임 호출됩니다.
    }

    protected virtual void ExecuteBasicAttack(BaseEntity entity)
    {
        // 기본값: 근접 공격 수행 (HitBox가 아닌 직접 데미지 부여 방식)
        ExecuteAttack(entity, target);
    }
}
