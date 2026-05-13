using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

/// <summary>
/// 소환사 보스의 12초 주기 AI 패턴입니다.
/// 소환 -> 이동/발사 -> 흡수 순서로 동작합니다.
/// </summary>
[CreateAssetMenu(fileName = "SummonerBossPattern", menuName = "Necromancer/AI/SummonerBossPattern")]
public class SummonerBossAIPatternSO : BossAIPatternSO
{
    public enum SummonerState { Summoning, Kiting, Shooting, Absorbing }

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

    [Header("Runtime (Debug)")]
    [SerializeField] private SummonerState summonerState = SummonerState.Summoning;
    [SerializeField] private float cycleTimer;
    [SerializeField] private float fireballTimer;
    [SerializeField] private int fireballsShot;
    [SerializeField] private float stationaryTimer;
    [SerializeField] private bool hasSpawnedInCurrentCycle; 

    private List<GameObject> activeMinions = new List<GameObject>();
    private List<GameObject> activeFireballs = new List<GameObject>(); // [추가] 날아가고 있는 화염구 추적

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        cycleTimer = 0f;
        fireballTimer = 0f;
        fireballsShot = 0;
        activeMinions.Clear();
        activeFireballs.Clear(); // [추가] 초기화
        
        // 시작 시 소환 상태로 진입
        summonerState = SummonerState.Summoning;
        stationaryTimer = summonStationaryTime;
        hasSpawnedInCurrentCycle = false;

        // [추가] 보스 사망 시 미니언 및 화염구도 함께 삭제되도록 이벤트 연결
        entity.Stats.Health.OnDeath -= HandleBossDeath; 
        entity.Stats.Health.OnDeath += HandleBossDeath;
        
        Debug.Log("<color=magenta>[SummonerBoss]</color> Initialized. Waiting for initial summon delay...");
    }

    private void HandleBossDeath()
    {
        Debug.Log("<color=red>[SummonerBoss]</color> Boss Died! Cleaning up everything...");
        CleanupAllMinions(true); 
        CleanupAllFireballs(); // [추가] 화염구 정리
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
            if (fb != null)
            {
                Destroy(fb);
            }
        }
        activeFireballs.Clear();
    }

    public override void Execute(BaseEntity entity)
    {
        UpdatePhase(entity);
        if (currentState == AIState.Thrown) return;

        // 1. 타겟팅 (플레이어 고정)
        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
            if (target == null) return;
        }

        // 2. 정지 시간 처리 (차징, 소환, 흡수 시 가만히 있기)
        if (stationaryTimer > 0)
        {
            stationaryTimer -= Time.deltaTime;
            StopNavAgent(entity);
            
            // 정지 중에도 내부 로직(타이머 등)은 흘러야 함
            UpdateCycleLogic(entity);
            return;
        }

        // 3. 메인 루프 로직
        UpdateCycleLogic(entity);

        // 4. 상태별 이동 로직 (Kiting 상태에서만 이동)
        if (summonerState == SummonerState.Kiting)
        {
            HandleKiting(entity);
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

            case SummonerState.Kiting:
                cycleTimer += Time.deltaTime;
                fireballTimer += Time.deltaTime;

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
            activeFireballs.Add(fb); // [추가] 화염구 추적 리스트에 추가
            
            var tracking = fb.GetComponent<TrackingFireball>();
            if (tracking != null)
            {
                tracking.Init(target, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, lifeTime);
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
        float dist = Vector2.Distance(entity.transform.position, target.position);
        var agent = entity.GetComponent<NavMeshAgent>();
        if (agent == null || !agent.isActiveAndEnabled) return;

        if (dist < idealDistance)
        {
            Vector2 runDir = ((Vector2)entity.transform.position - (Vector2)target.position).normalized;
            Vector2 targetPos = (Vector2)entity.transform.position + runDir * 5f;
            agent.isStopped = false;
            agent.speed = entity.Stats.MOVESPEED;
            agent.SetDestination(targetPos);
        }
        else
        {
            StopNavAgent(entity);
        }
    }
}
