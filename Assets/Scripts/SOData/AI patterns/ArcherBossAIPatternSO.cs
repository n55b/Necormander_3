using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Collections;

[CreateAssetMenu(fileName = "ArcherBossPattern", menuName = "Necromancer/AI/ArcherBossPattern")]
public class ArcherBossAIPatternSO : BossAIPatternSO
{
    public enum ArcherState 
    { 
        P1_Loop, P1_Pattern1, P1_Pattern2, Stunned, 
        P2_Loop, P2_Pattern1, P2_Pattern2,
        Transitioning
    }

    [Header("Phase Settings")]
    public float phase2HpThreshold = 350f;
    public EnemyMinionDataSO phase2Data; // [추가] 2페이즈 전용 스탯 데이터 (이 값이 있으면 체력 0 도달 시 부활하며 전환)
    [SerializeField] private bool isPhase2 = false;

    [Header("Basic Attack Loop")]
    public float baseAttackInterval = 1.0f;
    public float maxAttackSpeedMultiplier = 1.4f;
    public float attackSpeedRampTime = 5.0f; // 8초에서 5초로 단축하여 패턴을 더 자주 보게 함
    public int throwHitsRequired = 2;
    public GameObject normalArrowPrefab;
    public float projectileSpeed = 10f;

    [Header("Pattern 1 (Bombardment)")]
    public GameObject bombardmentIndicatorPrefab; // 경고용 프리팹 (옵션)
    public float bombardmentDuration = 6.0f;
    public float bombardmentInterval = 0.5f;

    [Header("Pattern 2 (Fan Spread)")]
    public float fanWarningTime = 2.0f;
    public int arrowsPerDirection = 3;
    public float spreadAngle = 15f;

    [Header("Stun & Drop")]
    public float stunDuration = 3.0f;
    public GameObject throwableBoxPrefab;

    [Header("Runtime")]
    [SerializeField] private ArcherState archerCurrentState = ArcherState.P1_Loop;
    [SerializeField] private float stateTimer = 0f;
    [SerializeField] private float attackTimer = 0f;
    [SerializeField] private float loopDuration = 0f;
    [SerializeField] private int throwHitCount = 0;
    
    // Pattern 2 internal states
    private int p2SubState = 0; 
    private float p2SubTimer = 0f;

    // P2 Pattern 1 internal timer
    // (Removed unused p2BombardmentTimer)
    
    private Coroutine bombardmentCoroutine;
    [System.NonSerialized] private BaseEntity _cachedEntity;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _cachedEntity = entity;
        isPhase2 = false;
        archerCurrentState = ArcherState.P1_Loop;
        loopDuration = 0f;
        throwHitCount = 0;
        attackTimer = baseAttackInterval;

        // Phase 1 MoveSpeed is 0
        entity.Stats.SetBaseMoveSpeed(0f);

        entity.Stats.Health.OnDamageReceived -= HandleDamageTaken;
        entity.Stats.Health.OnDamageReceived += HandleDamageTaken;

        entity.Stats.Health.OnBeforeDeath -= HandleBeforeDeath;
        entity.Stats.Health.OnBeforeDeath += HandleBeforeDeath;

