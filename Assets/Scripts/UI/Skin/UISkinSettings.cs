using System;
using UnityEngine;

/// <summary>스킨 이미지(UISkinPart)가 어떤 모양을 쓸지 고르는 역할 목록.</summary>
public enum UISkinRole
{
    [InspectorName("큰 창 (장식 테두리)")]        WindowFrame,
    [InspectorName("안쪽 패널")]                  InnerPanel,
    [InspectorName("툴팁 배경")]                  TooltipBack,
    [InspectorName("탭 버튼 · 바깥 테두리")]       TabOuter,
    [InspectorName("탭 버튼 · 안쪽 면")]           TabFace,
    [InspectorName("작은 버튼")]                  SmallButton,
    [InspectorName("구분선")]                     Divider,
    [InspectorName("체력바 테두리")]               HpBarFrame,
    [InspectorName("가드 게이지 테두리")]          GaugeFrame,
    [InspectorName("게이지 바탕 (빈 칸)")]         GaugeTrack,
    [InspectorName("게이지 채움")]                 GaugeFill,
    [InspectorName("스킬 칸 바탕")]               SkillSlotBack,
    [InspectorName("스킬 칸 테두리")]             SkillSlotFrame,
    [InspectorName("미니맵 테두리")]              MiniMapFrame,
    [InspectorName("아이템 칸 (등급색이 입혀짐)")]  ItemSlot,
    [InspectorName("아이템 상세 아이콘 테두리")]    ItemDetailFrame,
    [InspectorName("선택 표시 테두리")]            SelectionFrame,
    [InspectorName("작은 아이콘 칸")]             SmallIconSlot,
}

/// <summary>
/// UI 스킨 + 연출 설정. 기획자가 이 파일 하나에서 모양(스프라이트·색)과 타이밍·느낌을 조절한다.
/// 위치: Assets/Resources/UI/UISkinSettings.asset  (메뉴: Tools > UI 스킨)
///
/// · 플레이 중에 값을 바꿔도 바로 화면에 반영되고, 그대로 저장된다(ScriptableObject).
/// · 스프라이트 칸은 UI_Panel.png 의 조각이 기본으로 들어가 있다. 다른 그림으로 바꿔 끼워도 된다.
/// </summary>
[CreateAssetMenu(fileName = "UISkinSettings", menuName = "UI 스킨/UI 스킨 설정", order = 0)]
public class UISkinSettings : ScriptableObject
{
    public const string ResourcePath = "UI/UISkinSettings";

    private static UISkinSettings _active;
    /// <summary>게임이 쓰는 기본 설정 (Resources/UI/UISkinSettings).</summary>
    public static UISkinSettings Active
    {
        get
        {
            if (_active == null) _active = Resources.Load<UISkinSettings>(ResourcePath);
            return _active;
        }
    }

    /// <summary>설정 값이 바뀌면 화면의 스킨 이미지들이 다시 칠해진다.</summary>
    public static event Action Changed;

    // ─────────────────────────────────────────────────────────────────
    [Serializable]
    public class SkinEntry
    {
        public enum DrawMode
        {
            [InspectorName("9분할로 늘이기 (테두리 두께 유지)")] Sliced,
            [InspectorName("통째로 늘이기")] Simple,
            [InspectorName("반복해서 깔기")] Tiled,
        }

        [KoLabel("스프라이트"), Tooltip("비워 두면 그림 없이 아래 '색'으로 칠한 단색 사각형이 됩니다.")]
        public Sprite sprite;
        [KoLabel("색"), Tooltip("그림에 곱해지는 색입니다. 흰색이면 원본 그대로, 어두운 색일수록 어두워집니다. 알파(A)로 반투명하게 만들 수 있습니다.")]
        public Color color = Color.white;
        [KoLabel("그리는 방식"), Tooltip("창·테두리는 '9분할'이 좋습니다. 크기가 달라져도 모서리 장식이 찌그러지지 않습니다.")]
        public DrawMode drawMode = DrawMode.Sliced;
        [KoLabel("테두리 굵기 배율", 0.5f, 4f), Tooltip("1 = 그림 1픽셀이 UI 1칸(가장 선명). 2로 올리면 테두리·장식이 두 배로 굵어집니다.")]
        public float pixelScale = 1f;

        public SkinEntry() { }
        public SkinEntry(Color color) { this.color = color; }
    }

