using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 유닛의 모든 주요 스탯 컴포넌트들을 한곳에 모아주는 허브 클래스입니다.
/// 외부에서는 이 클래스를 통해 Health, Status, Visual 컴포넌트에 직접 접근합니다.
///
/// [26/07/17 스탯 재설계]
///  - 공용 스탯(플레이어+적) / 플레이어 전용 / 적 전용으로 나눴다.
///  - 소환수는 필드에 존재하지 않으므로 CharacterStat 을 갖지 않는다. 소환수 피해는
///    플레이어의 ATK/MAGIC 을 그대로 가져다 배율을 곱한다(Phase 4).
///  - 런타임 보정은 전부 Mods(StatModifierRegistry) 로 간다. 구 StatEventBus 는 삭제.
/// </summary>
[RequireComponent(typeof(CharacterStatus), typeof(CharacterHealth), typeof(CharacterVisualFeedback))]
public class CharacterStat : MonoBehaviour
{
    [Header("공용 스탯 (플레이어 + 적)")]
    [SerializeField] private float baseMaxHP = 100f;
    [Tooltip("물리 공격력. 플레이어 주먹과 소환수 피해가 공유하는 베이스값이다.")]
    [SerializeField] private float baseAtk = 10f;
    [Tooltip("마법 공격력. 마법 스킬이 이 값에 계수를 곱한다. 무투파라 기본은 0.")]
    [SerializeField] private float baseMagic = 0f;
    [Tooltip("공격 속도(회/초). 높을수록 빠르다. 실제 공격 간격이 필요하면 AttackInterval 을 쓸 것.")]
    [SerializeField] private float baseAtkSpd = 1f;
    [Tooltip("방어력(%). 그 자체가 감소율이다. 20 = 20% 감소. 게터에서 75% 로 잘린다.")]
    [SerializeField] private float baseDef = 0f;
    [SerializeField] private float baseMoveSpeed = 5f;
    [Tooltip("치명타 확률(%). 0 = 안 터짐.")]
    [SerializeField] private float baseCritChance = 0f;
    [Tooltip("치명타 피해량(%). 150 = 1.5배.")]
    [SerializeField] private float baseCritDamage = 150f;
    [Tooltip("회피율(0~1). 기본 0 = 절대 회피 안 함.")]
    [Range(0f, 1f)][SerializeField] private float baseEvasion = 0f;
    [Tooltip("적중률(0~1). 기본 1 = 무조건 맞힘. 지금은 이 두 값을 건드리는 게 없다 — 나중을 위한 자리.")]
    [Range(0f, 1f)][SerializeField] private float baseAccuracy = 1f;

    [Header("적 전용")]
    [Tooltip("공격 사거리. 플레이어는 안 쓴다(평타 히트박스가 따로 있음).")]
    [SerializeField] private float baseAtkRange = 2f;
    [Tooltip("이 유닛의 공격이 물리인지 마법인지. 적은 magic 스탯 없이 atk 으로 계산하고 태그만 단다.")]
    [SerializeField] private AttackType baseAttackType = AttackType.Physical;

    [Header("플레이어 전용")]
    [Tooltip("Q/E/R 스킬 쿨타임 감소율(%). 합연산, 상한 없음. 100 이면 이론상 즉시 재사용.")]
    [SerializeField] private float baseSkillCdr = 0f;
    [Tooltip("대쉬 쿨타임 감소율(%). 스킬 쿨감과 별개다.")]
    [SerializeField] private float baseDashCdr = 0f;
    [Tooltip("평타 1·2타 배율. 기본 1.0 = ATK 그대로. 3타(소환수 마무리)는 소환수 배율을 쓴다.")]
    [SerializeField] private float baseBasicAtkMult = 1f;

    // 하위 컴포넌트 직접 노출 (Read-only Accessors)
    public CharacterStatus Status { get; private set; }
    public CharacterHealth Health { get; private set; }
    public CharacterVisualFeedback Visual { get; private set; }

