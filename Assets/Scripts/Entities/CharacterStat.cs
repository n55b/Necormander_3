using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 유닛의 모든 주요 스탯 컴포넌트들을 한곳에 모아주는 허브 클래스입니다.
/// 외부에서는 이 클래스를 통해 Health, Status, Visual 컴포넌트에 직접 접근합니다.
/// </summary>
[RequireComponent(typeof(CharacterStatus), typeof(CharacterHealth), typeof(CharacterVisualFeedback))]
public class CharacterStat : MonoBehaviour
{
    [Header("캐릭터 기본 스탯 데이터")]
    [SerializeField] private float baseMaxHP = 100f;
    [SerializeField] private float baseAtk = 10f;
    [SerializeField] private float baseAtkSpd = 1f;
    [SerializeField] private float baseAtkRange = 2f;
    [SerializeField] private float baseDef = 0f;
    [SerializeField] private float baseMoveSpeed = 5f;
    [SerializeField] private float baseEvasion = 0f;
    [SerializeField] private float baseMissChance = 0f;

    // 하위 컴포넌트 직접 노출 (Read-only Accessors)
    public CharacterStatus Status { get; private set; }
    public CharacterHealth Health { get; private set; }
    public CharacterVisualFeedback Visual { get; private set; }

    [Header("런타임 정보")]
    public CommandData jobType; // 보석 계산을 위해 필요
    private bool _isAlly = false; // [추가] 아군 여부 캐싱
    private bool _isPlayer = false; // [추가] 플레이어 여부 캐싱

    public bool IsEnemy => !_isAlly && !_isPlayer; // [추가] 적군 여부 식별

