using UnityEngine;
using UnityEngine.UI;

// Geometry and velocity are uploaded at emission. The shared shader animates particles even while the game is paused.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UISparkBurst : MaskableGraphic
{
    private static int activeCount;
    private static Material shared;
    private static ParticleLibrary library;
    private static readonly int Clock = Shader.PropertyToID("_UIFxTime");
    private ParticleLibrary.Preset recipe;
    private float birth, life, unit;
    private int count;
    private bool playing;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCount() { activeCount = 0; }

    public static void Play(ref UISparkBurst burst, RectTransform origin)
    {
        if (!UISkinSettings.MotionOn || origin == null || !origin.gameObject.activeInHierarchy) return;
        if (library == null) library = Resources.Load<ParticleLibrary>("WorldAtmosphere/Particles");
        if (library == null) return;
        int limit = library.density == ParticleLibrary.Density.Low ? 4 : 12;
        if (activeCount >= limit && (burst == null || !burst.playing)) return;
        if (burst == null)
        {
            var go = new GameObject("UI sparks", typeof(RectTransform), typeof(LayoutElement), typeof(CanvasRenderer));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.transform.SetParent(origin, false);
            burst = go.AddComponent<UISparkBurst>();
            burst.raycastTarget = false;
            burst.rectTransform.anchorMin = burst.rectTransform.anchorMax = new Vector2(.5f,.5f);
            burst.rectTransform.anchoredPosition = Vector2.zero;
            if (shared == null) shared = Resources.Load<Material>("UI/Pixel Sparks");
            burst.material = shared;
        }
        var recipe = library.Find(WorldSparkField.Kind.UI);
        if (recipe == null || shared == null) return;
        burst.gameObject.SetActive(true);
        burst.recipe = recipe;
        burst.canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        burst.unit = 1 / Mathf.Max(.001f,PixelViewport.CanvasPixelsPerUnit(burst.canvas,origin));
        burst.rectTransform.sizeDelta = Vector2.one * (48 * burst.unit);
        burst.life = Mathf.Max(.05f,recipe.lifetime.y);
        burst.birth = Time.unscaledTime;
        burst.count = library.density == ParticleLibrary.Density.Low ? 4 : 8;
        if (!burst.playing) activeCount++;
        burst.playing = true;
        Shader.SetGlobalFloat(Clock,Time.unscaledTime);
        burst.SetVerticesDirty();
    }
    private void LateUpdate()
    {
        Shader.SetGlobalFloat(Clock,Time.unscaledTime);
        if (Time.unscaledTime - birth > life) gameObject.SetActive(false);
    }
    protected override void OnDisable()
    {
        if (playing) { activeCount--; playing=false; }
        base.OnDisable();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (!playing) return;
        for (int i=0;i<count;i++)
        {
            float angle = i*2*Mathf.PI/count + .21f;
            Vector2 direction = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            float speed = Mathf.Lerp(recipe.speed.x,recipe.speed.y,(i%3)*.5f);
            int first = mesh.currentVertCount;
            for (int j=0;j<4;j++)
            {
                Vector2 corner = new Vector2(j == 0 || j == 1 ? -1 : 1, j == 0 || j == 3 ? -1 : 1);
                UIVertex v = UIVertex.simpleVert;
                v.position = (direction*5 + corner*.5f) * unit;
                v.color = color;
                v.uv0 = new Vector4((recipe.paletteColumn+i%3+.5f)/16f,1-(recipe.paletteRow+.5f)/9f,birth,life);
                v.uv1 = new Vector4(direction.x*speed,direction.y*speed,unit,0);
                mesh.AddVert(v);
            }
            mesh.AddTriangle(first,first+1,first+2);
            mesh.AddTriangle(first,first+2,first+3);
        }
    }
}
