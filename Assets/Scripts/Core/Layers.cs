using UnityEngine;

/// <summary>
/// 프로젝트 전역 레이어 인덱스/마스크를 1회 캐싱해 제공하는 정적 클래스.
/// 코드 곳곳에 흩어진 LayerMask.GetMask("...")/NameToLayer("...") 문자열 파싱을
/// 상수 참조로 대체해 (1) 오타를 한곳에서 잡고 (2) 매 프레임/충돌마다의 문자열 조회를 없앤다.
///
/// 주의: 레이어명은 Project Settings 값이라 클래스 로드 시점에 이미 존재한다.
/// 여기 있는 이름은 전부 TagManager.asset 에 실재하는 것만 남긴다. 없는 이름을 쓰면
/// NameToLayer 는 -1 을, GetMask 는 그 이름을 조용히 무시한 값을 돌려주므로 아무도 눈치채지 못한다.
/// (실제로 Ally/Boss/Obstacle/BackGround 가 그렇게 죽어 있었다.)
/// 피아식별은 레이어가 아니라 BaseEntity.team(Team.Ally/Enemy)이 담당한다.
/// 'Boss' 는 레이어가 아니라 태그이므로 CompareTag("Boss") 로 검사한다.
/// </summary>
public static class Layers
{
    // ── 레이어 인덱스 (gameObject.layer 대입 / 비교용) ──
    public static readonly int Default        = LayerMask.NameToLayer("Default");
    public static readonly int Player         = LayerMask.NameToLayer("Player");
    public static readonly int PlayerDash     = LayerMask.NameToLayer("Player_Dash");
    public static readonly int Army           = LayerMask.NameToLayer("Army");
    public static readonly int Enemy          = LayerMask.NameToLayer("Enemy");
    public static readonly int EnemyFlying    = LayerMask.NameToLayer("Enemy_Flying");
    public static readonly int FlyingObject   = LayerMask.NameToLayer("FlyingObject");
    public static readonly int Wall           = LayerMask.NameToLayer("Wall");
    public static readonly int Unsteppable    = LayerMask.NameToLayer("Unsteppable");
    public static readonly int ObjectLayer    = LayerMask.NameToLayer("Object");
    public static readonly int Room           = LayerMask.NameToLayer("Room");
    public static readonly int MiniMap        = LayerMask.NameToLayer("MiniMap");
    public static readonly int Interactable   = LayerMask.NameToLayer("Interactable");

    // ── 사전 계산 마스크 (반복되던 조합들) ──
    public static readonly int EnemyMask            = LayerMask.GetMask("Enemy");
    public static readonly int ArmyMask             = LayerMask.GetMask("Army");
    public static readonly int PlayerMask           = LayerMask.GetMask("Player");
    public static readonly int ObjectMask           = LayerMask.GetMask("Object");
    public static readonly int WallMask             = LayerMask.GetMask("Wall");
    public static readonly int UnsteppableMask      = LayerMask.GetMask("Unsteppable");
    public static readonly int RoomMask             = LayerMask.GetMask("Room");
    public static readonly int PlayerArmy           = LayerMask.GetMask("Army", "Player");
    public static readonly int PlayerArmyEnemy      = LayerMask.GetMask("Player", "Army", "Enemy");
    public static readonly int EnemyWall            = LayerMask.GetMask("Enemy", "Wall");
    public static readonly int EnemyObjectWall      = LayerMask.GetMask("Enemy", "Object", "Wall");
    public static readonly int TrapTargets          = LayerMask.GetMask("Player", "Enemy", "Army", "Player_Dash");
}