        // 보스가 처음 등장할 때 무조건 방 중앙으로 강제 텔레포트
        RoomInstance room = GetCurrentRoom(entity);
        if (room != null)
        {
            Vector2 center = (Vector2)room.transform.position + room.centerOffset;
            var agent = entity.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.isActiveAndEnabled)
            {
                agent.Warp(center);
            }
            else
            {
                entity.transform.position = center;
            }
            Debug.Log($"<color=green>[ArcherBoss]</color> Warped to Center: {center}");
        }

        Debug.Log("<color=green>[ArcherBoss]</color> Phase 1 Started.");
    }

    private bool HandleBeforeDeath(CharacterHealth health)
    {
        // 2페이즈 데이터가 설정되어 있고, 1페이즈에서 죽었다면 -> 사망을 취소하고 2페이즈 시작
        if (!isPhase2 && phase2Data != null)
        {
            var entity = health.GetComponentInParent<BaseEntity>();
            if (entity != null)
            {
                EnterPhase2(entity);
                return true; // true를 반환하면 Die()를 건너뛰고 부활함
            }
        }
        return false;
    }

    private void HandleDamageTaken(DamageInfo info)
    {
        if (info.isThrowDamage)
        {
            throwHitCount++;
            Debug.Log($"<color=green>[ArcherBoss]</color> Hit by Throw Attack! Count: {throwHitCount}");
            
            // 1페이즈에서 투척 맞으면 상자 하나 드랍
            if (!isPhase2 && throwableBoxPrefab != null && _cachedEntity != null)
            {
                SpawnBox(info.attacker != null ? info.attacker.transform.position : _cachedEntity.Target.position);
            }
        }
    }

    public override void Execute(BaseEntity entity)
    {
        UpdatePhase(entity); // 타겟 갱신 등
        if (this.archerCurrentState == ArcherState.Transitioning || entity.CurrentState == AIState.Thrown || entity.CurrentState == AIState.Caught) return;

        if (entity.Target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) entity.Target = player.transform;
            if (entity.Target == null) return;
        }

        // 보스가 항상 플레이어를 바라보도록 회전
        entity.LookAtTarget(entity.Target);

        // Phase Transition Check (phase2Data가 없다면 체력 절반 시 강제 전환)
        if (!isPhase2 && phase2Data == null && entity.Stats.Health.CurHP <= entity.Stats.Health.MaxHP * 0.5f)
        {
            EnterPhase2(entity);
            return;
        }

        switch (archerCurrentState)
        {
            case ArcherState.P1_Loop:
                HandleP1Loop(entity);
                break;
            case ArcherState.P1_Pattern1:
                HandlePattern1(entity);
                break;
            case ArcherState.P1_Pattern2:
                HandlePattern2(entity);
                break;
            case ArcherState.Stunned:
                HandleStunned(entity);
                break;
            
            case ArcherState.P2_Loop:
                HandleP2Loop(entity);
                break;
            case ArcherState.P2_Pattern1:
                HandleP2Pattern1(entity);
                break;
            case ArcherState.P2_Pattern2:
                HandleP2Pattern2(entity);
                break;
        }
    }

    private void EnterPhase2(BaseEntity entity)
    {
        isPhase2 = true;

        if (phase2Data != null)
        {
            // 2페이즈 데이터가 있다면 스탯(체력 등)을 완전히 덮어씌움 (내부적으로 ResetHP 호출되어 만피 됨)
            entity.Stats.InitializeStats(phase2Data);
        }
        else
        {
            // 데이터가 없다면 임시로 이속만 5로 변경
            entity.Stats.SetBaseMoveSpeed(5f); 
        }
        throwHitCount = 0;
        loopDuration = 0f;
        archerCurrentState = ArcherState.P2_Loop;
        if (bombardmentCoroutine != null) entity.StopCoroutine(bombardmentCoroutine);
        
        Debug.Log("<color=red>[ArcherBoss]</color> Phase 2 Started!");
    }

    // ==========================================
    // Phase 1 Logic
    // ==========================================
    private void HandleP1Loop(BaseEntity entity)
    {
        loopDuration += Time.deltaTime;
        
        // 공속 계산 (최대 1.4까지 서서히 증가)
        float speedMult = 1.0f + Mathf.Min(loopDuration, attackSpeedRampTime) / attackSpeedRampTime * (maxAttackSpeedMultiplier - 1.0f);
        attackTimer -= Time.deltaTime * speedMult;

        if (attackTimer <= 0f)
        {
            ShootNormalArrow(entity, entity.Target.position);
            attackTimer = baseAttackInterval;
        }

        // 패턴 전환 조건 검사
        if (loopDuration >= attackSpeedRampTime || throwHitCount >= throwHitsRequired)
        {
            throwHitCount = 0;
            if (Random.value < 0.5f) EnterP1Pattern1(entity);
            else EnterP1Pattern2(entity);
        }
    }

    private void EnterP1Pattern1(BaseEntity entity)
    {
        archerCurrentState = ArcherState.P1_Pattern1;
        stateTimer = bombardmentDuration;
        bombardmentCoroutine = entity.StartCoroutine(BombardmentRoutine(entity));
        Debug.Log("<color=green>[ArcherBoss]</color> Phase 1 Pattern 1: Bombardment!");
    }

    private void HandlePattern1(BaseEntity entity)
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            EnterStunned(entity, true); // 패턴 1 종료 후 상자 드랍 기절
        }
    }

    private void EnterP1Pattern2(BaseEntity entity)
    {
        archerCurrentState = ArcherState.P1_Pattern2;
        p2SubState = 0; // 0: 십자경고1, 1: 대각경고, 2: 십자경고2
        p2SubTimer = fanWarningTime;
        Debug.Log("<color=green>[ArcherBoss]</color> Phase 1 Pattern 2: Cross/Diag/Cross!");
    }

    private void HandlePattern2(BaseEntity entity)
    {
        p2SubTimer -= Time.deltaTime;
        if (p2SubTimer <= 0f)
        {
            if (p2SubState == 0)
            {
                ShootFan(entity, 0f); // 십자 (+형태)
                p2SubState = 1;
                p2SubTimer = fanWarningTime;
            }
            else if (p2SubState == 1)
            {
                ShootFan(entity, 45f); // 대각선 (X형태)
                p2SubState = 2;
                p2SubTimer = fanWarningTime;
            }
            else if (p2SubState == 2)
            {
                ShootFan(entity, 0f); // 십자 (+형태)
                EnterStunned(entity, true);
            }
        }
    }

    private void EnterStunned(BaseEntity entity, bool dropBox)
    {
        archerCurrentState = ArcherState.Stunned;
        stateTimer = stunDuration;

        if (entity.Stats != null && entity.Stats.Status != null)
        {
            entity.Stats.Status.ApplyStatus(StatusType.Stun, stunDuration);
        }

        if (bombardmentCoroutine != null)
        {
            entity.StopCoroutine(bombardmentCoroutine);
            bombardmentCoroutine = null;
        }
        
        if (dropBox && throwableBoxPrefab != null)
        {
            // 상자 여러 개 드랍 (예: 3개)
            for (int i = 0; i < 3; i++)
            {
                Vector2 spawnPos = (Vector2)entity.transform.position + Random.insideUnitCircle * 3f;
                SpawnBox(spawnPos);
            }
        }
        Debug.Log($"<color=green>[ArcherBoss]</color> Stunned for {stunDuration}s. DropBox: {dropBox}");
    }

    private void HandleStunned(BaseEntity entity)
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            // 초기화 후 루프 복귀
            loopDuration = 0f;
            attackTimer = baseAttackInterval;
            throwHitCount = 0;
            archerCurrentState = isPhase2 ? ArcherState.P2_Loop : ArcherState.P1_Loop;
        }
    }

    // ==========================================
    // Phase 2 Logic
    // ==========================================
    private void HandleP2Loop(BaseEntity entity)
    {
        loopDuration += Time.deltaTime;
        
        float speedMult = 1.0f + Mathf.Min(loopDuration, attackSpeedRampTime) / attackSpeedRampTime * (maxAttackSpeedMultiplier - 1.0f);
        attackTimer -= Time.deltaTime * speedMult;

        if (attackTimer <= 0f)
        {
            ShootNormalArrow(entity, entity.Target.position);
            attackTimer = baseAttackInterval;
        }

        // 거리 유지 (카이팅)
        KitePlayer(entity);

        if (loopDuration >= attackSpeedRampTime || throwHitCount >= throwHitsRequired)
        {
            throwHitCount = 0;
            StopNavAgent(entity);
            if (Random.value < 0.5f) EnterP2Pattern1(entity);
            else EnterP2Pattern2(entity);
        }
    }

    private void EnterP2Pattern1(BaseEntity entity)
    {
        archerCurrentState = ArcherState.P2_Pattern1;
        stateTimer = bombardmentDuration;
        bombardmentCoroutine = entity.StartCoroutine(BombardmentRoutine(entity));
        attackTimer = baseAttackInterval; // 공속 1.0 고정
        Debug.Log("<color=red>[ArcherBoss]</color> Phase 2 Pattern 1: Parallel Bombardment + Kiting!");
    }

    private void HandleP2Pattern1(BaseEntity entity)
    {
        stateTimer -= Time.deltaTime;
        
        // 거리 유지 기본 사격 (공속 1.0 고정)
        attackTimer -= Time.deltaTime * 1.0f;
        if (attackTimer <= 0f)
        {
            ShootNormalArrow(entity, entity.Target.position);
            attackTimer = baseAttackInterval;
        }
        KitePlayer(entity);

        if (stateTimer <= 0f)
        {
            StopNavAgent(entity);
            EnterStunned(entity, false); // 상자 드롭 없이 기절
        }
    }

    private void EnterP2Pattern2(BaseEntity entity)
    {
        archerCurrentState = ArcherState.P2_Pattern2;
        p2SubState = -1; // -1: 중앙 이동 중
        
        RoomInstance room = GetCurrentRoom(entity);
        if (room != null)
        {
            Vector2 center = (Vector2)room.transform.position + room.centerOffset;
            var agent = entity.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.isStopped = false;
                agent.speed = entity.Stats.MOVESPEED * 1.5f; // 중앙으로 빠르게 이동
                agent.SetDestination(center);
            }
        }
        else
        {
            p2SubState = 0; // 방을 못 찾으면 즉시 시작
            p2SubTimer = fanWarningTime;
        }
        Debug.Log("<color=red>[ArcherBoss]</color> Phase 2 Pattern 2: Move to Center -> Cross/Diag/Cross!");
    }

    private void HandleP2Pattern2(BaseEntity entity)
    {
        if (p2SubState == -1)
        {
            RoomInstance room = GetCurrentRoom(entity);
            Vector2 center = room != null ? (Vector2)room.transform.position + room.centerOffset : (Vector2)entity.transform.position;
            
            if (Vector2.Distance(entity.transform.position, center) < 1.0f)
            {
                StopNavAgent(entity);
                p2SubState = 0;
                p2SubTimer = fanWarningTime;
            }
            return;
        }

        p2SubTimer -= Time.deltaTime;
        if (p2SubTimer <= 0f)
        {
            if (p2SubState == 0)
            {
                ShootFan(entity, 0f);
                p2SubState = 1;
                p2SubTimer = fanWarningTime;
            }
            else if (p2SubState == 1)
            {
                ShootFan(entity, 45f);
                p2SubState = 2;
                p2SubTimer = fanWarningTime;
            }
            else if (p2SubState == 2)
            {
                ShootFan(entity, 0f);
                EnterStunned(entity, false); // 2페이즈는 상자 없음
            }
        }
    }

    // ==========================================
    // Helper Methods
    // ==========================================

    private void KitePlayer(BaseEntity entity)
    {
        float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
        var agent = entity.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent == null || !agent.isActiveAndEnabled) return;

        if (dist < 12f) // 거리가 가까우면 전술적 이동 (방 안의 안전한 곳으로)
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

    private void ShootNormalArrow(BaseEntity entity, Vector2 targetPos)
    {
        if (normalArrowPrefab == null) return;
        GameObject arrow = Instantiate(normalArrowPrefab, entity.transform.position, Quaternion.identity);
        var proj = arrow.GetComponentInChildren<Projectile>();
        if (proj != null)
        {
            proj.Init(targetPos, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, 7f);
        }
    }

    private void ShootFan(BaseEntity entity, float baseAngleOffset)
    {
        if (normalArrowPrefab == null) return;
        
        float[] directions = { 0f, 90f, 180f, 270f };
        
        foreach (float dir in directions)
        {
            float centerAngle = dir + baseAngleOffset;
            float[] angles = { centerAngle - spreadAngle, centerAngle, centerAngle + spreadAngle };
            
            foreach (float ang in angles)
            {
                Vector2 fireDir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
                GameObject arrow = Instantiate(normalArrowPrefab, entity.transform.position, Quaternion.identity);
                var proj = arrow.GetComponentInChildren<Projectile>();
                if (proj != null)
                {
                    proj.Init((Vector2)entity.transform.position + fireDir, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, 7f);
                }
            }
        }
    }

    private IEnumerator BombardmentRoutine(BaseEntity entity)
    {
        RoomInstance room = GetCurrentRoom(entity);
        Vector2 center = room != null ? (Vector2)room.transform.position + room.centerOffset : (Vector2)entity.transform.position;
        float halfWidth = room != null ? room.roomSize.x / 2f - 1f : 5f;
        float halfHeight = room != null ? room.roomSize.y / 2f - 1f : 5f;

        while (true)
        {
            Vector2 randomPos = center + new Vector2(Random.Range(-halfWidth, halfWidth), Random.Range(-halfHeight, halfHeight));
            
            // 경고 이펙트 및 실제 폭발 데미지를 MagicianCircle 프리팹에 위임
            if (bombardmentIndicatorPrefab != null)
            {
                GameObject indicator = Instantiate(bombardmentIndicatorPrefab, randomPos, Quaternion.identity);
                var circleAttack = indicator.GetComponent<EnemyMagicianCircleAttack>();
                var telegraph = indicator.GetComponent<TelegraphHitbox>();
                var baseHitBox = indicator.GetComponent<BaseHitBox>();
                
                if (circleAttack != null)
                {
                    // 마법사 장판 스크립트를 재활용: 폭발 데미지, 타겟 레이어, 폭발 대기시간(0.5초), 반경(1.5f) 지정
                    circleAttack.Init(entity.Stats.ATK, entity.opponentLayer, entity.gameObject, 1.5f, 0.5f);
                }
                else if (telegraph != null)
                {
                    DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, false, 1f, false, "Archer Bombardment");
                    telegraph.Init(0.5f, info, entity.opponentLayer, new Vector2(3f, 3f)); // 반경 1.5면 지름 3.0
                }
                else if (baseHitBox != null)
                {
                    DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, false, 1f, false, "Archer Bombardment");
                    indicator.transform.localScale = new Vector3(3f, 3f, 1f);
                    baseHitBox.Init(info, entity.opponentLayer, 0.1f, 0.5f, false, null);
                }
                
                // 폭격 주기만큼 대기 후 다음 폭격
                yield return new WaitForSeconds(bombardmentInterval);
            }
            else
            {
                yield return new WaitForSeconds(bombardmentInterval);
                // 폭격 데미지 임시 땜빵용 (프리팹 없을 때만)
                ShootNormalArrow(entity, randomPos); 
            }
        }
    }

    private void SpawnBox(Vector2 position)
    {
        if (throwableBoxPrefab != null)
        {
            Instantiate(throwableBoxPrefab, position, Quaternion.identity);
        }
    }
}


