#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static class WorkshopUI
{
    public static readonly Color Ink=new Color32(30,29,40,255), Panel=new Color32(47,45,59,255),
        ButtonColor=new Color32(73,64,82,255), Mint=new Color32(145,226,194,255), Paper=new Color32(245,236,221,255);
    public static Canvas Canvas(string name,int order)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
        go.layer=LayerMask.NameToLayer("UI");
        var canvas=go.GetComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.planeDistance=.5f;
        canvas.sortingLayerName="UI"; canvas.sortingOrder=order;
        PixelArtUpscaleRig.BindCanvas(canvas);
        return canvas;
    }
    public static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
    {
        var go=new GameObject(name,typeof(RectTransform));
        go.layer=parent.gameObject.layer;
        var rect=(RectTransform)go.transform; rect.SetParent(parent,false);
        rect.pivot=rect.anchorMin=rect.anchorMax=new Vector2(0,1);
        rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(w,h);
        return rect;
    }
    public static Image Box(Transform parent,string name,float x,float y,float w,float h,Color color)
    {
        var pic=Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();
        pic.color=color;
        if (w < PixelViewport.Width || h < PixelViewport.Height) RoundedPlate.ApplyFlat(pic);
        return pic;
    }
    public static Image Frame(Transform parent,string name,float x,float y,float w,float h)
        => UIChrome.Panel(Rect(parent,name,x,y,w,h));

    public static TMP_Text Label(Transform parent,string caption,int x,int y,int w,int h,int size=10)
    {
        var label=Rect(parent,"Label",x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget=false; label.color=Paper; label.textWrappingMode=TextWrappingModes.NoWrap;
        label.overflowMode=TextOverflowModes.Ellipsis; label.alignment=TextAlignmentOptions.MidlineLeft;
        PixelText.Configure(label,size); label.text=caption;
        return label;
    }
    public static Button Button(Transform parent,string caption,int x,int y,int w,int h,Action action,bool selected=false)
    {
        var button=UIChrome.Button(Rect(parent,caption,x,y,w,h),selected);
        var label=Label(button.transform,caption,2,0,w-4,h); label.alignment=TextAlignmentOptions.Center;
        if (selected) label.color=Ink;
        button.onClick.AddListener(()=>action());
        return button;
    }
    // Every text box in the workshop: a mint outline while it has focus and a visible blinking caret.
    // Unity's default caret is dark grey and disappears against the dark panels.
    internal static void StyleInput(TMP_InputField field)
    {
        // Unity's own caret is placed from font line metrics that the pixel fonts here do not match, so it
        // lands above the box. It is hidden and WorkshopCaret draws one from the glyph positions instead.
        field.customCaretColor=true; field.caretColor=Color.clear; field.caretBlinkRate=.85f;
        field.selectionColor=new Color32(145,226,194,90);
        var outline=Rect(field.transform,"Focus outline",0,0,0,0);
        outline.anchorMin=Vector2.zero; outline.anchorMax=Vector2.one; outline.pivot=new Vector2(.5f,.5f);
        outline.offsetMin=outline.offsetMax=Vector2.zero;
        Edge(new Vector2(0,1),new Vector2(1,1),new Vector2(.5f,1),new Vector2(0,1));
        Edge(new Vector2(0,0),new Vector2(1,0),new Vector2(.5f,0),new Vector2(0,1));
        Edge(new Vector2(0,0),new Vector2(0,1),new Vector2(0,.5f),new Vector2(1,0));
        Edge(new Vector2(1,0),new Vector2(1,1),new Vector2(1,.5f),new Vector2(1,0));
        outline.gameObject.SetActive(field.isFocused);
        field.onSelect.AddListener(_=>outline.gameObject.SetActive(true));
        field.onDeselect.AddListener(_=>outline.gameObject.SetActive(false));
        WrapTextArea(field);
        // The caret object is built when the field is enabled. Fields here get their text component after
        // AddComponent, so the first enable had nothing to attach a caret to; enabling again builds it.
        if (field.enabled && field.gameObject.activeInHierarchy) { field.enabled=false; field.enabled=true; }
        var caret=new GameObject("Text caret",typeof(RectTransform),typeof(Image));
        caret.layer=field.gameObject.layer;
        var bar=(RectTransform)caret.transform; bar.SetParent(field.transform,false);
        // Anchored at the box pivot so anchoredPosition equals the box's local coordinates.
        bar.anchorMin=bar.anchorMax=((RectTransform)field.transform).pivot; bar.pivot=new Vector2(.5f,.5f);
        var image=caret.GetComponent<Image>(); image.color=Color.white; image.raycastTarget=false;
        field.gameObject.AddComponent<WorkshopCaret>().Bind(field,bar);

        void Edge(Vector2 min,Vector2 max,Vector2 pivot,Vector2 size)
        {
            var edge=new GameObject("Edge",typeof(RectTransform),typeof(Image));
            edge.layer=outline.gameObject.layer;
            var rect=(RectTransform)edge.transform; rect.SetParent(outline,false);
            rect.anchorMin=min; rect.anchorMax=max; rect.pivot=pivot;
            rect.anchoredPosition=Vector2.zero; rect.sizeDelta=size;
            var image=edge.GetComponent<Image>(); image.color=Mint; image.raycastTarget=false;
        }
    }

    // Unity scrolls the text so it starts at the left edge of the viewport. The boxes here inset their text a few
    // pixels inside the box, so with the whole box as viewport the text jumped onto the border once edited.
    // An inner "Text area" with the label's own inset becomes the viewport, so the inset is kept, and longer
    // text scrolls inside it instead of spilling over the border.
    private static void WrapTextArea(TMP_InputField field)
    {
        var text=field.textComponent!=null ? field.textComponent.rectTransform : null;
        var box=(RectTransform)field.transform;
        if (text==null || text.parent!=box) return;
        var corners=new Vector3[4]; text.GetWorldCorners(corners);
        Vector2 low=box.InverseTransformPoint(corners[0]), high=box.InverseTransformPoint(corners[2]);
        var bounds=box.rect;
        // Pixel-font glyphs reach about a pixel outside their text rect, so clipping at the text rect cut
        // the first glyph's left column and left it faint. Clipping happens on a slightly larger parent;
        // the text area itself, which Unity aligns the text to, keeps the label's exact rect.
        const float spillX=2, spillY=1;
        var clip=new GameObject("Text clip",typeof(RectTransform),typeof(RectMask2D));
        clip.layer=box.gameObject.layer;
        var clipRect=(RectTransform)clip.transform; clipRect.SetParent(box,false);
        clipRect.SetSiblingIndex(text.GetSiblingIndex());
        clipRect.anchorMin=Vector2.zero; clipRect.anchorMax=Vector2.one; clipRect.pivot=new Vector2(0,1);
        clipRect.offsetMin=new Vector2(low.x-bounds.xMin-spillX,low.y-bounds.yMin-spillY);
        clipRect.offsetMax=new Vector2(high.x-bounds.xMax+spillX,high.y-bounds.yMax+spillY);
        var area=new GameObject("Text area",typeof(RectTransform));
        area.layer=box.gameObject.layer;
        var rect=(RectTransform)area.transform; rect.SetParent(clipRect,false);
        rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.pivot=new Vector2(0,1);
        rect.offsetMin=new Vector2(spillX,spillY);
        rect.offsetMax=new Vector2(-spillX,-spillY);
        text.SetParent(rect,false);
        text.anchorMin=Vector2.zero; text.anchorMax=Vector2.one; text.pivot=new Vector2(0,1);
        text.offsetMin=text.offsetMax=Vector2.zero;
        // Labels default to an ellipsis, which hid the end of long input and the caret with it. A single-line
        // box now lets the text run on, clipped by the text area and scrolled to keep the caret in view.
        if (field.lineType==TMP_InputField.LineType.SingleLine)
        {
            var label=field.textComponent;
            label.overflowMode=TextOverflowModes.Overflow;
            label.textWrappingMode=TextWrappingModes.NoWrap;
        }
        // Wrapped text starts at the very top of the box, where the tallest glyphs were clipped; leave 2 pixels.
        else text.offsetMax=new Vector2(0,-2);
        field.textViewport=rect;
    }

    internal static void TextPreview(TMP_InputField field, RectTransform parent, int y, int height)
    {
        var panel = Frame(parent,"문구 미리보기",12,y,608,height);
        var label = Label(panel.transform,"",8,2,392,height-4,12);
        label.textWrappingMode = TextWrappingModes.Normal;
        var player = label.gameObject.AddComponent<TMPTextEffectPlayer>();
        void Replay() => player.SetText(field.text);
        field.onValueChanged.AddListener(player.SetText);
        field.resetOnDeActivation = false;
        void Wrap(string open, string close)
        {
            string raw = field.text;
            if (string.IsNullOrEmpty(raw)) return;
            int a = Mathf.Clamp(field.selectionStringAnchorPosition, 0, raw.Length);
            int b = Mathf.Clamp(field.selectionStringFocusPosition, 0, raw.Length);
            int from = a == b ? 0 : Mathf.Min(a,b);
            int to = a == b ? raw.Length : Mathf.Max(a,b);
            field.text = raw.Insert(to,close).Insert(from,open);
            field.selectionStringAnchorPosition = from + open.Length;
            field.selectionStringFocusPosition = to + open.Length;
        }
        int row = (height-4)/2;
        Button(panel.transform,"타이핑",410,2,94,row,()=>Wrap("<type>","</type>"));
        Button(panel.transform,"물결",508,2,94,row,()=>Wrap("<wave>","</wave>"));
        Button(panel.transform,"민트색",410,2+row,94,row,()=>Wrap("<color=#AFDFC9>","</color>"));
        Button(panel.transform,"다시 재생",508,2+row,94,row,Replay);
        Replay();
    }

    internal static void TitleButton()
    {
        if (!WorkshopTrial.Available) return;
        var canvas=Canvas("Workshop entry",100);
        Button(canvas.transform,"레벨 제작실",548,8,84,20,()=>WorkshopTrial.Open());
    }
}

