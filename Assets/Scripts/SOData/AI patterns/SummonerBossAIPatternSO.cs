using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

/// <summary>
/// 소환사 보스의 AI 패턴입니다.
/// 소환 -> (도망/돌진/3갈래 화염구) -> 단발 화염구 -> 흡수 등 주기적으로 동작합니다.
/// </summary>
[CreateAssetMenu(fileName = "SummonerBossPattern", menuName = "Necromancer/AI/SummonerBossPattern")]
public class SummonerBossAIPatternSO : BossAIPatternSO
{
    public enum SummonerState { Summoning, Kiting, Shooting, Absorbing, DashingStart, Dashing, RestingAfterDash, ReturningToCenter, SpreadPositioning, SpreadShooting }

    [Header("Cycle Settings")]
    public float cycleDuration = 12f;
    public float fireballInterval = 4f;
    public int fireballsPerCycle = 3;

    [Header("Summon Settings")]
    public MinionDataSO minionToSummon; 
    public int summonCount = 4;
    public float summonStationaryTime = 1.0f;

    [Header("Attack Settings")]
    public GameObject fireballPrefab;
    public float projectileSpeed = 5f;
    public float lifeTime = 7f;
    [Tooltip("화염구를 쏘기 전 기를 모으는(정지) 시간입니다.")]
    public float shootStationaryTime = 0.5f;

    [Header("Absorb Settings")]
    public float shieldPerMinion = 10f;
    public float absorbStationaryTime = 1.5f;

    [Header("Movement Settings")]
    public float idealDistance = 8f;
    public float minDistance = 5f;

    [Header("Dash Settings")]
    public float dashCooldown = 8f;
    public float dashSpeedMultiplier = 3f;
    public float dashDuration = 1.5f;
    public float dashRestTime = 1.0f;
    [SerializeField] private float dashCooldownTimer;
    private GameObject currentDashHitbox;
    private Vector2 dashTargetPos;
    private Vector2 dashDirection;
    private Vector2 centerTargetPos;

    [Header("Spread Fireball Settings")]
    public float spreadFireballInterval = 10f;
    public int spreadCount = 3;
    public float spreadAngle = 15f;
    [SerializeField] private float spreadFireballTimer;
    private Vector2 spreadTargetPos;

    [Header("Runtime (Debug)")]
    [SerializeField] private SummonerState summonerState = SummonerState.Summoning;
    [SerializeField] private float cycleTimer;
    [SerializeField] private float fireballTimer;
    [SerializeField] private int fireballsShot;
    [SerializeField] private float stationaryTimer;
    [SerializeField] private bool hasSpawnedInCurrentCycle; 

    private List<GameObject> activeMinions = new List<GameObject>();
    private List<GameObject> activeFireballs = new List<GameObject>();

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        cycleTimer = 0f;
        fireballTimer = 0f;
        fireballsShot = 0;
        dashCooldownTimer = 0f;
        spreadFireballTimer = 0f;
        activeMinions.Clear();
        activeFireballs.Clear(); 
        if (currentDashHitbox != null) Destroy(currentDashHitbox);
        
        summonerState = SummonerState.Summoning;
        stationaryTimer = summonStationaryTime;
        hasSpawnedInCurrentCycle = false;

        entity.Stats.Health.OnDeath -= HandleBossDeath; 
        entity.Stats.Health.OnDeath += HandleBossDeath;
        
