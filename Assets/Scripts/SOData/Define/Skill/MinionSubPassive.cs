using UnityEngine;

/// <summary>
/// 서브 소환수가 상시 제공하는 스탯 보너스 + 패시브.
///
/// 설계 3.4: 서브는 실체화하지 않고 Space 와도 무관하다. 장착만으로 항상 켜져 있다.
/// 값은 전부 '고정값'이다 (배율 아님) — ATKSPD 는 이 코드베이스에서 '공격 간격(초)'이라
/// 값이 낮을수록 빨라진다는 점에 주의.
/// </summary>
[System.Serializable]
public class MinionSubPassive
{
    [Header("상승 스테이터스")]
    [Tooltip("최대 체력 고정 증가.")]
    public float maxHpBonus = 0f;

    [Tooltip("공격 간격 감소량(초). 값이 클수록 공격이 빨라진다. 예: 0.1 = 간격 1.0 -> 0.9")]
    public float atkIntervalReduction = 0f;

    [Header("패시브")]
    [Tooltip("평타 1타당 추가되는 고정 피해.")]
    public float basicAttackDamageBonus = 0f;

    [Tooltip("이 소환수를 획득/장착한 순간 회복하는 체력.")]
    public float healOnAcquire = 0f;

    [Tooltip("방을 클리어할 때마다 회복하는 체력.")]
    public float healOnRoomClear = 0f;
}
