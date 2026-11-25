using UnityEngine;

/// <summary>
/// 런타임에 생성되거나 씬 사이에 유지되는 캔버스를 현재 게임 프레임에 연결한다.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class CanvasCameraBinder : MonoBehaviour
{
    Canvas _canvas;

    void Awake()    => Bind();
    void OnEnable() => Bind();

    void LateUpdate()
    {
        // 씬 전환으로 이전 Main Camera 가 파괴되면 다시 연결
        if (_canvas != null && (_canvas.worldCamera == null || !_canvas.worldCamera.isActiveAndEnabled))
            Bind();
    }

    void Bind()
    {
        if (_canvas == null) _canvas = GetComponent<Canvas>();
        PixelArtUpscaleRig.BindCanvas(_canvas);
        if (_canvas == null || _canvas.renderMode != RenderMode.ScreenSpaceCamera) return;

        var cam = _canvas.worldCamera;
        if (cam != null && cam.isActiveAndEnabled) return;

        var main = Camera.main;
        if (main != null) _canvas.worldCamera = main;
    }
}
