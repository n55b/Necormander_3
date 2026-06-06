using UnityEngine;
using UnityEngine.AI;

public enum AIState { Idle, Follow, Attack, Caught, Thrown }

/// <summary>
/// 유닛의 모든 행동(대기, 추격, 공격, 던져짐)을 관리하는 통합 AI 기반 클래스입니다.
/// 모든 행동 판단과 실행의 '단일 진실 공급원(Single Source of Truth)' 역할을 합니다.
/// </summary>
public abstract class AIPatternSO : ScriptableObject
{
    [Header("기본 설정")]
    public AIState currentState = AIState.Idle;
    public float pushRadius = 0.8f;
    public float pushStrength = 2.0f;

    [Header("런타임 데이터")]
    [SerializeField] protected Transform target;
    [SerializeField] protected float atkTimer;
    protected NavMeshPath testPath;

    // 외부(애니메이션 및 매니저 등)에서 참조할 프로퍼티
    public AIState CurrentState => currentState;
    public Transform Target => target;

    // 초기화: 모든 상태와 변수를 깨끗하게 비웁니다.
    public virtual void Init(BaseEntity entity)
    {
        testPath = new NavMeshPath();
        currentState = AIState.Idle;
        target = null;
        atkTimer = 0f;

        // 이동 중이었다면 즉시 정지
        StopNavAgent(entity);
    }

    // 매 프레임 실행: AI의 핵심 루프
    public virtual void Execute(BaseEntity entity)
    {
        // 현재 상태 Entity에게 전달하여 애니메이션 재생
        entity.UpdateAnimation(currentState);

        // [Test Mode] 오토배틀러 비활성화 시, 아군(Ally)은 모든 AI 판단(공격, 타겟팅)을 중단하고 플레이어만 따라다님
        if (GameManager.Instance != null && GameManager.Instance.testMode_DisableAutoBattle)
        {
            if (entity.team == Team.Ally)
            {
                var ally = entity as AllyController;
                if (ally != null && ally.player != null)
                {
                    target = ally.player;
                    float dist = Vector2.Distance(entity.transform.position, target.position);
                    
                    if (dist > 2.0f) currentState = AIState.Follow;
                    else currentState = AIState.Idle;

                    switch (currentState)
                    {
                        case AIState.Idle: OnIdle(entity); break;
                        case AIState.Follow: OnFollow(entity); break;
                    }
                    
                    entity.UpdateAnimation(currentState);
                    CalculateRotate(target, entity);
                    return; // 더 이상 하위 로직(적군 탐색 등)을 실행하지 않음
                }
            }
        }

        if(target != null)
        {
            CalculateRotate(target, entity);
        }

        // 던져진 상태일 때는 모든 AI 판단을 중지합니다.
        if (currentState == AIState.Thrown || currentState == AIState.Caught) return;

        // [핵심] 현재 타겟이 유효하지 않으면 즉시 해제하여 다음 UpdateTargeting에서 새 타겟을 찾게 함
        if (target != null && IsTargetInvalid(target))
        {
            target = null;
        }

        UpdateTargeting(entity);
        UpdateStateTransitions(entity);

        switch (currentState)
        {
            case AIState.Idle: OnIdle(entity); break;
            case AIState.Follow: OnFollow(entity); break;
            case AIState.Attack: OnAttack(entity); break;
        }

        // 공통 물리 로직 (밀어내기)
        //ApplySoftPush(entity);
    }

    // 외부에서 강제로 상태를 변경할 때 사용 (예: AllyController.OnPickedUp)
    public void SetState(BaseEntity entity, AIState newState)
    {
        if (currentState == newState) return;

        // 상태가 변경될 때마다 해당 상태의 진입 메서드 호출
        switch (currentState)
        {
            case AIState.Caught:    // Caught에서 다른 상태로 나가니까 OutCaught 호출
                OutCaught(entity);
                break;
        }

        currentState = newState;

        // 상태를 강제로 바꿀 때 즉시 애니메이션도 동기화!
        if (entity != null)
        {
            entity.UpdateAnimation(newState);
        }
    }

    // --- 가상 메서드 (자식 클래스에서 override) ---

