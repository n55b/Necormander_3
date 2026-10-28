using System;
using System.Collections.Generic;
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
    [InspectorName("73 방향성 카메라 반동")] CameraKick=73, [InspectorName("74 중요한 타격만 히트스톱")] HitStop, [InspectorName("75 제한적 슬로모션")] SlowMotion, [InspectorName("76 국소 왜곡 파동")] Distortion, [InspectorName("77 스킬 준비 UI 반짝임")] SkillReady, [InspectorName("78 피해 방향 표시")] HurtDirection, [InspectorName("79 타격음 층 분리")] ImpactSound, [InspectorName("80 연타 소리 미세 변화")] SoundVariation,
    [InspectorName("81 바닥 아이템 반짝임")] DropGlimmer = 81,
    [InspectorName("82 퍼펙트 가드 섬광")] PerfectGuardFlash = 82,
    [InspectorName("83 퍼펙트 가드 파동")] PerfectGuardRing = 83
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

public enum ExistingFeedbackCue
{
    [InspectorName("연결 안 함")] None,
    [InspectorName("최종형 슬래시")] Slash,
    [InspectorName("스쿼시 · 스트레치")] Squash,
    [InspectorName("피격 · 회복 섬광")] Flash,
    [InspectorName("걷기 연기")] WalkSmoke,
    [InspectorName("대시 연기")] DashSmoke,
    [InspectorName("대시 · 돌진 잔상")] Afterimage,
    [InspectorName("가림 실루엣")] Silhouette,
    [InspectorName("도트 디더링")] Dither,
    [InspectorName("화면 · UI 페이드")] Fade,
    [InspectorName("UI 흔들림")] UIShake,
    [InspectorName("카메라 흔들림")] CameraShake,
    [InspectorName("히트스톱")] HitStop
}

[Serializable]
public class ExistingFeedbackEntry
{
    public ExistingFeedbackCue cue;
    public bool enabled = true;
    public string title;
    [TextArea] public string description;
    // 수치를 복제하지 않는다. Catalog 인스펙터에서 이 원본들을 직접 편집한다.
#if UNITY_EDITOR
    public UnityEngine.Object[] sources;
#endif
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
    [Header("기존 연출 — 원본 설정 직접 조절")]
    public ExistingFeedbackEntry[] existingEffects;
    [Header("프리팹 / 화면과 이동")]
    public FeedbackOverlay overlayPrefab;
    [Header("로딩 중 이펙트 사전 준비")]
    [InspectorName("공용 효과 미리 준비할 개수")]
    [Tooltip("처음 타격 때 생길 생성 비용을 로딩 중에 나눠 처리한다. 0이면 사전 준비하지 않는다.")]
    [Range(0, 24)] public int prewarmBurstCount = 6;
    [InspectorName("미니언 타격 효과 미리 준비할 개수")]
    [Tooltip("등록된 미니언과 진화 형태의 타격 이펙트를 종류별로 미리 준비한다. 0이면 사전 준비하지 않는다.")]
    [Range(0, 24)] public int prewarmImpactCount = 6;
    [InspectorName("일반 타격 미리 준비할 개수")]
    [Tooltip("일반 타격 그림과 파티클을 각각 이 개수만큼 로딩 중 준비한다. 0이면 끈다.")]
    [Range(0, 24)] public int prewarmHitCount = 6;
    [InspectorName("미리 준비할 일반 타격 프리팹")]
    public PooledHitEffect[] prewarmHitPrefabs;
    [InspectorName("이동·대시 연기 미리 준비할 개수")]
    [Tooltip("걷기와 대시 연기를 각각 로딩 중 준비합니다. 0이면 기존처럼 사용할 때 생성합니다. DataManager의 전체 준비 상한도 적용됩니다.")]
    [Range(0, 24)] public int prewarmMovementCount = 6;
    [InspectorName("빙결 연출 미리 준비할 개수")]
    [Tooltip("등장 가능한 몬스터에 연결된 빙결 이펙트를 로딩 중 준비합니다. 0이면 사용할 때 생성합니다.")]
    [Range(0, 24)] public int prewarmFreezeCount = 3;
    [InspectorName("대시 잔상 미리 준비할 개수")]
    [Tooltip("각 캐릭터가 활성화된 뒤 잔상을 나눠 만든다. 0이면 첫 대시 때 생성한다.")]
    [Range(0, 8)] public int prewarmGhostCount = 2;
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
    private Dictionary<FeedbackCue, FeedbackRecipe> _effectLookup;
    private Dictionary<ExistingFeedbackCue, ExistingFeedbackEntry> _existingLookup;
    private FeedbackRecipe[] _cachedEffects;
    private ExistingFeedbackEntry[] _cachedExisting;

    private void OnEnable() => ClearLookups();
#if UNITY_EDITOR
    private void OnValidate() => ClearLookups();
#endif
    private void ClearLookups() { _effectLookup = null; _existingLookup = null; _cachedEffects = null; _cachedExisting = null; }

    public FeedbackRecipe Find(FeedbackCue cue)
    {
        if (_effectLookup == null || _cachedEffects != effects)
        {
            _effectLookup = new Dictionary<FeedbackCue, FeedbackRecipe>();
            _cachedEffects = effects;
            if (effects != null) foreach (var entry in effects)
                if (entry != null && !_effectLookup.ContainsKey(entry.cue)) _effectLookup.Add(entry.cue, entry);
        }
        return _effectLookup.TryGetValue(cue, out var result) ? result : null;
    }
    public FeedbackTier Tier(int tier) => tiers != null && tiers.Length>0 ? tiers[Mathf.Clamp(tier,0,tiers.Length-1)] : null;
    public bool ExistingEnabled(ExistingFeedbackCue cue)
    {
        if (!enabled) return false;
        if (_existingLookup == null || _cachedExisting != existingEffects)
        {
            _existingLookup = new Dictionary<ExistingFeedbackCue, ExistingFeedbackEntry>();
            _cachedExisting = existingEffects;
            if (existingEffects != null) foreach (var entry in existingEffects)
                if (entry != null && !_existingLookup.ContainsKey(entry.cue)) _existingLookup.Add(entry.cue, entry);
        }
        if (_existingLookup.TryGetValue(cue, out var result)) return result.enabled;
        return true; // 아직 연결하지 않은 이전 에셋의 동작은 보존한다.
    }
}
