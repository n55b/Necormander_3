using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 목록-상세 골격(640×360 논리 px). 목록 · 상세(ScrollRect) · 미리보기 · 설명 칸 · 행동 줄.
// 값 위젯은 쓰고 나서 Refresh()만 부른다. Draw(page)는 항목 전환·원소 추가·삭제·실행 취소·저장처럼 구조가 바뀔 때만 부른다.
public sealed partial class RoomWorkshopWindow
{
    private const int ListX=8,ListW=156,DetailX=168,DetailW=302,AsideX=474,AsideW=158,BodyY=56,ListH=220,DetailH=240,RowH=22,HelpY=208,ActionsY=299;
    private readonly List<Action> refreshers=new();   // Draw가 비운다
    private TMP_Text help, helpPath;                  // 설명 칸
    private string detailKey;                         // 직전 Detail의 항목. 같으면 스크롤을 되살린다
    private float detailRestore;                      // EndDetail이 적용할 content y

    private void Refresh(){ foreach(var r in refreshers) r(); RefreshUnsaved(); }

    // 목록 행: 색 막대(3px) · 제목(10) · 요약(8, 오른쪽 정렬) · 배지(8): "●" 미저장, 민트 4×4 = 원본값과 다름(FieldRow 민트와 같은 기준).
    // 방향키 이동은 페이지 Keys가 맡으므로 Navigation을 끈다. 요약·배지는 refresher로 고친다.
    // icon이 있으면 16px 그림(x7)을 두고 제목은 x26부터 폭 106, 요약은 x120 폭 22(한 가지 표시만)다.
    private Button ListRow(Transform list,int index,string id,string title,Func<string> summary,Color stripe,Func<(bool unsaved,bool changed)> badge,bool selected,Action pick,Sprite icon=null)
    {
        var button=WorkshopUI.Button(list,title,0,index*RowH,ListW,RowH-2,()=>Safe(pick),selected,id);
        WorkshopUI.NoNavigation(button);
        var name=button.GetComponentInChildren<TMP_Text>();
        name.alignment=TextAlignmentOptions.MidlineLeft;
        int titleX=icon!=null ? 26 : 8, briefX=icon!=null ? ListW-36 : 88;
        name.rectTransform.anchoredPosition=new Vector2(titleX,0); name.rectTransform.sizeDelta=new Vector2(icon!=null ? ListW-50 : 80,RowH-2);
        WorkshopUI.Box(button.transform,"Stripe",2,4,3,RowH-10,stripe).raycastTarget=false;
        if(icon!=null) WorkshopUI.Picture(button.transform,"Icon",7,2,16,16,icon);
        var brief=WorkshopUI.Label(button.transform,"",briefX,0,ListW-14-briefX,RowH-2,8);
        brief.alignment=TextAlignmentOptions.MidlineRight;
        if(selected) brief.color=WorkshopUI.Ink;
        var dot=WorkshopUI.Label(button.transform,"●",ListW-12,0,8,RowH-2,8);
        var mint=WorkshopUI.Box(button.transform,"Changed",ListW-7,(RowH-2)/2-2,4,4,WorkshopUI.Mint);
        mint.raycastTarget=false;
        void Badge(){ brief.text=summary?.Invoke() ?? ""; var (unsaved,changed)=badge(); dot.gameObject.SetActive(unsaved); mint.gameObject.SetActive(changed && !unsaved); }
        Badge(); refreshers.Add(Badge);
        return button;
    }

