using UnityEngine;

/// <summary>
/// 타이틀 '수정구슬' 연출 설정. (TitleCrystalBall 이 읽는다)
/// 에셋이라서 Play 중에 바꾼 값도 Play 를 꺼도 그대로 남는다.
/// 거리 단위: 특별한 말이 없으면 '그림 높이 대비 비율' (0.1 = 그림 높이의 10%).
/// </summary>
[CreateAssetMenu(fileName = "TitleCrystalBallSettings", menuName = "Settings/Title Crystal Ball Settings")]
public class TitleCrystalBallSettingsSO : ScriptableObject
{
    [System.Serializable]
    public class GlassLayer
    {
        [Tooltip("이 원을 켜고 끈다")]
        public bool enabled = true;
        [Tooltip("구슬 중심에서 얼마나 떨어져 있는지 (구슬 반지름 대비). X- 왼쪽, Y+ 위")]
        public Vector2 offset;
        [Tooltip("원 크기 (구슬 반지름 대비). 1 = 구슬과 같은 크기")]
        [Range(0.1f, 1.5f)] public float radius = 0.85f;
        [Tooltip("원 바깥을 덮는 색 (보통 검정)")]
        public Color color = Color.black;
        [Tooltip("원 바깥을 얼마나 어둡게 덮을지. 0 = 효과 없음, 1 = 완전히 덮음")]
        [Range(0f, 1f)] public float alpha = 0.3f;
        [Tooltip("마우스에 따라 움직이는 최대 거리. 클수록 '앞쪽' 층처럼 보인다")]
        [Range(0f, 0.3f)] public float parallax = 0.04f;
    }

    [Header("그림")]
    [Tooltip("구슬 안에 보일 그림")]
    public Sprite titleSprite;
    [Tooltip("그림 밝기. 1 = 원본")]
    [Range(0f, 2f)] public float imageBrightness = 1f;
    [Tooltip("그림에 곱해지는 색 (흰색 = 원본)")]
    public Color imageTint = Color.white;
    [Tooltip("구슬에서 멀어질수록 이 색으로 사라진다")]
    public Color backgroundColor = Color.black;

    [Header("구슬 바깥")]
    [Tooltip("구슬 테두리 바로 바깥의 그림 밝기. 0 = 바로 검정, 1 = 구슬 안과 같은 밝기")]
    [Range(0f, 1f)] public float outsideBrightness = 0.35f;
    [Tooltip("구슬 테두리에서 이만큼 멀어지면 완전히 배경색이 된다 (그림 높이 대비)")]
    [Range(0.01f, 2f)] public float outsideFade = 0.5f;
    [Tooltip("어두워지는 곡선. 1 = 일정하게, 클수록 테두리 근처는 밝게 유지되다가 멀리서 급하게 어두워짐")]
    [Range(0.2f, 4f)] public float outsideFadeCurve = 1.5f;

    [Header("구슬")]
    [Tooltip("구슬 기본 위치. (0,0) = 그림 왼쪽 아래, (1,1) = 오른쪽 위")]
    public Vector2 orbCenter = new Vector2(0.735f, 0.5f);
    [Tooltip("구슬 크기 (반지름, 그림 높이 대비). 0.5 = 화면 위아래에 딱 닿음")]
    [Range(0.05f, 1.5f)] public float orbRadius = 0.61f;

    [Header("둘러보기 (마우스)")]
    [Tooltip("마우스가 구슬 중심에서 이만큼 떨어지면 최대로 둘러본다. 더 멀어져도 더 움직이지 않음")]
    [Range(0.05f, 2f)] public float lookRadius = 0.6f;
    [Tooltip("구슬 자체가 움직이는 최대 거리")]
    [Range(0f, 0.3f)] public float orbMove = 0.03f;
    [Tooltip("구슬 안 그림이 움직이는 최대 거리. 마우스 쪽 풍경이 더 드러난다")]
    [Range(0f, 0.3f)] public float imagePan = 0.06f;
    [Tooltip("따라오는 속도(초). 클수록 느긋하게 따라온다")]
    [Range(0.01f, 2f)] public float smoothTime = 0.35f;
    [Tooltip("가만히 있어도 살짝 떠다니는 정도 (0 = 끔)")]
    [Range(0f, 1f)] public float idleBob = 0.08f;
    [Tooltip("떠다니는 속도")]
    [Range(0f, 3f)] public float idleBobSpeed = 1f;

    [Header("어둠 원 (원 바깥이 어두워짐, 2겹이 겹친 안쪽이 가장 밝음)")]
    public GlassLayer layer1 = new GlassLayer
        { offset = new Vector2(-0.15f, 0.01f), radius = 0.85f, alpha = 0.3f, parallax = 0.03f };
    public GlassLayer layer2 = new GlassLayer
        { offset = new Vector2(-0.30f, 0.05f), radius = 0.70f, alpha = 0.3f, parallax = 0.07f };

    /// <summary>값이 바뀌면 TitleCrystalBall 이 즉시 다시 그리도록 알림.</summary>
    public event System.Action Changed;

#if UNITY_EDITOR
    private void OnValidate() => Changed?.Invoke();
#endif

    /// <summary>처음 기본값으로 되돌린다 (그림은 유지).</summary>
    public void ResetToDefaults()
    {
        var sprite = titleSprite;
        var fresh = CreateInstance<TitleCrystalBallSettingsSO>();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), this);
        titleSprite = sprite;
        if (Application.isPlaying) Destroy(fresh); else DestroyImmediate(fresh);
        Changed?.Invoke();
    }
}