    // ─── ① 모양 ─────────────────────────────────────────────────────
    [Header("① 창 · 패널 모양")]
    [KoLabel("큰 창 (장식 테두리)"), Tooltip("아이템 주머니, 장착 정보 같은 큰 창의 바깥 틀")] public SkinEntry windowFrame = new SkinEntry();
    [KoLabel("안쪽 패널"), Tooltip("큰 창 안에 들어가는 어두운 칸들")] public SkinEntry innerPanel = new SkinEntry();
    [KoLabel("툴팁 배경")] public SkinEntry tooltipBack = new SkinEntry();
    [KoLabel("탭 버튼 · 바깥 테두리"), Tooltip("주머니의 정렬 버튼(획득순/개수순/등급순)")] public SkinEntry tabOuter = new SkinEntry();
    [KoLabel("탭 버튼 · 안쪽 면"), Tooltip("선택/비선택 색은 PouchUI 의 정렬 버튼 색이 곱해집니다.")] public SkinEntry tabFace = new SkinEntry();
    [KoLabel("작은 버튼")] public SkinEntry smallButton = new SkinEntry();
    [KoLabel("구분선")] public SkinEntry divider = new SkinEntry();

    [Header("② 플레이어 HUD 모양")]
    [KoLabel("체력바 테두리")] public SkinEntry hpBarFrame = new SkinEntry();
    [KoLabel("가드 게이지 테두리")] public SkinEntry gaugeFrame = new SkinEntry();
    [KoLabel("게이지 바탕 (빈 칸)")] public SkinEntry gaugeTrack = new SkinEntry();
    [KoLabel("게이지 채움"), Tooltip("체력·보호막·가드 공용. 흰 그림이라 각 바의 색(체력 빨강, 가드 파랑)이 그대로 입혀집니다.")] public SkinEntry gaugeFill = new SkinEntry();
    [KoLabel("스킬 칸 바탕")] public SkinEntry skillSlotBack = new SkinEntry();
    [KoLabel("스킬 칸 테두리")] public SkinEntry skillSlotFrame = new SkinEntry();

    [Header("③ 미니맵 · 아이템 모양")]
    [KoLabel("미니맵 테두리")] public SkinEntry miniMapFrame = new SkinEntry();
    [KoLabel("아이템 칸"), Tooltip("아이템 등급색이 곱해집니다. 밝은 그림을 쓰세요.")] public SkinEntry itemSlot = new SkinEntry();
    [KoLabel("아이템 상세 아이콘 테두리")] public SkinEntry itemDetailFrame = new SkinEntry();
    [KoLabel("선택 표시 테두리")] public SkinEntry selectionFrame = new SkinEntry();
    [KoLabel("작은 아이콘 칸"), Tooltip("장착 정보의 소환수 아이콘 칸 등")] public SkinEntry smallIconSlot = new SkinEntry();

    // ─── ② 연출 ─────────────────────────────────────────────────────
    [Header("④ 연출 · 공통")]
    [KoLabel("연출 전체 켜기"), Tooltip("끄면 아래 모든 움직임(창 열림, 번쩍임, 튀어오름)이 멈추고 모양만 남습니다.")]
    public bool motionEnabled = true;
    [KoLabel("일시정지 중에도 재생"), Tooltip("켜 두면 게임 시간이 멈춰도(옵션창, 히트스톱) UI 연출은 계속 움직입니다.")]
    public bool useUnscaledTime = true;
    [KoLabel("툭 튀는 모양 곡선"), Tooltip("가로: 진행(0→1), 세로: 튀는 정도(0=원래 크기, 1=최대). 0에서 시작해 0으로 끝나야 자연스럽습니다.")]
    public AnimationCurve punchCurve = MotionCurves.Punch();

    [Header("⑤ 창 열림 연출")]
    [KoLabel("큰 창이 열릴 때")] public OpenMotion windowOpen = OpenMotion.Window();
    [KoLabel("작은 창 · 툴팁이 뜰 때")] public OpenMotion popupOpen = OpenMotion.Popup();

    [Header("⑥ 체력 · 게이지 연출")]
    [KoLabel("체력바")] public BarMotion hpBar = BarMotion.Hp();
    [KoLabel("가드 게이지")] public BarMotion guardBar = BarMotion.Guard();

    [Header("⑦ 스킬 아이콘 연출")]
    [KoLabel("스킬 아이콘")] public SkillMotion skill = new SkillMotion();

    [Header("⑧ 마우스 반응")]
    [KoLabel("아이템 칸 · 버튼")] public HoverMotion hover = new HoverMotion();