    // 상세 ScrollRect를 만들고 content를 돌려준다. key가 직전과 같으면 그 스크롤을, 시험 복귀 중이면 WorkshopSession.Scroll을 되살린다.
    private RectTransform Detail(string key,string id)
    {
        var body=WorkshopUI.Scroll(content,id,DetailX,BodyY,DetailW,DetailH,y=>detailScrollY=y,out _);
        detailRestore=restoringScroll ? WorkshopSession.Scroll : key==detailKey ? detailScrollY : 0;
        detailKey=key;
        return body;
    }
    private void EndDetail(RectTransform detail,int height)
    {
        WorkshopUI.SetContentHeight(detail,height);
        detailScrollY=Mathf.Clamp(detailRestore,0,Mathf.Max(0,height-DetailH));
        detail.anchoredPosition=new Vector2(0,detailScrollY);   // 막대 위치는 다음 onValueChanged가 맞춘다
    }
    private RectTransform Aside()=>WorkshopUI.Frame(content,"Aside",AsideX,BodyY,AsideW,148).rectTransform;
    // 설명 칸: 호버·포커스한 필드의 라벨·단위·범위·설명 전문(Micro 8 줄바꿈, 넘치면 자름)과 맨 아래 코드 경로 한 줄.
    private void HelpBox()
    {
        var box=WorkshopUI.Frame(content,"Help",AsideX,HelpY,AsideW,88).rectTransform;
        help=WorkshopUI.Label(box,"",4,3,AsideW-8,68,8);
        help.textWrappingMode=TextWrappingModes.Normal; help.overflowMode=TextOverflowModes.Truncate; help.alignment=TextAlignmentOptions.TopLeft;
        helpPath=WorkshopUI.Label(box,"",4,73,AsideW-8,12,8);
    }
    private void ShowHelp(string text,string path){ if(help==null) return; help.text=text; helpPath.text=path; }
    // Micro 8 줄바꿈 블록. 다음 y를 돌려준다.
    private int Note(RectTransform parent,int y,string text)
    {
        var label=WorkshopUI.Label(parent,text,2,y,DetailW-6,0,8);
        label.textWrappingMode=TextWrappingModes.Normal; label.overflowMode=TextOverflowModes.Overflow; label.alignment=TextAlignmentOptions.TopLeft;
        int height=Mathf.CeilToInt(label.GetPreferredValues(text,DetailW-6,0).y);
        label.rectTransform.sizeDelta=new Vector2(DetailW-6,height);
        return y+height+2;
    }
    private void Section(RectTransform parent,ref int y,string title){ WorkshopUI.Label(parent,title,2,y,DetailW-6,14,12); y+=16; }

