using UnityEngine;

/// <summary>프리팹에 배치한 슬래시·발광·짧은 선 파편을 재생한다. 피해/카메라/시간은 변경하지 않는다.</summary>
public class ImpactFeedback : MonoBehaviour
{
    [Min(.01f), SerializeField] private float lifetime = .3f;
    [SerializeField] private Animator spriteAnimator;
    [SerializeField] private SpriteRenderer slash;
    [SerializeField] private LineRenderer[] streaks;
    [SerializeField] private LineRenderer[] arcs;
    [SerializeField] private LineRenderer flash;
    [SerializeField] private SpriteRenderer[] echoes;
    [Min(.01f), SerializeField] private float echoInterval = .055f;
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
    [Min(0f), SerializeField, Tooltip("파동 사이의 시간차(초). 그림 크기는 바꾸지 않고 주변 파동만 순서대로 퍼집니다.")]
    private float arcDelay = .035f;
    [Header("작은 빛 파편 (그림 배율과 독립)")]
    [SerializeField] private LineRenderer[] sparks;
    [Min(.01f), SerializeField, Tooltip("파편 하나가 보이는 시간(초). 전체 이펙트 수명 안에서 정리됩니다.")]
    private float sparkDuration = .2f;
    [Min(0f), SerializeField, Tooltip("파편별 출발 시간차(초). 0이면 동시에 퍼집니다.")]
    private float sparkDelayStep = .012f;
    [Min(0f), SerializeField, Tooltip("파편이 중심에서 바깥으로 날아가는 거리(유닛). 파편 자체 크기는 유지합니다.")]
    private float sparkTravelDistance = .6f;
    [Min(0f), SerializeField, Tooltip("파편 수명이 끝날 때까지 아래로 내려가는 거리(유닛).")]
    private float sparkDropDistance = .25f;
    private static readonly int EmissionId = Shader.PropertyToID("_Emission");
    private MaterialPropertyBlock _properties;
    private float _age;
    private float _alpha;
    private int _echoCount;
    private Color[] _echoColors;
    private Vector3[] _sparkOrigins;
    private Color[] _sparkColors;
    private Renderer[] _renderers;
    private bool[] _visibleBeforeCatalog;
    private bool _catalogHidden;

    private void Awake()
    {
        _properties = new MaterialPropertyBlock();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _visibleBeforeCatalog = new bool[_renderers.Length];
        _alpha = slash != null ? slash.color.a : 1f;
        _echoColors = new Color[echoes != null ? echoes.Length : 0];
        for (int i=0;i<_echoColors.Length;i++) if (echoes[i] != null) _echoColors[i]=echoes[i].color;
        _sparkOrigins = new Vector3[sparks != null ? sparks.Length : 0];
        _sparkColors = new Color[_sparkOrigins.Length];
        for (int i = 0; i < _sparkOrigins.Length; i++)
        {
            if (sparks[i] == null) continue;
            _sparkOrigins[i] = sparks[i].transform.localPosition;
            _sparkColors[i] = sparks[i].startColor;
        }
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
        RenderVisuals();
    }
    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= Mathf.Max(.01f, lifetime)) Destroy(gameObject);
    }
    // Aseprite 클립도 Renderer.enabled를 쓰므로 Animator 갱신 후에 적용한다.
    private void LateUpdate() => RenderVisuals();
    private void RenderVisuals()
    {
        if (!PolishFeedback.ExistingEnabled(ExistingFeedbackCue.Slash))
        {
            // 연출 도중 꺼도 마지막 그림이 남지 않으며 Update의 수명 정리는 계속된다.
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (!_catalogHidden) _visibleBeforeCatalog[i] = _renderers[i].enabled;
                _renderers[i].enabled = false;
            }
            _catalogHidden = true;
            return;
        }
        if (_catalogHidden)
        {
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = _visibleBeforeCatalog[i];
            _catalogHidden = false;
        }
        float t = Mathf.Clamp01(_age / Mathf.Max(.01f, lifetime));
        // 원본 슬래시와 잔상은 32 PPU / 1배 고정. 크기 변화는 별도 선 도형에만 적용한다.
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
        if (arcs != null) for (int i = 0; i < arcs.Length; i++)
        {
            var arc = arcs[i];
            if (arc == null) continue;
            float age = _age - i * arcDelay;
            float progress = Mathf.Clamp01(age / Mathf.Max(.01f, lifetime * arcLifetimeRatio));
            float a = age < 0f ? 0f : 1f - progress;
            var color=arc.startColor; color.a=a*arcOpacity; arc.startColor=color; arc.endColor=color;
            arc.transform.localScale=Vector3.one*Mathf.Lerp(arcScaleRange.x,arcScaleRange.y,1f-(1f-progress)*(1f-progress));
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
        if (sparks != null) for (int i = 0; i < sparks.Length; i++)
        {
            var spark = sparks[i];
            if (spark == null) continue;
            float age = _age - i * sparkDelayStep;
            float progress = Mathf.Clamp01(age / Mathf.Max(.01f, sparkDuration));
            Vector3 origin = _sparkOrigins[i];
            spark.transform.localPosition = origin
                + origin.normalized * (sparkTravelDistance * (1f - (1f-progress)*(1f-progress)))
                + Vector3.down * (sparkDropDistance * progress * progress);
            var color = _sparkColors[i];
            color.a *= age < 0f ? 0f : (1f - progress) * (1f - progress);
            spark.startColor = color;
            spark.endColor = color;
        }
    }
}