    // ─────────────────────────────────────────────────────────────────
    public SkinEntry Get(UISkinRole role)
    {
        switch (role)
        {
            case UISkinRole.WindowFrame:     return windowFrame;
            case UISkinRole.InnerPanel:      return innerPanel;
            case UISkinRole.TooltipBack:     return tooltipBack;
            case UISkinRole.TabOuter:        return tabOuter;
            case UISkinRole.TabFace:         return tabFace;
            case UISkinRole.SmallButton:     return smallButton;
            case UISkinRole.Divider:         return divider;
            case UISkinRole.HpBarFrame:      return hpBarFrame;
            case UISkinRole.GaugeFrame:      return gaugeFrame;
            case UISkinRole.GaugeTrack:      return gaugeTrack;
            case UISkinRole.GaugeFill:       return gaugeFill;
            case UISkinRole.SkillSlotBack:   return skillSlotBack;
            case UISkinRole.SkillSlotFrame:  return skillSlotFrame;
            case UISkinRole.MiniMapFrame:    return miniMapFrame;
            case UISkinRole.ItemSlot:        return itemSlot;
            case UISkinRole.ItemDetailFrame: return itemDetailFrame;
            case UISkinRole.SelectionFrame:  return selectionFrame;
            case UISkinRole.SmallIconSlot:   return smallIconSlot;
        }
        return null;
    }

    /// <summary>연출이 쓰는 한 프레임 시간. '일시정지 중에도 재생'을 따른다.</summary>
    public static float DeltaTime
    {
        get
        {
            var s = Active;
            return s == null || s.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }
    }

    /// <summary>연출을 해도 되는가 (설정 파일이 없으면 기본값으로 켬).</summary>
    public static bool MotionOn => Active == null || Active.motionEnabled;

    public static float EvaluatePunch(float t)
    {
        var s = Active;
        var curve = s != null ? s.punchCurve : null;
        if (curve == null || curve.length == 0) return Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        return curve.Evaluate(Mathf.Clamp01(t));
    }

    public static void NotifyChanged() => Changed?.Invoke();

    private void OnValidate() => NotifyChanged();
}

// ─────────────────────────────────────────────────────────────────────
// 연출 값 묶음
// ─────────────────────────────────────────────────────────────────────

[Serializable]
public class OpenMotion
{
    [KoLabel("사용")] public bool enabled = true;
    [KoLabel("기다렸다 시작 (초)", 0f, 1f), Tooltip("창이 켜지고 나서 연출이 시작되기까지의 시간. 그동안은 시작 모습으로 멈춰 있습니다.")]
    public float delay = 0f;
    [KoLabel("걸리는 시간 (초)", 0.01f, 1.5f)] public float duration = 0.18f;
    [KoLabel("시작 크기", 0.3f, 1.5f), Tooltip("1보다 작으면 작게 시작해 커지고, 1보다 크면 크게 시작해 줄어듭니다. 1이면 크기 변화 없음.")]
    public float startScale = 0.92f;
    [KoLabel("크기 변화 곡선"), Tooltip("가로: 진행(0→1), 세로: 진행률(0=시작 크기, 1=원래 크기). 1을 살짝 넘었다 돌아오면 '통' 튀는 느낌이 납니다.")]
    public AnimationCurve scaleCurve = MotionCurves.Overshoot();
    [KoLabel("시작 투명도", 0f, 1f), Tooltip("0이면 완전히 투명한 상태에서 나타납니다. 1이면 투명도 변화 없음.")]
    public float startAlpha = 0f;
    [KoLabel("투명도 변화 곡선")] public AnimationCurve alphaCurve = MotionCurves.EaseOut();
    [KoLabel("시작 위치 차이 (UI 칸)"), Tooltip("(0, -10)이면 10칸 아래에서 올라옵니다. 위치를 코드가 정하는 창(툴팁)은 (0, 0)으로 두세요.")]
    public Vector2 startOffset = Vector2.zero;

    public static OpenMotion Window() => new OpenMotion { duration = 0.2f, startScale = 0.92f, startAlpha = 0f, startOffset = new Vector2(0f, -10f) };
    public static OpenMotion Popup() => new OpenMotion { duration = 0.1f, startScale = 0.85f, startAlpha = 0f, startOffset = Vector2.zero };
}

[Serializable]
public class BarMotion
{
    [Header("피해 잔상 — 깎인 만큼 잠시 남았다가 따라 줄어드는 바")]
    [KoLabel("잔상 사용")] public bool trail = true;
    [KoLabel("잔상 색")] public Color trailColor = new Color(1f, 0.92f, 0.75f, 1f);
    [KoLabel("잔상이 머무는 시간 (초)", 0f, 2f)] public float trailHold = 0.35f;
    [KoLabel("잔상이 줄어드는 시간 (초)", 0.01f, 2f)] public float trailShrink = 0.35f;
    [KoLabel("잔상 줄어드는 곡선"), Tooltip("가로: 진행(0→1), 세로: 줄어든 정도(0=그대로, 1=현재 값까지 다 줄어듦).")]
    public AnimationCurve trailCurve = MotionCurves.EaseInOut();

