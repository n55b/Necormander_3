using UnityEngine;

/// <summary>
/// 플로팅 텍스트 타입별 스타일 정의.
/// 생성: Assets 우클릭 → Create → UI → FloatingTextStyle
/// </summary>
[CreateAssetMenu(menuName = "UI/FloatingTextStyle", fileName = "FloatingTextStyle")]
public class FloatingTextStyleSO : ScriptableObject
{
    [Header("식별자")]
    [Tooltip("FloatingTextSpawner에서 이 이름으로 스타일을 찾습니다 (예: \"Poison\", \"Critical\")")]
    public string typeKey;

    [Header("색상")]
    public Color color = Color.white;

    [Header("크기")]
    [Tooltip("기본 스케일 배율 (1 = 기본 크기)")]
    public float scale = 1f;

    [Header("아웃라인")]
    public Color outlineColor = Color.black;
    [Range(0f, 0.5f)] public float outlineWidth = 0.2f;

    [Header("연출")]
    [Tooltip("등장 시 펀치(통통 튀는) 애니메이션 강도. 0이면 펀치 없음")]
    public float punchStrength = 1f;

    [Tooltip("등장 시 회전 흔들림 사용 여부 (크리티컬 등 강한 타격감용)")]
    public bool useShakeRotation = false;

    [Tooltip("회전 흔들림 강도 (useShakeRotation이 true일 때만 사용)")]
    public float shakeStrength = 30f;
}
