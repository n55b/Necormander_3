using UnityEngine;
using UnityEngine.AI;

public enum AIState { Idle, Follow, Attack, Thrown }

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
        // 던져진 상태일 때는 모든 AI 판단을 중지합니다.
        if (currentState == AIState.Thrown) return;

        UpdateTargeting(entity);
        UpdateStateTransitions(entity);

        switch (currentState)
        {
            case AIState.Idle: OnIdle(entity); break;
            case AIState.Follow: OnFollow(entity); break;
            case AIState.Attack: OnAttack(entity); break;
        }

        // 공통 물리 로직 (밀어내기)
        ApplySoftPush(entity);
    }

    // 외부에서 강제로 상태를 변경할 때 사용 (예: AllyController.OnPickedUp)
    public void SetState(AIState newState)
    {
        currentState = newState;
    }

    // --- 가상 메서드 (자식 클래스에서 override) ---

    protected virtual void UpdateTargeting(BaseEntity entity) { }
    protected virtual void UpdateStateTransitions(BaseEntity entity) { }
    protected virtual void OnIdle(BaseEntity entity) { }
    protected virtual void OnFollow(BaseEntity entity) { }
    protected virtual void OnAttack(BaseEntity entity) { }

    // --- 공통 유틸리티 기능 (모든 브레인이 공유) ---

    /// <summary>
    /// [핵심 공격 로직] 타겟의 스탯을 가져와 데미지를 입히는 통합 로직입니다.
    /// </summary>
    protected virtual void ExecuteAttack(BaseEntity entity, Transform currentTarget)
    {
        if (IsTargetInvalid(currentTarget)) return;

        if (currentTarget.TryGetComponent<CharacterStat>(out var targetStat))
        {
            DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject);
            targetStat.GetDamage(info);
            // Debug.Log($"<color=red>[AI Attack]</color> {entity.name} -> {currentTarget.name}에게 데미지 부여");
        }
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

    protected bool IsTargetInvalid(Transform t)
    {
        if (t == null) return true;
        if (t.TryGetComponent<CharacterStat>(out var stat))
        {
            return stat.IsDead || stat.Invincible;
        }
        return false;
    }
}
