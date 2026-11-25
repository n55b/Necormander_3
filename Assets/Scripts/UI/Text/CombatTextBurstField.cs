using UnityEngine;
using UnityEngine.UI;

// One canvas mesh owns the sparks and speed lines behind every active damage label.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CombatTextBurstField : MaskableGraphic
{
    public const int Capacity = 24;
    private struct Burst
    {
        public CombatTextLibrary.Style style;
        public Vector3 world;
        public Color accent;
        public float age, phase;
    }
    private readonly Burst[] bursts = new Burst[Capacity];
    private int edgeOwner = -1;
    private Vector2 origin, xAxis, yAxis;
    private const float Duration = .57f;
    public int ActiveCount { get; private set; }

    internal void Emit(CombatTextLibrary.Style style, Vector3 world, Color ink, int sequence)
    {
        if (!UISkinSettings.MotionOn || (style.shards == 0 && style.edgeRays == 0) ||
            !PixelViewport.TryWorldToGamePoint(world, out var p) || p.x < 0 || p.x > PixelViewport.Width ||
            p.y < 0 || p.y > PixelViewport.Height) return;
        int slot = -1;
        for (int i = 0; i < Capacity; i++)
        {
            var prior = bursts[i];
            if (prior.style == null) { slot = i; break; }
            if (prior.style.priority >= style.priority) continue;
            if (slot < 0 || prior.style.priority < bursts[slot].style.priority ||
                (prior.style.priority == bursts[slot].style.priority && prior.age > bursts[slot].age)) slot = i;
        }
        if (slot < 0) return;
        if (bursts[slot].style == null) ActiveCount++;
        if (edgeOwner == slot) edgeOwner = -1;
        if (style.edgeRays > 0 && (edgeOwner < 0 || style.priority > bursts[edgeOwner].style.priority))
            edgeOwner = slot;
        bursts[slot] = new Burst { style = style, world = world, phase = (sequence % 36) * .71f,
            accent = Color.Lerp(TextMotionLibrary.PaletteColor(style.swatch, ink), ink, .18f) };
        SetVerticesDirty();
    }

    public void Clear()
    {
        System.Array.Clear(bursts, 0, Capacity);
        ActiveCount = 0;
        edgeOwner = -1;
        SetVerticesDirty();
    }

    private void LateUpdate()
    {
        if (ActiveCount == 0) return;
        if (!UISkinSettings.MotionOn) { Clear(); return; }
        for (int i = 0; i < Capacity; i++)
        {
            if (bursts[i].style == null) continue;
            bursts[i].age += Time.deltaTime;
            if (bursts[i].age < Duration) continue;
            bursts[i] = default;
            ActiveCount--;
            if (edgeOwner == i) edgeOwner = -1;
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (ActiveCount == 0 || !PixelViewport.TryGamePointToCanvas(Vector2.zero, canvas, rectTransform, out origin) ||
            !PixelViewport.TryGamePointToCanvas(Vector2.right * PixelViewport.Width, canvas, rectTransform, out var right) ||
            !PixelViewport.TryGamePointToCanvas(Vector2.up * PixelViewport.Height, canvas, rectTransform, out var top)) return;
        xAxis = (right - origin) / PixelViewport.Width;
        yAxis = (top - origin) / PixelViewport.Height;
        for (int i = 0; i < Capacity; i++)
        {
            var burst = bursts[i];
            if (burst.style == null || !PixelViewport.TryWorldToGamePoint(burst.world, out var center)) continue;
            float t = Mathf.Clamp01(burst.age / Duration);
            float fade = 1 - Mathf.SmoothStep(0, 1, t);
            Color tail = burst.accent;
            tail.a = fade * .85f;
            Color core = Color.Lerp(burst.accent, Color.white, .77f);
            core.a = fade;
            int count = Mathf.Clamp(burst.style.shards, 0, 24);
            float travel = 1 - (1 - t) * (1 - t);
            DrawMark(mesh, center, burst.style.mark, travel, tail, core);
            for (int j = 0; j < count; j++)
            {
                float angle = burst.phase + j * 2.399963f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float reach = burst.style.burstRadius * (.62f + (j % 4) * .13f);
                var head = center + direction * (7 + reach * travel) + Vector2.down * (8 * t * t);
                var back = head - direction * (2 + reach * .32f * fade);
                Blade(mesh, back, head, j % 3 == 0 ? 2 : 1, tail, core);
                if (j % 3 == 0)
                {
                    var glint = head + direction * 4;
                    Blade(mesh, glint - Vector2.up * 3, glint + Vector2.up * 3, 1, tail, core);
                }
            }
            if (i == edgeOwner) DrawEdges(mesh, center, burst, t, tail, core);
        }
    }

    private void DrawMark(VertexHelper mesh, Vector2 center, CombatTextLibrary.Mark mark, float travel, Color tail, Color core)
    {
        if (mark == CombatTextLibrary.Mark.None) return;
        float radius = Mathf.Lerp(7, 22, travel);
        if (mark == CombatTextLibrary.Mark.TwinSlash)
        {
            for (int i = -1; i <= 1; i += 2)
            {
                var shoulder = center + Vector2.right * (i * radius);
                Blade(mesh, shoulder - new Vector2(7, 10), shoulder + new Vector2(5, 6), 2, tail, core);
            }
            return;
        }
        if (mark == CombatTextLibrary.Mark.Guard)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3;
                var from = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var to = new Vector2(Mathf.Cos(a + Mathf.PI / 3), Mathf.Sin(a + Mathf.PI / 3));
                Blade(mesh, center + from * radius, center + Vector2.Lerp(from, to, .76f) * radius, 1, tail, core);
            }
            return;
        }
        // Flares frame the word on both sides, leaving a clear strip through its baseline.
        for (int i = -1; i <= 1; i += 2)
        {
            var flare = center + new Vector2(i * (8 + radius), i * 3);
            float arm = mark == CombatTextLibrary.Mark.Nova ? 10 : 6;
            Blade(mesh, flare - Vector2.up * arm, flare + Vector2.up * arm, 2, tail, core);
            Blade(mesh, flare - Vector2.right * arm, flare + Vector2.right * arm, 2, tail, core);
        }
    }

    private void DrawEdges(VertexHelper mesh, Vector2 center, Burst burst, float t, Color tail, Color core)
    {
        int count = Mathf.Clamp(burst.style.edgeRays, 0, 32);
        tail.a *= .3f;
        core.a *= .72f;
        for (int j = 0; j < count; j++)
        {
            float u = Mathf.Repeat((j / 4 + .5f) / Mathf.Ceil(count / 4f) + .08f * Mathf.Sin(burst.phase + j), 1);
            Vector2 edge = (j % 4) switch
            {
                0 => new Vector2(PixelViewport.Width * u, 0),
                1 => new Vector2(PixelViewport.Width, PixelViewport.Height * u),
                2 => new Vector2(PixelViewport.Width * u, PixelViewport.Height),
                _ => new Vector2(0, PixelViewport.Height * u)
            };
            Vector2 start = Vector2.Lerp(edge, center, t * .14f);
            Vector2 tip = Vector2.Lerp(edge, center, .15f + .16f * Mathf.Sin(Mathf.PI * t) + .04f * (j % 3));
            Blade(mesh, start, tip, j % 3 == 0 ? 2 : 1, tail, core);
        }
    }

    private void Blade(VertexHelper mesh, Vector2 back, Vector2 tip, float width, Color tail, Color core)
    {
        var delta = tip - back;
        var wing = new Vector2(-delta.y, delta.x).normalized * width;
        var shoulder = back + delta * .34f;
        int first = mesh.currentVertCount;
        mesh.AddVert(Local(back), tail, Vector2.zero);
        mesh.AddVert(Local(shoulder + wing), tail, Vector2.zero);
        mesh.AddVert(Local(tip), core, Vector2.zero);
        mesh.AddVert(Local(shoulder - wing), core, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first, first + 2, first + 3);
    }

    private Vector2 Local(Vector2 point) => origin + xAxis * point.x + yAxis * point.y;
}
