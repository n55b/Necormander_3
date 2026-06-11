using UnityEngine;
using UnityEngine.AI;

[CreateAssetMenu(fileName = "WarriorBossPattern", menuName = "Necromancer/AI/WarriorBossPattern")]
public class WarriorBossAIPatternSO : BossAIPatternSO
{
    public enum WarriorState { Chasing, MeleeAttacking, RushPositioning, RushCharging, Rushing, Stunned }

    [Header("Warrior Settings")]
    public float specialRushInterval = 14f;
    public float meleeAttackRange = 2.5f;
    public float meleeCooldown = 1.5f;
    public float chargeTime = 3f;
    public float stunTime = 3f;

    [Header("Hitbox Prefabs (Optional)")]
    [Tooltip("비워두면 스크립트에서 임시 빨간 도형을 자동 생성합니다.")]
    public GameObject slamHitboxPrefab; // 원형 또는 직사각형
    public GameObject slashHitboxPrefab; // 부채꼴 대용
    public GameObject massiveRushHitboxPrefab; // 맵 전체

    [Header("Runtime (Debug)")]
    [SerializeField] private WarriorState warriorState = WarriorState.Chasing;
    [SerializeField] private float specialTimer = 0f;
    [SerializeField] private float actionTimer = 0f;
    [SerializeField] private Vector2 targetRushPos;
    [SerializeField] private Vector2 rushOppositePos;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        warriorState = WarriorState.Chasing;
        specialTimer = 0f;
        actionTimer = meleeCooldown; 
    }

    public override void Execute(BaseEntity entity)
    {
        UpdatePhase(entity);
        if (entity.CurrentState == AIState.Thrown || entity.CurrentState == AIState.Caught) return;

        if (entity.Target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) entity.Target = player.transform;
            if (entity.Target == null) return;
        }

        // 특수 패턴 쿨타임은 병렬로 계속 흐름
        specialTimer += Time.deltaTime;

        // 특수 패턴 진입 체크 (현재 근접 공격 중이 아닐 때만 진입)
        if (specialTimer >= specialRushInterval && warriorState == WarriorState.Chasing)
        {
            EnterRushPositioning(entity);
        }

        switch (warriorState)
        {
            case WarriorState.Chasing:
                actionTimer -= Time.deltaTime;
                float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
                
                if (dist <= meleeAttackRange && actionTimer <= 0f)
                {
                    PerformMeleeAttack(entity);
                }
                else
                {
                    // 추격 로직
                    var agent = entity.GetComponent<NavMeshAgent>();
                    if (agent != null && agent.isActiveAndEnabled)
                    {
                        agent.isStopped = false;
                        agent.speed = entity.Stats.MOVESPEED;
                        agent.SetDestination(entity.Target.position);
                    }
                }
                break;

            case WarriorState.MeleeAttacking:
                actionTimer -= Time.deltaTime;
                StopNavAgent(entity);
                if (actionTimer <= 0f)
                {
                    warriorState = WarriorState.Chasing;
                    actionTimer = meleeCooldown;
                }
                break;

            case WarriorState.RushPositioning:
                // 3시 또는 9시 목적지로 이동
                var rushAgent = entity.GetComponent<NavMeshAgent>();
                if (rushAgent != null && rushAgent.isActiveAndEnabled)
                {
                    rushAgent.isStopped = false;
                    rushAgent.speed = entity.Stats.MOVESPEED * 1.5f; // 좀 더 빠르게 이동
                    rushAgent.SetDestination(targetRushPos);
                }

                // 목적지에 거의 도달했거나 너무 오래 걸리면 강제 차징
                if (Vector2.Distance(entity.transform.position, targetRushPos) <= 1.5f)
                {
                    // 도착 시 차징 시작
                    StopNavAgent(entity);
                    warriorState = WarriorState.RushCharging;
                    actionTimer = chargeTime;
                    
                    // 반대편을 바라봄
                    if (rushOppositePos.x > entity.transform.position.x)
                        entity.SpriteRenderer.flipX = true; // 오른쪽을 봄
                    else
                        entity.SpriteRenderer.flipX = false; // 왼쪽을 봄
                }
                break;

            case WarriorState.RushCharging:
                StopNavAgent(entity);
                actionTimer -= Time.deltaTime;
                if (actionTimer <= 0f)
                {
                    ExecuteMassiveRush(entity);
                }
                break;

            case WarriorState.Rushing:
                // 돌진은 히트박스를 깔고 즉시(혹은 매우 빠르게) 반대편으로 이동 후 스턴 상태로 넘어감
                warriorState = WarriorState.Stunned;
                actionTimer = stunTime;
                
                if (entity.Stats != null && entity.Stats.Status != null)
                {
                    entity.Stats.Status.SetDebuffBool(DebuffBoolType.Stunned, stunTime);
                }
                
                // 보스 모델 자체를 반대편으로 빠르게 순간이동 시키거나 연출
                entity.transform.position = rushOppositePos;
                break;

            case WarriorState.Stunned:
                StopNavAgent(entity);
                actionTimer -= Time.deltaTime;
                if (actionTimer <= 0f)
                {
                    warriorState = WarriorState.Chasing;
                    specialTimer = 0f; // 특수 패턴 타이머 리셋
                    actionTimer = meleeCooldown;
                }
                break;
        }
    }

    private void PerformMeleeAttack(BaseEntity entity)
    {
        warriorState = WarriorState.MeleeAttacking;
        actionTimer = 0.5f; // 공격 모션 및 판정 지속 시간 (임시)

        bool isSlam = Random.value > 0.5f;
        GameObject prefab = isSlam ? slamHitboxPrefab : slashHitboxPrefab;

        Vector2 dir = ((Vector2)entity.Target.position - (Vector2)entity.transform.position).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 spawnPos = (Vector2)entity.transform.position + dir * 1.5f;

        GameObject hitboxObj = null;

        if (prefab != null)
        {
            hitboxObj = Instantiate(prefab, spawnPos, Quaternion.Euler(0, 0, angle - 90f));
        }
        else
        {
            // 프리팹이 없다면 코드 상에서 임시 도형 생성
            hitboxObj = CreateTemporaryShape(isSlam ? PrimitiveType.Sphere : PrimitiveType.Cube, 
                isSlam ? new Vector2(2.5f, 2.5f) : new Vector2(3f, 3f), 
                new Color(1f, 0f, 0f, 0.5f));
            hitboxObj.transform.position = spawnPos;
            hitboxObj.transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        var hitbox = hitboxObj.GetComponent<GenericHitbox>();
        if (hitbox == null) hitbox = hitboxObj.AddComponent<GenericHitbox>();
        
        hitboxObj.layer = LayerMask.NameToLayer("FlyingObject"); // 물리 계층 맞춤 (투사체와 동일하게 범용 레이어 사용)
        hitbox.Init(entity.Stats.ATK, entity.opponentLayer, entity.gameObject, 0.5f);
    }


    private void EnterRushPositioning(BaseEntity entity)
    {
        warriorState = WarriorState.RushPositioning;
        
        RoomInstance currentRoom = GetCurrentRoom(entity);
        Vector2 rightPos, leftPos;

        if (currentRoom != null)
        {
            Vector2 center = (Vector2)currentRoom.transform.position + currentRoom.centerOffset;
            float halfWidth = currentRoom.roomSize.x / 2f;
            rightPos = center + new Vector2(halfWidth - 1.5f, 0);
            leftPos = center + new Vector2(-halfWidth + 1.5f, 0);
        }
        else
        {
            // 방을 찾지 못했을 때 기존 레이캐스트 기반 로직 (Fallback)
            int wallLayer = LayerMask.GetMask("Wall"); 
            RaycastHit2D rightHit = Physics2D.Raycast(entity.transform.position, Vector2.right, 50f, wallLayer);
            RaycastHit2D leftHit = Physics2D.Raycast(entity.transform.position, Vector2.left, 50f, wallLayer);

            rightPos = rightHit.collider != null ? rightHit.point - new Vector2(1.5f, 0) : (Vector2)entity.transform.position + Vector2.right * 10f;
            leftPos = leftHit.collider != null ? leftHit.point + new Vector2(1.5f, 0) : (Vector2)entity.transform.position + Vector2.left * 10f;
        }

        // 둘 중 플레이어에게서 더 먼 방향을 선택
        float distToRight = Vector2.Distance(entity.Target.position, rightPos);
        float distToLeft = Vector2.Distance(entity.Target.position, leftPos);

        bool goRight = distToRight > distToLeft;

        if (goRight)
        {
            targetRushPos = rightPos;
            rushOppositePos = leftPos;
        }
        else
        {
            targetRushPos = leftPos;
            rushOppositePos = rightPos;
        }

        Debug.Log($"<color=orange>[WarriorBoss]</color> Selected Rush Position: {(goRight ? "3 o'clock (Right)" : "9 o'clock (Left)")}");
    }

    private void ExecuteMassiveRush(BaseEntity entity)
    {
        warriorState = WarriorState.Rushing;
        
        RoomInstance currentRoom = GetCurrentRoom(entity);
        Vector2 dir = (rushOppositePos - (Vector2)entity.transform.position).normalized;
        
        Vector2 spawnPos;
        float width, height;

        if (currentRoom != null)
        {
            Vector2 center = (Vector2)currentRoom.transform.position + currentRoom.centerOffset;
            width = currentRoom.roomSize.x - 3.0f; // 보스가 서있는 쪽 제외
            height = currentRoom.roomSize.y;
            
            // 안전지대는 보스가 서있는 곳 뒷편이므로, 보스가 3시(우측)에 있으면 중심점은 방 중앙에서 약간 좌측으로 치우침
            spawnPos = center + dir * 1.5f; 
        }
        else
        {
            width = Vector2.Distance(entity.transform.position, rushOppositePos);
            height = 15f;
            spawnPos = (Vector2)entity.transform.position + dir * (width / 2f);
        }

        GameObject hitboxObj = null;

        if (massiveRushHitboxPrefab != null)
        {
            hitboxObj = Instantiate(massiveRushHitboxPrefab, spawnPos, Quaternion.identity);
            hitboxObj.transform.localScale = new Vector3(width, height, 1f);
        }
        else
        {
            hitboxObj = CreateTemporaryShape(PrimitiveType.Cube, new Vector2(width, height), new Color(1f, 0.5f, 0f, 0.5f));
            hitboxObj.transform.position = spawnPos;
            hitboxObj.transform.rotation = Quaternion.identity;
        }

        hitboxObj.layer = LayerMask.NameToLayer("FlyingObject"); // 물리 계층 맞춤 (투사체와 동일하게 범용 레이어 사용)

        var hitbox = hitboxObj.GetComponent<GenericHitbox>();
        if (hitbox == null) hitbox = hitboxObj.AddComponent<GenericHitbox>();
        
        hitbox.Init(entity.Stats.ATK * 2f, entity.opponentLayer, entity.gameObject, 1.0f); // 돌진 데미지 2배

        Debug.Log($"<color=orange>[WarriorBoss]</color> MASSIVE RUSH EXECUTED!");
    }

    private GameObject CreateTemporaryShape(PrimitiveType type, Vector2 scale, Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        DestroyImmediate(obj.GetComponent<Collider>()); // 즉시 3D 물리 제거 (충돌 에러 방지)
        
        if (type == PrimitiveType.Cube)
        {
            var col = obj.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
        }
        else
        {
            var col = obj.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
        }

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Unlit/Transparent 머티리얼 적용이 안되더라도 일단 색상 변경 시도
            renderer.material.color = color;
        }

        obj.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        return obj;
    }
}
