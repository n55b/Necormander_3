using UnityEngine;

/// <summary>Offsets the actor's picture in render space while leaving movement and colliders continuous.</summary>
[DefaultExecutionOrder(1000)]
public sealed class PixelActorMotion : MonoBehaviour
{
    public enum Actor { Player, Enemy, Summon }

    private static readonly int OffsetProperty = Shader.PropertyToID("_PixelMotionOffset");
    private Actor actor;
    private SpriteRenderer[] sprites;
    private Transform visualAnchor;
    private MaterialPropertyBlock properties;
    private Vector3 previousOffset;
    private SpriteRenderer struckSprite;
    private Vector2 recoilPeak, appliedRecoil;
    private float recoilAge;
    private float descentAge, descentDuration, descentHeight;
    private EnemyTier arrivalTier;
    private SpriteRenderer arrivalBody;
    private CharacterHealth arrivalHealth;
    private CanvasGroup arrivalBar;
    private float barAlpha;
    private bool revealingBar;
    private int entrySide;
    private bool crossedInAir;
    private VisualSquashStretch arrivalPose;
    internal bool IsDescending => descentDuration > 0;

    internal void Descend(EnemyTier tier, VisualSquashStretch pose, SpriteRenderer body, CharacterHealth health)
    {
        CancelDescent();
        arrivalTier = tier;
        arrivalBody = body;
        arrivalHealth = health;
        crossedInAir = false;
        entrySide = (GetInstanceID() & 2) == 0 ? 1 : -1;
        arrivalPose = pose;
        descentAge = 0;
        descentDuration = tier == EnemyTier.Boss ? .48f : tier == EnemyTier.Elite ? .34f : .26f;
        descentHeight = tier == EnemyTier.Boss ? 48 : tier == EnemyTier.Elite ? 32 : 22;
        var camera = PixelArtUpscaleRig.RenderCamera;
        if (camera != null && PolishFeedback.ExistingEnabled(ExistingFeedbackCue.Squash))
        {
            if (arrivalBar != null)
            {
                barAlpha = arrivalBar.alpha;
                arrivalBar.alpha = 0f;
                revealingBar = true;
            }
            var offset = AirPose(0);
            float unit = 2f * camera.orthographicSize / PixelViewport.Height;
            ApplyOffset((Vector3)(offset * unit));
            EmitAirBurst(offset, unit, true);
        }
    }

