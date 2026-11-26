using UnityEngine;

/// <summary>
/// 메인 소환수. 우클릭 액티브(minionSkill), 필드 자동 공격(finisher), 스왑/대쉬 공격(dashModifier),
/// 대기 효과(standbyInfusions)를 정한다. 슬롯은 InventoryManager.SLOT_COUNT 칸이고, 필드에는 활성 소환수만
/// 나와(SummonFieldAgent) 자동 공격한다.
/// </summary>
[CreateAssetMenu(fileName = "NewMainMinion", menuName = "Necromancer/Data/Main Minion")]
public class MainMinionDataSO : MinionDataSO
{
    [Header("적중 연출 성장")]
    [Range(0,3), Tooltip("0=기본, 1=강화/첫 진화, 2=상위, 3=최종. 실제 강화 수치가 추가되며 최대 3입니다.")]
    public int feedbackTier;
    public Color feedbackColor = new Color(.45f, .85f, 1f);
    [Header("우클릭 액티브")]
    [Tooltip("우클릭으로 발동하는 일회성 스킬. 쿨타임만 확인하고 조건 없이 나간다.\n" +
             "Space 스왑 때 범위 안에 적이 있는데 스왑 전 소환수가 대쉬 공격을 낼 수 없으면, 이 스킬이 쿨타임을 무시하고(돌리지도 않고) 대신 나간다.")]
    public MinionSkillSO minionSkill;

    [Header("플레이어 변화")]
    [Tooltip("이 소환수가 플레이어의 대쉬를 어떻게 바꾸는지.")]
    public MinionDashModifier dashModifier = new MinionDashModifier();

    [Tooltip("필드에 나온 이 소환수가 자기 주기로 내는 자동 공격(이름은 옛 '마무리'를 그대로 둔다 — 에셋 직렬화 호환).\n" +
             "주기·발견 범위·공통 피해 배율은 Player Melee 프리팹의 PlayerSkillController 에 있다.")]
    public MinionFinisher finisher = new MinionFinisher();

    [Header("대기 효과 (필드에 없을 때 플레이어 평타에 묻어남)")]
    [Tooltip("이 소환수가 대기 슬롯에 있을 때(현현하지 않았을 때) 플레이어 평타에 붙는 상태이상들.\n" +
             "효과마다 확률을 따로 추첨한다(여러 개가 동시에 붙을 수 있다).")]
    public MinionStandbyInfusion[] standbyInfusions = new MinionStandbyInfusion[0];

    public bool HasStandbyInfusion => MinionStandbyInfusion.AnyActive(standbyInfusions);

    [Header("적중 연출")]
    [Tooltip("피격 대상에게 재생할 완성 이펙트 프리팹. 비우면 히트박스의 기존 연출을 사용한다. 자동 공격·우클릭 스킬·대쉬에 공통 적용.")]
    public GameObject hitEffectPrefab;

    public void ApplyHitEffect(BaseHitBox box)
    {
        if (box == null || hitEffectPrefab == null) return;
        box.hitEffectPrefab = hitEffectPrefab;
        box.hitParticlePrefab = null; // 복합 이펙트 안의 파편을 사용한다.
    }

    [Header("애니메이션 — 미니언이 자기 연출을 전부 갖는다")]
    [Tooltip("필드 소환수 자동 공격(finisher)의 연출. finisher 로직이 여기서 비주얼/시퀀스/타이밍을 읽는다.")]
    public MinionAnimSet basicAnim = new MinionAnimSet();

    [Tooltip("우클릭 액티브(minionSkill)의 연출. 스킬 로직이 여기서 비주얼/시퀀스/타이밍을 읽는다.")]
    public MinionAnimSet skillAnim = new MinionAnimSet();

    [Tooltip("대쉬 연출(위치별 클립). 히트박스와 무관한 순수 비주얼 — 대쉬 판정은 dashModifier 가 낸다.\n" +
             "hitAtOrigin(네크 인형)은 startClip 하나만, 경로형은 start/hitBox/end 세 칸을 채운다.")]
    public MinionDashAnim dashAnim = new MinionDashAnim();

