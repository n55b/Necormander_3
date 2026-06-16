using UnityEngine;

public enum SkillKeyword
{
    None = 0,
    Vulnerability = 1, // 취약 (파괴, 밀치기, 끌어당김 등)
    Debuff = 2,        // 디버프 (비폭, 출혈, 상처, 부식, 골절 등)
    Strike = 3,        // 격파
    Stun = 4,          // 기절
    Smash = 5          // 강타
}

public enum DebuffType
{
    None = 0,
    Explosion,   // 비폭
    Bleed,       // 출혈
    Wound,       // 상처
    Corrosion,   // 부식
    Fracture     // 골절
}

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


    protected void PlaySkillSound()
    {
        if (skillSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(skillSound, skillSoundVolume);
    }


    public abstract void ExecuteSkill(Transform user, Transform target = null, System.Collections.Generic.List<Transform> validTargets = null);
}

public abstract class PlayerSkillSO : SkillSO
{
    // 추가적인 플레이어 전용 데이터 (스테미나 소모량 등)
}

public abstract class MinionSkillSO : SkillSO
{
    [Header("Reaction")]
    public SkillKeyword reactKeyword;

    // 추가적인 미니언 전용 데이터
}
