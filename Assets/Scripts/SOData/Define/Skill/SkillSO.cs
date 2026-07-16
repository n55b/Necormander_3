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
    BloodPop,    // 비폭
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

public abstract class PlayerSkillSO : SkillSO
{
    // 추가적인 플레이어 전용 데이터 (스테미나 소모량 등)

    [Header("Hand Skill Animation")]
    [Tooltip("Hand 오브젝트의 HandSkillAnimator에서 재생할 State/Clip 이름 (예: \"020_Physics_Leap\"). 비워두면 손 모션 없이 스킬만 발동됩니다.")]
    public string handSkillAnimName;

    [Header("Hit Timing")]
    [Range(0f, 1f)]
    [Tooltip("HandSkill 클립 길이 중 실제 타격(데미지/히트박스)이 발생해야 하는 시점 비율 (0=시작, 1=끝). Animation Event 대신 이 비율로 타이밍을 맞췄니다.")]
    public float hitTimingRatio = 0.4f;
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

    [Header("Skill Animation")]
    [Tooltip("스킬 발동 시 시전 위치에 재생할 애니메이션 비주얼 오브젝트(도트/애니메이터 포함). 비워두면 재생하지 않습니다.")]
    public GameObject skillAnimVisual;

    [Tooltip("전체 시전 시간(초). 애니메이션 재생 속도가 이 길이에 정확히 맞도록 자동 조절됩니다. " +
             "0 이면 클립 원본 길이를 그대로 씁니다.\n" +
             "[중요] 나중에 공속 등으로 시전이 빨라지면 이 값만 줄이면 됩니다 — 애니메이션과 타격 시점이 " +
             "전부 비율로 묶여 있어서 같이 따라옵니다.")]
    public float skillAnimDuration = 0f;

    [Tooltip("순서대로 재생할 애니메이터 상태 이름들. 비우면 기본 상태 하나만 재생됩니다.\n" +
             "aseprite 임포터는 태그마다 상태를 만들어 놓고 트랜지션을 하나도 안 걸기 때문에, " +
             "여기에 적지 않은 상태는 영원히 재생되지 않습니다. (예: Start, Slash, End)")]
    public string[] animSequence;

    [Tooltip("위 시퀀스와 '동시에' 겹쳐 재생할 이펙트 상태 이름. 비우면 없음. (예: DashDoll 의 Effect)")]
    public string effectState = "";

    [Header("Damage Timing — 초 대신 그림이 정한다")]
    [Tooltip("이 태그가 재생되는 '동안'만 판정이 열린다. 예: MeleeDoll 의 Slash / " +
             "태그 경계는 이미 아티스트가 그림에 찍어둔 마커다. 그래서 애니를 다시 타이밍해도 " +
             "판정이 알아서 따라온다 — 초나 비율을 손으로 맞출 필요가 없다.")]
    public string damageState = "";

    [Tooltip("태그로 준비/타격이 안 나뉠 때 쓴다(예: DashDoll 은 Attack 하나에 다 들어있음). " +
             "Aseprite 에서 타격 프레임 셀의 user data 에 `event:OnHitEvent` 을 적으면 임포터가 " +
             "그 프레임에 AnimationEvent 를 심어준다. 여기에 OnHitEvent 를 적으면 그 순간 판정이 열린다. " +
             "비워두면 damageState(태그) 방식. 적으면 이쪽이 우선.")]
    public string hitEvent = "";

    [Range(0f, 1f)]
    [Tooltip("hitEvent 방식일 때만 사용. 판정이 열려 있는 시간(전체 시전 시간 대비 비율).")]
    public float hitWindowRatio = 0.15f;
}
