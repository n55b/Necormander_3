using System;
using TMPro;
using UnityEngine;
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
