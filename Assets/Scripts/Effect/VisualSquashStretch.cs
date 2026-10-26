using System;
using UnityEngine;

/// <summary>물리 루트가 아닌 전용 그림 Transform에만 적용하는 짧은 변형. UnityEvent에서 Play(이름)으로 호출.</summary>
[DisallowMultipleComponent]
public class VisualSquashStretch : MonoBehaviour
{
    [Serializable]
    public class Pulse
    {
        public string name;
        public Vector2 scale = Vector2.one;
        [Min(0.01f)] public float duration = .16f;
        public AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(.18f, 1f), new Keyframe(.65f, -.18f), new Keyframe(1f, 0f));
        public Pulse(string name, float x, float y, float duration)
        { this.name = name; scale = new Vector2(x, y); this.duration = duration; }
    }

    [Tooltip("충돌체/카메라/UI를 포함하지 않는 그림 전용 Transform. 이 Transform의 scale은 이 컴포넌트가 소유한다.")]
    [SerializeField] private Transform visual;
    [Tooltip("그림과 물리가 같은 루트인 몬스터용. _FeedbackScale을 지원하는 기존 재질로 그림 정점만 변형한다. 지정하면 Visual보다 우선한다.")]
    [SerializeField] private SpriteRenderer deformRenderer;
    [Tooltip("실제 체력이 줄었을 때 Hit 연출. 체력이 없는 소환수 퍼펫은 비워둔다.")]
    [SerializeField] private CharacterHealth healthSource;
    [SerializeField] private Pulse[] pulses = {
        new Pulse("Attack", 1.09f, .93f, .14f), new Pulse("Dash", 1.12f, .91f, .18f),
        new Pulse("Land", 1.07f, .94f, .14f), new Pulse("Hit", 1.10f, .90f, .17f),
        new Pulse("Guard", .95f, 1.06f, .13f), new Pulse("Squash", 1.10f, .90f, .18f),
        new Pulse("Stretch", .92f, 1.10f, .18f)
    };
    [SerializeField] private bool useUnscaledTime;
    [Range(.01f, 1f), SerializeField, Tooltip("연타 시 이전 변형을 부드럽게 섞어 없애는 구간. 전체 동작 시간 대비 비율이며 0.2 = 앞 20%. 크면 이전 반동이 오래 남습니다.")]
    private float pulseBlendRatio = .2f;
    private Vector3 _baseScale;
    private bool _initialized;
    private Pulse _active;
    private Vector2 _scale = Vector2.one;
    private Vector2 _startScale;
    private float _elapsed;
    private static readonly int FeedbackScaleId = Shader.PropertyToID("_FeedbackScale");
    private MaterialPropertyBlock _properties;

    private void OnEnable()
    {
        if (healthSource == null) return;
        healthSource.OnDamageTaken += OnDamageTaken;
        healthSource.OnDeath += ResetVisual;
    }
    private void OnDamageTaken(float damage)
    {
        if (damage > 0f && !healthSource.IsDead) PlayHit();
    }

    private bool Initialize()
    {
        if (_initialized) return deformRenderer != null || visual != null;
        if (deformRenderer != null)
        {
            if (deformRenderer.sharedMaterial == null || !deformRenderer.sharedMaterial.HasProperty(FeedbackScaleId))
            { Debug.LogWarning("[VisualSquashStretch] _FeedbackScale을 지원하는 재질이 필요합니다.", this); return false; }
            _properties = new MaterialPropertyBlock();
            _initialized = true;
            return true;
        }
        if (visual == null) return false;
        // 방/캐릭터 루트를 잘못 넣어 충돌 판정까지 변형하는 설정은 거절한다.
        if (visual.GetComponentInChildren<Collider2D>(true) != null || visual.GetComponentInChildren<Rigidbody2D>(true) != null)
        { Debug.LogWarning("[VisualSquashStretch] 그림 전용 Transform을 지정하세요.", this); return false; }
        _baseScale = visual.localScale;
        _initialized = true;
        return true;
    }

    public void Play(string pulseName)
    {
        if (!isActiveAndEnabled || !Initialize()) return;
        foreach (var pulse in pulses)
        {
            if (pulse == null || pulse.name != pulseName) continue;
            _startScale = _scale;
            _active = pulse;
            _elapsed = 0f;
            return;
        }
    }
    public void Squash() => Play("Squash");
    public void Stretch() => Play("Stretch");
    public void PlayAttack() => Play("Attack");
    public void PlayDash() => Play("Dash");
    public void PlayLand() => Play("Land");
    public void PlayHit() => Play("Hit");
    public void PlayGuard() => Play("Guard");

    private void LateUpdate() => Advance(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

    private void Advance(float deltaTime)
    {
        if (_active == null || (visual == null && deformRenderer == null)) return;
        _elapsed += Mathf.Max(0f, deltaTime);
        float t = Mathf.Clamp01(_elapsed / Mathf.Max(.01f, _active.duration));
        if (t >= 1f) { ResetVisual(); return; }
        float weight = _active.curve != null ? _active.curve.Evaluate(t) : Mathf.Sin(t * Mathf.PI);
        // 연속 호출 시 현재 변형에서 이어지고, 종료 때는 정확한 원래 스케일로 돌아온다.
        _scale = Vector2.one + (_active.scale - Vector2.one) * weight
            + (_startScale - Vector2.one) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(.01f, pulseBlendRatio))));
        _scale.x = Mathf.Clamp(_scale.x, .5f, 1.5f);
        _scale.y = Mathf.Clamp(_scale.y, .5f, 1.5f);
        if (deformRenderer != null)
        {
            deformRenderer.GetPropertyBlock(_properties);
            _properties.SetVector(FeedbackScaleId, new Vector4(_scale.x, _scale.y, 1f, 1f));
            deformRenderer.SetPropertyBlock(_properties);
            // 프레임마다 스프라이트의 크기가 달라도 화면 가장자리에서 잘리지 않는다.
            if (deformRenderer.sprite != null)
            {
                var b = deformRenderer.sprite.bounds;
                var s = new Vector3(_scale.x, _scale.y, 1f);
                var flip = new Vector3(deformRenderer.flipX ? -1f : 1f, deformRenderer.flipY ? -1f : 1f, 1f);
                deformRenderer.localBounds = new Bounds(Vector3.Scale(b.center, Vector3.Scale(s, flip)), Vector3.Scale(b.size, s));
            }
        }
        else visual.localScale = Vector3.Scale(_baseScale, new Vector3(_scale.x, _scale.y, 1f));
    }

    public void ResetVisual()
    {
        _active = null;
        _scale = Vector2.one;
        if (!_initialized) return;
        if (deformRenderer != null)
        {
            deformRenderer.GetPropertyBlock(_properties);
            _properties.SetVector(FeedbackScaleId, Vector4.one);
            deformRenderer.SetPropertyBlock(_properties);
            deformRenderer.ResetLocalBounds();
        }
        else if (visual != null) visual.localScale = _baseScale;
    }
    private void OnDisable()
    {
        if (healthSource != null)
        {
            healthSource.OnDamageTaken -= OnDamageTaken;
            healthSource.OnDeath -= ResetVisual;
        }
        ResetVisual();
    }
}
