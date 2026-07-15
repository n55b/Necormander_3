using UnityEngine;

/// <summary>
/// 메인 소환수가 플레이어 평타 콤보의 마지막에 넣는 마무리 일격.
///
/// 설계 3.3: "플레이어 기본 공격 콤보 회수 + 1을 하여 마지막에 소환수의 마무리 일격이 발동
/// (평타 2타로 줄여주셈)". 즉 메인 소환수가 없으면 앞 2타만 빠르게 반복하고, 있으면
/// 3타 타이밍에 이것이 대신 나간다. 이때 플레이어는 아무것도 하지 않고 Idle 로 있는다.
/// </summary>
[System.Serializable]
public class MinionFinisher
{
    [Header("피해")]
    [Tooltip("몇 번 때릴지.")]
    public int hitCount = 1;

    [Tooltip("타당 피해 = 소환수 ATK x 이 값.")]
    public float damageMultiplier = 1f;

    [Header("범위")]
    [Tooltip("히트박스 크기(유닛). x = 사거리, y = 폭.")]
    public Vector2 hitBoxSize = new Vector2(3f, 2f);

    [Tooltip("타격이 유지되는 시간(초). 다단히트면 이 시간 안에 균등 배분된다.")]
    public float duration = 0.25f;

    [Header("연출")]
    [Tooltip("마무리 일격 시 재생할 소환수 비주얼. 비워두면 소환수가 안 보인다.")]
    public GameObject visual;

    [Tooltip("적에게 경직을 주는지.")]
    public bool causesHitstun = true;

    [Tooltip("넉백 세기. 0 이면 없음.")]
    public float knockbackForce = 4f;

    [Tooltip("슈퍼아머 삭감량.")]
    public float superArmorDamage = 30f;

    /// <summary>실제로 발동할 내용이 있는가.</summary>
    public bool IsValid => hitCount > 0;
}
