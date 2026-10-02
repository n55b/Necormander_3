using UnityEngine;

/// <summary>
/// 메인 소환수. 스페이스바 액티브를 갖고, 플레이어의 대쉬와 평타 마무리를 바꾼다.
/// 슬롯은 1칸(InventoryManager.SLOT_MAIN). 필드에 실체로 존재하지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "NewMainMinion", menuName = "Necromancer/Data/Main Minion")]
public class MainMinionDataSO : MinionDataSO
{
    [Header("스페이스바 액티브")]
    [Tooltip("Space 로 발동하는 일회성 스킬. 쿨타임만 확인하고 조건 없이 나간다.")]
    public MinionSkillSO minionSkill;

    [Header("플레이어 변화")]
    [Tooltip("이 소환수가 플레이어의 대쉬를 어떻게 바꾸는지.")]
    public MinionDashModifier dashModifier = new MinionDashModifier();

    [Tooltip("플레이어 평타 콤보 첫 타에 이 소환수가 넣는 일격(이름은 옛 '마무리'를 그대로 둔다 — 에셋 직렬화 호환).")]
    public MinionFinisher finisher = new MinionFinisher();

    [Header("애니메이션 — 미니언이 자기 연출을 전부 갖는다")]
    [Tooltip("평타 콤보 마무리(finisher)의 연출. finisher 로직이 여기서 비주얼/시퀀스/타이밍을 읽는다.")]
    public MinionAnimSet basicAnim = new MinionAnimSet();

    [Tooltip("스페이스바 액티브(minionSkill)의 연출. 스킬 로직이 여기서 비주얼/시퀀스/타이밍을 읽는다.")]
    public MinionAnimSet skillAnim = new MinionAnimSet();

    [Tooltip("대쉬 연출(위치별 클립). 히트박스와 무관한 순수 비주얼 — 대쉬 판정은 dashModifier 가 낸다.\n" +
             "hitAtOrigin(네크 인형)은 startClip 하나만, 경로형은 start/hitBox/end 세 칸을 채운다.")]
    public MinionDashAnim dashAnim = new MinionDashAnim();

    [Header("보상방 강화 (최대 3단계)")]
    [Tooltip("보상방에서 강화할 때마다 한 칸씩 올라간다. 각 칸의 값은 '기본 대비 총 배율'이다(누적 아님).\n" +
             "칸 수가 곧 최대 강화 횟수지만 MinionEnhance.MAX_LEVEL(3)을 넘지 않는다.")]
    public MinionEnhanceStep[] enhanceSteps = MinionEnhanceStep.DefaultSteps();

    /// <summary>이 소환수의 최대 강화 단계.</summary>
    public int MaxEnhanceLevel => Mathf.Min(MinionEnhance.MAX_LEVEL, enhanceSteps != null ? enhanceSteps.Length : 0);

    /// <summary>단계(1부터)의 강화 수치. 0 이하거나 범위를 넘으면 null(=강화 없음 / 더 없음).</summary>
    public MinionEnhanceStep GetEnhanceStep(int level)
        => (level <= 0 || level > MaxEnhanceLevel) ? null : enhanceSteps[level - 1];

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
}
