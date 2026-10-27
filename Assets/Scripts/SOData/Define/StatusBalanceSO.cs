using UnityEngine;

/// <summary>전 유닛이 공유하는 상태이상 기본 수치. 개별 공격에 명시한 지속시간은 우선한다.</summary>
[CreateAssetMenu(fileName = "StatusBalance", menuName = "Necromancer/기획/상태이상 공통 설정")]
public sealed class StatusBalanceSO : ScriptableObject
{
    [Header("공통 지속시간")]
    [Min(0.01f), Tooltip("별도 시간이 없는 출혈·중독·비폭·취약의 지속시간(초)입니다. 공격이나 스킬에 명시한 시간은 그 설정을 따릅니다.")]
    public float defaultDuration = 5f;

    [Header("기절")]
    [Min(0.01f), Tooltip("공격에서 시간을 지정하지 않은 기절의 기본 지속시간(초)입니다.")]
    public float stunDuration = 1f;
    [Min(0.01f), Tooltip("일반 공격으로 부여하는 기절의 최소 시간(초)입니다. 보스 그로기처럼 제한을 우회하는 기절에는 적용하지 않습니다.")]
    public float stunMinDuration = 0.5f;
    [Min(0.01f), Tooltip("일반 공격으로 부여하는 기절의 최대 시간(초)입니다. 최소 시간보다 작게 입력하면 최소 시간으로 보정합니다.")]
    public float stunMaxDuration = 2f;
    [Min(0f), Tooltip("기절이 자연 종료된 뒤 다시 기절하지 않는 시간(초)입니다. 0이면 내성이 없습니다.")]
    public float stunImmunityDuration = 3f;

    [Header("빙결")]
    [Min(0.01f), Tooltip("공격에서 시간을 지정하지 않은 빙결의 기본 지속시간(초)입니다.")]
    public float freezeDuration = 2.5f;
    [Min(0f), Tooltip("피해로 빙결이 깨진 뒤 다시 얼지 않는 시간(초)입니다. 자연 종료에는 적용하지 않습니다.")]
    public float freezeImmunityDuration = 1f;
    [Min(0f), Tooltip("일반 빙결을 타격으로 해제할 때 추가되는 고정 피해입니다. 무기 전용 빙결 효과와는 별개입니다.")]
    public float freezeBreakDamage = 20f;

    [Header("출혈·중독")]
    [Min(0f), Tooltip("출혈 중 직접 타격을 받을 때 추가되는 고정 피해입니다.")]
    public float bleedHitDamage = 2f;
    [Min(0f), Tooltip("중독이 한 번 발동할 때의 고정 피해입니다. 초당 피해는 이 값 / 중독 피해 간격입니다.")]
    public float poisonTickDamage = 5f;
    [Min(0.01f), Tooltip("중독 피해 사이의 간격(초)입니다. 작을수록 자주 피해를 주므로 초당 피해도 늘어납니다.")]
    public float poisonTickInterval = 1f;

    [Header("비폭")]
    [Min(1), Tooltip("이 스택에 도달하면 폭발하고 스택을 소모합니다.")]
    public int bloodPopThreshold = 10;
    [Min(0f), Tooltip("비폭이 자신과 주변 대상에게 주는 고정 피해입니다.")]
    public float bloodPopDamage = 20f;
    [Min(0.01f), Tooltip("비폭의 폭발 반지름(Unity 유닛)입니다. 표시 크기와 타격 범위를 함께 바꿉니다.")]
    public float bloodPopRadius = 2.5f;
    [Min(0f), Tooltip("비폭 폭발 전에 표시하는 예고 시간(초)입니다. 0이면 즉시 타격합니다.")]
    public float bloodPopFuse = 0.5f;
    [Min(0.01f), Tooltip("비폭 폭발의 타격 판정이 남는 시간(초)입니다.")]
    public float bloodPopHitDuration = 0.2f;

    [Header("취약")]
    [Min(0f), Tooltip("취약 상태에서 받는 물리 피해의 추가 비율입니다. 0.2는 +20%입니다. 다른 피해 유형에는 적용하지 않습니다.")]
    public float vulnerableDamageBonus = 0.2f;
}
