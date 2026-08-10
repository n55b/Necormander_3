using UnityEngine;
using UnityEngine.AI;

// [26/07/17] Caught/Thrown 삭제 — 유닛을 집어 들고 던지는 투척 전용 상태였다.
public enum AIState { Idle, Follow, Attack, Skill }

/// <summary>
/// 유닛의 모든 행동(대기, 추격, 공격)을 관리하는 통합 AI 기반 클래스입니다.
/// 모든 행동 판단과 실행의 '단일 진실 공급원(Single Source of Truth)' 역할을 합니다.
/// </summary>
public abstract class AIPatternSO : ScriptableObject
{
    [Header("기본 설정")]
    public float pushRadius = 0.8f;
    public float pushStrength = 2.0f;

    // 외부(애니메이션 및 매니저 등)에서 참조할 프로퍼티
    // 초기화: 모든 상태와 변수를 깨끗하게 비웁니다.
    public virtual void Init(BaseEntity entity)
    {
        
        entity.CurrentState = AIState.Idle;
        entity.Target = null;
        entity.AtkTimer = 1000f; // [수정] 조우 시 즉시 첫 공격이 발동되도록 가득 채움

        // 이동 중이었다면 즉시 정지
        StopNavAgent(entity);
    }

    // 매 프레임 실행: AI의 핵심 루프
    public virtual void Execute(BaseEntity entity)
    {
        // [추가] 상태와 무관하게 타이머는 무조건 매 프레임 돕니다.
        entity.AtkTimer += Time.deltaTime;

        // 현재 상태 Entity에게 전달하여 애니메이션 재생
        entity.UpdateAnimation(entity.CurrentState);

        // ponytail: 여기 있던 'testMode_DisableAutoBattle 시 아군은 플레이어만 따라다님' 분기는
        // 아군 엔티티(AllyController)가 더 이상 스폰되지 않으면서 도달 불가가 되어 제거했다.

        if(entity.Target != null)
        {
            entity.LookAtTarget(entity.Target);
        }

        // 스킬 시전(강제 제어) 상태일 때는 모든 AI 판단을 중지합니다.
        if (entity.CurrentState == AIState.Skill) return;

        // [핵심] 현재 타겟이 유효하지 않으면 즉시 해제하여 다음 UpdateTargeting에서 새 타겟을 찾게 함
        if (entity.Target != null && IsTargetInvalid(entity, entity.Target))
        {
            entity.Target = null;
        }

        UpdateTargeting(entity);
        UpdateStateTransitions(entity);

        switch (entity.CurrentState)
        {
            case AIState.Idle: OnIdle(entity); break;
            case AIState.Follow: OnFollow(entity); break;
            case AIState.Attack: OnAttack(entity); break;
        }

        // 공통 물리 로직 (밀어내기)
        //ApplySoftPush(entity);
    }

/// <summary>
    /// 피격/스턴 등으로 공격이 중단될 때 호출된다(BaseEntity.CancelAttack).
    /// 코루틴이 StopCoroutine 으로 끊기면 루틴 내부의 정리 코드는 실행되지 않으므로,
    /// 예고 표시처럼 루틴이 만든 월드 오브젝트는 여기서 치워야 씬에 남지 않는다.
    /// </summary>
    public virtual void OnAttackCancelled(BaseEntity entity) { }


    // 외부에서 강제로 상태를 변경할 때 사용
    public void SetState(BaseEntity entity, AIState newState)
    {
        if (entity.CurrentState == newState) return;

        // [26/07/17] Caught 이탈 시 OutCaught 를 부르던 분기 삭제 — Caught 상태 자체가 없어졌다.

        entity.CurrentState = newState;

        // 상태를 강제로 바꿀 때 즉시 애니메이션도 동기화!
        if (entity != null)
        {
            entity.UpdateAnimation(newState);
        }
    }

    public void ResetAttackTimer(BaseEntity entity)
    {
        if (entity != null) entity.AtkTimer = 0f;
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
        if (IsTargetInvalid(entity, currentTarget)) return;

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

    

    protected bool IsTargetInvalid(BaseEntity entity, Transform t)
    {
        if (t == null) return true;

        // 1. 레이어 체크: FlyingObject인 경우(들린 상태 또는 날아가는 상태) 즉시 타겟 제외
        int flyingLayer = Layers.FlyingObject;
        if (t.gameObject.layer == flyingLayer) return true;


        // 3. 체력 및 무적 상태 체크
        // [수정] 타겟팅 판단 시에도 엉뚱한(들려있는) 자식의 Stat을 보지 않도록 주의
        CharacterStat stat;
        if (t == entity.Target && entity.TargetStat != null)
            stat = entity.TargetStat;
        else
            stat = t.GetComponent<CharacterStat>();
        if (stat == null)
        {
            foreach (var s in t.GetComponentsInChildren<CharacterStat>())
            {
                if (s.gameObject.layer != flyingLayer) { stat = s; break; }
            }
        }

        if (stat != null)
        {
            // 무적은 '플레이어에 한해' 타겟 해제 사유가 아니다. 예전엔 무적이면 무조건 타겟을 놓아서,
            // 피격 무적 1초 동안 몹이 Idle 로 돌아가고 공격 자체를 안 했다(StartTelegraph 가 target null
            // 이면 히트박스를 안 만든다) — 우클릭 카운터/가드를 연습할 기회가 구조적으로 없었다.
            // 이제 몹은 계속 때리고, 무적이면 피해만 안 들어간다.
            // 적 무적(보스 페이즈 전환 등)은 그대로 걸러야 하므로 검사를 통째로 지우진 않는다.
            if (stat.Health.IsDead) return true;
            return stat.Health.Invincible && !stat.transform.root.CompareTag("Player");
        }
        return false;
    }
}
