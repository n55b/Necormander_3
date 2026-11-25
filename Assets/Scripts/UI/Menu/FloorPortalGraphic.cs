using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Drawn on top of the final game frame. Positions below are in logical 640 x 360 pixels.
public sealed class FloorPortalGraphic : MaskableGraphic
{
    private readonly List<FloorPortalActor> actors = new(2);
    private HudMotionElement[] hud;
    private readonly List<LayoutGroup> pausedHudLayouts = new();
    private bool hudExiting;
    private Canvas layer;
    private float progress;
    private PortalItemStream items;
    private static FloorPortalGraphic activePortal;
    internal static float AtmosphereWeight => activePortal != null && activePortal.isActiveAndEnabled ? activePortal.progress : 0;
    private Material sky;
    private PlayerController party;
    private Shader silhouette, capturePose;
    private bool captured;
    private static readonly int Coverage = Shader.PropertyToID("_Coverage");
    private static readonly int TravelTime = Shader.PropertyToID("_TravelTime");

    private static Texture2D travelPalette;
    internal static Color32 TravelColor(int column, int row)
    {
        if (travelPalette == null) travelPalette = Resources.Load<Texture2D>("WorldAtmosphere/necrohanderPalette");
        return travelPalette.GetPixel(column, travelPalette.height - 1 - row);
    }