// Right-click on a stepper button steps backwards; Unity buttons only answer the left button.
internal sealed class WorkshopRightClick : MonoBehaviour, IPointerClickHandler
{
    internal Action action;

    public void OnPointerClick(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Right || action == null) return;
        if (TryGetComponent<Selectable>(out var button) && !button.IsInteractable()) return;
        action();
    }
}

// A white blinking bar after the character at the caret, sized to the box (single line) or the glyph line.
internal sealed class WorkshopCaret : MonoBehaviour
{
    private TMP_InputField field;
    private RectTransform bar;
    private int lastPosition = -1;
    private float blinkStart;

    internal void Bind(TMP_InputField input, RectTransform caret)
    {
        field = input; bar = caret;
        bar.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (field == null || bar == null) return;
        bool show = field.isFocused && field.selectionAnchorPosition == field.selectionFocusPosition;
        if (!show) { bar.gameObject.SetActive(false); lastPosition = -1; return; }
        int position = Mathf.Clamp(field.stringPosition, 0, field.text.Length);
        if (position != lastPosition) { lastPosition = position; blinkStart = Time.unscaledTime; }
        bool on = (Time.unscaledTime - blinkStart) % 1.06f < .6f;
        bar.gameObject.SetActive(on);
        if (!on) return;

        var text = field.textComponent;
        var box = (RectTransform)field.transform;
        var info = text.textInfo;
        float x; float? top = null, bottom = null;
        int count = info?.characterCount ?? 0;
        if (count == 0 || position == 0 || info == null)
        {
            var r = text.rectTransform.rect;
            x = box.InverseTransformPoint(text.transform.TransformPoint(new Vector3(r.xMin + text.margin.x, 0, 0))).x + 1;
            if (count > 0 && info != null) x = box.InverseTransformPoint(text.transform.TransformPoint(info.characterInfo[0].bottomLeft)).x;
        }
        else
        {
            var character = info.characterInfo[Mathf.Min(position, count) - 1];
            x = box.InverseTransformPoint(text.transform.TransformPoint(new Vector3(character.xAdvance, 0, 0))).x + 1;
            if (field.lineType != TMP_InputField.LineType.SingleLine && character.lineNumber < info.lineCount)
            {
                // The tallest glyph on the caret's line, so a full stop at the end does not shrink the caret.
                var line = info.lineInfo[character.lineNumber];
                float lineTop = float.MinValue, lineBottom = float.MaxValue;
                for (int i = line.firstCharacterIndex; i <= line.lastCharacterIndex && i < count; i++)
                {
                    var glyph = info.characterInfo[i];
                    if (!glyph.isVisible) continue;
                    lineTop = Mathf.Max(lineTop, glyph.topLeft.y); lineBottom = Mathf.Min(lineBottom, glyph.bottomLeft.y);
                }
                if (lineTop > lineBottom)
                {
                    top = box.InverseTransformPoint(text.transform.TransformPoint(new Vector3(0, lineTop, 0))).y + 1;
                    bottom = box.InverseTransformPoint(text.transform.TransformPoint(new Vector3(0, lineBottom, 0))).y - 1;
                }
            }
        }
        var rect = box.rect;
        float high = top ?? rect.yMax - 3, low = bottom ?? rect.yMin + 3;
        x = Mathf.Clamp(x, rect.xMin + 2, rect.xMax - 2);
        bar.anchoredPosition = new Vector2(Mathf.Round(x) + .5f, (high + low) * .5f);
        bar.sizeDelta = new Vector2(1, Mathf.Max(6, high - low));
    }
}

#endif
