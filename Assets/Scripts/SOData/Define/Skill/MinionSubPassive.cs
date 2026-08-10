using UnityEngine;

/// <summary>
/// 서브 소환수가 상시 제공하는 스탯 보너스 + 패시브.
///
/// 설계 3.4: 서브는 실체화하지 않고 R 키와도 무관하다. 장착만으로 항상 켜져 있다.
///
/// [주의] 서브는 플레이어의 스탯을 올린다. 메인 소환수 피해가 같이 올라가는 건 메인이
/// 플레이어 ATK 를 참조하기 때문이지, 서브가 메인에 직접 손대는 게 아니다.
/// </summary>
[System.Serializable]
public class MinionSubPassive
{
    [Header("상승 스테이터스")]
    [Tooltip("최대 체력 고정 증가.")]
    public float maxHpBonus = 0f;

    // [26/07/17] atkIntervalReduction(초 차감) -> atkSpeedBonus(%) 로 전환.
    // 초를 그냥 빼는 방식은 %와 안 섞였고, 간격이 이미 작을 때 -0.5초 같은 값이 들어오면
    // 공속이 무한대로 튀었다. 이제 공속이 '회/초'라 비율로 얹는 게 자연스럽다.
    //
    // FormerlySerializedAs 를 일부러 안 걸었다. 뜻이 '초'에서 '비율'로 바뀌어서 숫자를 그대로
    // 물려받으면 조용히 틀린 값이 된다(예: 0.5초 차감 = +100% 인데 +50% 로 들어옴).
    // 해당 에셋 2개는 손으로 환산해서 넣었다.
    [Tooltip("공격 속도 증가 비율. 0.2 = +20%. 값이 클수록 빨라진다.")]
    public float atkSpeedBonus = 0f;

    [Tooltip("스킬 쿨타임 감소 비율. 0.2 = -20%. CharacterStat.SKILL_CDR 에 합산되므로 Q/E/R 에 전부 먹는다.")]
    public float skillCooldownReduction = 0f;

    [Header("패시브")]
    [Tooltip("평타 1타당 추가되는 고정 피해.")]
    public float basicAttackDamageBonus = 0f;

    [Tooltip("내 스킬(Q/E/R)이 적을 때릴 때마다 회복하는 체력. 아래 내부 쿨타임이 있어 다단히트로 도배되지 않는다.")]
    public float healOnSkillHit = 0f;

    [Tooltip("healOnSkillHit 의 내부 쿨타임(초).")]
    public float healOnSkillHitCooldown = 5f;

    [Tooltip("이 소환수를 획득/장착한 순간 회복하는 체력.")]
    public float healOnAcquire = 0f;

    [Tooltip("방을 클리어할 때마다 회복하는 체력.")]
    public float healOnRoomClear = 0f;
}
