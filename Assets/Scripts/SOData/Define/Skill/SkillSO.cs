using UnityEngine;

// SkillKeyword(취약/격파/강타)와 DebuffType 은 삭제됐다.
// 스킬은 더 이상 상태이상을 부여하지 않는다 — 부여 수단은 유물/아이템 전용이다.
// 스킬은 피해와 넉백/끌어당김만 담당한다.

public abstract class SkillSO : ScriptableObject
{
    public string skillName;
    [TextArea] public string description;
    public Sprite icon;           // UI에 표시할 스킬 아이콘 (null이면 MinionDataSO.minionIcon 대체)
    public float cooldownTime = 5f; // 스킬의 기본 쿨타임 (초 단위)

    [Header("소리")]
    [Tooltip("스킬 발동 시 재생할 사운드 (null이면 무음)")]
    public AudioClip skillSound;
    [Range(0f, 1f)]
    public float skillSoundVolume = 0.85f;

    [Header("흔들림 강도")]
    [Tooltip("적중한 스킬의 반동 픽셀 수에 곱하는 배율입니다. 0이면 카메라 이동이 없습니다.")]
    public float shakeForce = 0.5f;

    [Header("타격 시 시간 정지")]
    [Tooltip("0보다 크면 이 스킬이 명중할 때 히트스탑 발동. 값(초)이 곧 정지 시간. 0이면 없음.")]
    public float hitStopDuration = 0f;


    protected void PlaySkillSound()
    {
        if (skillSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(skillSound, skillSoundVolume);
    }


    public abstract void ExecuteSkill(Transform user, Transform target = null, System.Collections.Generic.List<Transform> validTargets = null);
}

public abstract class MinionSkillSO : SkillSO
{
    /// <summary>
    /// 미니언 스킬의 실제 진입점. SkillSO.ExecuteSkill 는 플레이어 스킬용 시그니처라
    /// 소환수 데이터(ATK 등)를 실을 자리가 없어서 별도 오버로드를 둔다.
    /// user 에는 MinionSkillCaster 가 붙어 있어야 코루틴(타격 지연/넉백)을 돌릴 수 있다.
    /// </summary>
    /// <returns>실제로 시전했으면 true. false 면 호출자가 쿨타임을 먹이지 않아야 한다.</returns>
    public abstract bool Execute(Transform user, MinionDataSO data, System.Collections.Generic.List<Transform> validTargets);

    /// <summary>SkillSO 계약 유지용. 데이터 없이 들어오면 스킬은 스스로 판단해 폴백한다.</summary>
    public override void ExecuteSkill(Transform user, Transform target = null, System.Collections.Generic.List<Transform> validTargets = null)
        => Execute(user, null, validTargets);

    // 애니메이션 설정(비주얼/시퀀스/타이밍)은 여기서 미니언(MainMinionDataSO.skillAnim)으로 이사했다.
    // 스킬은 로직(데미지/판정/넉백)만 갖고, 연출은 시전한 미니언 데이터에서 읽는다.
    // → 기획자는 애니메이션을 미니언 한 곳(MinionAnimSet)에서 전부 설정한다.
    // 연결 방법 / 이벤트 vs 태그 / 속도 조절은 repo 루트의 Docs/Guides/MINION_ANIMATION_GUIDE.md 참조.
}
