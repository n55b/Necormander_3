using UnityEngine;

/// <summary>
/// 메인 소환수가 플레이어 평타 콤보의 마지막에 넣는 마무리 일격 — '게임플레이' 부분만.
///
/// [26/07/23] 애니메이션(비주얼/시퀀스/타이밍/이펙트)은 MainMinionDataSO.basicAnim(MinionAnimSet)으로
/// 이사했다. 여기 남은 건 데미지·판정·넉백 같은 로직뿐이다. 기획자는 애니를 미니언 한 곳에서 설정한다.
/// ▶ 애니메이션 연결 방법은 repo 루트의 MINION_ANIMATION_GUIDE.md 참조.
///
/// 설계 3.3: "플레이어 기본 공격 콤보 회수 + 1을 하여 마지막에 소환수의 마무리 일격이 발동
/// (평타 2타로 줄여주셈)". 즉 메인 소환수가 없으면 앞 2타만 빠르게 반복하고, 있으면
/// 3타 타이밍에 이것이 대신 나간다. 이때 플레이어는 아무것도 하지 않고 Idle 로 있는다.
/// </summary>
[System.Serializable]
public class MinionFinisher
{
    [Header("피해")]
    [Tooltip("몇 번 때릴지. 0 이면 마무리 일격이 없는 것으로 치고 콤보가 2타로 끝난다.")]
    public int hitCount = 1;

    [Tooltip("타당 피해 = 소환수 ATK x 이 값.")]
    public float damageMultiplier = 1f;

    [Header("속성/상태이상")]
    [Tooltip("이 마무리 일격의 속성. 마법이면 플레이어의 마법 피해 증폭을 탄다.")]
    public DamageType element = DamageType.Physical;

    [Tooltip("타격 시 부여할 상태이상. None 이면 안 검(지속은 기본값).")]
    public StatusType onHitStatus = StatusType.None;

    [Header("범위")]
    [Tooltip("히트박스 크기(유닛). x = 사거리, y = 폭.")]
    public Vector2 hitBoxSize = new Vector2(3f, 2f);

    [Tooltip("이 소환수 마무리 전용 히트박스 프리팹(모양 결정, 텔레그래프+BaseHitBox 포함). 비우면 " +
             "Player Melee.prefab 의 MeleeCombatController 의 Telegraph Prefab 으로 폴백한다.")]
    public GameObject hitBoxPrefab;

    [Tooltip("조준 방향으로 소환수를 얼마나 밀지. 소환수 스프라이트는 좌우로만 뒤집히므로, " +
             "대각선 조준은 히트박스를 기울이는 대신 소환 위치를 그쪽으로 밀어서 맞춘다.")]
    public float spawnOffset = 1f;

    [Header("연출(넉백/경직)")]
    [Tooltip("적에게 경직을 주는지.")]
    public bool causesHitstun = true;

    [Tooltip("넉백 세기. 0 이면 없음.")]
    public float knockbackForce = 4f;

    [Tooltip("슈퍼아머 삭감량.")]
    public float superArmorDamage = 30f;

    /// <summary>실제로 발동할 내용이 있는가.</summary>
    public bool IsValid => hitCount > 0;
}
