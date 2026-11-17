using UnityEngine;

/// <summary>
/// 메인 소환수의 자동 공격 — '게임플레이' 부분만.
///
/// 필드에 나온 소환수가 자기 주기로 적을 친다(SummonFieldAgent → MeleeCombatController.CastSummonBasicAttack).
/// 플레이어 평타와는 연결되지 않는다. 주기·발견 범위·공통 피해 배율은 Player Melee 프리팹의
/// PlayerSkillController(autoAttackInterval / autoAttackRange / autoAttackDamageMultiplier)가 정한다.
/// 클래스/필드 이름(finisher)은 에셋 직렬화 호환을 위해 그대로 둔다.
///
/// [26/07/23] 애니메이션(비주얼/시퀀스/타이밍/이펙트)은 MainMinionDataSO.basicAnim(MinionAnimSet)으로
/// 이사했다. 여기 남은 건 데미지·판정·넉백 같은 로직뿐이다. 기획자는 애니를 미니언 한 곳에서 설정한다.
/// ▶ 애니메이션 연결 방법은 repo 루트의 Docs/Guides/MINION_ANIMATION_GUIDE.md 참조.
///
/// (옛 설계의 '플레이어 평타 콤보에 소환수 일격이 같이 나간다'는 폐기됐다 — internalCooldown 은 쓰지 않는다.)
/// </summary>
[System.Serializable]
public class MinionFinisher
{
    [Header("설명(스킬 설명창 V)")]
    [Tooltip("스킬 설명창에 표시할 이름. 비우면 '자동 공격'으로 표시.")]
    public LocText uiTitle;
    [Tooltip("스킬 설명창에 표시할 설명 문구.")]
    public LocText uiDescription;
    [Tooltip("스킬 설명창 아이콘(선택). 비우면 아이콘 숨김.")]
    public Sprite uiIcon;

    // 사용하지 않는다. 자동 공격 주기는 Player Melee 프리팹의 PlayerSkillController.autoAttackInterval 이 정한다.
    // 기존 에셋 값이 깨지지 않도록 필드만 남겨 두고 인스펙터에서 숨긴다.
    [HideInInspector] public float internalCooldown = 1.5f;

    [Header("피해")]
    [Tooltip("자동 공격 1회에 몇 번 때릴지. 0 이면 이 소환수는 자동 공격을 하지 않는다.")]
    public int hitCount = 1;

    [Tooltip("타당 피해 = 플레이어 ATK x 이 값 x 강화 x 무기 basicDamagePerHit x PlayerSkillController.autoAttackDamageMultiplier.")]
    public float damageMultiplier = 1f;

    [Header("속성/상태이상")]
    [Tooltip("이 자동 공격의 속성. 마법이면 플레이어의 마법 피해 증폭을 탄다.")]
    public DamageType element = DamageType.Physical;

    [Tooltip("타격 시 부여할 상태이상. None 이면 안 검(지속은 기본값).")]
    public StatusType onHitStatus = StatusType.None;

    [Header("범위")]
    [Tooltip("히트박스 크기(유닛). x = 사거리, y = 폭.")]
    public Vector2 hitBoxSize = new Vector2(3f, 2f);

    [Tooltip("이 소환수 자동 공격 전용 히트박스 프리팹(모양 결정, 텔레그래프+BaseHitBox 포함). 비우면 " +
             "Player Melee.prefab 의 MeleeCombatController 의 Telegraph Prefab 으로 폴백한다.")]
    public GameObject hitBoxPrefab;

    [Tooltip("조준 방향으로 소환수를 얼마나 밀지. 소환수 스프라이트는 좌우로만 뒤집히므로, " +
             "대각선 조준은 히트박스를 기울이는 대신 소환 위치를 그쪽으로 밀어서 맞춘다.")]
    public float spawnOffset = 1f;

    [Tooltip("소환수(비주얼)를 히트박스 중심에서 '플레이어 쪽'으로 이만큼 뒤로 물린다. 0=히트박스 자리에 그대로.\n" +
             "히트박스와 안 겹치게 바깥에서 때리는 느낌을 줄 때 키운다(히트박스는 조준 위치 그대로 유지).")]
    public float minionOffsetFromHitBox = 0f;

    [Header("소환수 자동 공격")]
    [Tooltip("소환수가 적과 이 거리까지 다가가면 멈춰서 기본 공격을 한다(유닛). 원거리 소환수는 크게 잡는다. 판정이 이 거리에 닿도록 자동으로 앞으로 밀린다.")]
    [Min(0.1f)] public float summonAttackDistance = 2f;

    [Header("연출(넉백/경직)")]
    [Tooltip("적에게 경직을 주는지.")]
    public bool causesHitstun = true;

    [Tooltip("넉백 세기. 0 이면 없음.")]
    public float knockbackForce = 4f;

    [Tooltip("슈퍼아머 삭감량.")]
    public float superArmorDamage = 30f;

    /// <summary>실제로 발동할 내용이 있는가.</summary>
    public bool IsValid => hitCount > 0;

    public string Describe()
        => !string.IsNullOrEmpty(uiDescription) ? uiDescription : !IsValid ? Loc.T("MinionFinisher.no_attack", "자동 공격 없음")
            : Loc.F("MinionFinisher.hits", "{0}회 타격 · 타격당 피해 배율 {1:0.##}배.", hitCount, damageMultiplier)
                + (onHitStatus != StatusType.None ? Loc.F("MinionFinisher.on_hit_status", " 적중 시 {0}.", MinionStandbyInfusion.StatusLabel(onHitStatus)) : "");
}