        Debug.Log("<color=magenta>[SummonerBoss]</color> Initialized. Waiting for initial summon delay...");
    }

    private void HandleBossDeath()
    {
        Debug.Log("<color=red>[SummonerBoss]</color> Boss Died! Cleaning up everything...");
        CleanupAllMinions(true); 
        CleanupAllFireballs(); 
        if (currentDashHitbox != null) Destroy(currentDashHitbox);
    }

    private void CleanupAllMinions(bool shouldKill)
    {
        foreach (var m in activeMinions)
        {
            if (m != null)
            {
                if (shouldKill)
                {
                    var health = m.GetComponentInChildren<CharacterHealth>();
                    if (health != null && !health.IsDead)
                        health.GetDamage(new DamageInfo(9999f, DamageType.Fixed, null));
                    else
                        Destroy(m);
                }
                else
                {
                    Destroy(m);
                }
            }
        }
        activeMinions.Clear();
    }

    private void CleanupAllFireballs()
    {
        foreach (var fb in activeFireballs)
        {
            if (fb != null) Destroy(fb);
        }
        activeFireballs.Clear();
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

        if (stationaryTimer > 0)
        {
            stationaryTimer -= Time.deltaTime;
            StopNavAgent(entity);
            UpdateCycleLogic(entity);
            return;
        }

        UpdateCycleLogic(entity);

        if (summonerState == SummonerState.Kiting)
        {
            HandleKiting(entity);
        }
        else if (summonerState == SummonerState.Dashing)
        {
            HandleDashing(entity);
        }
        else if (summonerState == SummonerState.ReturningToCenter)
        {
            HandleReturningToCenter(entity);
        }
        else if (summonerState == SummonerState.SpreadPositioning)
        {
            HandleSpreadPositioning(entity);
        }
        else
        {
            StopNavAgent(entity);
        }
    }

    private void UpdateCycleLogic(BaseEntity entity)
    {
        switch (summonerState)
        {
            case SummonerState.Summoning:
                if (stationaryTimer <= 0)
                {
                    if (!hasSpawnedInCurrentCycle)
                    {
                        PerformActualSummon(entity);
                        hasSpawnedInCurrentCycle = true;
                    }
                    PrepareNextFireball();
                }
                break;

            case SummonerState.RestingAfterDash:
                if (stationaryTimer <= 0)
                {
                    RoomInstance room = GetCurrentRoom(entity);
                    if (room != null)
                    {
                        centerTargetPos = (Vector2)room.transform.position + room.centerOffset;
                        summonerState = SummonerState.ReturningToCenter;
                    }
                    else
                    {
                        summonerState = SummonerState.Kiting;
                    }
                }
                break;

            case SummonerState.Kiting:
                cycleTimer += Time.deltaTime;
                fireballTimer += Time.deltaTime;
                dashCooldownTimer -= Time.deltaTime;
                spreadFireballTimer += Time.deltaTime;

                if (spreadFireballTimer >= spreadFireballInterval)
                {
                    EnterSpreadPositioning(entity);
                    break;
                }

                if (dashCooldownTimer <= 0f && IsNearWall(entity))
                {
                    EnterDashingStart(entity);
                    break;
                }

                if (fireballsShot < fireballsPerCycle && fireballTimer >= fireballInterval)
                {
                    PrepareNextFireball();
                }

                if (cycleTimer >= cycleDuration)
                {
                    EnterAbsorbState(entity);
                }
                break;

            case SummonerState.Shooting:
                if (stationaryTimer <= 0)
                {
                    PerformActualShoot(entity);
                    summonerState = SummonerState.Kiting;
                }
                break;

            case SummonerState.Absorbing:
                if (stationaryTimer <= 0)
                {
                    ResetCycle(entity);
                }
                break;
        }
    }

    private bool IsNearWall(BaseEntity entity)
    {
        RoomInstance room = GetCurrentRoom(entity);
        if (room == null) return false;

        Vector2 pos = entity.transform.position;
        Vector2 center = (Vector2)room.transform.position + room.centerOffset;
        float halfWidth = room.roomSize.x / 2f;
        float halfHeight = room.roomSize.y / 2f;
        
        float distToLeft = Mathf.Abs(pos.x - (center.x - halfWidth));
        float distToRight = Mathf.Abs(pos.x - (center.x + halfWidth));
        float distToTop = Mathf.Abs(pos.y - (center.y + halfHeight));
        float distToBottom = Mathf.Abs(pos.y - (center.y - halfHeight));
        
        float minWallDist = Mathf.Min(distToLeft, distToRight, distToTop, distToBottom);
        return minWallDist < 2.5f; // NavMesh 에어리어 바운딩 등을 고려해 넉넉하게 2.5 유닛 이내로 접근 시 벽으로 간주
    }


    private float dashTimeoutTimer = 0f;

    private void EnterDashingStart(BaseEntity entity)
    {
        summonerState = SummonerState.Dashing;
        dashCooldownTimer = dashCooldown;
        
        dashDirection = ((Vector2)entity.Target.position - (Vector2)entity.transform.position).normalized;
        float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
        dashTargetPos = (Vector2)entity.transform.position + dashDirection * (dist * 1.5f);

        // NavMeshAgent 임시 비활성화 (순수 물리 직선 이동을 위해)
        var agent = entity.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        // 돌진 데미지 판정을 위한 히트박스 생성 (본체 크기)
        currentDashHitbox = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(currentDashHitbox.GetComponent<Collider>());
        var col = currentDashHitbox.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        
        currentDashHitbox.transform.SetParent(entity.transform);
        currentDashHitbox.transform.localPosition = Vector2.zero;
        currentDashHitbox.transform.localScale = Vector3.one * 1.5f; // 본체와 비슷하게 크게
        currentDashHitbox.GetComponent<MeshRenderer>().enabled = false;
        
        currentDashHitbox.layer = LayerMask.NameToLayer("FlyingObject"); 
        var hitbox = currentDashHitbox.AddComponent<GenericHitbox>();
        hitbox.Init(entity.Stats.ATK * 1.5f, entity.opponentLayer, entity.gameObject, dashDuration);

        dashTimeoutTimer = dashDuration; // 돌진 타임아웃
        Debug.Log("<color=yellow>[SummonerBoss]</color> Dashing to player!");
    }

    private void HandleDashing(BaseEntity entity)
    {
        dashTimeoutTimer -= Time.deltaTime;
        
        // 순수 물리(Rigidbody2D)로 강제 직선 이동 (NavMesh 무시)
        var rb = entity.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = dashDirection * (entity.Stats.MOVESPEED * dashSpeedMultiplier);
        }

        if (Vector2.Distance(entity.transform.position, dashTargetPos) < 1.0f || dashTimeoutTimer <= 0)
        {
            // 돌진 완료시 속도 초기화 및 NavMeshAgent 원상복구
            if (rb != null) rb.linearVelocity = Vector2.zero;
            var agent = entity.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = true;

            // 잠깐 휴식 후 중앙으로 복귀
            if (currentDashHitbox != null) Destroy(currentDashHitbox);
            summonerState = SummonerState.RestingAfterDash;
            stationaryTimer = dashRestTime;
        }
    }

    private void HandleReturningToCenter(BaseEntity entity)
    {
        var agent = entity.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.isStopped = false;
            agent.speed = entity.Stats.MOVESPEED * dashSpeedMultiplier; // 빠른 속도로 복귀
            agent.SetDestination(centerTargetPos);
        }

        if (Vector2.Distance(entity.transform.position, centerTargetPos) < 1.5f)
        {
            // 중앙에 도착하면 다시 패턴 재개
            summonerState = SummonerState.Kiting;
        }
    }

    private void EnterSpreadPositioning(BaseEntity entity)
    {
        summonerState = SummonerState.SpreadPositioning;
        RoomInstance room = GetCurrentRoom(entity);
        if (room != null)
        {
            Vector2 center = (Vector2)room.transform.position + room.centerOffset;
            float halfWidth = room.roomSize.x / 2f;
            float halfHeight = room.roomSize.y / 2f;
            
            // 12, 5, 7시 방향 (벽에서 약간 띄운 위치)
            Vector2 pos12 = center + new Vector2(0, halfHeight - 2f);
            Vector2 pos5 = center + new Vector2(halfWidth - 2f, -halfHeight + 2f);
            Vector2 pos7 = center + new Vector2(-halfWidth + 2f, -halfHeight + 2f);

            int r = Random.Range(0, 3);
            spreadTargetPos = (r == 0) ? pos12 : (r == 1) ? pos5 : pos7;
        }
        else
        {
            spreadTargetPos = (Vector2)entity.transform.position;
        }
        Debug.Log("<color=yellow>[SummonerBoss]</color> Spread Positioning!");
    }

    private void HandleSpreadPositioning(BaseEntity entity)
    {
        var agent = entity.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.isStopped = false;
            agent.speed = entity.Stats.MOVESPEED * dashSpeedMultiplier; // 빠르게 이동
            agent.SetDestination(spreadTargetPos);
        }

        if (Vector2.Distance(entity.transform.position, spreadTargetPos) < 1.5f)
        {
            EnterSpreadShooting(entity);
        }
    }

    private void EnterSpreadShooting(BaseEntity entity)
    {
        summonerState = SummonerState.SpreadShooting;
        PerformSpreadShoot(entity);
        spreadFireballTimer = 0f;
        summonerState = SummonerState.Kiting;
    }

    private void PerformSpreadShoot(BaseEntity entity)
    {
        Debug.Log("<color=orange>[SummonerBoss]</color> Spread Fireball Released!");
        if (fireballPrefab == null || entity.Target == null) return;
        
        Vector2 dirToTarget = ((Vector2)entity.Target.position - (Vector2)entity.transform.position).normalized;
        float baseAngle = Mathf.Atan2(dirToTarget.y, dirToTarget.x) * Mathf.Rad2Deg;

        float[] angles = { baseAngle - spreadAngle, baseAngle, baseAngle + spreadAngle };
        
        foreach (float ang in angles)
        {
            Vector2 fireDir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
            GameObject fb = Instantiate(fireballPrefab, entity.transform.position, Quaternion.identity);
            activeFireballs.Add(fb);
            
            var tracking = fb.GetComponent<TrackingFireball>();
            if (tracking != null)
            {
                // InitLinear 추가된 메서드 사용 (직선 발사)
                tracking.InitLinear((Vector2)entity.transform.position + fireDir, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, lifeTime);
            }
        }
    }

    // --- 기존 상태 함수들 ---
    private void PrepareNextFireball()
    {
        summonerState = SummonerState.Shooting;
        stationaryTimer = shootStationaryTime;
        fireballTimer = 0f;
    }

    private void ResetCycle(BaseEntity entity)
    {
        cycleTimer = 0f;
        fireballTimer = 0f;
        fireballsShot = 0;
        hasSpawnedInCurrentCycle = false;
        EnterSummonState(entity);
    }

    private void EnterSummonState(BaseEntity entity)
    {
        summonerState = SummonerState.Summoning;
        stationaryTimer = summonStationaryTime;
        hasSpawnedInCurrentCycle = false;
        Debug.Log("<color=magenta>[SummonerBoss]</color> Cycle Start: Summoning...");
    }

    private void PerformActualSummon(BaseEntity entity)
    {
        if (minionToSummon == null) return;
        for (int i = 0; i < summonCount; i++)
        {
            Vector2 spawnPos = (Vector2)entity.transform.position + Random.insideUnitCircle * 2f;
            GameObject minion = GameManager.Instance.dataManager.CreateUnit(minionToSummon, spawnPos);
            if (minion != null) activeMinions.Add(minion);
        }
    }

    private void PerformActualShoot(BaseEntity entity)
    {
        Debug.Log("<color=orange>[SummonerBoss]</color> Fireball Released!");
        fireballsShot++;

        if (fireballPrefab != null)
        {
            GameObject fb = Instantiate(fireballPrefab, entity.transform.position, Quaternion.identity);
            activeFireballs.Add(fb); 
            
            var tracking = fb.GetComponent<TrackingFireball>();
            if (tracking != null)
            {
                tracking.Init(entity.Target, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, lifeTime);
            }
        }
    }

    private void EnterAbsorbState(BaseEntity entity)
    {
        summonerState = SummonerState.Absorbing;
        stationaryTimer = absorbStationaryTime;

        int survivors = 0;
        foreach (var m in activeMinions)
        {
            if (m != null)
            {
                var health = m.GetComponentInChildren<CharacterHealth>();
                if (health != null && !health.IsDead)
                {
                    survivors++;
                    Destroy(m);
                }
            }
        }
        activeMinions.Clear();

        if (survivors > 0)
        {
            float shieldAmount = survivors * shieldPerMinion;
            entity.Stats.Status.AddShield(shieldAmount, 999f);
            Debug.Log($"<color=cyan>[SummonerBoss]</color> Absorbed {survivors} minions. Gained {shieldAmount} Shield!");
        }
    }

    private void HandleKiting(BaseEntity entity)
    {
        float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
        var agent = entity.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent == null || !agent.isActiveAndEnabled) return;

        if (dist < idealDistance) // 거리가 가까우면 전술적 이동
        {
            agent.isStopped = false;
            agent.speed = entity.Stats.MOVESPEED;

            // 이미 이동 중(목표지가 설정됨)이고 목표에 도달하지 않았다면 계속 이동
            if (agent.hasPath && agent.remainingDistance > 0.5f)
            {
                return;
            }

            // 새로운 전술적 위치 탐색 후 이동
            Vector2 tacticalPos = GetTacticalPosition(entity, entity.Target);
            agent.SetDestination(tacticalPos);
        }
        else
        {
            StopNavAgent(entity);
        }
    }
}
