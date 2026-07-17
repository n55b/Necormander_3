using UnityEngine;

/// <summary>
/// 상태이상 아이콘 도감. 유닛 머리 위 아이콘(EnemyDebuffTerminal)이 여기서 그림을 찾는다.
///
/// [26/07/17] 구 취약/상처/부식/골절 필드는 삭제하고 신규 5종 + 경직으로 재편했다.
/// 아이콘 이름과 뜻이 안 맞는 건(예: 출혈에 Execute_Icon) 아틀라스에 딱 6개뿐이라
/// 자리만 채워둔 것이다. 전용 아이콘이 나오면 갈아끼우면 된다.
/// </summary>
[CreateAssetMenu(fileName = "DebuffData", menuName = "Necromancer/Registry/DebuffData")]
public class DebuffDataSO : ScriptableObject
{
    [Tooltip("기절 — 행동 완전 불가")]
    public Sprite stunIcon;

    [Tooltip("빙결 — 행동 불가. 맞으면 터지고 풀림")]
    public Sprite freezeIcon;

    [Tooltip("출혈 — 맞을 때마다 추가 고정 피해")]
    public Sprite bleedIcon;

    [Tooltip("중독 — 초당 고정 피해")]
    public Sprite poisonIcon;

    [Tooltip("비폭 — 스택형. 10스택에 폭발")]
    public Sprite bloodPopIcon;

    [Tooltip("경직 — 평타에 묻는 짧은 행동 불가. 기절과 별개다")]
    public Sprite hitstunIcon;

    /// <summary>
    /// 비어 있으면 그냥 null 을 준다 — 경고를 찍지 않는다.
    /// 여기는 피격마다 도는 핫패스(BaseHitBox -> 넉백 -> 경직 부여)라 한 번 비면 콘솔이 잠긴다.
    /// 게다가 아이콘이 없으면 흰 사각형으로 눈에 그대로 보이므로 경고가 알려줄 게 없다.
    /// </summary>
    public Sprite GetSprite(StatusType type) => type switch
    {
        StatusType.Stun => stunIcon,
        StatusType.Freeze => freezeIcon,
        StatusType.Bleed => bleedIcon,
        StatusType.Poison => poisonIcon,
        StatusType.BloodPop => bloodPopIcon,
        StatusType.Hitstun => hitstunIcon,
        _ => null
    };
}