    private static float Ease(float from, float to, float time) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, time));

    private Vector2 AirPose(float time)
    {
        // Cross the contact point in the air, catch briefly, then drop into the landing.
        float cross = Ease(0, .28f, time);
        float tuck = Ease(.28f, .53f, time);
        float fall = Mathf.InverseLerp(.40f, 1f, time);
        float x = Mathf.Lerp(.44f, -.14f, cross) * (1f - tuck);
        float y = 1f + .13f * cross - .05f * tuck - 1.08f * fall * fall;
        arrivalPose?.StretchForDescent(time);
        return new Vector2(entrySide * x, y) * descentHeight;
    }

    private void EmitAirBurst(Vector2 offset, float unit, bool first)
    {
        var center = arrivalBody != null ? arrivalBody.bounds.center : transform.position;
        Vector2 point = (Vector2)center + offset * unit;
        var heading = new Vector2(first ? entrySide : -entrySide, first ? .25f : .65f);
        int count = arrivalTier == EnemyTier.Boss ? 8 : arrivalTier == EnemyTier.Elite ? 6 : 4;
        WorldSparkField.Emit(first ? WorldSparkField.Kind.Dust : WorldSparkField.Kind.Arrival, point, heading, count);
        if (first) PolishFeedback.Play(FeedbackCue.BossVanish, point, heading, playSound: false);
    }

    private Vector2 AdvanceDescent(float unit)
    {
        if (!IsDescending) return Vector2.zero;
        if (arrivalHealth != null && arrivalHealth.IsDead)
        {
            CancelDescent();
            return Vector2.zero;
        }
        descentAge += Time.deltaTime;
        float t = Mathf.Clamp01(descentAge / descentDuration);
        if (t < 1f && PolishFeedback.ExistingEnabled(ExistingFeedbackCue.Squash))
        {
            var offset = AirPose(t);
            if (revealingBar) arrivalBar.alpha = barAlpha * Ease(.62f, 1f, t);
            if (!crossedInAir && t >= .28f)
            {
                crossedInAir = true;
                EmitAirBurst(offset, unit, false);
            }
            return offset;
        }
        FinishDescent();
        if (PolishFeedback.ExistingEnabled(ExistingFeedbackCue.Squash)) arrivalPose?.LandFromDescent();
        else arrivalPose?.ResetVisual();
        bool boss = arrivalTier == EnemyTier.Boss;
        WorldSparkField.Emit(WorldSparkField.Kind.Arrival, transform.position, Vector2.up,
            boss ? 12 : arrivalTier == EnemyTier.Elite ? 8 : 5);
        if (boss)
        {
            PolishFeedback.Play(FeedbackCue.BossLand, transform.position, Vector2.up, 3);
            CameraManager.Instance?.Request(ImpactKind.BossLanding, Vector2.up, strength: .65f);
        }
        return Vector2.zero;
    }

    private void FinishDescent()
    {
        descentDuration = 0;
        if (revealingBar && arrivalBar != null) arrivalBar.alpha = barAlpha;
        revealingBar = false;
    }

    internal void CancelDescent()
    {
        if (!IsDescending) return;
        FinishDescent();
        arrivalPose?.ReleaseDescentStretch();
    }

    public void Strike(SpriteRenderer body, Vector2 direction, float pixels)
    {
        if (actor != Actor.Enemy || body == null || !PolishFeedback.ExistingEnabled(ExistingFeedbackCue.Squash)) return;
        CancelDescent();
        struckSprite = body;
        recoilPeak = Vector2.ClampMagnitude(direction.normalized * pixels + appliedRecoil * .25f, pixels * 1.15f);
        recoilAge = 0;
    }

    public static PixelActorMotion Attach(GameObject root, Actor kind, Transform visual = null)
    {
        var motion = root.AddComponent<PixelActorMotion>();
        motion.actor = kind;
        motion.visualAnchor = visual;
        if (kind == Actor.Enemy)
            motion.arrivalBar = root.GetComponentInChildren<WorldHPBar>(true)?.GetComponent<CanvasGroup>();
        if (visual == null)
        {
            motion.sprites = root.GetComponentsInChildren<SpriteRenderer>(true);
            motion.properties = new MaterialPropertyBlock();
        }
        return motion;
    }

    private void LateUpdate()
    {
        var camera = PixelArtUpscaleRig.RenderCamera;
        var settings = CameraSettingsSO.Instance;
        if (camera == null || settings == null)
        {
            if (IsDescending) CancelDescent();
            return;
        }
        var step = actor switch
        {
            Actor.Player => settings.playerMotion,
            Actor.Enemy => settings.enemyMotion,
            _ => settings.summonMotion
        };
        float worldPerPixel = 2f * camera.orthographicSize / PixelViewport.Height;
        var offset = GetOffset(camera, transform.position, step) + (Vector3)(AdvanceDescent(worldPerPixel) * worldPerPixel);
        Vector2 recoil = Vector2.zero;
        if (recoilPeak != Vector2.zero)
        {
            // Start at contact, then spring once past the resting pose. Counts logical screen pixels at any zoom.
            recoil = recoilPeak * (Mathf.Exp(-17f * recoilAge) * Mathf.Cos(26f * recoilAge));
            if (!HitStopManager.IsPaused) recoilAge += Time.unscaledDeltaTime;
            if (recoilAge > .23f || !PolishFeedback.ExistingEnabled(ExistingFeedbackCue.Squash)) recoilPeak = Vector2.zero;
        }
        ApplyOffset(offset, recoil, worldPerPixel);
    }

    public static Vector3 GetOffset(Camera camera, Vector3 position, PixelMotionStep step)
    {
        if (step == PixelMotionStep.Continuous) return Vector3.zero;
        var viewport = camera.WorldToViewportPoint(position);
        float x = PixelViewport.Width * (int)step;
        float y = PixelViewport.Height * (int)step;
        viewport.x = Mathf.Round(viewport.x * x) / x;
        viewport.y = Mathf.Round(viewport.y * y) / y;
        return camera.ViewportToWorldPoint(viewport) - position;
    }

    private void ApplyOffset(Vector3 offset, Vector2 recoil = default, float worldPerPixel = 0)
    {
        if (offset == previousOffset && recoil == appliedRecoil) return;
        if (visualAnchor != null)
            visualAnchor.position = transform.position + offset;
        else if (sprites != null)
        {
            foreach (var sprite in sprites)
            {
                if (sprite == null || sprite.sharedMaterial == null ||
                    !sprite.sharedMaterial.HasProperty(OffsetProperty)) continue;
                sprite.GetPropertyBlock(properties);
                properties.SetVector(OffsetProperty, sprite == struckSprite ? offset + (Vector3)(recoil * worldPerPixel) : offset);
                sprite.SetPropertyBlock(properties);
            }
        }
        previousOffset = offset;
        appliedRecoil = recoil;
    }

    private void OnDisable() { CancelDescent(); recoilPeak = Vector2.zero; ApplyOffset(Vector3.zero); }
}