    /// <summary>런타임 스탯 보정치(유물/아이템/서브 소환수). 여기에 걸면 아래 게터들이 알아서 반영한다.</summary>
    public StatModifierRegistry Mods { get; } = new StatModifierRegistry();

    private bool _isAlly = false;  // [추가] 아군 여부 캐싱
    private bool _isPlayer = false; // [추가] 플레이어 여부 캐싱

    public bool IsEnemy => !_isAlly && !_isPlayer; // [추가] 적군 여부 식별
    public bool IsPlayer => _isPlayer;

    // 서브 소환수 패시브는 플레이어에게만 붙는다. 매번 GetComponent 하지 않도록 캐싱.
    private SubSummonPassiveController _subPassive;
    private SubSummonPassiveController SubPassive
    {
        get
        {
            if (!_isPlayer) return null;
            if (_subPassive == null) _subPassive = GetComponentInParent<SubSummonPassiveController>();
            return _subPassive;
        }
    }

    [Header("셋팅 이후 Action들")]
    [SerializeField] private UnityEvent setDoneActions;

    private bool _isInitialized = false;

    /// <summary>방어력 감소율 상한. 유물이 겹쳐도 여기서 잘린다.</summary>
    public const float MAX_DEF_PERCENT = 75f;

    // ── 공용 스탯 ─────────────────────────────────────────────────────

    /// <summary>물리 공격력. 플레이어 주먹 + 소환수 피해의 공통 베이스.</summary>
    public float ATK
    {
        get
        {
            float bonus = GetTreasureBonus(TreasureEffectType.GlobalMinionStats)
                        + Mods.Percent(StatType.Attack);
            return (baseAtk + Mods.Flat(StatType.Attack)) * (1f + bonus);
        }
    }

    /// <summary>마법 공격력. 마법 스킬 전용.</summary>
    public float MAGIC => (baseMagic + Mods.Flat(StatType.Magic)) * (1f + Mods.Percent(StatType.Magic));

    /// <summary>물리 피해 증폭률. 데미지 계산에서 (1 + 이 값)이 곱해진다. 0.1 = +10%. 유물로만 오른다.
    /// -1 로 바닥을 깔아 (1+증폭)이 음수로 뒤집히지 않게 한다.</summary>
    public float PHYS_AMP => Mathf.Max(-1f, Mods.Flat(StatType.PhysDamageAmp));

    /// <summary>마법 피해 증폭률. 물리와 동일한 규칙.</summary>
    public float MAGIC_AMP => Mathf.Max(-1f, Mods.Flat(StatType.MagicDamageAmp));

    public float MAXHP
    {
        get
        {
            // 증강 보상(최대 체력 +N)은 InventoryManager 가 런 내내 들고 있고 여기서 매번 읽어 간다.
            // 보물 보너스와 같은 방식이라, 층을 넘어 플레이어가 새로 스폰돼도 재적용이 필요 없다.
            float augment = (_isPlayer && InventoryManager.Instance != null)
                ? InventoryManager.Instance.AugmentMaxHpBonus : 0f;
            float flat = Mods.Flat(StatType.Health)
                       + (SubPassive != null ? SubPassive.MaxHpBonus : 0f)
                       + augment;
            float bonus = GetTreasureBonus(TreasureEffectType.GlobalMinionStats)
                        + Mods.Percent(StatType.Health);
            return (baseMaxHP + flat) * (1f + bonus);
        }
    }

    public float CURHP => (Health != null) ? Health.CurHP : MAXHP;

