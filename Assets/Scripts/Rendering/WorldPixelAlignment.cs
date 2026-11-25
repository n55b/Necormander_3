using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>Aligns the output camera to the tile grid after Cinemachine computes follow and shake.</summary>
internal sealed class WorldPixelAlignment
{
    private Camera camera;
    private Tilemap tilemap;
    internal Tilemap Ground { get; private set; }

    public void Bind(Camera output, RenderTexture frame)
    {
        camera = output;
        camera.targetTexture = frame;
        camera.aspect = (float)PixelViewport.Width / PixelViewport.Height;
        camera.allowMSAA = false;
        camera.allowDynamicResolution = false;
        if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
            data.antialiasing = AntialiasingMode.None;

        tilemap = null;
        Ground = null;
        foreach (var root in camera.gameObject.scene.GetRootGameObjects())
        {
            foreach (var map in root.GetComponentsInChildren<Tilemap>())
            {
                if (tilemap == null) tilemap = map;
                if (map.name == "Ground") Ground = map;
            }
        }
        Align();
    }

    public void OnCameraUpdated(CinemachineBrain brain)
    {
        if (brain.OutputCamera == camera) Align();
    }

    private void Align()
    {
        if (camera == null || !camera.orthographic) return;

        var step = CameraSettingsSO.Instance != null ? CameraSettingsSO.Instance.cameraMotion : PixelMotionStep.Continuous;
        if (step == PixelMotionStep.Continuous) return;
        float unitsPerPixel = camera.orthographicSize * 2f / (PixelViewport.Height * (int)step);
        var view = camera.transform;
        var relative = view.position - (tilemap != null ? tilemap.CellToWorld(Vector3Int.zero) : Vector3.zero);
        float horizontal = Vector3.Dot(relative, view.right);
        float vertical = Vector3.Dot(relative, view.up);
        float dx = Mathf.Round(horizontal / unitsPerPixel) * unitsPerPixel - horizontal;
        float dy = Mathf.Round(vertical / unitsPerPixel) * unitsPerPixel - vertical;
        view.position += view.right * dx + view.up * dy;
    }
}
