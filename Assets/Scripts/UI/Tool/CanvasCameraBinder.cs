using UnityEngine;

/// <summary>
/// Screen Space - Camera 캔버스의 Render Camera 를 Camera.main 으로 자동 연결한다.
/// 프리팹(GameManager, ScreenFadeCanvas 등)이나 씬을 넘어 살아남는 캔버스는 씬 카메라를 직접 참조할 수 없어서 필요하다.
/// Main Camera 에서 포스트 프로세스가 돌기 때문에, 이 캔버스들도 포스트 프로세스를 받는다.
/// (카메라가 없으면 Unity 는 Overlay 처럼 그리므로 화면에서 사라지지는 않는다.)
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
        if (_canvas == null || _canvas.renderMode != RenderMode.ScreenSpaceCamera) return;

        var cam = _canvas.worldCamera;
        if (cam != null && cam.isActiveAndEnabled) return;

        var main = Camera.main;
        if (main != null) _canvas.worldCamera = main;
    }
}
