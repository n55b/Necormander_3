using UnityEngine;

/// <summary>
/// 버튼에 마우스를 올리면 아래변에 밑줄이 차오르는 연출 설정. (ButtonHoverUnderline 이 읽는다)
/// 여러 버튼이 이 에셋 하나를 같이 쓰므로, 한 번 바꾸면 연결된 버튼 전부에 적용된다.
/// 에셋이라서 Play 중에 바꾼 값도 Play 를 꺼도 그대로 남는다.
/// 길이/두께/위치 단위: 캔버스 픽셀 (CanvasScaler 기준 해상도).
/// </summary>
[CreateAssetMenu(fileName = "ButtonHoverUnderlineSettings", menuName = "Settings/Button Hover Underline Settings")]
public class ButtonHoverUnderlineSettingsSO : ScriptableObject
{
    public enum FillDirection
    {
        [InspectorName("왼쪽 → 오른쪽")] LeftToRight,
        [InspectorName("오른쪽 → 왼쪽")] RightToLeft,
        [InspectorName("가운데 → 양옆")] CenterOut,
    }

    public enum HideStyle
    {
        [InspectorName("되감기 (들어온 쪽으로 줄어듦)")] Rewind,
        [InspectorName("계속 진행 (반대쪽으로 빠져나감)")] SweepOut,
        [InspectorName("흐려지며 사라짐")] Fade,
        [InspectorName("즉시 사라짐")] Instant,
    }

    [Header("모양")]
    [Tooltip("밑줄 색")]
    public Color color = Color.white;
    [Tooltip("밑줄 두께 (캔버스 픽셀)")]
    [Range(0.5f, 20f)] public float thickness = 3f;
    [Tooltip("버튼 아래변 기준 위아래 위치 (캔버스 픽셀). 0 = 아래변에 딱 붙음, - = 더 아래, + = 위로")]
    [Range(-40f, 40f)] public float offsetY = 0f;
    [Tooltip("밑줄 그림 (비워두면 단색 막대)")]
    public Sprite sprite;

    [Header("길이")]
    [Tooltip("다 찼을 때 길이 (버튼 너비 대비). 1 = 버튼 아래변 전체")]
    [Range(0f, 1.5f)] public float lengthRatio = 1f;
    [Tooltip("양 끝에 더 늘리는 길이 (캔버스 픽셀, 한쪽당). - 로 하면 양쪽이 짧아짐")]
    [Range(-100f, 100f)] public float extraLength = 0f;
    [Tooltip("길이가 버튼보다 짧을 때 어디에 붙을지. 0 = 왼쪽, 0.5 = 가운데, 1 = 오른쪽")]
    [Range(0f, 1f)] public float align = 0.5f;

    [Header("나타날 때")]
    [Tooltip("차오르는 방향")]
    public FillDirection direction = FillDirection.LeftToRight;
    [Tooltip("끝까지 차오르는 데 걸리는 시간 (초). 작을수록 빠름")]
    [Range(0.01f, 2f)] public float showDuration = 0.25f;
    [Tooltip("차오르는 속도 변화. 가로 = 시간, 세로 = 채워진 정도 (0→1)")]
    public AnimationCurve showCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));
    [Tooltip("마우스를 올리고 나서 차오르기 시작할 때까지 기다리는 시간 (초)")]
    [Range(0f, 1f)] public float showDelay = 0f;

    [Header("사라질 때")]
    [Tooltip("마우스가 벗어났을 때 사라지는 방식")]
    public HideStyle hideStyle = HideStyle.Rewind;
    [Tooltip("다 사라지는 데 걸리는 시간 (초)")]
    [Range(0.01f, 2f)] public float hideDuration = 0.18f;
    [Tooltip("사라지는 속도 변화. 가로 = 시간, 세로 = 진행 정도 (0→1)")]
    public AnimationCurve hideCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));

    [Header("기타")]
    [Tooltip("키보드/패드로 버튼이 선택됐을 때도 밑줄을 보여줄지")]
    public bool showOnSelect = true;
    [Tooltip("게임이 일시정지(Time.timeScale = 0)여도 움직이게 할지")]
    public bool ignoreTimeScale = true;

    /// <summary>값이 바뀌면 연결된 버튼들이 즉시 다시 그리도록 알림.</summary>
    public event System.Action Changed;

    public void NotifyChanged() => Changed?.Invoke();

#if UNITY_EDITOR
    private void OnValidate() => Changed?.Invoke();
#endif

    /// <summary>처음 기본값으로 되돌린다 (그림은 유지).</summary>
    public void ResetToDefaults()
    {
        var keepSprite = sprite;
        var fresh = CreateInstance<ButtonHoverUnderlineSettingsSO>();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), this);
        sprite = keepSprite;
        if (Application.isPlaying) Destroy(fresh); else DestroyImmediate(fresh);
        Changed?.Invoke();
    }
}