    [Header("깎였을 때 (피격 · 가드 소모)")]
    [KoLabel("테두리 번쩍임 색")] public Color hitFlashColor = Color.white;
    [KoLabel("번쩍임 시간 (초)", 0f, 1f), Tooltip("0이면 번쩍이지 않습니다.")] public float hitFlashTime = 0.15f;
    [KoLabel("툭 튀는 크기", 1f, 1.3f), Tooltip("1이면 튀지 않습니다. 1.05 = 5% 커졌다 돌아옴.")] public float hitPunch = 1.04f;
    [KoLabel("툭 튀는 시간 (초)", 0f, 1f)] public float hitPunchTime = 0.12f;

    [Header("차올랐을 때 (회복)")]
    [KoLabel("테두리 번쩍임 색")] public Color healFlashColor = new Color(0.55f, 1f, 0.6f, 1f);
    [KoLabel("번쩍임 시간 (초)", 0f, 1f), Tooltip("0이면 번쩍이지 않습니다. 가드처럼 천천히 계속 차는 게이지는 0을 권장합니다.")]
    public float healFlashTime = 0.3f;

    [Header("보호막이 생길 때 (체력바 전용)")]
    [KoLabel("테두리 번쩍임 색")] public Color shieldFlashColor = Color.white;
    [KoLabel("번쩍임 시간 (초)", 0f, 1f)] public float shieldFlashTime = 0.25f;
    [KoLabel("툭 튀는 크기", 1f, 1.3f)] public float shieldPunch = 1.06f;

    [Header("위험 경고 — 바가 적게 남으면 테두리가 깜빡임")]
    [KoLabel("경고 사용")] public bool danger = true;
    [KoLabel("경고 시작 비율", 0f, 1f), Tooltip("0.3이면 바가 30% 이하로 남았을 때 깜빡이기 시작합니다.")] public float dangerThreshold = 0.3f;
    [KoLabel("한 번 깜빡이는 시간 (초)", 0.1f, 3f)] public float dangerPeriod = 0.8f;
    [KoLabel("경고 색")] public Color dangerColor = new Color(1f, 0.25f, 0.25f, 1f);
    [KoLabel("경고 세기", 0f, 1f), Tooltip("0=티 안 남, 1=가장 진하게 경고 색으로 바뀜.")] public float dangerStrength = 0.7f;

    public static BarMotion Hp() => new BarMotion();
    public static BarMotion Guard() => new BarMotion
    {
        trailColor = new Color(0.75f, 0.95f, 1f, 1f),
        trailHold = 0.25f, trailShrink = 0.3f,
        hitFlashColor = new Color(0.75f, 0.95f, 1f, 1f), hitFlashTime = 0.12f,
        hitPunch = 1.03f, hitPunchTime = 0.1f,
        healFlashTime = 0f,
        shieldFlashTime = 0f, shieldPunch = 1f,
        danger = false,
    };
}

[Serializable]
public class SkillMotion
{
    [KoLabel("사용")] public bool enabled = true;
    [KoLabel("쿨타임 중 테두리 색"), Tooltip("쿨타임이 도는 동안 테두리를 이 색으로 바꿔 '아직 못 씀'을 보여줍니다. 흰색이면 바뀌지 않습니다.")]
    public Color coolingFrameColor = new Color(0.45f, 0.4f, 0.5f, 1f);
    [KoLabel("준비 완료 번쩍임 색")] public Color readyFlashColor = new Color(1f, 0.95f, 0.7f, 1f);
    [KoLabel("번쩍임 시간 (초)", 0f, 1.5f)] public float readyFlashTime = 0.35f;
    [KoLabel("튀어오르는 크기", 1f, 1.6f), Tooltip("쿨타임이 끝나는 순간 아이콘이 이만큼 커졌다 돌아옵니다. 1이면 튀지 않습니다.")]
    public float readyPunch = 1.18f;
    [KoLabel("튀어오르는 시간 (초)", 0f, 1f)] public float readyPunchTime = 0.22f;
}

[Serializable]
public class HoverMotion
{
    [KoLabel("사용")] public bool enabled = true;
    [KoLabel("마우스를 올렸을 때 높이 (UI 픽셀)", 0f, 12f)] public float liftPixels = 3f;
    [KoLabel("마우스를 올렸을 때 크기", 0.8f, 1.5f)] public float hoverScale = 1.06f;
    [KoLabel("눌렀을 때 크기", 0.6f, 1.2f)] public float pressScale = 0.94f;
    [KoLabel("크기가 바뀌는 시간 (초)", 0f, 0.5f), Tooltip("0이면 즉시 바뀝니다.")] public float time = 0.08f;
}

/// <summary>기본 곡선 모음.</summary>
public static class MotionCurves
{
    public static AnimationCurve Overshoot() => new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 3f), new Keyframe(0.65f, 1.06f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));
    public static AnimationCurve EaseOut() => new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));
    public static AnimationCurve EaseInOut() => AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public static AnimationCurve Punch() => new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 6f), new Keyframe(0.3f, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));
}
