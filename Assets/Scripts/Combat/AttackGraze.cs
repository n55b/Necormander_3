using UnityEngine;

// Each attack can graze once. Projectiles also test relative motion between samples.
internal struct AttackGraze
{
    private Collider2D hazard;
    private bool approached, projectile, hasPrevious;
    private Vector2 previousOffset, lastDirection;
    private static float nextAllowed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCooldown() => nextAllowed = 0;

    public void Arm(Collider2D shape, LayerMask targets, float damage, bool projectile = false)
    {
        this = default;
        this.projectile = projectile;
        hazard = (targets.value & Layers.PlayerMask) != 0 && damage > 0 ? shape : null;
    }

    public void Cancel() { hazard = null; approached = false; hasPrevious = false; }

    public void Contact(Collider2D collider)
    {
        if (hazard == null) return;
        var player = PolishFeedback.Player;
        if (player == null || collider != player.BodyCollider) return;
        if (projectile && player.IsDashing && !player.IsInputBlocked && player.Stat != null && player.Stat.Health != null && player.Stat.Health.Invincible)
            Reward(player);
        else Cancel();
    }

    public void Sample()
    {
        if (hazard == null || !hazard.enabled || Time.timeScale <= 0) return;
        var player = PolishFeedback.Player;
        var body = player != null ? player.BodyCollider : null;
        if (body == null || !body.enabled || player.Stat == null || player.Stat.Health == null || player.Stat.Health.IsDead || player.IsInputBlocked || PlayerController.IsGameplayPaused)
        { approached = hasPrevious = false; return; }
        var settings = CameraSettingsSO.Instance;
        if (settings == null || settings.grazeDuration <= 0) return;
        Bounds attack = hazard.bounds, target = body.bounds;
        Vector2 offset = attack.center - target.center;
        Vector2 travel = offset - previousOffset;
        Bounds near = attack;
        near.Expand(settings.grazeMargin * 2f);
        bool insideMargin = false;
        if (Mathf.Abs(offset.x) <= near.extents.x + target.extents.x &&
            Mathf.Abs(offset.y) <= near.extents.y + target.extents.y)
        {
            var gap = hazard.Distance(body);
            if (!gap.isValid) { Cancel(); return; }
            if (gap.distance <= 0) { Contact(body); return; }
            insideMargin = gap.distance <= settings.grazeMargin;
            if (insideMargin) lastDirection = (gap.pointA - gap.pointB).normalized;
        }
        // A fast shot can enter and leave the margin between physics poses. The inner
        // swept box excludes trajectories that could have hit; physics owns contact.
        if (projectile && hasPrevious && !insideMargin && travel.sqrMagnitude > settings.grazeMargin * settings.grazeMargin)
        {
            var shape = new Bounds(Vector3.zero, attack.size + target.size);
            var ray = new Ray(previousOffset, travel.normalized);
            bool crossesBody = shape.IntersectRay(ray, out float inner) && inner <= travel.magnitude;
            shape.Expand(settings.grazeMargin * 2f);
            if (crossesBody) approached = false;
            else if (shape.IntersectRay(ray, out float outer) && outer <= travel.magnitude)
            {
                approached = true;
                lastDirection = (previousOffset + travel.normalized * outer).normalized;
            }
        }
        previousOffset = offset;
        hasPrevious = true;
        if (insideMargin) approached = true;
        else if (approached) Reward(player);
    }

    private void Reward(PlayerController player)
    {
        var settings = CameraSettingsSO.Instance;
        Vector2 direction = lastDirection.sqrMagnitude > .01f ? lastDirection : ((Vector2)hazard.bounds.center - (Vector2)player.BodyCollider.bounds.center).normalized;
        Cancel();
        if (settings == null || settings.grazeDuration <= 0 || PlayerController.IsGameplayPaused ||
            Time.unscaledTime < nextAllowed) return;
        nextAllowed = Time.unscaledTime + settings.grazeCooldown;
        HitStopManager.Instance?.DoSlowMotion(settings.grazeDuration, settings.grazeTimeScale);
        if (!projectile) return;
        var point = player.BodyCollider.bounds.center;
        PolishFeedback.Play(FeedbackCue.ProjectileGraze, point, direction)?.FollowOrigin(player.transform);
        WorldSparkField.Emit(WorldSparkField.Kind.Reflect, point, direction, 9);
    }
}
