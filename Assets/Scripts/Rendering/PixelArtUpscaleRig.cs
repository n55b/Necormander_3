using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>Presents the game at viewport resolution with a 640 × 360 logical UI.</summary>
[DefaultExecutionOrder(-2000)]
public sealed class PixelArtUpscaleRig : MonoBehaviour
{
    public const string WorldUiLayer = "World UI";
    private const string PointProcessor = "necromanderViewportPoint";

    private static PixelArtUpscaleRig instance;
    private PixelFramePresentation presentation;
    private Camera sceneCamera;
    private Camera fallbackCamera;
    private readonly WorldPixelAlignment worldAlignment = new();

    public static Camera RenderCamera => instance != null ? instance.sceneCamera : null;
    internal static UnityEngine.Tilemaps.Tilemap SceneGround => instance != null ? instance.worldAlignment.Ground : null;
    internal static PixelFramePresentation Output => instance != null ? instance.presentation : null;
    public static Vector2Int OutputSize => Output?.DisplayRect.size ?? Vector2Int.zero;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        InputSystem.RegisterProcessor<ViewportPointProcessor>(PointProcessor);
        var output = new GameObject("640 × 360 Presentation", typeof(RectTransform));
        output.AddComponent<PixelArtUpscaleRig>();
        output.AddComponent<WorldSparkField>();
        output.AddComponent<LightingDebugPanel>();
    }

    private void Awake()
    {
        instance = this;
        DontDestroyOnLoad(gameObject);
        presentation = new PixelFramePresentation(transform, CameraSettingsSO.Instance?.upscaleShader);
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        CinemachineCore.CameraUpdatedEvent.AddListener(worldAlignment.OnCameraUpdated);
        Canvas.preWillRenderCanvases += PixelText.Prepare;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += BeginCamera;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += EndCamera;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        CinemachineCore.CameraUpdatedEvent.RemoveListener(worldAlignment.OnCameraUpdated);
        if (sceneCamera != null && sceneCamera.targetTexture == presentation.Frame)
            sceneCamera.targetTexture = null;
        presentation.Dispose();
        Canvas.preWillRenderCanvases -= PixelText.Prepare;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= BeginCamera;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= EndCamera;
        if (instance == this) instance = null;
    }

    private Camera coveredCamera;
    private int previousMask;

    private void BeginCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (!UnityNote.SceneLoader.FloorIsCovered || camera != sceneCamera) return;
        // The presentation shader outputs a single palette color at full fade. Keep Camera.main available for setup.
        coveredCamera = camera;
        previousMask = camera.cullingMask;
        camera.cullingMask = 0;
    }

    private void EndCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (camera != coveredCamera) return;
        camera.cullingMask = previousMask;
        coveredCamera = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene();
    private void OnSceneUnloaded(Scene scene) => BindScene();

    private UnityEngine.UI.Selectable[] controls = new UnityEngine.UI.Selectable[64];
    private float nextControlRefresh;

    private void Update()
    {
        if (Time.unscaledTime >= nextControlRefresh)
        {
            nextControlRefresh = Time.unscaledTime + .2f;
            int total = UnityEngine.UI.Selectable.allSelectableCount;
            if (total > controls.Length) System.Array.Resize(ref controls, Mathf.NextPowerOfTwo(total));
            int count = UnityEngine.UI.Selectable.AllSelectablesNoAlloc(controls);
            for (int i = 0; i < count; i++)
            {
                var control = controls[i];
                if (control != null && !control.TryGetComponent<UIHoverMotion>(out _))
                    control.gameObject.AddComponent<UIHoverMotion>();
                controls[i] = null;
            }
        }
        presentation.Refresh();
        if (sceneCamera == null || !sceneCamera.isActiveAndEnabled ||
            (sceneCamera == fallbackCamera && Camera.main != null))
            BindScene();
    }

    private void BindScene()
    {
        var main = Camera.main;
        if (main == null)
        {
            if (fallbackCamera == null)
            {
                var holder = new GameObject("UI Camera");
                holder.transform.SetParent(transform, false);
                holder.transform.position = Vector3.back * 10f;
                fallbackCamera = holder.AddComponent<Camera>();
                fallbackCamera.orthographic = true;
                fallbackCamera.clearFlags = CameraClearFlags.SolidColor;
                fallbackCamera.backgroundColor = Color.black;
                fallbackCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            }
            main = fallbackCamera;
        }

        if (fallbackCamera != null) fallbackCamera.enabled = main == fallbackCamera;
        if (sceneCamera != null && sceneCamera != main && sceneCamera.targetTexture == presentation.Frame)
            sceneCamera.targetTexture = null;
        sceneCamera = main;
        worldAlignment.Bind(sceneCamera, presentation.Frame);
        WorldAtmosphere.Bind(sceneCamera);

        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            BindCanvas(canvas);
        foreach (var module in FindObjectsByType<InputSystemUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            BindPointer(module);
    }

    public static void BindCanvas(Canvas canvas)
    {
        if (instance == null || instance.sceneCamera == null || canvas == null || canvas == instance.presentation.Canvas) return;
        if (!canvas.isRootCanvas) return;
        if (canvas.renderMode != RenderMode.WorldSpace)
        {
            // Former overlay canvases must sit in front of scene geometry.
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.planeDistance = instance.sceneCamera.nearClipPlane + .1f;
            }
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (!scaler) scaler = canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize)
            {
                float authoredScale = Mathf.Max(.01f, scaler.scaleFactor);
                scaler.referenceResolution = new Vector2(PixelViewport.Width, PixelViewport.Height) / authoredScale;
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.matchWidthOrHeight = 1f;
            }
            canvas.pixelPerfect = false;
            canvas.worldCamera = instance.sceneCamera;
            canvas.sortingLayerName = "UI";
        }
        else
        {
            if (canvas.sortingLayerName == "UI") canvas.sortingLayerName = WorldUiLayer;
            if (canvas.worldCamera == null) canvas.worldCamera = instance.sceneCamera;
        }
    }

    private static void BindPointer(InputSystemUIInputModule module)
    {
        var point = module.point?.action;
        if (point == null) return;
        for (int i = 0; i < point.bindings.Count; i++)
        {
            var binding = point.bindings[i];
            var processors = binding.effectiveProcessors;
            if (!string.IsNullOrEmpty(processors) && processors.Contains(PointProcessor)) continue;
            binding.overrideProcessors = string.IsNullOrEmpty(processors)
                ? PointProcessor
                : processors + "," + PointProcessor;
            point.ApplyBindingOverride(i, binding);
        }
    }

    public sealed class ViewportPointProcessor : InputProcessor<Vector2>
    {
        public override Vector2 Process(Vector2 value, InputControl control) => PixelViewport.DisplayToTarget(value);
    }
}
