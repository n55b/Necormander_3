using UnityEngine;

internal struct PlayerLocomotion
{
    public const float InputDeadZoneSquared = 0.0001f;
    public const float StopThresholdSquared = 0.0004f;
    private const float TurnDotThreshold = 0.85f;
    private Vector2 steeringVelocity;

    public Vector2 Direction { get; private set; }

    public void Reset(Vector2 direction = default)
    {
        Direction = direction;
        steeringVelocity = Vector2.zero;
    }

    public void Step(Vector2 input, float accelerationTime, float stopTime, float turnTime, float deltaTime)
    {
        Vector2 target = Vector2.ClampMagnitude(input, 1f);
        bool stopping = target.sqrMagnitude < InputDeadZoneSquared;
        bool turning = !stopping && Direction.sqrMagnitude > StopThresholdSquared &&
            Vector2.Dot(Direction.normalized, target.normalized) < TurnDotThreshold;
        float response = stopping ? stopTime : turning ? turnTime : accelerationTime;
        if (turning && Vector2.Dot(steeringVelocity, target - Direction) < 0f)
            steeringVelocity = Vector2.zero;

        Direction = Vector2.ClampMagnitude(Vector2.SmoothDamp(Direction, target, ref steeringVelocity,
            Mathf.Max(0.001f, response), Mathf.Infinity, deltaTime), 1f);
        if (stopping && Direction.sqrMagnitude < StopThresholdSquared) Reset();
    }
}
