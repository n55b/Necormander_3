using UnityEngine;

/// <summary>
/// 메인 소환수가 플레이어의 대쉬를 어떻게 바꾸는지.
///
/// 기본 대쉬는 순수 이동이다 (MeleeDodgeController: 속도 15 x 지속 0.2 = 3유닛, 무적, 벽 클램프).
/// 히트박스도 데미지도 '폭'이라는 개념도 원래 없었고, 여기 있는 것이 전부 신규다.
/// 모든 값이 1/0 이면 기본 대쉬와 완전히 동일하게 동작한다 (메인 소환수가 없을 때의 상태).
/// </summary>
[System.Serializable]
public class MinionDashModifier
{
    [Header("형태")]
    [Tooltip("대쉬 거리 배율. 1 = 기본(3유닛).")]
    public float lengthMultiplier = 1f;

    [Tooltip("대쉬 히트박스의 폭(유닛). 0 이면 히트박스를 만들지 않는다.")]
    public float width = 0f;

    [Header("피해")]
    [Tooltip("대쉬 경로의 적을 몇 번 때릴지. 0 이면 피해 없음.")]
    public int hitCount = 0;

    [Tooltip("타당 피해 = 플레이어 ATK x 이 값.")]
    public float damageMultiplier = 0.5f;

    [Tooltip("맞은 적을 대쉬 방향으로 밀쳐낼지.")]
    public bool pushesEnemies = false;

    [Tooltip("밀쳐내는 거리(유닛). pushesEnemies 가 true 일 때만 쓰인다.")]
    public float pushForce = 3f;

    /// <summary>히트박스를 만들 이유가 있는가.</summary>
    public bool DealsDamage => hitCount > 0 && width > 0f;
}
