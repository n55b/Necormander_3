using UnityEngine;

// [26/07/17] SkillKeyword(취약/격파/강타)와 DebuffType 은 삭제됐다.
// 스킬은 더 이상 상태이상을 부여하지 않는다 — 부여 수단은 유물/아이템 전용이다.
// 스킬은 피해와 넉백/끌어당김만 담당한다.

public abstract class SkillSO : ScriptableObject
{
    public string skillName;
    [TextArea] public string description;
    public Sprite icon;           // UI에 표시할 스킬 아이콘 (null이면 MinionDataSO.minionIcon 대체)
    public float cooldownTime = 5f; // 스킬의 기본 쿨타임 (초 단위)

    [Header("Sound")]
    [Tooltip("스킬 발동 시 재생할 사운드 (null이면 무음)")]
    public AudioClip skillSound;
    [Range(0f, 1f)]
    public float skillSoundVolume = 0.85f;

    [Header("Shake Force")]
    [Tooltip("스킬 발동 시 카메라 흔들림 강도 (0.5f = 기본, 강한 스킬은 1~1.5f 권장)")]
    public float shakeForce = 0.5f;

    [Header("Hit Stop")]
    [Tooltip("0보다 크면 이 스킬이 명중할 때 히트스탑 발동. 값(초)이 곧 정지 시간. 0이면 없음.")]
    public float hitStopDuration = 0f;


    /// <summary>
    /// 스킬 사운드를 재생합니다. ExecuteSkill() 시작 시점에 호출하세요.
    /// </summary>
    /// <summary>
    /// 스킬 연출용 카메라 흔들림. ExecuteSkill() 내에서 호출합니다.
    /// force: 흔들림 강도 (1f = 기본, 강한 스킬은 2~3 권장)
    /// </summary>
    protected void ShakeCamera()
    {
        if (CameraManager.Instance != null)
            CameraManager.Instance.HitShakeCamera(shakeForce);
    }

    /// <summary>
    /// 히트스탑 발동. hitStopDuration이 0보다 크면 그대로 발동합니다(타입 제한 없음).
    /// 강타/처형처럼 묵직한 타격에만 값을 채워서 쓰는 걸 권장하지만, 강제하지는 않습니다.
    /// 처형(Execute) 트리거는 CharacterHealth.GetDamage()의 처형 체크 구간에서 별도로 직접 호출합니다.
    /// </summary>
    protected void DoHitStop()
    {
        if (hitStopDuration <= 0f) return;

        if (HitStopManager.Instance != null)
            HitStopManager.Instance.DoHitStop(hitStopDuration);
    }



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

    // [26/07/23] 애니메이션 설정(비주얼/시퀀스/타이밍)은 여기서 미니언(MainMinionDataSO.skillAnim)으로 이사했다.
    // 스킬은 로직(데미지/판정/넉백)만 갖고, 연출은 시전한 미니언 데이터에서 읽는다.
    // → 기획자는 애니메이션을 미니언 한 곳(MinionAnimSet)에서 전부 설정한다.
    // 연결 방법 / 이벤트 vs 태그 / 속도 조절은 repo 루트의 MINION_ANIMATION_GUIDE.md 참조.
}
