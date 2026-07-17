using UnityEngine;

/// <summary>
/// 상태이상 아이콘 도감. 유닛 머리 위 아이콘(EnemyDebuffTerminal)이 여기서 그림을 찾는다.
///
/// [26/07/17] 구 취약/상처/부식/골절 필드는 삭제. 신규 5종(기절/빙결/출혈/중독/비폭)은
/// Phase 5 에서 상태이상 본체가 들어올 때 필드를 채운다. 지금은 철거 후라 두 개뿐이다.
/// </summary>
[CreateAssetMenu(fileName = "DebuffData", menuName = "Necromancer/Registry/DebuffData")]
public class DebuffDataSO : ScriptableObject
{
    [Tooltip("기절 — 행동 완전 불가")]
    public Sprite stunnedIcon;

    [Tooltip("경직 — 평타에 묻는 짧은 행동 불가. 기절과 별개다")]
    public Sprite hitstunnedIcon;

    /// <summary>
    /// 비어 있으면 그냥 null 을 준다 — 경고를 찍지 않는다.
    /// 여기는 피격마다 도는 핫패스(BaseHitBox -> 넉백 -> 경직 부여)라 한 번 비면 콘솔이 잠긴다.
    /// 게다가 아이콘이 없으면 흰 사각형으로 눈에 그대로 보이므로 경고가 알려줄 게 없다.
    /// </summary>
    public Sprite GetSprite(DebuffBoolType type) => type switch
    {
        DebuffBoolType.Stunned => stunnedIcon,
        DebuffBoolType.Hitstunned => hitstunnedIcon,
        _ => null
    };
}