    // 필드 한 줄(높이 24, Range가 유한하면 아래 슬라이더 줄 +16). 이름은 필드 경로, 그 안에 "−"·"값"·"+"·"원본".
    // 라벨(0,0,118) · [−](120) · 값(140,50) · [+](192) · 단위(212,24) · 원본 칸(238,58): 바뀐 칸이면 "원본 n단위", 누르면 원본값으로.
    // 숫자는 Stepper, bool은 Toggle, enum은 Picker(한 칸에 들어가도록). 바뀐 칸은 라벨이 민트다.
    // d.byPath에 path가 없으면(SO 필드 이름이 바뀜) "필드 없음" 줄을 그린다. 다음 y를 돌려준다.
    // label: 행 라벨 대체(효과 카드의 "수치"). unitOf: 배율·단위를 그릴 때 계산(능력치·비율 보정에 따라 바뀜, 원본 칸도 같은 배율).
    // redraw: 쓰기 뒤 Refresh 대신 Draw(page)(다른 줄의 단위나 결과 문장이 바뀔 때). lines: 문자열 입력칸 줄 수.
    // choices: 열거형을 가로 묶음(Choice, 이름 "<경로>/<라벨>")으로 그린다. disabledReason: 열거형 Picker의 막힌 칸과 이유.
    // 문자열 행(높이 20×lines+16): 입력칸 "값"(120,1,176) · 그 아래 "글꼴 경고"(아틀라스에 없는 글자) · 바뀐 칸이면 라벨 아래 [원본으로].
    // 그림 행(높이 36): 32px 그림 버튼 "아이콘" · 이름 · [기본](비우기). 누르면 같은 시트의 조각 격자가 뜬다.
    private int FieldRow(RectTransform parent,int y,DraftStore.Draft d,string path,string note=null,string label=null,
        Func<(float scale,string unit)> unitOf=null,bool redraw=false,int lines=1,string[] choices=null,Func<int,string> disabledReason=null)
    {
        if(!d.byPath.TryGetValue(path,out var f))
        { WorkshopUI.Label(parent,"필드 없음: "+path,2,y,DetailW-6,22,8); return y+24; }
        var (scale,unit)=unitOf?.Invoke() ?? (f.scale,f.unit);
        bool text=f.type==typeof(string) && !f.readOnly;
        bool number=!f.IsReference && !f.IsAsset && f.type!=typeof(bool) && f.type!=typeof(string) && !f.type.IsEnum;
        bool slider=number && !float.IsInfinity(f.min) && !float.IsInfinity(f.max);
        int height=text ? 20*lines+16 : f.IsAsset ? 36 : slider ? 40 : 24;
        var row=WorkshopUI.Box(parent,path,0,y,DetailW-6,height,Color.clear);
        void Write(string value)=>Safe(()=>{ DraftStore.Set(d,f,value); if(redraw) Draw(page); else Refresh(); });
        var caption=WorkshopUI.Label(row.transform,label ?? f.label,0,0,118,22,10);
        Button reset=null;
        void Mark(){ bool changed=d.Changed(f); caption.color=changed ? WorkshopUI.Mint : WorkshopUI.Paper; if(reset!=null) reset.gameObject.SetActive(changed); }
        if(number)
        {
            refreshers.Add(WorkshopUI.Stepper(row.transform,null,120,1,90,()=>Number(d.Value(f)),v=>Write(Format(f,v)),Step(f,unit),f.min,f.max,scale));
            if(slider) refreshers.Add(WorkshopUI.Slider(row.transform,"슬라이더",120,24,176,f.min,f.max,()=>(float)Number(d.Value(f)),
                v=>Write(Format(f,Math.Round(v,4))),f.type==typeof(int)));
        }
        else if(f.type==typeof(bool))
            refreshers.Add(WorkshopUI.Toggle(row.transform,"값",120,1,90,()=>On(d.Value(f)),on=>Write(on.ToString())));
        else if(f.type.IsEnum)
        {
            var names=Enum.GetNames(f.type);
            refreshers.Add(choices!=null ? WorkshopUI.Choice(row.transform,path,120,1,90,20,choices,()=>Array.IndexOf(names,d.Value(f)),i=>Write(names[i])) :
                WorkshopUI.Picker(row.transform,content,"값",120,1,90,names.Select(n=>DesignerInspectorLabels.EnumLabel(f.type,n)).ToArray(),
                ()=>Array.IndexOf(names,d.Value(f)),i=>Write(names[i]),disabledReason));
        }
        else if(text)
        {
            var input=Input(row.transform,"값",d.Value(f),120,1,176,20*lines,lines>1);
            input.onValueChanged.AddListener(Write);   // 포커스 없이 넣은 값(검사·붙여넣기)도 바로 초안에 든다
            var glyphs=WorkshopUI.Label(row.transform,"",120,20*lines+2,176,12,8);
            glyphs.name="글꼴 경고"; glyphs.color=Warn; glyphs.overflowMode=TextOverflowModes.Truncate;
            glyphs.GetComponent<PixelText>().UseFaceResolver(catalog.FontForInput);   // 없는 글자를 보여 준다
            refreshers.Add(()=>
            {
                string value=d.Value(f);
                if(!input.isFocused && input.text!=value) input.SetTextWithoutNotify(value);
                string missing=PixelText.MissingGlyphs(value);
                glyphs.gameObject.SetActive(missing.Length>0);
                glyphs.text="⚠ 글꼴에 없는 글자: "+missing;
            });
        }
        else if(f.IsAsset) IconRow(row,d,f,Write);
        else WorkshopUI.Label(row.transform,d.Value(f),120,0,90,22,8);
        if(unit!=null && !text && !f.IsAsset) WorkshopUI.Label(row.transform,unit,212,0,24,22,8);
        if(f.readOnly)
        {
            WorkshopUI.Label(row.transform,"읽기 전용",238,0,58,22,8);
            foreach(var s in row.GetComponentsInChildren<Selectable>()) s.interactable=false;
        }
        else if(text || f.IsAsset)
        {
            reset=text ? WorkshopUI.Button(row.transform,"원본으로",0,22,60,16,()=>Write(null),false,"원본") :
                WorkshopUI.Button(row.transform,"원본으로",238,2,58,18,()=>Write(null),false,"원본");
            PixelText.Configure(reset.GetComponentInChildren<TMP_Text>(),8);
        }
        else
        {
            string original=ProjectParameters.Original(d.entry.source,f);
            string shown=number ? (Number(original)*scale).ToString("0.##",CultureInfo.InvariantCulture) :
                f.type==typeof(bool) ? (On(original) ? "켜짐" : "꺼짐") : f.type.IsEnum ? DesignerInspectorLabels.EnumLabel(f.type,original) : original;
            reset=WorkshopUI.Button(row.transform,"원본 "+shown+(unit ?? ""),238,2,58,18,()=>Write(null),false,"원본");
            PixelText.Configure(reset.GetComponentInChildren<TMP_Text>(),8);
        }
        Mark(); refreshers.Add(Mark);
        string help=HelpText(f);
        WorkshopUI.Hover(row,()=>ShowHelp(help,f.path));
        foreach(var s in row.GetComponentsInChildren<Selectable>()) WorkshopUI.Hover(s,()=>ShowHelp(help,f.path));
        y+=height;
        return note==null ? y : Note(parent,y,note);
    }
    private static readonly Color Warn=new Color32(255,176,64,255);
    // 그림 행의 위젯. 후보 = 구운 카탈로그(WorkshopCatalog.assets)에서 지금 그림과 같은 시트의 조각. 비었으면 원본 아이템 아이콘들의 시트.
    private void IconRow(Image row,DraftStore.Draft d,ProjectParameters.Field f,Action<string> write)
    {
        var table=WorkshopCatalog.Shared?.assets ?? Array.Empty<WorkshopCatalog.AssetRef>();
        Sprite Current()=>f.read() as Sprite;
        Sprite[] Options()
        {
            var sheets=Current() is Sprite now ? new HashSet<Texture2D>{now.texture} :
                new HashSet<Texture2D>(EnsureCatalog().Select(e=>e.source).OfType<ItemSO>().Where(i=>i.icon!=null).Select(i=>i.icon.texture));
            return table.Select(a=>a.asset as Sprite).Where(s=>s!=null && sheets.Contains(s.texture)).Distinct().ToArray();
        }
        var button=WorkshopUI.Button(row.transform,"",120,1,32,32,()=>Safe(()=>{
            var options=Options();
            WorkshopUI.IconPicker(content,"아이콘",options,()=>Array.IndexOf(options,Current()),i=>write(WorkshopCatalog.AssetKey(options[i])));
        }),false,"아이콘");
        var picture=WorkshopUI.Picture(button.transform,"Icon",0,0,32,32,null);
        var name=WorkshopUI.Label(row.transform,"",156,0,80,16,8);
        WorkshopUI.Button(row.transform,"기본",156,18,40,16,()=>write(""),false,"기본");
        void Show(){ var s=Current(); picture.sprite=s; picture.enabled=s!=null; name.text=s!=null ? s.name : "비어 있음"; }
        Show(); refreshers.Add(Show);
    }
    private static string HelpText(ProjectParameters.Field f)
    {
        string range=!float.IsInfinity(f.min) && !float.IsInfinity(f.max) ? "범위 "+Shown(f,f.min)+"~"+Shown(f,f.max) :
            !float.IsInfinity(f.min) ? "최소 "+Shown(f,f.min) : null;
        string head=string.Join(" · ",new[]{f.label,f.unit,range}.Where(s=>!string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(f.help) ? head : head+"\n"+f.help;
    }
    private static string Shown(ProjectParameters.Field f,float v)=>(v*f.scale).ToString("0.##",CultureInfo.InvariantCulture);
    // DATA에 소문자 true로 적힌 값도 켜짐이다(모든 수치 화면 DisplayValue와 같은 판정).
    private static bool On(string text)=>bool.TryParse(text,out bool on) && on;
    private static double Number(string text)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double v) ? v : 0;
    private static string Format(ProjectParameters.Field f,double v)=>f.type==typeof(int) ? ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) :
        ((float)v).ToString(CultureInfo.InvariantCulture);
    // 스테퍼 한 칸(표시 단위): int·피해·스택·%·%p는 1, 그 밖(초·유닛·실수)은 0.1. unit은 FieldRow가 그린 단위(unitOf 포함)다.
    private static double Step(ProjectParameters.Field f,string unit)=>f.type==typeof(int) || unit is "피해" or "스택" or "%" or "%p" ? 1 : 0.1;
}
