using UnityEngine;

/// <summary>프리팹에 그린 테두리를 밝기만 변화시킨다. 메시·재질을 매 프레임 생성하지 않는다.</summary>
public sealed class AmbientOutlinePulse : MonoBehaviour
{
    [SerializeField, InspectorName("카탈로그 항목")] private ExistingFeedbackCue catalogCue = ExistingFeedbackCue.DoorOutline;
    [SerializeField, InspectorName("테두리 선")] private LineRenderer[] lines;
    [SerializeField, InspectorName("스프라이트 윤곽")] private SpriteRenderer[] spriteOutlines;
    [SerializeField, InspectorName("빛 색상")] private Color color = new Color(.4f, .95f, 1f, .6f);
    [SerializeField, Range(0f, 1f), InspectorName("가장 어두울 때 밝기")] private float minimumBrightness = .55f;
    [SerializeField, Min(.1f), InspectorName("맥동 주기(초)")] private float period = 2f;
    [SerializeField, Min(0f), InspectorName("켜짐·꺼짐 시간(초)")] private float fadeTime = .22f;
    private bool open = true;
    private float alpha, phase;
    public ExistingFeedbackCue CatalogCue => catalogCue;
    public void SetOpen(bool value) => open = value;
    private void OnEnable() { alpha = 0; phase = PolishFeedback.VisualRange(0, Mathf.PI * 2); }
    private void Update()
    {
        bool visible = open && PolishFeedback.ExistingEnabled(catalogCue);
        alpha = fadeTime <= 0 ? (visible ? 1 : 0) : Mathf.MoveTowards(alpha, visible ? 1 : 0, Time.deltaTime / fadeTime);
        float pulse = Mathf.Lerp(minimumBrightness, 1, .5f + .5f * Mathf.Sin(Time.time * Mathf.PI * 2 / Mathf.Max(.1f, period) + phase));
        Color c = color; c.a *= alpha * pulse;
        if (lines != null) foreach (var line in lines) if (line != null)
        {
            line.enabled = c.a > .001f;
            if (line.enabled) line.startColor = line.endColor = c;
        }
        if (spriteOutlines != null) foreach (var sprite in spriteOutlines) if (sprite != null)
        {
            sprite.enabled = c.a > .001f;
            if (sprite.enabled) sprite.color = c;
        }
    }
}
