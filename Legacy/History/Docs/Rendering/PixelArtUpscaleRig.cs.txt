using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 저해상도 렌더 + 서브픽셀 보정 + sharp bilinear 업스케일 카메라 리그. (설정: CameraSettingsSO)
///
/// [구성] 플레이 중에만 CameraSettingsApplier 가 Main Camera 에 붙인다. 씬에 직접 붙일 필요 없음.
///   - World 카메라 (런타임 생성, 자식): 월드를 저해상도 RT(아트 픽셀 1:1)에 그린다. 위치는 아트 픽셀 격자에 스냅.
///   - Main Camera (Brain / MainCamera 태그 그대로): 실제 위치·줌 그대로 RT 를 화면 전체 쿼드로 그린다.
///     스냅으로 버려진 소수점 오프셋만큼 UV 를 밀어서 → 카메라 이동이 부드럽다.
///     Main Camera 의 위치/크기는 그대로라 Camera.main.ScreenToWorldPoint 등은 정확히 동작한다.
///
/// [결과] Orthographic Size 를 아무 값(7, 7.5 …)으로 써도
///   - 아트 픽셀 두께가 균일하고 (비정수 배율도 sharp bilinear 로 처리)
///   - 타일이 RT 에서 픽셀 격자에 정확히 맞춰 그려지므로 타일 틈이 생기지 않는다.
///
/// [레이어] UI 레이어(5)는 Main Camera 가 화면 해상도로 직접 그린다 (월드 스페이스 UI 가 선명하게 유지됨).
///   그 외 레이어는 전부 World 카메라(RT)로 그린다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class PixelArtUpscaleRig : MonoBehaviour
{
    const int UILayer = 5;
    static readonly int TexSizeId = Shader.PropertyToID("_TexSize");

    [SerializeField] int assetsPPU = 32;
    [SerializeField] Shader upscaleShader;

    Camera _display;
    Camera _world;
    RenderTexture _rt;
    Material _mat;
    Mesh _mesh;
    GameObject _worldGO;
    GameObject _quadGO;

    int  _origCullingMask;
    bool _origPostProcessing;

    public void Configure(int ppu, Shader shader)
    {
        assetsPPU = Mathf.Max(1, ppu);
        if (shader != null) upscaleShader = shader;
    }

    void OnEnable()
    {
        _display = GetComponent<Camera>();
        if (upscaleShader == null) upscaleShader = Shader.Find("Hidden/PixelArt/SharpUpscale");
        if (upscaleShader == null)
        {
            Debug.LogError("[PixelArtUpscaleRig] Hidden/PixelArt/SharpUpscale 셰이더를 찾을 수 없습니다. CameraSettings 에셋의 Upscale Shader 를 지정하세요.");
            enabled = false;
            return;
        }

        _mat = new Material(upscaleShader) { name = "PixelArtSharpUpscale (runtime)", hideFlags = HideFlags.HideAndDontSave };

        var displayData = _display.GetUniversalAdditionalCameraData();

        // ── World 카메라 ───────────────────────────────────────────────
        _worldGO = new GameObject("PixelWorldCamera (runtime)") { hideFlags = HideFlags.DontSave };
        _worldGO.transform.SetParent(transform, false);
        _world = _worldGO.AddComponent<Camera>();
        _world.CopyFrom(_display);
        _world.orthographic = true;
        _world.cullingMask = _display.cullingMask & ~(1 << UILayer);
        _world.depth = _display.depth - 1;   // 화면 카메라보다 먼저 그린다
        _world.allowMSAA = false;
        _world.allowHDR = _display.allowHDR;

        var worldData = _world.GetUniversalAdditionalCameraData();
        worldData.renderType = CameraRenderType.Base;
        worldData.renderPostProcessing = false; // 포스트 프로세스는 Main Camera 에서 한 번만 (업스케일된 월드 + UI 전체)
        worldData.antialiasing = AntialiasingMode.None;
        worldData.volumeLayerMask = displayData.volumeLayerMask;
        worldData.volumeTrigger = transform;
        worldData.dithering = displayData.dithering;
        worldData.stopNaN = displayData.stopNaN;

        // ── 화면 카메라(Main Camera) ────────────────────────────────────
        _origCullingMask = _display.cullingMask;
        _origPostProcessing = displayData.renderPostProcessing;
        _display.cullingMask = 1 << UILayer;
        // 포스트 프로세스는 Main Camera 설정 그대로 사용 → 업스케일된 월드와 UI 레이어에 화면 해상도로 적용

        // ── 화면 전체 쿼드 ──────────────────────────────────────────────
        _quadGO = new GameObject("PixelWorldDisplay (runtime)") { hideFlags = HideFlags.DontSave, layer = UILayer };
        _quadGO.transform.SetParent(transform, false);
        _mesh = BuildQuad();
        _quadGO.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = _quadGO.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var layers = SortingLayer.layers;
        mr.sortingLayerID = layers.Length > 0 ? layers[0].id : 0;
        mr.sortingOrder = short.MinValue; // 같은 카메라의 월드 스페이스 UI 보다 항상 뒤

        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        Sync();
    }

    void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

        if (_display != null && _mat != null) // OnEnable 이 끝까지 실행된 경우에만 복구
        {
            _display.cullingMask = _origCullingMask;
            _display.GetUniversalAdditionalCameraData().renderPostProcessing = _origPostProcessing;
        }

        if (_world != null) _world.targetTexture = null;
        DestroySafe(_worldGO);
        DestroySafe(_quadGO);
        DestroySafe(_mesh);
        DestroySafe(_mat);
        if (_rt != null) { _rt.Release(); DestroySafe(_rt); }
        _worldGO = _quadGO = null; _world = null; _mesh = null; _mat = null; _rt = null;
    }

    // Brain(LateUpdate)이 Main Camera 위치를 정한 뒤, 렌더 직전에 매 프레임 맞춘다.
    void OnBeginContextRendering(ScriptableRenderContext ctx, List<Camera> cams) => Sync();

    void Sync()
    {
        if (_display == null || _world == null || _mat == null) return;

        int   ppu    = assetsPPU;
        float unit   = 1f / ppu;
        float size   = _display.orthographicSize;   // Cinemachine Lens 값 (SetZoom 등 런타임 변경도 그대로 반영)
        float aspect = _display.aspect;

        // 화면에 보일 영역 (아트 픽셀 단위, 소수 가능)
        float visH = 2f * size * ppu;
        float visW = visH * aspect;

        // RT 는 보일 영역 + 사방 1픽셀 여유 (서브픽셀 오프셋용)
        int rtW = Mathf.CeilToInt(visW) + 2;
        int rtH = Mathf.CeilToInt(visH) + 2;
        EnsureRT(rtW, rtH);

        // World 카메라: RT 의 왼쪽/아래 가장자리가 아트 픽셀 격자에 오도록 스냅
        Vector3 p = _display.transform.position;
        float halfRW = rtW * 0.5f * unit;
        float halfRH = rtH * 0.5f * unit;
        float left   = Mathf.Round((p.x - halfRW) * ppu) * unit;
        float bottom = Mathf.Round((p.y - halfRH) * ppu) * unit;
        _world.transform.SetPositionAndRotation(new Vector3(left + halfRW, bottom + halfRH, p.z), _display.transform.rotation);
        _world.orthographicSize = halfRH;
        _world.nearClipPlane = _display.nearClipPlane;
        _world.farClipPlane  = _display.farClipPlane;

        // 화면 영역이 RT 의 어디에 해당하는지 (RT 픽셀 단위) → UV 로
        float x0 = (p.x - size * aspect - left)   * ppu;
        float y0 = (p.y - size          - bottom) * ppu;
        _mat.mainTextureScale  = new Vector2(visW / rtW, visH / rtH);
        _mat.mainTextureOffset = new Vector2(x0 / rtW, y0 / rtH);
        _mat.SetVector(TexSizeId, new Vector4(rtW, rtH, 1f / rtW, 1f / rtH));

        // 쿼드: Main Camera 화면을 정확히 덮는다
        var t = _quadGO.transform;
        t.localPosition = new Vector3(0f, 0f, (_display.nearClipPlane + _display.farClipPlane) * 0.5f);
        t.localRotation = Quaternion.identity;
        t.localScale    = new Vector3(2f * size * aspect, 2f * size, 1f);
    }

    void EnsureRT(int w, int h)
    {
        if (_rt != null && _rt.width == w && _rt.height == h) return;

        if (_rt != null)
        {
            _world.targetTexture = null;
            _rt.Release();
            DestroySafe(_rt);
        }

        _rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default)
        {
            name = "PixelWorldRT",
            filterMode = FilterMode.Bilinear, // sharp bilinear 셰이더가 경계만 섞도록 bilinear 필요
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            hideFlags = HideFlags.HideAndDontSave,
        };
        _rt.Create();
        _world.targetTexture = _rt;
        _mat.mainTexture = _rt;
    }

    static Mesh BuildQuad()
    {
        var m = new Mesh { name = "PixelWorldQuad", hideFlags = HideFlags.HideAndDontSave };
        m.vertices  = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
        m.uv        = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        m.bounds = new Bounds(Vector3.zero, new Vector3(1, 1, 1000)); // 절대 컬링되지 않도록
        return m;
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
