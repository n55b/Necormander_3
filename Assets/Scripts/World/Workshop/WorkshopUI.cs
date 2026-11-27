using System;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
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
    // id는 GameObject 이름이 된다. 검사가 이름으로 누르므로 캡션이 바뀌어도 이름은 고정한다. 생략하면 캡션이 이름이다.
    public static Button Button(Transform parent,string caption,int x,int y,int w,int h,Action action,bool selected=false,string id=null)
    {
        var button=UIChrome.Button(Rect(parent,id ?? caption,x,y,w,h),selected);
        var label=Label(button.transform,caption,2,0,w-4,h); label.alignment=TextAlignmentOptions.Center;
        if (selected) label.color=Ink;
        button.onClick.AddListener(()=>action());
        return button;
    }
    // 전체 화면 차단막. click이 있으면 투명 버튼이 되어 누르면 닫는다(목록 팝업). 모달은 click 없이 뒤의 탭·메뉴 클릭만 막는다.
    internal static Image Blocker(Transform parent,Color color,Action click=null)
    {
        var image=Box(parent,"Blocker",0,0,PixelViewport.Width,PixelViewport.Height,color);
        if(click==null) return image;
        var button=image.gameObject.AddComponent<Button>();
        button.transition=Selectable.Transition.None; button.navigation=new Navigation{mode=Navigation.Mode.None};
        button.onClick.AddListener(()=>click());
        return image;
    }

    // ── 값 위젯. 반환값은 자기 표시를 고치는 refresher다. write는 호출하는 화면이 Safe·Refresh로 감싸서 넘긴다. ──

    // 입력칸 하나. 글꼴에 없는 한글은 제작실 입력 글꼴로 대신 그린다.
    internal static TMP_InputField Input(Transform parent,string name,int x,int y,int w,int h,int size=12)
    {
        var box=Frame(parent,name,x,y,w,h);
        var field=box.gameObject.AddComponent<TMP_InputField>();
        field.textViewport=box.rectTransform;
        var label=Label(box.transform,"",4,0,w-8,h,size);
        if(WorkshopTrial.Library!=null) label.GetComponent<PixelText>().UseFaceResolver(WorkshopTrial.Library.FontForInput);
        label.richText=false; field.textComponent=label; field.richText=false;
        return field;
    }

    // 상세 열: 투명 Image(휠 판정) + RectMask2D + ScrollRect(세로, Clamped, 관성 없음).
    // 휠 한 칸 = 22px(한 행): scrollSensitivity = 22 / 입력 모듈의 scrollDeltaPerTick(LevelWorkshop은 6).
    // moved는 정수로 맞춘 content y를 받는다(비트맵 글자 정렬·스크롤 위치 기록). 오른쪽 2px 막대는 내용이 넘칠 때만 보인다.
    internal static RectTransform Scroll(Transform parent,string id,int x,int y,int w,int h,Action<float> moved,out ScrollRect scroll)
    {
        var view=Box(parent,id,x,y,w,h,Color.clear);
        view.gameObject.AddComponent<RectMask2D>();
        var body=Rect(view.transform,"Content",0,0,w-2,h);
        Box(view.transform,"Scroll bar",w-2,0,2,h,ButtonColor).raycastTarget=false;
        scroll=view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport=view.rectTransform; scroll.content=body; scroll.horizontal=false; scroll.vertical=true;
        scroll.movementType=ScrollRect.MovementType.Clamped; scroll.inertia=false;
        float tick=EventSystem.current!=null && EventSystem.current.currentInputModule is InputSystemUIInputModule module && module.scrollDeltaPerTick>0 ? module.scrollDeltaPerTick : 6;
        scroll.scrollSensitivity=22/tick;
        scroll.onValueChanged.AddListener(_=>
        {
            var p=body.anchoredPosition; float rounded=Mathf.Round(p.y);
            if(rounded!=p.y) body.anchoredPosition=new Vector2(p.x,rounded);
            PlaceBar(body); moved?.Invoke(rounded);
        });
        SetContentHeight(body,h);
        return body;
    }
    internal static void SetContentHeight(RectTransform content,int height)
    {
        content.sizeDelta=new Vector2(content.sizeDelta.x,Mathf.Max(height,((RectTransform)content.parent).rect.height));
        PlaceBar(content);
    }
    private static void PlaceBar(RectTransform content)
    {
        var view=(RectTransform)content.parent; var bar=(RectTransform)view.Find("Scroll bar");
        float h=view.rect.height, total=content.rect.height;
        bar.gameObject.SetActive(total>h);
        if(total<=h) return;
        float size=Mathf.Max(8,Mathf.Round(h*h/total));
        bar.sizeDelta=new Vector2(2,size);
        bar.anchoredPosition=new Vector2(bar.anchoredPosition.x,-Mathf.Round((h-size)*Mathf.Clamp01(content.anchoredPosition.y/(total-h))));
    }

    // [−][값][+] 한 줄(높이 20, −·+는 18px). id 아래에 "−"·"값"·"+"를 만든다. id가 null이면 parent에 바로 둔다(FieldRow: "<경로>/+").
    // 값을 누르면 같은 자리에 "값 입력"을 띄운다. Enter·다른 곳 클릭 = 확정, Esc = 취소. 입력과 step은 표시 단위(read×scale)다.
    // Shift ×10, Ctrl ×0.1. 저장값은 [min,max]로 자르고 소수 4자리로 반올림한다.
    internal static Action Stepper(Transform parent,string id,int x,int y,int w,Func<double> read,Action<double> write,
        double step,double min,double max,double scale=1,string format="0.##")
    {
        Transform box=parent;
        if(id!=null){ box=Rect(parent,id,x,y,w,20); x=y=0; }
        TMP_Text shown=null;
        void Refresh()=>shown.text=(read()*scale).ToString(format,CultureInfo.InvariantCulture);
        void Commit(double display){ write(Math.Round(Math.Clamp(display/scale,min,max),4)); Refresh(); }
        void Nudge(int sign)
        {
            var keys=Keyboard.current;
            double amount=step*(keys!=null && keys.shiftKey.isPressed ? 10 : keys!=null && keys.ctrlKey.isPressed ? .1 : 1);
            Commit(read()*scale+sign*amount);
        }
        Button(box,"−",x,y,18,20,()=>Nudge(-1));
        var value=Button(box,"",x+20,y,w-40,20,()=>{},false,"값");
        Button(box,"+",x+w-18,y,18,20,()=>Nudge(1));
        shown=value.GetComponentInChildren<TMP_Text>(); PixelText.Configure(shown,12);
        value.onClick.AddListener(()=>
        {
            var field=Input(box,"값 입력",x+20,y,w-40,20);
            field.contentType=TMP_InputField.ContentType.DecimalNumber;
            field.SetTextWithoutNotify(shown.text);
            field.onEndEdit.AddListener(text=>
            {
                field.gameObject.SetActive(false); UnityEngine.Object.Destroy(field.gameObject);
                if(!field.wasCanceled && double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double typed)) Commit(typed);
            });
            field.Select(); field.ActivateInputField();
        });
        Refresh();
        return Refresh;
    }

    // LightingDebugPanel.Add와 같은 구조(Track/Travel/Handle). 끄는 동안 write만 부르고, refresher는 손잡이만 옮긴다.
    internal static Action Slider(Transform parent,string id,int x,int y,int w,float min,float max,Func<float> read,Action<float> write,bool whole=false)
    {
        var hit=Box(parent,id,x,y,w,14,Color.clear);
        Box(hit.transform,"Track",3,6,w-6,2,ButtonColor).raycastTarget=false;
        var area=Rect(hit.transform,"Travel",3,1,w-6,12);
        var handle=Box(area,"Handle",0,0,4,12,Mint);
        handle.rectTransform.pivot=new Vector2(.5f,.5f);
        handle.rectTransform.sizeDelta=new Vector2(4,-2);
        handle.rectTransform.anchoredPosition=Vector2.zero;
        handle.raycastTarget=false;
        var slider=hit.gameObject.AddComponent<UnityEngine.UI.Slider>();
        slider.targetGraphic=handle; slider.handleRect=handle.rectTransform;
        slider.minValue=min; slider.maxValue=max; slider.wholeNumbers=whole;
        slider.onValueChanged.AddListener(v=>write(v));
        void Refresh()=>slider.SetValueWithoutNotify(read());
        Refresh();
        return Refresh;
    }

    // 버튼 하나. "켜짐"(민트 글자)/"꺼짐". 쓰고 나면 자기 표시를 스스로 고친다(페이지 Refresh가 없는 곳에서도).
    internal static Action Toggle(Transform parent,string id,int x,int y,int w,Func<bool> read,Action<bool> write)
    {
        TMP_Text label=null;
        void Refresh(){ bool on=read(); label.text=on ? "켜짐" : "꺼짐"; label.color=on ? Mint : Paper; }
        label=Button(parent,"",x,y,w,20,()=>{ write(!read()); Refresh(); },false,id).GetComponentInChildren<TMP_Text>();
        Refresh();
        return Refresh;
    }

    // 8개 이하 선택지: 가로 버튼 묶음. 묶음 이름은 id, 버튼 이름은 id+"/"+라벨(id="대상"이면 "대상/모든 적").
    // 선택 표시 면이 달라서 refresher가 묶음 자식을 SetActive(false) 뒤 Destroy하고 다시 만든다(같은 프레임 이름 검색에 옛 버튼이 걸리지 않게).
    internal static Action Choice(Transform parent,string id,int x,int y,int w,int h,string[] labels,Func<int> read,Action<int> write)
    {
        var group=Rect(parent,id,x,y,w,h);
        Action refresh=null;
        refresh=()=>
        {
            var focused=EventSystem.current!=null ? EventSystem.current.currentSelectedGameObject : null;
            string keep=focused!=null && focused.transform.parent==group && group.gameObject.activeInHierarchy ? focused.name : null;   // write가 화면을 다시 그렸으면 옛 묶음이다
            foreach(Transform child in group){ child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            int current=read(), n=labels.Length, cell=(w-2*(n-1))/n;
            for(int i=0;i<n;i++)
            {
                int index=i;
                var button=Button(group,labels[i],i*(cell+2),0,cell,h,()=>{ write(index); refresh(); },index==current,id+"/"+labels[i]);
                if(button.name==keep) button.Select();
            }
        };
        refresh();
        return refresh;
    }

    // 8개 초과: 현재 라벨 버튼 하나. 누르면 overlayParent에 차단막 + 검색칸 + 3열 격자(열 폭 100, 행 20, 쪽당 24개).
    // 오버레이는 하나만 둔다. 바깥 클릭·선택 시 닫히고 Esc는 창 Update가 CloseOverlay()로 닫는다.
    // disabledReason(i)가 null이 아니면 흐리게 보이고, 가리키면 그 이유를 아래 한 줄에 쓴다.
    internal static Action Picker(Transform parent,RectTransform overlayParent,string id,int x,int y,int w,string[] labels,
        Func<int> read,Action<int> write,Func<int,string> disabledReason=null)
    {
        TMP_Text label=null;
        void Refresh(){ int i=read(); label.text=(i>=0 && i<labels.Length ? labels[i] : "")+" ▼"; }
        void Open()
        {
            CloseOverlay();
            overlay=Rect(overlayParent,id+" 선택",0,0,PixelViewport.Width,PixelViewport.Height).gameObject;
            Blocker(overlay.transform,new Color(0,0,0,.5f),()=>CloseOverlay());
            const int Columns=3, PerPage=24, BoxX=160, BoxY=51, BoxW=320, BoxH=258;
            var box=Frame(overlay.transform,"Picker",BoxX,BoxY,BoxW,BoxH).rectTransform;
            var search=Input(box,"선택지 검색",8,8,BoxW-16,20);
            var grid=Rect(box,"Grid",8,32,BoxW-16,PerPage/Columns*22);
            var reason=Label(box,"",8,212,BoxW-16,14,8);
            var pageLabel=Label(box,"",8,BoxH-28,80,20,8);
            int pageIndex=0;
            void Fill()
            {
                foreach(Transform child in grid){ child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
                var shown=Enumerable.Range(0,labels.Length).Where(i=>labels[i].IndexOf(search.text,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
                int pages=Math.Max(1,(shown.Length+PerPage-1)/PerPage); pageIndex=Mathf.Clamp(pageIndex,0,pages-1);
                int current=read();
                for(int n=pageIndex*PerPage;n<Math.Min(shown.Length,(pageIndex+1)*PerPage);n++)
                {
                    int i=shown[n], cell=n%PerPage;
                    string why=disabledReason?.Invoke(i);
                    var option=Button(grid,labels[i],cell%Columns*102,cell/Columns*22,100,20,()=>
                    {
                        if(why!=null){ reason.text=why; return; }
                        CloseOverlay(); write(i); Refresh();
                    },i==current,id+"/"+labels[i]);
                    if(why!=null){ option.GetComponentInChildren<TMP_Text>().alpha=.45f; Hover(option,()=>reason.text=why); }
                }
                pageLabel.text=(pageIndex+1)+" / "+pages;
            }
            Button(box,"이전",BoxW-120,BoxH-28,52,20,()=>{ pageIndex--; Fill(); },false,"선택지 이전");
            Button(box,"다음",BoxW-60,BoxH-28,52,20,()=>{ pageIndex++; Fill(); },false,"선택지 다음");
            search.onValueChanged.AddListener(_=>{ pageIndex=0; Fill(); });
            Fill();
            search.Select(); search.ActivateInputField();
        }
        label=Button(parent,"",x,y,w,20,Open,false,id).GetComponentInChildren<TMP_Text>();
        Refresh();
        return Refresh;
    }
    private static GameObject overlay;
    internal static bool CloseOverlay()
    {
        if(overlay==null) return false;
        overlay.SetActive(false); UnityEngine.Object.Destroy(overlay); overlay=null;
        return true;
    }

    // 마우스 올림·키보드 선택 때 부른다(설명 칸).
    internal static void Hover(Component target,Action enter)=>target.gameObject.AddComponent<WorkshopHover>().enter=enter;
    // 목록 버튼의 방향키·W·S 이동을 끈다. 목록 이동은 페이지 Keys가 맡는다.
    internal static void NoNavigation(Selectable s)=>s.navigation=new Navigation{mode=Navigation.Mode.None};

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
        Button(canvas.transform,"제작실",548,8,84,20,()=>WorkshopTrial.Open(),id:"레벨 제작실");   // id는 검사 클릭 계약
    }
}