    private float ShieldbearerSelfMult
    {
        get
        {
            if (jobType != CommandData.SkeletonShieldbearer) return 1f;
            var inven = InventoryManager.Instance;
            if (inven == null) return 1f;

            float mult = 1f;
            if (inven.HasUniqueEffect(GemUniqueType.ShieldsWillCourage)) mult += 0.1f;
            if (inven.HasUniqueEffect(GemUniqueType.ShieldsWillWind)) mult += 0.1f;
            if (inven.HasUniqueEffect(GemUniqueType.ShieldsWillClash)) mult += 0.1f;
            
            if (Status != null && Status.TotalShield > 0)
            {
                int guardianLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Shield_Guardian));
                if (guardianLevel >= 4) // (8) 스택
                {
                    mult += 0.15f;
                }
            }
            return mult;
        }
    }

    [Header("셋팅 이후 Action들")]
    [SerializeField] private UnityEvent setDoneActions;

    private bool _isInitialized = false;

    // --- 외부 참조용 단축 프로퍼티 (데이터 중심 + 보석 보너스 합산) ---

    // 공격력: (기본 공격력) * (1 + 보석 배율 + 보물 배율) * 노화 감소
    public float ATK
    {
        get
        {
            float agingReduction = (Status != null) ? GemRuleSystem.GetAgingSlowReduction(Status.GetDebuffStack(DebuffStackType.Aging), IsEnemy) : 0f;
            float agingMult = Mathf.Max(0.1f, 1f - agingReduction);

            // [유니크] 무기 부식 (WeaponCorrosion): 부식된 적 공격력 10% 감소
            float corrosionWeaponMult = 1f;
            if (IsEnemy && Status != null && Status.GetDebuffBool(DebuffBoolType.Corroded))
            {
                var inven = InventoryManager.Instance;
                if (inven != null && inven.HasUniqueEffect(GemUniqueType.WeaponCorrosion))
                {
                    corrosionWeaponMult = 0.9f;
                }
            }

            // 플레이어는 보석/보물(미니언용) 보너스를 받지 않음
            float bonusMult = _isPlayer ? 0f : (GetGemBonus(StatType.Attack) + GetTreasureBonus(TreasureEffectType.GlobalMinionStats));
            
            float uniqueArcherMult = 1f;
            if (!IsEnemy && jobType == CommandData.SkeletonArcher)
            {
                var inven = InventoryManager.Instance;
                if (inven != null)
                {
                    // [유니크] 발여호미 (TensionPower): 공격력 25% 증가
                    if (inven.HasUniqueEffect(GemUniqueType.TensionPower)) uniqueArcherMult *= 1.25f;

                    // [유니크] 반구저기 (ReflectingNature): 발이부중 필수. 체력 50% 미만 시 공격력 15% 증가
                    if (inven.HasUniqueEffect(GemUniqueType.ReflectingNature) && inven.HasUniqueEffect(GemUniqueType.UnseenMiss))
                    {
                        if (Health != null && Health.CurHP < MAXHP * 0.5f) uniqueArcherMult *= 1.15f;
                    }
                }
            }

            float uniqueSpearmanMult = 1f;
            if (!IsEnemy && jobType == CommandData.SkeletonSpearman)
            {
                var inven = InventoryManager.Instance;
                if (inven != null)
                {
                    if (inven.HasUniqueEffect(GemUniqueType.ThousandStabs)) uniqueSpearmanMult *= 1.03f;
                }
            }
            
            float uniqueWarriorMult = 1f;
            if (!IsEnemy && jobType == CommandData.SkeletonWarrior)
            {
                var inven = InventoryManager.Instance;
                if (inven != null)
                {
                    if (inven.HasUniqueEffect(GemUniqueType.WarriorsMedal)) uniqueWarriorMult *= 1.15f;
                }
            }

            float allyWillClashMult = 1f;
            if (_isAlly && ShieldbearerUniqueManager.IsWillClashActive) allyWillClashMult += 0.08f;

            return (baseAtk * (1f + bonusMult) * agingMult * corrosionWeaponMult * uniqueArcherMult * uniqueSpearmanMult * uniqueWarriorMult) * ShieldbearerSelfMult * allyWillClashMult;
        }
    }


    // 최대 체력: (기본 체력 + 보석 고정치) * (1 + 보물 배율)
    public float MAXHP 
    {
        get
        {
            float gemFlatBonus = _isPlayer ? 0f : GetGemBonus(StatType.Health);
            float treasureMult = _isPlayer ? 0f : GetTreasureBonus(TreasureEffectType.GlobalMinionStats);
            float uniqueWarriorMult = 1f;
            if (!IsEnemy && jobType == CommandData.SkeletonWarrior && InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.WarriorsMedal))
            {
                uniqueWarriorMult *= 1.15f;
            }
            return (baseMaxHP + gemFlatBonus) * (1f + treasureMult) * ShieldbearerSelfMult * uniqueWarriorMult;
        }
    }

    public float CURHP => (Health != null) ? Health.CurHP : MAXHP;

    // 공격 속도: (기본 주기 / 보너스) / 한기 감소 -> 주기가 길어질수록 느려짐
    public float ATKSPD
    {
        get
        {
            float chillReduction = (Status != null) ? GemRuleSystem.GetChillSlowReduction(Status.GetDebuffStack(DebuffStackType.Chill), IsEnemy) : 0f;
            float chillMult = Mathf.Max(0.1f, 1f - chillReduction);
            
            float bonusMult = _isPlayer ? 0f : GetGemBonus(StatType.AttackSpeed);

            // [유니크] 노화 사냥꾼 (AgingHunter): 방 전체 적의 노화 스택 100당 10% 증가
            if ((_isPlayer || _isAlly) && InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.AgingHunter))
            {
                float totalAgingStacks = 0f;
                foreach (var enemyStatus in CharacterStatus.ActiveEnemies)
                {
                    if (enemyStatus != null) totalAgingStacks += enemyStatus.GetDebuffStack(DebuffStackType.Aging);
                }
                bonusMult += (totalAgingStacks / 100f) * 0.1f;
            }

            float uniqueArcherDivisor = 1f;
            if (!IsEnemy && jobType == CommandData.SkeletonArcher)
            {
                var inven = InventoryManager.Instance;
                if (inven != null)
                {
                    // [유니크] 발여호미 (TensionPower): 기본 공격 빈도 25% 느려짐 (주기 * 1.25)
                    if (inven.HasUniqueEffect(GemUniqueType.TensionPower)) uniqueArcherDivisor *= 1.25f;

                    // [유니크] 반구저기 (ReflectingNature): 발이부중 필수. 체력 50% 이상 시 공격속도 15% 증가 (주기 감소)
                    if (inven.HasUniqueEffect(GemUniqueType.ReflectingNature) && inven.HasUniqueEffect(GemUniqueType.UnseenMiss))
                    {
                        if (Health != null && Health.CurHP >= MAXHP * 0.5f) uniqueArcherDivisor *= 0.85f;
                    }
                }
            }

            float allyWillCourageDivisor = 1f;
            if (_isAlly && ShieldbearerUniqueManager.IsWillCourageActive) allyWillCourageDivisor = 1f / 1.12f;
            
            float uniqueWarriorDivisor = 1f;
            if (!IsEnemy && jobType == CommandData.SkeletonWarrior && InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.WarriorsMedal))
            {
                uniqueWarriorDivisor = 1f / 1.15f;
            }

            float selfMultDivisor = 1f / ShieldbearerSelfMult;

            return (baseAtkSpd * uniqueArcherDivisor * allyWillCourageDivisor * uniqueWarriorDivisor * selfMultDivisor / (1f + bonusMult)) / chillMult;
        }
    }

    public float ATKRANGE => baseAtkRange;
    public float DEF 
    {
        get
        {
            float uniqueWarriorMult = 1f;
            if (!IsEnemy && jobType == CommandData.SkeletonWarrior && InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.WarriorsMedal))
            {
                uniqueWarriorMult = 1.15f;
            }
            return baseDef * uniqueWarriorMult;
        }
    }
    public float EVASION => baseEvasion;

    public float MISS_CHANCE
    {
        get
        {
            float chance = baseMissChance;
            // [유니크] 침침한 시야 (DimVision): 노화 스택 50 이상 시 25% 미스 확률 증가
            if (IsEnemy && Status != null && Status.GetDebuffStack(DebuffStackType.Aging) >= 50)
            {
                if (InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.DimVision))
                {
                    chance += 0.25f;
                }
            }
            return chance;
        }
    }

    // 이동 속도: 기본 속도 * 상태이상 배율 * (한기+노화 감소)
    public float MOVESPEED
    {
        get
        {
            if (Status == null) return baseMoveSpeed;
            if (Status.GetDebuffBool(DebuffBoolType.Frozen) || Status.GetDebuffBool(DebuffBoolType.Stunned)) return 0f;

            float chillReduction = GemRuleSystem.GetChillSlowReduction(Status.GetDebuffStack(DebuffStackType.Chill), IsEnemy);
            
            // [유니크] 냉혹한 사냥꾼 (ColdBloodedHunter) - 한기 걸린 적 이속 10% 추가 감소
            if (IsEnemy && Status.GetDebuffStack(DebuffStackType.Chill) > 0f)
            {
                if (InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.ColdBloodedHunter))
                {
                    chillReduction += 0.1f; // 10% 추가 감소
                }
            }

            float agingReduction = GemRuleSystem.GetAgingSlowReduction(Status.GetDebuffStack(DebuffStackType.Aging), IsEnemy);

            // [유니크] 고려장 (Goryeojang): 노화 최고스택 적 2.0f 반경 이내 시 둔화
            if (IsEnemy && AgingUniqueManager.HighestAgingEnemy != null)
            {
                float dist = Vector2.Distance(transform.position, AgingUniqueManager.HighestAgingEnemy.transform.position);
                if (dist <= 2.0f)
                {
                    agingReduction += GemRuleSystem.GetGoryeojangSlowReduction();
                }
            }

            float reductionMult = Mathf.Max(0.1f, 1f - (chillReduction + agingReduction));
            
            float finalSpeed = (baseMoveSpeed * Status.MoveSpeedMultiplier) * reductionMult;
            
            // [유니크] 부식석 발자취 (PoisonFootprint) - 아군 이동속도 15% 증가
            if (!IsEnemy && InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.PoisonFootprint))
            {
                finalSpeed *= 1.15f;
            }
            
            float allyWillWindMult = 1f;
            if (_isAlly && ShieldbearerUniqueManager.IsWillWindActive) allyWillWindMult += 0.14f;
            
            float uniqueWarriorMult = 1f;
            if (!IsEnemy && jobType == CommandData.SkeletonWarrior && InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.WarriorsMedal))
            {
                uniqueWarriorMult = 1.15f;
            }

            return finalSpeed * ShieldbearerSelfMult * allyWillWindMult * uniqueWarriorMult;
        }
    }

    // 부활 시간 보너스 (필요 시 외부에서 참조)
    public float RESPAWN_BONUS => _isPlayer ? 0f : GetGemBonus(StatType.RespawnTime);

    public bool IsDead => Health != null && Health.IsDead;

    private void OnEnable()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnGemTreeUpdated += RefreshFinalStats;
        }
    }

    private void OnDisable()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnGemTreeUpdated -= RefreshFinalStats;
        }
    }

    /// <summary>
    /// 보석 트리 업데이트 등 전역 스탯 변화가 생겼을 때 호출되어 하위 컴포넌트들을 갱신합니다.
    /// </summary>
    private void RefreshFinalStats()
    {
        if (!_isInitialized) return;

        // 플레이어는 보석 트리의 영향을 받지 않으므로 갱신 스킵 (필요 시 주석 해제)
        // if (_isPlayer) return;

        // 최대 체력 변화를 Health 담당자에게 알림 (체력바 UI 갱신 등)
        if (Health != null)
        {
            Health.ResetHP(); // 또는 현재 체력 비율을 유지하며 최대 체력만 변경하는 로직 필요 시 추가
        }

        // Debug.Log($"<color=white>[Stat]</color> {gameObject.name} stats refreshed by Gem Tree update.");
    }

    private float GetGemBonus(StatType type)
    {
        if (InventoryManager.Instance == null || !_isAlly || _isPlayer) return 0f;
        return InventoryManager.Instance.GetAggregatedGemBonus(jobType, type); // [수정] jobType 전달
    }

    private float GetTreasureBonus(TreasureEffectType type)
    {
        if (InventoryManager.Instance == null || !_isAlly || _isPlayer) return 0f;
        return InventoryManager.Instance.GetTreasureBonus(type);
    }

    private void UpdateTeamStatus()
    {
        // 1. BaseEntity가 있다면 팀 확인 (미니언인 경우)
        var entity = GetComponentInParent<BaseEntity>();
        if (entity != null)
        {
            _isAlly = (entity.team == Team.Ally);
            _isPlayer = false;
            return;
        }

        // 2. PlayerController가 있다면 플레이어
        if (GetComponentInParent<PlayerController>() != null)
        {
            _isAlly = true;
            _isPlayer = true;
            return;
        }

        // 3. 태그 및 레이어 기반 보조 확인
        _isPlayer = CompareTag("Player") || gameObject.layer == LayerMask.NameToLayer("Player");
        _isAlly = _isPlayer || CompareTag("Army") || gameObject.layer == LayerMask.NameToLayer("Army");
    }

    // [중앙집집중형 초기화]
    public void Setup()
    {
        if (_isInitialized)
        {
            UpdateTeamStatus(); // 이미 초기화되었더라도 팀 정보는 갱신할 수 있음
            return;
        }

        Status = GetComponent<CharacterStatus>();
        Health = GetComponent<CharacterHealth>();
        Visual = GetComponent<CharacterVisualFeedback>();

        UpdateTeamStatus(); // Init 전에 IsEnemy 설정

        if (Status != null) Status.Init(this); // [추가] Status에 IsEnemy 참조 전달
        if (Visual != null) Visual.Init(Health, Status);
        if (Health != null) Health.Init(this, Status);

        // 셋업이 완전히 끝난 후 액션 실행
        setDoneActions.Invoke();

        // 만약 Spawner가 같은 오브젝트에 있다면 직접 초기화 함수를 호출해버리는 게 제일 안전합니다.
        var spawner = GetComponent<FloatingTextSpawner>();
        if (spawner != null)
        {
            spawner.Initialize(this); // 직접 만든 초기화 함수 호출
        }
        
        _isInitialized = true;
    }

    /// <summary>
    /// 데이터(SO)로부터 수치를 주입받고 각 컴포넌트를 초기화합니다.
    /// </summary>
    public void InitializeStats(MinionDataSO data)
    {
        Setup();

        if (data != null)
        {
            jobType = data.minionType; // 직업 정보 캐싱 (보석 계산용)

            baseMaxHP = data.maxHP;
            baseAtk = data.attack;
            baseAtkSpd = data.attackSpeed; // 공격속도는 간격(주기)이므로 작아질수록 좋음
            baseAtkRange = data.attackRange;
            baseDef = data.defense;
            baseMoveSpeed = data.moveSpeed;
            baseEvasion = data.baseEvasion;
            baseMissChance = data.baseMissChance;

            // [추가] 보스 여부 전달
            if (Status != null) Status.IsElite = data.isElite;
        }

        UpdateTeamStatus(); // 데이터 주입 시점에 팀 정보 다시 확인

        if (Health != null) Health.ResetHP();
        if (Status != null) Status.ClearStatus();
        if (Visual != null) Visual.ResetVisuals();
    }

    /// <summary>
    /// AI 패턴 등에서 동적으로 기본 이동 속도를 변경할 때 사용합니다.
    /// </summary>
    public void SetBaseMoveSpeed(float speed)
    {
        baseMoveSpeed = speed;
    }

    /// <summary>
    /// [신규] UI(예: 핸드슬롯 툴팁)에서 보석 효과가 반영된 최종 예상 스탯을 미리 계산하여 반환합니다.
    /// </summary>
    public static (float hp, float atk, float spd) GetPreviewStats(MinionDataSO data)
    {
        float hp = data.maxHP;
        float atk = data.attack;
        float spd = data.moveSpeed;

        var inven = InventoryManager.Instance;
        if (inven != null)
        {
            float gemHp = inven.GetAggregatedGemBonus(data.minionType, StatType.Health);
            float gemAtk = inven.GetAggregatedGemBonus(data.minionType, StatType.Attack);
            float treasureHp = inven.GetTreasureBonus(TreasureEffectType.GlobalMinionStats);

            hp = (hp + gemHp) * (1f + treasureHp);
            atk = atk * (1f + gemAtk);

            if (data.minionType == CommandData.SkeletonWarrior && inven.HasUniqueEffect(GemUniqueType.WarriorsMedal))
            {
                hp *= 1.15f;
                atk *= 1.15f;
                spd *= 1.15f;
            }

            if (data.minionType == CommandData.SkeletonArcher)
            {
                if (inven.HasUniqueEffect(GemUniqueType.TensionPower)) atk *= 1.25f;
            }

            if (data.minionType == CommandData.SkeletonSpearman)
            {
                if (inven.HasUniqueEffect(GemUniqueType.ThousandStabs)) atk *= 1.03f;
            }
        }

        return (hp, atk, spd);
    }

    /// <summary>
    /// 분신 소환 등 특수한 경우에 스탯을 절반으로 깎는 로직
    /// </summary>
    public void ApplySplitStats()
    {
        baseMaxHP *= 0.5f;
        baseAtk *= 0.5f;
        if (Health != null) Health.ResetHP();
    }
}
