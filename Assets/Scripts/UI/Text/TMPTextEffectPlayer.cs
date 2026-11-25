using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class TMPTextEffectPlayer : MonoBehaviour
{
    [SerializeField] private bool parseOwnTextOnEnable;
    private TMP_Text label;
    private TMP_MeshInfo[] baseline;
    private Vector2[] offsets=Array.Empty<Vector2>();
    private readonly List<TextEffectRange> parsed = new();
    private readonly List<Span> spans = new();
    private string authoredMarkup;
    private bool revealComplete;
    private bool ownsPose;
    private bool[] hiddenGlyphs = Array.Empty<bool>();
    private VectorUiInlineIcons inlineIcons;

    internal bool GlyphHidden(int index) =>
        spans.Count != 0 && index < hiddenGlyphs.Length && hiddenGlyphs[index];
    private string sourceText;
    internal bool padCombatInk;
    private struct Span { internal TextEffectRange range; internal TextMotionLibrary.Preset preset; internal float start; internal bool cue; }
    public int MeshGenerations { get; private set; }

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        inlineIcons = GetComponent<VectorUiInlineIcons>();
    }
    private void OnEnable()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        label.OnPreRenderText += Capture;
        if (authoredMarkup != null && label.text == sourceText) SetText(authoredMarkup);
        else if (parseOwnTextOnEnable && TextEffectParser.HasMarkup(label.text)) SetText(label.text);
    }
    private void OnDisable()
    {
        label.OnPreRenderText -= Capture;
        ResetPose();
    }
    private void Capture(TMP_TextInfo info)
    {
        baseline = null;
        if (padCombatInk && PixelText.TryGrid(label, out var grid)) PadInk(info, grid);
        if (spans.Count == 0 || label.text != sourceText) return;
        baseline = info.CopyMeshInfoVertexData();
        MeshGenerations++;
        // TMP uploads this pose itself after invoking OnPreRenderText.
        Pose(info);
    }

    // The bitmap atlas has a texel of clear padding. Expose one logical pixel on
    // the right and bottom so the shadow can extend past a glyph's tight quad.
    private static void PadInk(TMP_TextInfo info, PixelText.GlyphGrid grid)
    {
        var right = grid.LocalOffset(Vector2.right);
        var down = grid.LocalOffset(Vector2.down);
        for (int i = 0; i < info.characterCount; i++)
        {
            var ch = info.characterInfo[i];
            if (!ch.isVisible || ch.elementType != TMP_TextElementType.Character) continue;
            var mesh = info.meshInfo[ch.materialReferenceIndex];
            int v = ch.vertexIndex;
            var points = mesh.vertices;
            var uv = mesh.uvs0;
            float width = points[v + 3].x - points[v].x;
            float height = points[v + 1].y - points[v].y;
            if (width <= 0 || height <= 0) continue;
            float du = (uv[v + 3].x - uv[v].x) * right.x / width;
            float dv = (uv[v + 1].y - uv[v].y) * down.y / height;
            points[v] += down;
            points[v + 2] += right;
            points[v + 3] += right + down;
            uv[v].y += dv;
            uv[v + 2].x += du;
            uv[v + 3].x += du;
            uv[v + 3].y += dv;
        }
    }

    public void SetText(string raw)
    {
        if (label == null) label = GetComponent<TMP_Text>();
        label.text = TextEffectParser.Parse(raw, parsed);
        ApplyEffects(parsed);
        authoredMarkup = parsed.Count > 0 ? raw : null;
    }

    // Localization can publish the same source again during a layout refresh.
    internal void AcceptMarkup(string raw)
    {
        if (raw == authoredMarkup && spans.Count > 0) label.text = sourceText;
        else SetText(raw);
    }

    public void FinishReveal()
    {
        revealComplete = true;
        if (spans.Count > 0) RequestFreshMesh();
    }

    public void PlayPreset(string id)
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (!enabled) enabled = true;
        if (label.text != sourceText) ClearEffects();
        spans.RemoveAll(IsCue);
        AddSpan(new TextEffectRange { preset = id, length = label.text.Length, param = 1 }, true);
        BeginPose();
    }

    private static bool IsCue(Span span) => span.cue;

    public void ApplyEffects(List<TextEffectRange> ranges)
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (!enabled) enabled = true;
        authoredMarkup = null;
        revealComplete = false;
        spans.Clear();
        foreach (var range in ranges) AddSpan(range, false);
        BeginPose();
    }

    private void AddSpan(TextEffectRange range, bool cue)
    {
        var preset = TextMotionLibrary.Instance?.Find(range.preset ?? range.type.ToString());
        if (preset != null)
            spans.Add(new Span { range = range, preset = preset, cue = cue,
                start = preset.useGameplayTime ? Time.time : Time.unscaledTime });
    }

    private void BeginPose()
    {
        sourceText = label.text;
        baseline = null;
        if (spans.Count > 0 || ownsPose) RequestFreshMesh();
        ownsPose = false;
    }

    private void RequestFreshMesh()
    {
        // SetVerticesDirty alone may reuse TMP's previous (already animated) buffers.
        label.havePropertiesChanged = true;
        label.SetVerticesDirty();
    }

    public void ClearEffects()
    {
        authoredMarkup = null;
        ResetPose();
    }

    private void ResetPose()
    {
        spans.Clear();
        baseline = null;
        if (ownsPose && label != null) RequestFreshMesh();
        ownsPose = false;
    }
    private void LateUpdate()
    {
        if (spans.Count == 0) return;
        if (label.text != sourceText) { ClearEffects(); return; }
        if (baseline == null || label.havePropertiesChanged) return;
        if (!Pose(label.textInfo)) { ResetPose(); return; }
        label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
    }
    private bool Pose(TMP_TextInfo info)
    {
        bool any = false;
        if (baseline == null || baseline.Length < info.materialCount) return false;
        for (int m = 0; m < info.materialCount; m++)
        {
            int count = info.meshInfo[m].vertexCount;
            Array.Copy(baseline[m].vertices, info.meshInfo[m].vertices, count);
            Array.Copy(baseline[m].colors32, info.meshInfo[m].colors32, count);
        }
        bool native = PixelText.TryGrid(label, out var grid);
        if(offsets.Length<info.characterCount) Array.Resize(ref offsets,Mathf.NextPowerOfTwo(info.characterCount));
        Array.Clear(offsets,0,info.characterCount);
        if (hiddenGlyphs.Length < info.characterCount) Array.Resize(ref hiddenGlyphs, offsets.Length);
        Array.Clear(hiddenGlyphs, 0, info.characterCount);
        bool motion=UISkinSettings.MotionOn;
        foreach (var span in spans)
        {
            var p = span.preset;
            float age = (p.useGameplayTime ? Time.time : Time.unscaledTime) - span.start;
            float speed = Mathf.Max(.01f, span.range.param);
            bool typing = p.shape == TextMotionLibrary.Shape.Typewriter;
            if (typing && (revealComplete || !motion)) continue;
            if (!typing && !p.loop && age * speed >= p.duration) continue;
            if (!typing) any = true;
            int visible = typing ? Mathf.FloorToInt(age * Mathf.Max(1, p.charactersPerSecond) * speed) : 0;
            int ordinal = 0;
            int word = 0;
            bool space = true;
            for (int i = 0; i < info.characterCount && i < label.maxVisibleCharacters; i++)
            {
                var ch = info.characterInfo[i];
                if (ch.index < span.range.startIndex || ch.index >= span.range.startIndex + span.range.length) continue;
                if (typing)
                {
                    if (ordinal++ >= visible && ch.isVisible)
                    {
                        any = true;
                        hiddenGlyphs[i] = true;
                        var colors = info.meshInfo[ch.materialReferenceIndex].colors32;
                        for (int j = ch.vertexIndex; j < ch.vertexIndex + 4; j++) colors[j].a = 0;
                    }
                    continue;
                }
                if (char.IsWhiteSpace(ch.character)) { space = true; continue; }
                if (space) { word++; space = false; }
                if (!ch.isVisible) continue;
                int unit = p.unit == TextMotionLibrary.Unit.Glyph ? i : p.unit == TextMotionLibrary.Unit.Word ? word - 1 : 0;
                Vector2 move = p.Sample(age, unit, speed, out float tint);
                if (!motion) { move=Vector2.zero; tint=.28f; }
                offsets[i]+=move;
                if(tint<=0) continue;
                var accent=motion ? p.Accent(age,unit,speed) : p.accent;
                var mesh = info.meshInfo[ch.materialReferenceIndex];
                int v = ch.vertexIndex;
                for (int j = v; j < v + 4; j++)
                {
                    Color32 old = mesh.colors32[j];
                    Color32 color = Color.Lerp(old, accent, tint);
                    color.a = old.a;
                    mesh.colors32[j] = color;
                }
            }
        }
        for(int i=0;i<info.characterCount && i<label.maxVisibleCharacters;i++)
        {
            var ch=info.characterInfo[i];
            if(!ch.isVisible || offsets[i]==Vector2.zero) continue;
            var mesh=info.meshInfo[ch.materialReferenceIndex];
            Vector3 delta = native ? grid.LocalOffset(offsets[i]) : (Vector3)offsets[i];
            for(int j=0;j<4;j++) mesh.vertices[ch.vertexIndex+j]+=delta;
        }
        if (inlineIcons != null)
        {
            // Vector sprites have their own graphic. Keep TMP's textured quad hidden on every upload.
            for (int i = 0; i < info.characterCount; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible || ch.elementType != TMP_TextElementType.Sprite ||
                    ch.textElement?.glyph is not TMP_SpriteGlyph glyph || !VectorUiCatalog.TryGet(glyph.sprite, out _)) continue;
                var colors = info.meshInfo[ch.materialReferenceIndex].colors32;
                for (int j = ch.vertexIndex; j < ch.vertexIndex + 4; j++) colors[j].a = 0;
            }
            inlineIcons.RefreshReveal(this);
        }
        ownsPose = true;
        return any;
    }
}
