using UnityEngine;

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

    // 하위 컴포넌트 직접 노출 (Read-only Accessors)
    public CharacterStatus Status { get; private set; }
    public CharacterHealth Health { get; private set; }
    public CharacterVisualFeedback Visual { get; private set; }

    [Header("런타임 직업 정보")]
    [SerializeField] private CommandData jobType; // 보석 계산을 위해 필요

    private bool _isInitialized = false;

    // --- 외부 참조용 단축 프로퍼티 (데이터 중심 + 보석 보너스 합산) ---
    
    // 공격력: (기본 공격력) * (1 + 보석 배율)
    public float ATK => baseAtk * (1f + GetGemBonus(StatType.Attack));

    // 최대 체력: (기본 체력) * (1 + 보석 배율)
    public float MAXHP => baseMaxHP * (1f + GetGemBonus(StatType.Health));

    public float CURHP => (Health != null) ? Health.CurHP : MAXHP;

    // 공격 속도: 기본 공격 주기 / (1 + 보석 배율) -> 배율이 높을수록 주기가 짧아짐(빨라짐)
    public float ATKSPD => baseAtkSpd / (1f + GetGemBonus(StatType.AttackSpeed));

    public float ATKRANGE => baseAtkRange;
    public float DEF => baseDef;

    // 이동 속도: 기본 속도 * 상태이상 배율
    public float MOVESPEED => (baseMoveSpeed * (Status != null ? Status.MoveSpeedMultiplier : 1f));
    
    // 부활 시간 보너스 (필요 시 외부에서 참조)
    public float RESPAWN_BONUS => GetGemBonus(StatType.RespawnTime);

    public bool IsDead => Health != null && Health.IsDead;

    private float GetGemBonus(StatType type)
    {
        if (InventoryManager.Instance == null) return 0f;
        return InventoryManager.Instance.GetGemBonus(jobType, type);
    }

    // [중앙집집중형 초기화]
    public void Setup()
    {
        if (_isInitialized) return;

        Status = GetComponent<CharacterStatus>();
        Health = GetComponent<CharacterHealth>();
        Visual = GetComponent<CharacterVisualFeedback>();

        if (Visual != null) Visual.Init(Health, Status);
        if (Health != null) Health.Init(this, Status);

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
            jobType = data.minionType; // 직업 정보 저장 (보석 계산용)
            baseMaxHP = data.maxHP;
            baseAtk = data.attack;
            baseAtkSpd = data.attackSpeed;
            baseAtkRange = data.attackRange;
            baseDef = data.defense;
            baseMoveSpeed = data.moveSpeed;
        }
        
        if (Health != null) Health.ResetHP();
        if (Status != null) Status.ClearStatus();
        if (Visual != null) Visual.ResetVisuals();
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
