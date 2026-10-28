using UnityEngine;

/// <summary>방 출입구 양쪽 기둥과 열린 상태의 빛을 표시한다. 충돌과 이동은 기존 문이 담당한다.</summary>
[ExecuteAlways]
public sealed class PortalDoorVisual : MonoBehaviour
{
    [Header("포탈 스프라이트")]
    [SerializeField] private SpriteRenderer firstPillar;
    [SerializeField] private SpriteRenderer secondPillar;
    [SerializeField] private Sprite inactiveSprite;
    [SerializeField] private Sprite activeSprite;
    [InspectorName("기둥 간격 (유닛)")]
    [Tooltip("기본 3은 벽에 뚫리는 세 타일 폭이다. 기둥 중심 간 거리.")]
    [SerializeField, Min(0.1f)] private float pillarSpacing = 3f;
    [InspectorName("기둥 높이 보정")]
    [Tooltip("기둥의 발이 문 위치에 닿도록 화면 위쪽으로 이동한다.")]
    [SerializeField] private float pillarHeight = -1f;
    [InspectorName("기둥 크기")]
    [SerializeField, Min(0.01f)] private float pillarScale = 1f;

    [Header("열린 문 이펙트")]
    [SerializeField] private GameObject effectRoot;
    [SerializeField] private SpriteRenderer glow;
    [SerializeField] private SpriteRenderer core;
    [SerializeField] private SpriteRenderer[] streaks;
    [InspectorName("이펙트 색상")]
    [SerializeField] private Color effectColor = new Color(0.557f, 0.851f, 0.878f, 1f);
    [InspectorName("빛 폭 (유닛)")]
    [SerializeField, Min(0.01f)] private float effectWidth = 3f;
    [InspectorName("빛 두께 (유닛)")]
    [SerializeField, Min(0.01f)] private float effectThickness = 0.65f;
    [InspectorName("빛 위치 보정")]
    [Tooltip("방향과 무관한 월드 좌표 보정. x는 오른쪽, y는 위쪽이다.")]
    [SerializeField] private Vector2 effectOffset = new Vector2(0f, 0.9f);
    [InspectorName("바깥 빛 투명도")]
    [SerializeField, Range(0f, 1f)] private float glowOpacity = 0.27f;
    [InspectorName("중심 빛 투명도")]
    [SerializeField, Range(0f, 1f)] private float coreOpacity = 0.55f;
    [InspectorName("빛 줄기 투명도")]
    [SerializeField, Range(0f, 1f)] private float streakOpacity = 0.75f;
    [InspectorName("빛 깜박임 속도")]
    [SerializeField, Min(0f)] private float pulseSpeed = 2f;
    [InspectorName("빛 깜박임 크기")]
    [SerializeField, Range(0f, 1f)] private float pulseAmount = 0.18f;
    [InspectorName("줄기 이동 속도")]
    [SerializeField] private float streakSpeed = 0.25f;

    private bool _open = true;
    private bool _needsRefresh;

    private void OnEnable() => Refresh();
    private void OnValidate() => _needsRefresh = true;

    public void SetOpen(bool open)
    {
        _open = open;
        Refresh();
    }

    private void Refresh()
    {
        // 프리팹의 문 루트가 방향에 따라 회전해도 기둥 그림은 세워 둔다.
        Vector3 axis = transform.right.normalized;
        PlacePillar(firstPillar, -axis * (pillarSpacing * 0.5f));
        PlacePillar(secondPillar, axis * (pillarSpacing * 0.5f));
        if (effectRoot == null) return;
        effectRoot.transform.position = transform.position + (Vector3)effectOffset;
        effectRoot.transform.rotation = transform.rotation;
        effectRoot.SetActive(_open);
        SetSize(glow, effectWidth, effectThickness);
        SetSize(core, effectWidth, effectThickness * 0.35f);
        UpdateEffect(0f);
    }

    private void PlacePillar(SpriteRenderer pillar, Vector3 offset)
    {
        if (pillar == null) return;
        pillar.transform.position = transform.position + offset + Vector3.up * pillarHeight;
        pillar.transform.rotation = Quaternion.identity;
        pillar.transform.localScale = Vector3.one * pillarScale;
        pillar.sprite = _open ? activeSprite : inactiveSprite;
    }

    private static void SetSize(SpriteRenderer renderer, float width, float height)
    {
        if (renderer == null || renderer.sprite == null) return;
        Vector2 size = renderer.sprite.bounds.size;
        if (size.x > 0f && size.y > 0f)
            renderer.transform.localScale = new Vector3(width / size.x, height / size.y, 1f);
    }

    private void Update()
    {
        if (_needsRefresh) { _needsRefresh = false; Refresh(); }
        if (_open && Application.isPlaying) UpdateEffect(Time.time);
    }

    private void UpdateEffect(float time)
    {
        float pulse = 1f - pulseAmount * (0.5f + 0.5f * Mathf.Sin(time * pulseSpeed));
        Tint(glow, glowOpacity * pulse);
        Tint(core, coreOpacity * pulse);
        if (streaks == null) return;
        for (int i = 0; i < streaks.Length; i++)
        {
            SpriteRenderer streak = streaks[i];
            if (streak == null) continue;
            float phase = Mathf.Repeat(time * streakSpeed + (float)i / Mathf.Max(1, streaks.Length), 1f);
            streak.transform.localPosition = new Vector3((phase - 0.5f) * effectWidth * 0.8f,
                (i - (streaks.Length - 1) * 0.5f) * effectThickness * 0.22f, 0f);
            SetSize(streak, effectWidth * 0.16f, effectThickness * 0.045f);
            Tint(streak, streakOpacity * Mathf.Sin(phase * Mathf.PI) * pulse);
        }
    }

    private void Tint(SpriteRenderer renderer, float alpha)
    {
        if (renderer != null) renderer.color = new Color(effectColor.r, effectColor.g, effectColor.b, effectColor.a * alpha);
    }
}