    /// <summary>
    /// 공격 속도(회/초). 높을수록 빠르다.
    /// [26/07/17] 예전엔 '공격 간격(초)'이라 낮을수록 빨랐다 — 스탯으로 올릴 때 방향이 반대여서
    /// 뒤집었다. 타이머용 '초'가 필요하면 AttackInterval 을 쓸 것.
    /// </summary>
    public float ATKSPD
    {
        get
        {
            float bonus = Mods.Percent(StatType.AttackSpeed)
                        + (SubPassive != null ? SubPassive.AtkSpeedBonus : 0f);
            float result = (baseAtkSpd + Mods.Flat(StatType.AttackSpeed)) * (1f + bonus);
            return Mathf.Max(0.05f, result);
        }
    }

    /// <summary>공격 간격(초). AI 쿨타임 타이머가 쓰는 값. ATKSPD 의 역수다.</summary>
    public float AttackInterval => 1f / Mathf.Max(0.05f, ATKSPD);

    /// <summary>
    /// 방어력(%). 그 자체가 감소율이다 — 20 이면 20% 감소.
    /// 상한 75% 는 여기서 자른다. 계산식이 아니라 게터에서 자르는 이유: UI 에도 75 로 보여야 하고,
    /// 상한을 넘긴 방어력이 어딘가에서 음수 피해로 뒤집히는 걸 원천 차단하려고.
    /// </summary>
    public float DEF
    {
        get
        {
            float def = (baseDef + Mods.Flat(StatType.Defense)) * (1f + Mods.Percent(StatType.Defense));
            return Mathf.Clamp(def, 0f, MAX_DEF_PERCENT);
        }
    }

    /// <summary>치명타 확률(%).</summary>
    public float CRIT_CHANCE => Mathf.Max(0f, baseCritChance + Mods.Flat(StatType.CritChance));

    /// <summary>치명타 피해량(%). 150 = 1.5배. 하향도 가능하되 100% 밑으로는 안 내려간다(크리가 손해면 이상하므로).</summary>
    public float CRIT_DAMAGE => Mathf.Max(100f, baseCritDamage + Mods.Flat(StatType.CritDamage));

    /// <summary>회피율(0~1).</summary>
    public float EVASION => Mathf.Clamp01(baseEvasion + Mods.Flat(StatType.Evasion));

    /// <summary>적중률(0~1). 1 = 무조건 맞힘.</summary>
    public float ACCURACY => Mathf.Clamp01(baseAccuracy + Mods.Flat(StatType.Accuracy));

    public float MOVESPEED
    {
        get
        {
            if (Status == null) return baseMoveSpeed;
            if (Status.HasStatus(StatusType.Stun) || Status.HasStatus(StatusType.Hitstun)) return 0f;

            float bonus = Mods.Percent(StatType.MoveSpeed);
            return (baseMoveSpeed + Mods.Flat(StatType.MoveSpeed)) * (1f + bonus) * Status.MoveSpeedMultiplier;
        }
    }

    // ── 적 전용 ───────────────────────────────────────────────────────

    public float ATKRANGE => baseAtkRange;

    /// <summary>이 유닛의 공격이 물리인지 마법인지. 데미지 팝업 색과 향후 속성별 증감의 기준.</summary>
    public AttackType ATTACK_TYPE => baseAttackType;

    // ── 플레이어 전용 ─────────────────────────────────────────────────

    /// <summary>스킬 쿨감 비율(0~1 이상). 합연산이고 상한이 없다 — 1 이면 쿨타임 0.</summary>
    // 서브 소환수 항은 비율(0.2)로 들고 있고 이 식은 퍼센트 단위라 100 을 곱해서 넣는다.
    public float SKILL_CDR => (baseSkillCdr + Mods.Flat(StatType.SkillCooldownReduction)
                               + (SubPassive != null ? SubPassive.SkillCooldownReduction * 100f : 0f)) / 100f;

    /// <summary>대쉬 쿨감 비율(0~1 이상).</summary>
    public float DASH_CDR => (baseDashCdr + Mods.Flat(StatType.DashCooldownReduction)) / 100f;

    /// <summary>평타 1·2타 배율.</summary>
    public float BASIC_ATK_MULT => baseBasicAtkMult + Mods.Flat(StatType.BasicAttackMultiplier);

