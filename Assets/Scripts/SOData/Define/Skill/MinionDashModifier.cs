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
    [Header("설명(스킬 설명창 V)")]
    [Tooltip("스킬 설명창에 표시할 이름. 비우면 '대쉬 공격'으로 표시.")]
    public string uiTitle;
    [Tooltip("스킬 설명창에 표시할 설명 문구.")]
    [TextArea] public string uiDescription;
    [Tooltip("스킬 설명창 아이콘(선택). 비우면 아이콘 숨김.")]
    public Sprite uiIcon;

    [Header("형태")]
    [Tooltip("대쉬 거리 배율. 1 = 기본(3유닛).")]
    public float lengthMultiplier = 1f;

    [Tooltip("대쉬 히트박스의 폭(유닛). 0 이면 (경로 모드에서) 히트박스를 만들지 않는다.")]
    public float width = 0f;

    [Header("판정 방식")]
    [Tooltip("true 면 대쉬 경로를 훑는 대신, 대쉬 '출발 지점'에 원형 판정 1발을 남긴다(네크 인형). " +
             "이동 자체는 그대로 하되 출발 자리에 원이 깔린다.")]
    public bool hitAtOrigin = false;

    [Tooltip("hitAtOrigin 일 때 출발점 원의 반지름(유닛).")]
    public float originRadius = 2f;

    [Tooltip("이 소환수 대쉬 전용 히트박스 프리팹(모양을 결정). 비우면 Player Melee.prefab 의 MeleeDodgeController " +
             "기본값으로 폴백한다 — 경로 모드→Dash Hit Box Prefab, 출발점 모드→Origin Hit Box Prefab.")]
    public BaseHitBox hitBoxPrefab;

    [Header("피해")]
    [Tooltip("대쉬 경로/출발원의 적을 몇 번 때릴지. 0 이면 피해 없음.")]
    public int hitCount = 0;

    [Tooltip("타당 피해 = 플레이어 ATK x 이 값.")]
    public float damageMultiplier = 0.5f;

    [Header("속성/상태이상")]
    [Tooltip("이 대쉬 타격의 속성. 마법이면 플레이어의 마법 피해 증폭을 탄다.")]
    public DamageType element = DamageType.Physical;

    [Tooltip("타격 시 부여할 상태이상. None 이면 안 검(지속은 기본값).")]
    public StatusType onHitStatus = StatusType.None;

    [Tooltip("맞은 적을 대쉬 방향으로 밀쳐낼지.")]
    public bool pushesEnemies = false;

    [Tooltip("밀쳐내는 거리(유닛). pushesEnemies 가 true 일 때만 쓰인다.")]
    public float pushForce = 3f;

    /// <summary>히트박스를 만들 이유가 있는가. 출발원 모드는 폭 없이도 판정을 낸다.</summary>
    public bool DealsDamage => hitCount > 0 && (hitAtOrigin || width > 0f);
}
