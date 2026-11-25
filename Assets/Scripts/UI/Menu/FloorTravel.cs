using UnityEngine;

// Normalized beats on the unscaled floor-travel clock. Coordinates are logical pixels.
internal static class FloorTravel
{
    internal const float Seconds = 3.0f;
    internal const float PoseStart = .08f;
    internal const float LiftStart = .14f;
    internal static float BackdropOpacity(float time) =>
        .14f * Ease(0, .11f, time) + .86f * Ease(.11f, .64f, time);
    internal static readonly Vector2 Mouth = new(320, 180);
    internal static float Ease(float a, float b, float time) =>
        Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, time));

    internal static Vector2 Hover(Vector2 start, int index, float time)
    {
        float lift = Ease(LiftStart, .35f, time);
        var berth = Mouth + (index == 0 ? new Vector2(-49, 19) : new Vector2(45 + (index - 1) * 19, 23 - (index - 1) * 21));
        return Vector2.Lerp(start, berth, lift) + Vector2.up *
            (Mathf.Sin(lift * Mathf.PI) * 12f + lift * Mathf.Sin(time * 30f + index * 1.8f) * 2.5f);
    }

    internal static float Intake(int index, int actors, float time)
    {
        float start = index == 0 ? .47f : Mathf.Lerp(.605f, .69f, (index - 1f) / Mathf.Max(1, actors - 2));
        float end = index == 0 ? .635f : Mathf.Lerp(.745f, .805f, (index - 1f) / Mathf.Max(1, actors - 2));
        float t = Mathf.InverseLerp(start, end, time);
        return t * (.12f + .88f * t * t);
    }
}