    /// <summary>스킬 쿨타임에 쿨감을 먹인 최종 초. 쿨감 100%면 0 이 된다(상한 없음 — 기획 확정).</summary>
    public float ApplySkillCooldown(float baseCooldown) => Mathf.Max(0f, baseCooldown * (1f - SKILL_CDR));

    /// <summary>대쉬 쿨타임에 쿨감을 먹인 최종 초.</summary>
    public float ApplyDashCooldown(float baseCooldown) => Mathf.Max(0f, baseCooldown * (1f - DASH_CDR));

    // ── 기타 ──────────────────────────────────────────────────────────

    public float BaseMaxHP => baseMaxHP;
    public float BaseAtk => baseAtk;
    public float BaseMoveSpeed => baseMoveSpeed;
    public bool IsDead => Health != null && Health.IsDead;

    private void OnEnable()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnMinionUpdated += RefreshFinalStats;
        }
    }

    private void OnDisable()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnMinionUpdated -= RefreshFinalStats;
        }
    }

    /// <summary>
    /// 인벤토리(소환수 장착/세이브 로드) 등 전역 스탯 변화가 생겼을 때 호출되어 하위 컴포넌트들을 갱신합니다.
    /// </summary>
    private void RefreshFinalStats()
    {
        if (!_isInitialized) return;

        // 최대 체력 변화를 Health 담당자에게 알림 (체력바 UI 갱신 등)
        if (Health != null)
        {
            Health.ResetHP();
        }
    }

    // [26/07/17] _isPlayer 게이트를 걷어냈다. 예전엔 플레이어를 보물 보너스에서 명시적으로
    // 제외했는데, 이제 소환수가 플레이어의 ATK 를 공유하므로 버프 경로가 하나여야 한다.
    // "아군 공격력 증가"를 베이스 ATK 하나에만 걸면 주먹과 소환수 피해에 동시에 먹는다.
    private float GetTreasureBonus(TreasureEffectType type)
    {
        if (InventoryManager.Instance == null || !_isAlly) return 0f;
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

        // 3. 플레이어 본체거나 군대(미니언) 태그/레이어인지 체크 (자식 오브젝트일 수 있으므로 root 태그도 확인)
        _isPlayer = CompareTag("Player") || gameObject.layer == Layers.Player || transform.root.CompareTag("Player");
        _isAlly = _isPlayer || tag == "Army" || gameObject.layer == Layers.Army || transform.root.tag == "Army";
    }

    // [중앙집중형 초기화]
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
    public void InitializeStats(EnemyMinionDataSO data)
    {
        Setup();

        if (data != null)
        {
            baseMaxHP = data.maxHP;
            baseAtk = data.attack;
            baseAtkSpd = data.attackSpeed; // 회/초 (높을수록 빠름)
            baseAtkRange = data.attackRange;
            baseDef = data.defense;
            baseMoveSpeed = data.moveSpeed;
            baseEvasion = data.baseEvasion;
            baseAccuracy = data.baseAccuracy;
            baseAttackType = data.attackType;

            // 적은 치명타를 굴리지 않는다(기본값 0). 필요해지면 EnemyMinionDataSO 에 필드를 열면 된다.
            baseCritChance = 0f;

            // [추가] 보스 여부 전달
            if (Status != null) Status.IsElite = data.isElite;
        }

        UpdateTeamStatus(); // 데이터 주입 시점에 팀 정보 다시 확인

        Mods.Clear(); // 오브젝트 재사용 시 이전 유닛의 보정치를 물려받지 않도록
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
    /// 런타임에 현재 일반 방어력(퍼센트)을 수정합니다. (본 마스터 기믹 등에서 사용)
    /// </summary>
    public void SetCurrentDef(float newDef)
    {
        baseDef = Mathf.Max(0f, newDef);
    }
}