    [Header("보상방 강화 (최대 MinionEnhance.MAX_LEVEL 단계, 현재 1)")]
    [Tooltip("보상방에서 강화할 때마다 한 칸씩 올라간다. 각 칸의 값은 '기본 대비 총 배율'이다(누적 아님).\n" +
             "칸 수가 곧 최대 강화 횟수지만 MinionEnhance.MAX_LEVEL(현재 1)을 넘지 않는다.")]
    public MinionEnhanceStep[] enhanceSteps = MinionEnhanceStep.DefaultSteps();

    /// <summary>이 소환수의 최대 강화 단계.</summary>
    public int MaxEnhanceLevel => Mathf.Min(MinionEnhance.MAX_LEVEL, enhanceSteps != null ? enhanceSteps.Length : 0);

    /// <summary>단계(1부터)의 강화 수치. 0 이하거나 범위를 넘으면 null(=강화 없음 / 더 없음).</summary>
    public MinionEnhanceStep GetEnhanceStep(int level)
        => (level <= 0 || level > MaxEnhanceLevel) ? null : enhanceSteps[level - 1];

    [Header("진화 (갈래 선택)")]
    [Tooltip("이 소환수가 진화할 수 있는 형태(보통 2개). 각 형태는 별도의 MainMinionDataSO 에셋이라\n" +
             "이름·스킬·범위·애니메이션·강화표가 전부 그 에셋 것으로 바뀐다. 비우면 최종 형태.")]
    public MainMinionDataSO[] evolutions;

    [Tooltip("진화로만 얻는 형태면 체크. 마을 소환수 선택 NPC 와 보상방 소환수 카드 풀에서 빠진다.\n" +
             "(세이브 복원 때문에 GrowthRegistry 에는 반드시 등록돼 있어야 한다.)")]
    public bool isEvolvedForm;

    [Tooltip("진화에 필요한 강화 단계를 이 소환수만 따로 정할 때. -1 이면 InventoryManager 의 공통값을 쓴다.\n" +
             "대부분 -1 로 둔다.")]
    [Min(-1)] public int evolveRequiredLevelOverride = -1;

    [Header("세이브 호환")]
    [Tooltip("세이브는 소환수를 에셋 이름으로 저장한다. 에셋 이름을 바꿨다면 예전 이름을 여기 남겨 두면\n" +
             "옛 세이브도 이 소환수로 불러온다. (예: 'MeleeDoll Minion' → 'Fighter Minion')")]
    [WorkshopReadOnly] public string[] formerAssetNames = new string[0];

    /// <summary>현재 에셋 이름이거나 예전 에셋 이름 중 하나인가.</summary>
    public bool MatchesSaveKey(string key)
        => !string.IsNullOrEmpty(key) && (name == key || (formerAssetNames != null && System.Array.IndexOf(formerAssetNames, key) >= 0));

    /// <summary>진화할 형태가 하나라도 연결돼 있는가.</summary>
    public bool CanEvolve
    {
        get
        {
            if (evolutions == null) return false;
            foreach (var e in evolutions) if (e != null && e != this) return true;
            return false;
        }
    }

    // ── 카드/툴팁: 액티브 스킬을 대표로 내세운다 ──────────────────────
    public override string ResolveTitle()
        => (minionSkill != null && !string.IsNullOrEmpty(minionSkill.skillName))
            ? minionSkill.skillName
            : base.ResolveTitle();

    public override Sprite ResolveIcon()
        => (minionSkill != null && minionSkill.icon != null) ? minionSkill.icon : base.ResolveIcon();

    public override string ResolveDescription()
        => base.ResolveDescription()
           ?? ((minionSkill != null && !string.IsNullOrEmpty(minionSkill.description)) ? minionSkill.description : null);

    private void OnValidate() => ProjectParameters.IssueKeys(this, standbyInfusions);
}
