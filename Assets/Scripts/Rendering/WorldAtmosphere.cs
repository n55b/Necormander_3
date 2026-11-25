using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent, DefaultExecutionOrder(1600)]
public sealed class WorldAtmosphere : MonoBehaviour
{
    public enum View { Composite, WorldColor, EmissionMask, FloorMask, FogOpacity }
    public View Preview { get; set; }
    private Material material, groundMaterial, backdropMaterial;
    private MeshRenderer groundSurface, backdropSurface;
    private static readonly int WorldDepth = Shader.PropertyToID("_WorldDepthStrength");
    private static readonly int CloudMap = Shader.PropertyToID("_CloudCoverMap");
    private static readonly int CloudProjection = Shader.PropertyToID("_CloudProjection");
    private static Vector2 cloudTravel;
    private static int lastCloudFrame;
    private Texture2D cloudMap;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCloudTravel() { cloudTravel = Vector2.zero; lastCloudFrame = -1; }
    private AtmosphereLibrary profiles;
    private AtmosphereLibrary.Look look;
    private WorldFloorMask floor;
    internal WorldFloorMask Floor => floor;
    private bool hasLook;
    private float windPhase;
    internal float Airborne => look.airborne * (1f - Mathf.Clamp01(LightingPreview.Active?.airSuppression ?? 0));
    private string sceneName;
    public string ProfileSource { get; private set; }
    public static WorldAtmosphere Current { get; private set; }
    private Vector2 flash;
    private float flashUntil;
    private Vector2 accent;
    private float accentUntil;
    private PlayerController litPlayer;
    private PlayerHandAimController hands;
    internal void Accent(Vector2 point)
    {
        accent = point;
        accentUntil = Time.time + .18f;
    }

    private void OnEnable()
    {
        Current = this;
        sceneName = gameObject.scene.name;
        DamageEventBus.OnAttackHitResolved += OnHit;
    }
    private void OnDisable()
    {
        DamageEventBus.OnAttackHitResolved -= OnHit;
        if (surfaceRenderer != null) surfaceRenderer.enabled = false;
        if (groundSurface != null) groundSurface.enabled = false;
        if (backdropSurface != null) backdropSurface.enabled = false;
        if (Current != this) return;
        Shader.SetGlobalFloat(WorldDepth, 0);
        Shader.SetGlobalVector(CloudProjection, Vector4.zero);
        Current = null;
        Shader.SetGlobalFloat(Wind, 0);
        foreach (var source in GroundShadowLight.Globals) source.Tint(Color.white);
    }
    private void OnHit(CharacterHealth target, CharacterStat attacker, DamageInfo hit, bool critical)
    {
        if (target == null || hit.isRedirected || !DamageRules.IsPlayerSourced(hit.category) ||
            !PolishFeedback.ExistingEnabled(ExistingFeedbackCue.Flash)) return;
        var sprite = target.HitBody;
        flash = sprite != null ? sprite.bounds.center : target.transform.position;
        flashUntil = Time.time + (critical || hit.feedbackComboFinish ? .16f : .085f);
    }
    private MeshRenderer surfaceRenderer;
    private static readonly int Wind = Shader.PropertyToID("_CanopyWindPixels");

    internal static void Bind(Camera camera)
    {
        if (!Application.isPlaying || camera == null || !camera.orthographic ||
            (camera.cullingMask & (1 << Layers.Default)) == 0 ||
            PixelArtUpscaleRig.SceneGround == null || camera.GetComponent<WorldAtmosphere>() != null) return;
        camera.gameObject.AddComponent<WorldAtmosphere>();
    }

