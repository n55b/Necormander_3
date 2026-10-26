using UnityEngine;

/// <summary>프리팹에 배치한 슬래시·발광·짧은 선 파편을 재생한다. 피해/카메라/시간은 변경하지 않는다.</summary>
public class ImpactFeedback : MonoBehaviour
{
    [Min(.01f), SerializeField] private float lifetime = .3f;
    [SerializeField] private Animator spriteAnimator;
    [SerializeField] private SpriteRenderer slash;
    [SerializeField] private Transform spritePulse;
    [SerializeField] private LineRenderer[] streaks;
    [SerializeField] private LineRenderer[] arcs;
    [SerializeField] private LineRenderer flash;
    [SerializeField] private SpriteRenderer[] echoes;
    [Min(.01f), SerializeField] private float echoInterval = .055f;
    [SerializeField] private AnimationCurve expansion = AnimationCurve.EaseInOut(0f, .65f, 1f, 1.25f);
    [SerializeField] private AnimationCurve emission = AnimationCurve.EaseInOut(0f, 3f, .4f, 1f);
    [SerializeField] private AnimationCurve opacity = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.55f, 1f), new Keyframe(1f, 0f));
    [Header("선 파편 / 원형 파동 / 순간 섬광")]
    [Range(.01f, 1f), SerializeField] private float streakLifetimeRatio = .65f;
    [SerializeField] private Vector2 streakScaleRange = new Vector2(.55f, 1.9f);
    [Range(.01f, 1f), SerializeField] private float arcLifetimeRatio = .6f;
    [Range(0f, 1f), SerializeField] private float arcOpacity = .7f;
    [SerializeField] private Vector2 arcScaleRange = new Vector2(.3f, 1.65f);
    [Min(.001f), SerializeField] private float flashDuration = .07f;
    [SerializeField] private Vector2 flashScaleRange = new Vector2(.6f, 1.8f);
    private static readonly int EmissionId = Shader.PropertyToID("_Emission");
    private MaterialPropertyBlock _properties;
    private Vector3 _baseScale;
    private float _age;
    private float _alpha;
    private int _echoCount;
    private Color[] _echoColors;

    private void Awake()
    {
        _properties = new MaterialPropertyBlock();
        if (spritePulse != null) _baseScale = spritePulse.localScale;
        _alpha = slash != null ? slash.color.a : 1f;
        _echoColors = new Color[echoes != null ? echoes.Length : 0];
        for (int i=0;i<_echoColors.Length;i++) if (echoes[i] != null) _echoColors[i]=echoes[i].color;
    }
    private void OnEnable()
    {
        _age = 0f;
        _echoCount = 0;
        if (echoes != null) foreach (var echo in echoes) if (echo != null) echo.enabled = false;
        if (spriteAnimator != null && spriteAnimator.runtimeAnimatorController != null)
        {
            var clips = spriteAnimator.runtimeAnimatorController.animationClips;
            if (clips.Length > 0) spriteAnimator.speed = clips[0].length / Mathf.Max(.01f, lifetime);
            spriteAnimator.Play(0, 0, 0f);
            spriteAnimator.Update(0f);
        }
    }
    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / Mathf.Max(.01f, lifetime));
        if (spritePulse != null) spritePulse.localScale = _baseScale * expansion.Evaluate(t);
        if (slash != null)
        {
            var color = slash.color;
            color.a = _alpha * Mathf.Clamp01(opacity.Evaluate(t));
            slash.color = color;
            slash.GetPropertyBlock(_properties);
            _properties.SetFloat(EmissionId, Mathf.Max(1f, emission.Evaluate(t)));
            slash.SetPropertyBlock(_properties);
        }
        if (streaks != null)
        foreach (var streak in streaks)
        {
            if (streak == null) continue;
            // 파편은 슬래시보다 먼저 사라져 다음 적중을 가리지 않는다.
            float alpha = Mathf.Clamp01(1f - t / Mathf.Max(.01f, streakLifetimeRatio));
            var color = streak.startColor;
            color.a = alpha;
            streak.startColor = color;
            color = streak.endColor; color.a = 0f; streak.endColor = color;
            streak.transform.localScale = Vector3.one * Mathf.Lerp(streakScaleRange.x, streakScaleRange.y, 1f - (1f-t)*(1f-t));
        }
        if (arcs != null) foreach(var arc in arcs)
        {
            if (arc == null) continue;
            float a = Mathf.Clamp01(1f-t/Mathf.Max(.01f, arcLifetimeRatio));
            var color=arc.startColor; color.a=a*arcOpacity; arc.startColor=color; arc.endColor=color;
            arc.transform.localScale=Vector3.one*Mathf.Lerp(arcScaleRange.x,arcScaleRange.y,1f-(1f-t)*(1f-t));
        }
        if (flash != null)
        {
            float progress = Mathf.Clamp01(_age / Mathf.Max(.001f, flashDuration));
            float a = 1f - progress;
            var color=flash.startColor; color.a=a; flash.startColor=color; flash.endColor=color;
            flash.transform.localScale=Vector3.one*Mathf.Lerp(flashScaleRange.x,flashScaleRange.y,progress);
        }
        if (echoes != null && slash != null)
        {
            for(int i=0;i<echoes.Length;i++)
            {
                var echo=echoes[i]; if(echo==null) continue;
                float start=echoInterval*(i+1);
                if (_age < start) continue;
                if (_echoCount<=i)
                {
                    echo.sprite=slash.sprite; echo.flipX=slash.flipX; echo.flipY=slash.flipY;
                    echo.enabled=true; _echoCount=i+1;
                }
                var color=echo.color; color.a=_echoColors[i].a*Mathf.Clamp01(1f-(_age-start)/Mathf.Max(.01f,lifetime-start));
                echo.color=color;
            }
        }
        if (t >= 1f) Destroy(gameObject);
    }
}