    private static Canvas CreateTravelCanvas(Camera cam)
    {
        var canvas = new GameObject("Floor travel canvas",typeof(RectTransform),typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = cam.nearClipPlane + .05f;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = short.MaxValue - 1;
        PixelArtUpscaleRig.BindCanvas(canvas);
        return canvas;
    }

    public static FloorPortalGraphic Capture(PlayerController player)
    {
        var cam = PixelArtUpscaleRig.RenderCamera;
        if (player == null || cam == null || PixelArtUpscaleRig.Output == null) return null;
        var body = player.GetComponentInChildren<YSortableObject>()?.Renderer;
        var shader = Resources.Load<Shader>("WorldAtmosphere/PortalSilhouette");
        if (body == null || body.sprite == null || shader == null) return null;
        var captureShader = Resources.Load<Shader>("WorldAtmosphere/PortalPoseCapture");
        if (captureShader == null || !shader.isSupported || !captureShader.isSupported) return null;
        var skyShader = Resources.Load<Shader>("WorldAtmosphere/FloorTravelBackdrop");
        if (skyShader == null || !skyShader.isSupported) return null;
        var canvas = CreateTravelCanvas(cam);
        var go = new GameObject("Floor portal", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(canvas.transform, false);
        var graphic = go.AddComponent<FloorPortalGraphic>();
        graphic.layer = canvas;
        graphic.sky = new Material(skyShader);
        graphic.sky.SetColor("_Night", TravelColor(14,7));
        graphic.sky.SetColor("_Dust", TravelColor(4,7));
        graphic.sky.SetColor("_Ice", TravelColor(0,7));
        graphic.material = graphic.sky;
        graphic.raycastTarget = false;
        graphic.rectTransform.pivot = graphic.rectTransform.anchorMin = Vector2.zero;
        graphic.rectTransform.anchorMax = Vector2.one;
        graphic.rectTransform.offsetMin = graphic.rectTransform.offsetMax = Vector2.zero;
        graphic.party = player;
        graphic.silhouette = shader;
        graphic.capturePose = captureShader;
        graphic.SetProgress(0);
        return graphic;
    }

    private void CaptureParty()
    {
        captured = true;
        hud = FindObjectsByType<HudMotionElement>(FindObjectsSortMode.None);
        if (party == null || layer.worldCamera == null) return;
        var body = party.GetComponentInChildren<YSortableObject>()?.Renderer;
        if (body == null || body.sprite == null) return;
        var material = new Material(capturePose);
        try
        {
            var hero = FloorPortalActor.Capture(transform, layer.worldCamera, body.transform, body,
                (party.transform.lossyScale.x < 0) ^ body.flipX, 0, material, silhouette);
            if (hero == null) return;
            actors.Add(hero);
            var skills = party.GetComponentInChildren<PlayerSkillController>();
            var ally = skills != null ? skills.GetFieldAgent(skills.ActiveSlot) : null;
            if (ally != null && ally.FieldPicture != null)
            {
                var sprite = ally.FieldPicture;
                var visual = ally.IsVisualVisible ? ally.TravelVisual : sprite.transform;
                var actor = FloorPortalActor.Capture(transform, layer.worldCamera, visual, sprite,
                    sprite.flipX ^ (sprite.transform.lossyScale.x < 0), 1, material, silhouette);
                if (actor != null) actors.Add(actor);
            }
            items = PortalItemStream.Create(this, hero.StartPoint);
        }
        finally { Destroy(material); }
    }

    public static IEnumerator CaptureAsync(PlayerController player, System.Action<FloorPortalGraphic> ready)
    {
        ready(Capture(player));
        yield break;
    }

    public void SetProgress(float value)
    {
        activePortal = this;
        progress = Mathf.Clamp01(value);
        sky.SetFloat(Coverage, FloorTravel.BackdropOpacity(progress));
        if (!captured && progress >= FloorTravel.PoseStart) CaptureParty();
        foreach (var actor in actors) actor.Present(progress, actors.Count);
        PresentHud(progress >= FloorTravel.PoseStart ? progress : 0f);
        if (items != null) items.SetProgress(progress);
        sky.SetFloat(TravelTime, progress);
    }

    private void PresentHud(float time)
    {
        if (hud == null) return;
        if (time > 0 && !hudExiting)
        {
            Canvas.ForceUpdateCanvases();
            // Hold driven slots in their settled positions while their children leave the frame.
            foreach (var element in hud)
            {
                if (element == null || ((RectTransform)element.transform).drivenByObject is not LayoutGroup layout ||
                    !layout.isActiveAndEnabled) continue;
                pausedHudLayouts.Add(layout);
                layout.enabled = false;
            }
            hudExiting = true;
        }
        foreach (var element in hud) if (element != null) element.Depart(time);
        if (time > 0 || !hudExiting) return;
        foreach (var layout in pausedHudLayouts) if (layout != null) layout.enabled = true;
        pausedHudLayouts.Clear();
        hudExiting = false;
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var bounds = GetPixelAdjustedRect();
        mesh.AddVert(new Vector3(bounds.xMin, bounds.yMin), Color.white, Vector2.zero);
        mesh.AddVert(new Vector3(bounds.xMin, bounds.yMax), Color.white, Vector2.up);
        mesh.AddVert(new Vector3(bounds.xMax, bounds.yMax), Color.white, Vector2.one);
        mesh.AddVert(new Vector3(bounds.xMax, bounds.yMin), Color.white, Vector2.right);
        mesh.AddTriangle(0, 1, 2);
        mesh.AddTriangle(0, 2, 3);
    }

    internal static void Quad(VertexHelper mesh, Vector2 point, Vector2 size, Color color)
    {
        Vector2 p = Vector2Int.RoundToInt(point);
        int n = mesh.currentVertCount;
        mesh.AddVert(p,color,Vector2.zero); mesh.AddVert(p + Vector2.up * size.y,color,Vector2.zero);
        mesh.AddVert(p + size,color,Vector2.zero); mesh.AddVert(p + Vector2.right * size.x,color,Vector2.zero);
        mesh.AddTriangle(n,n+1,n+2); mesh.AddTriangle(n,n+2,n+3);
    }

    protected override void OnDestroy()
    {
        if (sky != null) Destroy(sky);
        if (activePortal == this) activePortal = null;
        PresentHud(0);
        if (layer != null) Destroy(layer.gameObject);
        base.OnDestroy();
    }
}