    private void Awake()
    {
        gameObject.AddComponent<GroundShadowField>();
        profiles = Resources.Load<AtmosphereLibrary>("WorldAtmosphere/Profiles");
        cloudMap = Resources.Load<Texture2D>("WorldAtmosphere/CloudCover");
        var shader = Resources.Load<Shader>("WorldAtmosphere/WorldAtmosphere");
        if (shader == null) return;
        var palette = Resources.Load<Texture2D>("WorldAtmosphere/necrohanderPalette");
        material = new Material(shader);
        material.SetTexture("_Palette", palette);
        surfaceRenderer = CreatePlane("World mist and bloom", "World Atmosphere", 0, material);
        var depthShader = Resources.Load<Shader>("WorldAtmosphere/WorldDepthPlanes");
        if (depthShader == null) return;
        groundMaterial = new Material(depthShader);
        groundMaterial.SetTexture("_Palette", palette);
        backdropMaterial = new Material(groundMaterial);
        backdropMaterial.EnableKeyword("DISTANT_BASIN");
        groundSurface = CreatePlane("Low mist and ground contact", "Ground", short.MaxValue, groundMaterial);
        backdropSurface = CreatePlane("Distant basin", "BackGround", short.MinValue, backdropMaterial);
    }

    private MeshRenderer CreatePlane(string title, string sortingLayer, int order, Material surfaceMaterial)
    {
        var surface = new GameObject(title, typeof(MeshFilter), typeof(MeshRenderer));
        surface.transform.SetParent(transform, false);
        surface.transform.localPosition = Vector3.forward * 3;
        surface.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var renderer = surface.GetComponent<MeshRenderer>();
        renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 200);
        renderer.sortingLayerName = sortingLayer;
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = surfaceMaterial;
        return renderer;
    }

    private void LateUpdate()
    {
        if (UnityNote.SceneLoader.FloorIsCovered) return;
        var settings = CameraSettingsSO.Instance;
        if (material == null || settings == null) return;
        var preview = LightingPreview.Active;
        float fog = preview?.mist ?? settings.mistStrength;
        float bloom = preview?.bloom ?? settings.bloomStrength;
        if (profiles == null) return;
        var generator = MapGenerator.Instance;
        var goal = profiles.Resolve(sceneName, generator != null ? generator.CurrentRoom : null, out var source);
        float portal = FloorPortalGraphic.AtmosphereWeight;
        ProfileSource = portal > 0 ? "포탈" : source;
        goal = AtmosphereLibrary.Look.Blend(goal, profiles.portal, portal);
        look = hasLook ? AtmosphereLibrary.Look.Blend(look, goal, 1 - Mathf.Exp(-4f * Time.deltaTime / Mathf.Max(.01f, profiles.blendSeconds))) : goal;
        hasLook = true;
        if (floor == null) floor = WorldFloorMask.Attach(PixelArtUpscaleRig.SceneGround);
        if (floor != null) { floor.Prepare(); floor.Bind(material); }
        bool diagnostics = Preview != View.Composite;
        if (material.IsKeywordEnabled("ATMOS_VIEW") != diagnostics)
        {
            if (diagnostics) material.EnableKeyword("ATMOS_VIEW"); else material.DisableKeyword("ATMOS_VIEW");
        }
        if (diagnostics) material.SetInt("_PreviewView",(int)Preview);
        float depth = look.depth * (1f - Mathf.Clamp01(preview?.depthSuppression ?? 0f));
        Shader.SetGlobalFloat(WorldDepth, diagnostics || groundMaterial == null ? 0f : depth);
        if (groundMaterial != null)
        {
            groundSurface.enabled = depth > 0 && !diagnostics && floor != null && floor.Texture != null;
            backdropSurface.enabled = depth > 0 && !diagnostics;
            if (groundSurface.enabled)
            {
                floor.Bind(groundMaterial);
                groundMaterial.SetFloat("_Mist", fog * look.fog);
                groundMaterial.SetVector("_AirLook", new Vector4(look.airPalette.x,look.airPalette.y,Airborne,0));
            }
            backdropMaterial.SetVector("_AirLook", new Vector4(look.airPalette.x,look.airPalette.y,0,0));
        }
        material.SetFloat("_Mist", fog * look.fog);
        material.SetFloat("_Bloom", bloom * look.glow);
        material.SetFloat("_Shafts", look.shafts * (preview?.shafts ?? 1));
        float windStep = Time.deltaTime * look.wind * (preview?.wind ?? 1);
        windPhase += windStep;
        if (lastCloudFrame != Time.frameCount)
        {
            // Advance in world space so crossing a room or rebinding a camera preserves cloud positions.
            cloudTravel.x = Mathf.Repeat(cloudTravel.x + windStep * (.54f/27f),1);
            cloudTravel.y = Mathf.Repeat(cloudTravel.y + windStep * (.19f/19f),1);
            lastCloudFrame = Time.frameCount;
        }
        float cloudStrength = depth * look.cloudShadow * (1 - Mathf.Clamp01(preview?.cloudSuppression ?? 0)) *
            Mathf.Clamp01(preview?.intensity ?? 1) * Mathf.Clamp01((preview?.shadows ?? settings.groundShadowStrength)/.60f);
        Shader.SetGlobalTexture(CloudMap,cloudMap);
        Shader.SetGlobalVector(CloudProjection,new Vector4(cloudTravel.x,cloudTravel.y,
            diagnostics ? 0 : cloudStrength,cloudMap != null ? 1 : 0));
        Shader.SetGlobalFloat("_AtmosphereWindTime", windPhase);
        material.SetVector("_AirLook", new Vector4(look.airPalette.x, look.airPalette.y, Airborne, 0));
        surfaceRenderer.enabled = diagnostics || Airborne > 0 || fog > 0 || bloom > 0 ||
            look.shafts * (preview?.shafts ?? 1) > 0 || preview is { highlight: > 0 };
        Color keyColor = look.shaftColor.a > 0 ? look.shaftColor : Color.white;
        Color shadeColor = look.shadeColor.a > 0 ? look.shadeColor : look.ambient;
        material.SetColor("_SunTint", (keyColor * (preview?.sun ?? Color.white)).linear);
        material.SetColor("_ShadeTint", shadeColor.linear);
        Shader.SetGlobalColor("_WorldShadowTint", shadeColor.linear);
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (litPlayer != player)
        {
            litPlayer = player;
            hands = player != null ? player.GetComponentInChildren<PlayerHandAimController>(true) : null;
        }
        material.SetVector("_LeftHandLight", hands != null ? hands.LightSource(true) : Vector4.zero);
        material.SetVector("_RightHandLight", hands != null ? hands.LightSource(false) : Vector4.zero);
        material.SetVector("_BloomShape",new Vector4(preview?.threshold ?? LightingPreview.DefaultThreshold,
            preview?.radius ?? LightingPreview.DefaultRadius,0,0));
        float highlight = preview?.highlight ?? LightingPreview.DefaultHighlight;
        bool highlights = highlight > 0;
        if (material.IsKeywordEnabled("SUN_HIGHLIGHT") != highlights)
        {
            if (highlights) material.EnableKeyword("SUN_HIGHLIGHT"); else material.DisableKeyword("SUN_HIGHLIGHT");
        }
        float azimuth = (preview?.azimuth ?? settings.groundShadowAngle + 180) * Mathf.Deg2Rad;
        float altitude = preview != null ? preview.elevation * Mathf.Deg2Rad : Mathf.Atan2(1, settings.groundShadowLength);
        material.SetVector("_Sun", new Vector4(Mathf.Cos(azimuth),Mathf.Sin(azimuth),Mathf.Sin(altitude),
            highlight * (preview?.intensity ?? 1)));
        material.SetVector("_HitLight", new Vector4(flash.x, flash.y, Mathf.Clamp01((flashUntil - Time.time) * 10), 0));
        material.SetVector("_AccentLight", new Vector4(accent.x, accent.y, Mathf.Clamp01((accentUntil - Time.time) / .18f), 0));
        foreach (var sourceLight in GroundShadowLight.Globals) sourceLight.Tint(look.ambient * (preview?.sun ?? Color.white), look.globalIntensity * (preview?.intensity ?? 1));
        Shader.SetGlobalFloat(Wind, PolishFeedback.ExistingEnabled(ExistingFeedbackCue.TreeMotion) ? preview?.canopy ?? settings.canopyWindPixels : 0);
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (groundMaterial != null) Destroy(groundMaterial);
        if (backdropMaterial != null) Destroy(backdropMaterial);
    }
}
