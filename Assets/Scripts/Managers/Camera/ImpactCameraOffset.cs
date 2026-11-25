using Unity.Cinemachine;
using UnityEngine;

// Offset the virtual camera before the Body-stage confiner and final pixel alignment.
public sealed class ImpactCameraOffset : CinemachineExtension
{
    internal Vector2 direction;
    internal float pixels, duration, decay, started;
    internal int priority;
    private Vector3 appliedOffset;
    private float zoomCharge, visibleZoom, zoomVelocity;
    private float pendingZoomWeight;
    private int sampledFrame = -1;

    internal void PushZoom(float weight)
    {
        pendingZoomWeight = Mathf.Max(pendingZoomWeight, weight);
    }

    internal void ClearZoom() => pendingZoomWeight = zoomCharge = visibleZoom = zoomVelocity = 0;

    private void ApplyZoom(ref CameraState state)
    {
        if (pendingZoomWeight <= 0 && zoomCharge <= 0 && visibleZoom <= 0) return;
        var settings = CameraSettingsSO.Instance;
        if (settings == null) return;
        if (sampledFrame != Time.frameCount)
        {
            sampledFrame = Time.frameCount;
            float dt = Time.unscaledDeltaTime;
            float limit = settings.comboZoomMaxPixels;
            if (limit <= 0) { ClearZoom(); return; }
            float headroom = Mathf.Clamp01(1f - zoomCharge / limit);
            zoomCharge = Mathf.Min(limit, zoomCharge + pendingZoomWeight * settings.comboZoomPixelsPerHit * headroom);
            pendingZoomWeight = 0;
            visibleZoom = Mathf.SmoothDamp(visibleZoom, zoomCharge, ref zoomVelocity, .19f, Mathf.Infinity, dt);
            zoomCharge *= Mathf.Exp(-.69314718f * dt / Mathf.Max(.05f, settings.comboZoomHalfLife));
            if (visibleZoom < .05f && zoomCharge < .05f) ClearZoom();
        }
        var lens = state.Lens;
        lens.OrthographicSize *= 1f - 2f * Mathf.Min(visibleZoom, settings.comboZoomMaxPixels) / PixelViewport.Height;
        state.Lens = lens;
    }

    internal Vector2 Sample(float now)
    {
        if (pixels <= 0 || duration <= 0) return Vector2.zero;
        float t = Mathf.Clamp01((now - started) / duration);
        return direction * (pixels * Mathf.Pow(1f - t, decay) * Mathf.Cos(t * Mathf.PI * 2));
    }

    public override void PrePipelineMutateCameraStateCallback(CinemachineVirtualCameraBase vcam, ref CameraState state, float deltaTime)
    {
        ApplyZoom(ref state);
        Vector2 offset = Sample(Time.unscaledTime);
        float units = 2 * state.Lens.OrthographicSize / PixelViewport.Height;
        appliedOffset = state.RawOrientation * (Vector3)(offset * units);
        state.PositionCorrection += appliedOffset;
    }

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
        ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize || appliedOffset == Vector3.zero) return;
        var room = MapGenerator.Instance != null ? MapGenerator.Instance.CurrentRoom : null;
        var ground = room != null ? room.groundTilemap : PixelArtUpscaleRig.SceneGround;
        if (ground == null) return;
        var bounds = ground.localBounds;
        var min = ground.transform.TransformPoint(bounds.min);
        var max = ground.transform.TransformPoint(bounds.max);
        Vector3 center = state.GetCorrectedPosition() - appliedOffset;
        float halfHeight = state.Lens.OrthographicSize;
        float halfWidth = halfHeight * PixelViewport.Width / PixelViewport.Height;
        Vector3 allowed = appliedOffset;
        allowed.x = LimitTravel(allowed.x, center.x, min.x + halfWidth, max.x - halfWidth);
        allowed.y = LimitTravel(allowed.y, center.y, min.y + halfHeight, max.y - halfHeight);
        state.PositionCorrection += allowed - appliedOffset;
    }

    // Small rooms can already be visible in their entirety. Only limit added travel.
    internal static float LimitTravel(float offset, float center, float min, float max)
        => Mathf.Clamp(offset, Mathf.Min(0, min - center), Mathf.Max(0, max - center));
}

