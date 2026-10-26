using System;
using UnityEngine;

// 번호는 기획 후보 목록과 동일하다. 가드 9~16, 상태이상 33~38은 이번 범위에서 제외.
public enum FeedbackCue
{
    [InspectorName("01 타격 방향 파편")] DirectionHit=1, [InspectorName("02 콤보 마지막 타 전용 섬광")] ComboFinish, [InspectorName("03 치명타 이중 반짝임")] CriticalEcho, [InspectorName("04 재질별 피격")] MaterialHit, [InspectorName("05 무기 궤적 잔광")] WeaponTrail, [InspectorName("06 강타 바닥 먼지")] HeavyDust, [InspectorName("07 다단히트 리듬")] MultiHit, [InspectorName("08 마무리 일격 표시")] KillHit,
    [InspectorName("17 출발 먼지")] MoveStart=17, [InspectorName("18 급정지 마찰")] MoveBrake, [InspectorName("19 급회전 먼지")] MoveTurn, [InspectorName("20 대시 출발 압축파")] DashStart, [InspectorName("21 대시 끝 착지파")] DashEnd, [InspectorName("22 대시 충전 완료 신호")] DashReady, [InspectorName("23 표면별 발걸음")] FootSurface, [InspectorName("24 이동 잔상 리듬")] DashTrail,
    [InspectorName("25 미니언 명령 연결선")] MinionCommand=25, [InspectorName("26 동시 평타 준비 신호")] MinionReady, [InspectorName("27 소환 조립")] MinionAppear, [InspectorName("28 귀환 흡수")] MinionReturn, [InspectorName("29 마리오네트 이중 타격 구별")] MarionetteHit, [InspectorName("30 진화 계통 색")] MinionFamily, [InspectorName("31 처치 영혼 흐름")] SoulReturn, [InspectorName("32 진화 확정 의식")] Evolve,
    [InspectorName("39 회복 상승 입자")] Heal=39, [InspectorName("40 저체력 심박")] LowHealth,
    [InspectorName("41 발사 섬광")] ProjectileLaunch=41, [InspectorName("42 짧은 궤적 꼬리")] ProjectileTrail, [InspectorName("43 유도 중 잔광")] HomingTrail, [InspectorName("44 벽 충돌 반응")] WallHit, [InspectorName("45 바닥 충돌 흔적")] GroundHit, [InspectorName("46 반사 전후 색 구별")] ReflectedTrail, [InspectorName("47 관통 통과 섬광")] PierceHit, [InspectorName("48 탄 소멸 수축")] ProjectileExpire,
    [InspectorName("49 조준 확정 신호")] AimLock=49, [InspectorName("50 공격 직전 집중선")] AttackRelease, [InspectorName("51 돌진 출발 흙먼지")] ChargeStart, [InspectorName("52 벽 충돌 파편")] ChargeWall, [InspectorName("53 보스 착지 이중 파동")] BossLand, [InspectorName("54 카운터 성공 파열")] CounterSuccess, [InspectorName("55 은신·등장 영혼 흔적")] BossVanish, [InspectorName("56 보스 처치 수축")] BossDeath,
    [InspectorName("57 상자 틈새 빛")] ChestOpen=57, [InspectorName("58 보상 튀어나오기")] DropLand, [InspectorName("59 희귀도별 반짝임")] RarityGlow, [InspectorName("60 획득 UI 연결")] Pickup, [InspectorName("61 스택 증가 반동")] StackUp, [InspectorName("62 중첩 한도 표시")] StackMax, [InspectorName("63 강화 확정 섬광")] Upgrade, [InspectorName("64 구매 결과 반응")] Purchase,
    [InspectorName("65 포탈 흡입 입자")] PortalAmbient=65, [InspectorName("66 포탈 도착 파동")] PortalArrive, [InspectorName("67 방 클리어 정화")] RoomClear, [InspectorName("68 문 개방 먼지")] DoorOpen, [InspectorName("69 물가 잔물결")] WaterRipple, [InspectorName("70 수풀 스침")] Foliage, [InspectorName("71 국소 먼지·빛줄기")] AmbientDust, [InspectorName("72 파괴 소품 반응")] PropBreak,
    [InspectorName("73 방향성 카메라 반동")] CameraKick=73, [InspectorName("74 중요한 타격만 히트스톱")] HitStop, [InspectorName("75 제한적 슬로모션")] SlowMotion, [InspectorName("76 국소 왜곡 파동")] Distortion, [InspectorName("77 스킬 준비 UI 반짝임")] SkillReady, [InspectorName("78 피해 방향 표시")] HurtDirection, [InspectorName("79 타격음 층 분리")] ImpactSound, [InspectorName("80 연타 소리 미세 변화")] SoundVariation
}