    protected virtual void UpdateTargeting(BaseEntity entity) { }
    protected virtual void UpdateStateTransitions(BaseEntity entity) { }
    protected virtual void OnIdle(BaseEntity entity) { }
    protected virtual void OnFollow(BaseEntity entity) { }
    protected virtual void OnAttack(BaseEntity entity) { }
    protected virtual void OutCaught(BaseEntity entity)
    {
        if(entity._target != null)
        {
            entity._target = null;
        }
    }

    // --- 공통 유틸리티 기능 (모든 브레인이 공유) ---

    /// <summary>
    /// [핵심 공격 로직] 타겟의 스탯을 가져와 데미지를 입히는 통합 로직입니다.
    /// </summary>
    protected virtual void ExecuteAttack(BaseEntity entity, Transform currentTarget)
    {
        if (IsTargetInvalid(currentTarget)) return;

        // [수정] 이제 AI 패턴이 직접 데미지를 주지 않고, Entity에게 공격 실행을 맡깁니다.
        // 이를 통해 보석 효과(무기 속성 부여 등)가 정상적으로 적용됩니다.
        entity.ExecuteAttack(currentTarget);
    }

    protected void StopNavAgent(BaseEntity entity)
    {
        var agent = entity.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
    }

    // 밀어내기 로직 임시 비활성화: Navmesh Agent에서 가능
    /*
    protected void ApplySoftPush(BaseEntity entity)
    {
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (entity.gameObject.layer == flyingLayer) return;

        Vector2 pushDir = Vector2.zero;
        Collider2D[] neighbors = Physics2D.OverlapCircleAll(entity.transform.position, pushRadius);
        int count = 0;

        foreach (var col in neighbors)
        {
            if (col.gameObject == entity.gameObject) continue;
            if (col.gameObject.layer == flyingLayer) continue;

            if (col.gameObject.layer == entity.gameObject.layer)
            {
                Vector2 diff = (Vector2)entity.transform.position - (Vector2)col.transform.position;
                float distance = diff.magnitude;

                if (distance < pushRadius)
                {
                    float strength = 1.0f - (distance / pushRadius);
                    pushDir += diff.normalized * strength;
                    count++;
                }
            }
        }

        if (count > 0 && entity.GetComponent<Rigidbody2D>() != null)
        {
            if (currentState != AIState.Follow)
            {
                entity.GetComponent<Rigidbody2D>().linearVelocity = pushDir * pushStrength;
            }
        }
    }
    */

    protected bool IsTargetInvalid(Transform t)
    {
        if (t == null) return true;

        // 1. 레이어 체크: FlyingObject인 경우(들린 상태 또는 날아가는 상태) 즉시 타겟 제외
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (t.gameObject.layer == flyingLayer) return true;

        // 2. AI 상태 체크: Thrown 상태인 유닛은 타겟팅 대상에서 제외
        BaseEntity targetEntity = t.GetComponentInParent<BaseEntity>();
        if (targetEntity != null && targetEntity.Brain != null && targetEntity.Brain.CurrentState == AIState.Thrown
        || currentState == AIState.Caught)
            return true;

        // 3. 체력 및 무적 상태 체크
        // [수정] 타겟팅 판단 시에도 엉뚱한(들려있는) 자식의 Stat을 보지 않도록 주의
        CharacterStat stat = t.GetComponent<CharacterStat>();
        if (stat == null)
        {
            foreach (var s in t.GetComponentsInChildren<CharacterStat>())
            {
                if (s.gameObject.layer != flyingLayer) { stat = s; break; }
            }
        }

        if (stat != null)
        {
            return stat.Health.IsDead || stat.Health.Invincible;
        }
        return false;
    }

    // 공격 할 때, 상대 바라보게
    protected void CalculateRotate(Transform target, BaseEntity entity)
    {
        if(target == null) return;

        if (target.position.x - entity.transform.position.x > 0.0f)
        {
            entity.SpriteRenderer.flipX = true;
        }
        else if (target.position.x - entity.transform.position.x < 0.0f)
        {
            entity.SpriteRenderer.flipX = false;
        }
    }
}
