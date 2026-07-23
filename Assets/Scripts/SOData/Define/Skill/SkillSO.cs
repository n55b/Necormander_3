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

public abstract class PlayerSkillSO : SkillSO
{
    // 추가적인 플레이어 전용 데이터 (스테미나 소모량 등)

    [Header("Damage Type")]
    [Tooltip("이 스킬이 물리인지 마법인지.\n" +
             "물리 = 플레이어의 ATK * 스킬 계수, 마법 = MAGIC * 스킬 계수.\n" +
             "지금 22개 스킬은 전부 물리(무투파)다. 마법 스킬은 아직 없다.")]
    public AttackType skillDamageType = AttackType.Physical;

    /// <summary>이 스킬의 피해 베이스값. 물리면 ATK, 마법이면 MAGIC 을 가져온다.</summary>
    public float GetBaseDamage(CharacterStat stat)
    {
        if (stat == null) return 0f;
        return skillDamageType == AttackType.Magic ? stat.MAGIC : stat.ATK;
    }

    /// <summary>이 스킬의 피해 표시 타입. 팝업 색과 향후 속성별 증감의 기준.</summary>
    public DamageType ResolveDamageType()
        => skillDamageType == AttackType.Magic ? DamageType.Magic : DamageType.Physical;

    [Header("Hand Skill Animation")]
    [Tooltip("Hand 오브젝트의 HandSkillAnimator에서 재생할 State/Clip 이름 (예: \"020_Physics_Leap\"). 비워두면 손 모션 없이 스킬만 발동됩니다.")]
    public string handSkillAnimName;

    [Header("Hit Timing")]
    [Range(0f, 1f)]
    [Tooltip("HandSkill 클립 길이 중 실제 타격(데미지/히트박스)이 발생해야 하는 시점 비율 (0=시작, 1=끝). Animation Event 대신 이 비율로 타이밍을 맞췄니다.")]
    public float hitTimingRatio = 0.4f;

    [Header("강화 (장착 장비의 강화레벨에 따라 세짐 — 경로만, 수치 미정)")]
    [Tooltip("이 스킬이 강화될 때 어떻게 세지는지. [SerializeReference] 라 종류를 자유롭게 늘릴 수 있다.\n" +
             "비우면 강화 영향 없음. 데미지는 DamageEnhanceEffect, 타수 손볼 스킬은 HitCountEnhanceEffect 추가.\n" +
             "스킬별로 다르게 잡으려면 각 스킬 에셋에서 이 리스트를 다르게 채운다.")]
    [SerializeReference] public System.Collections.Generic.List<EnhanceEffect> enhanceEffects
        = new System.Collections.Generic.List<EnhanceEffect>();

    /// <summary>현재 장착 장비의 강화레벨. 장비가 없으면 0.</summary>
    public static int CurrentEnhanceLevel
        => PlayerSkillInventoryManager.Instance != null
           && PlayerSkillInventoryManager.Instance.EquippedEquipment != null
            ? PlayerSkillInventoryManager.Instance.EquippedEquipment.enhanceLevel : 0;

    /// <summary>강화까지 반영한 최종 스킬 피해. 각 스킬은 GetBaseDamage(stat)*배율 대신 이걸 쓰면 강화가 자동 반영된다.</summary>
    public float ResolveDamage(CharacterStat stat, float skillMultiplier)
    {
        float dmg = GetBaseDamage(stat) * skillMultiplier;
        int lvl = CurrentEnhanceLevel;
        if (enhanceEffects != null)
            foreach (var e in enhanceEffects) if (e != null) dmg *= e.DamageMultiplier(lvl);
        return dmg;
    }

    /// <summary>강화까지 반영한 최종 타수. baseCount 에 강화 추가타를 더한다.</summary>
    public int ResolveHitCount(int baseCount)
    {
        int lvl = CurrentEnhanceLevel;
        int bonus = 0;
        if (enhanceEffects != null)
            foreach (var e in enhanceEffects) if (e != null) bonus += e.BonusHitCount(lvl);
        return baseCount + bonus;
    }
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