public enum FeedbackShape { [InspectorName("방향 파편")] Rays, [InspectorName("원형 파동")] Ring, [InspectorName("작은 점")] Dots, [InspectorName("십자빛")] Cross, [InspectorName("영혼 연결선")] Link, [InspectorName("모이는 입자")] Inward, [InspectorName("반원 잔광")] Arc, [InspectorName("절단선")] Slash }

[Serializable]
public class FeedbackRecipe
{
    public FeedbackCue cue;
    public string title;
    [TextArea] public string description;
    public bool enabled = true;
    public FeedbackBurst prefab;
    public FeedbackShape shape;
    public Color color = Color.white;
    [Min(0)] public float intensity = 1f;
    [Min(.02f)] public float duration = .24f;
    [Min(0)] public float delay;
    [Range(1,16)] public int count = 5;
    [Min(.01f)] public float radius = .45f;
    [Min(.001f)] public float width = .03125f;
    public Vector2 drift;
    [Range(0,3)] public int minimumTier;
    public bool scaleWithTier;
    [Min(0)] public float interval = .06f;
    public AnimationCurve motion = AnimationCurve.EaseInOut(0,0,1,1);
    public AudioClip sound;
    [Range(0,1)] public float volume = .3f;
    [Range(0,.3f)] public float pitchVariation = .05f;
}

[Serializable]
public class FeedbackTier
{
    public string title;
    [Range(.1f,2)] public float intensity = 1;
    [Range(.1f,2)] public float density = 1;
    [Range(.1f,2)] public float reach = 1;
}

[CreateAssetMenu(menuName="Necromancer/연출/전체 연출 설정",fileName="FeedbackCatalog")]
public class FeedbackCatalog : ScriptableObject
{
    [Header("전체 켜기 / 출력 제한")]
    public bool enabled = true;
    [Range(0,2)] public float intensity = 1;
    [Min(1)] public int simultaneousBursts = 72;
    [Min(1)] public int burstsPerFrame = 12;
    [Min(1)] public int soundsPerFrame = 3;
    [Header("공격 성장 단계 — 그림 배율은 바꾸지 않음")]
    public FeedbackTier[] tiers = {
        new FeedbackTier {title="기본",intensity=.55f,density=.45f,reach=.7f},
        new FeedbackTier {title="강화",intensity=.7f,density=.65f,reach=.85f},
        new FeedbackTier {title="진화",intensity=.85f,density=.85f,reach=1f},
        new FeedbackTier {title="최종",intensity=1f,density=1f,reach=1.1f}
    };
    [Header("번호별 효과 — 체크를 끄면 해당 연출만 중단")]
    public FeedbackRecipe[] effects;
    [Header("프리팹 / 화면과 이동")]
    public FeedbackOverlay overlayPrefab;
    [Range(0,1)] public float lowHealthRatio = .25f;
    [Min(.1f)] public float lowHealthInterval = 1.25f;
    [Min(.05f)] public float ambientInterval = .9f;
    [Min(.05f)] public float trailInterval = .075f;
    [Min(.05f)] public float stepInterval = .23f;
    [Min(.05f)] public float waterInterval = .35f;
    [Min(.01f)] public float dashTrailRampDuration = .3f;
    public Vector2 dashTrailIntervalRange = new Vector2(.6f,1.4f);
    public Color dirtColor = new Color(.75f,.65f,.46f,.8f);
    public Color stoneColor = new Color(.8f,.85f,.9f,.8f);
    public Color waterColor = new Color(.45f,.8f,1f,.7f);
    [Min(0)] public float cameraForce = .25f;
    [Range(0,.1f)] public float hitStopSeconds = .025f;
    [Range(.05f,1)] public float slowMotionScale = .35f;
    [Range(.02f,.5f)] public float slowMotionSeconds = .18f;
    public FeedbackRecipe Find(FeedbackCue cue)
    {
        if(effects != null) foreach(var entry in effects) if(entry != null && entry.cue == cue) return entry;
        return null;
    }
    public FeedbackTier Tier(int tier) => tiers != null && tiers.Length>0 ? tiers[Mathf.Clamp(tier,0,tiers.Length-1)] : null;
}
