using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Uses TMP’s inline sprite metrics to position the corresponding vector artwork.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
public sealed class VectorUiInlineIcons : MonoBehaviour
{
    private struct InlineIcon
    {
        public int glyphIndex;
        public Sprite Sprite;
        public Rect Bounds;
        public Color32 Tint;
    }

    private readonly List<InlineIcon> glyphs = new List<InlineIcon>();
    private readonly List<VectorUiImage> views = new List<VectorUiImage>();
    private TextMeshProUGUI label;
    private bool pendingLayout;

    private void Awake() => label = GetComponent<TextMeshProUGUI>();

    private void OnEnable()
    {
        label.OnPreRenderText += CaptureGlyphs;
        label.SetVerticesDirty();
    }

    private void OnDisable()
    {
        label.OnPreRenderText -= CaptureGlyphs;
        foreach (var view in views)
            if (view != null) view.enabled = false;
    }

    private void CaptureGlyphs(TMP_TextInfo info)
    {
        glyphs.Clear();
        for (int i = 0; i < info.characterCount; i++)
        {
            var character = info.characterInfo[i];
            if (!character.isVisible || character.elementType != TMP_TextElementType.Sprite ||
                character.textElement?.glyph is not TMP_SpriteGlyph glyph || !VectorUiCatalog.TryGet(glyph.sprite, out _))
                continue;

            var mesh = info.meshInfo[character.materialReferenceIndex];
            int start = character.vertexIndex;
            Vector2 min = mesh.vertices[start];
            Vector2 max = mesh.vertices[start + 2];
            glyphs.Add(new InlineIcon
            {
                glyphIndex = i,
                Sprite = glyph.sprite,
                Bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y),
                Tint = character.vertex_BL.color
            });
            for (int corner = 0; corner < 4; corner++) mesh.colors32[start + corner].a = 0;
        }

        pendingLayout = true;
    }

    private void LateUpdate()
    {
        // TMP can send the callback while the Canvas is rebuilding its graphic list.
        if (!pendingLayout) return;
        pendingLayout = false;
        while (views.Count < glyphs.Count)
        {
            var view = new GameObject("Inline vector icon", typeof(RectTransform), typeof(VectorUiImage))
                .GetComponent<VectorUiImage>();
            view.gameObject.layer = gameObject.layer;
            view.transform.SetParent(label.rectTransform, false);
            view.rectTransform.anchorMin = view.rectTransform.anchorMax = view.rectTransform.pivot = Vector2.zero;
            view.raycastTarget = false;
            views.Add(view);
        }

        TryGetComponent<TMPTextEffectPlayer>(out var effect);
        for (int i = 0; i < views.Count; i++)
        {
            var view = views[i];
            view.enabled = i < glyphs.Count;
            if (!view.enabled) continue;
            var icon = glyphs[i];
            view.canvasRenderer.SetAlpha(effect != null && effect.GlyphHidden(icon.glyphIndex) ? 0f : 1f);
            view.sprite = icon.Sprite;
            view.color = icon.Tint;
            view.maskable = label.maskable;
            view.rectTransform.anchoredPosition = icon.Bounds.position - label.rectTransform.rect.min;
            view.rectTransform.sizeDelta = icon.Bounds.size;
        }
    }
    internal void RefreshReveal(TMPTextEffectPlayer effect)
    {
        for (int i = 0; i < views.Count && i < glyphs.Count; i++)
            views[i].canvasRenderer.SetAlpha(effect.GlyphHidden(glyphs[i].glyphIndex) ? 0f : 1f);
    }
}
