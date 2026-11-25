using UnityEngine;

/// <summary>Connects the actor's head gauge to the timing and aim owned by its attack pattern.</summary>
public static class BossAttackIndicator
{
    public static void Begin(BaseEntity entity, float duration, Vector2 direction = default, Color? color = null)
    {
        var gauge = entity != null ? entity.AttackGauge : null;
        if (gauge == null) return;

        if (color.HasValue) gauge.SetColor(color.Value);
        SetDirection(gauge, entity, direction);
        gauge.gameObject.SetActive(true);
        gauge.StartFill(duration);
    }

    public static void Aim(BaseEntity entity, Vector2 direction)
    {
        var gauge = entity != null ? entity.AttackGauge : null;
        if (gauge != null && gauge.IsFilling) SetDirection(gauge, entity, direction);
    }

    public static void Stop(BaseEntity entity)
    {
        var gauge = entity != null ? entity.AttackGauge : null;
        if (gauge == null) return;
        gauge.StopFill();
        gauge.gameObject.SetActive(false);
    }

    private static void SetDirection(CenterMeetFill gauge, BaseEntity entity, Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f && entity.Target != null)
            direction = (Vector2)(entity.Target.position - entity.transform.position);
        float angle = direction.sqrMagnitude > 0.0001f ? Vector2.SignedAngle(Vector2.up, direction) : 0f;
        gauge.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

}
