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

    [Tooltip("플레이어 평타 콤보 마지막(3타)에 이 소환수가 넣는 마무리 일격.")]
    public MinionFinisher finisher = new MinionFinisher();

    [Header("애니메이션 — 미니언이 자기 연출을 전부 갖는다")]
    [Tooltip("평타 콤보 마무리(finisher)의 연출. finisher 로직이 여기서 비주얼/시퀀스/타이밍을 읽는다.")]
    public MinionAnimSet basicAnim = new MinionAnimSet();

    [Tooltip("스페이스바 액티브(minionSkill)의 연출. 스킬 로직이 여기서 비주얼/시퀀스/타이밍을 읽는다.")]
    public MinionAnimSet skillAnim = new MinionAnimSet();

    [Tooltip("대쉬 연출(위치별 클립). 히트박스와 무관한 순수 비주얼 — 대쉬 판정은 dashModifier 가 낸다.\n" +
             "hitAtOrigin(네크 인형)은 startClip 하나만, 경로형은 start/hitBox/end 세 칸을 채운다.")]
    public MinionDashAnim dashAnim = new MinionDashAnim();

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
