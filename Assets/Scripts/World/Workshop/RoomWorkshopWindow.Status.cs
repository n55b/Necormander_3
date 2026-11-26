using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 상태이상 화면(첫 도메인). 옛 StatusEffectEditorWindow의 목록·설명·타임라인·결과 카드·한눈에 보기를 목록-상세 골격으로 옮겼다.
// 초안은 DraftStore(BALANCE/STATUS/<guid>)에 있다. 저장하면 살아 있는 StatusBalanceSO에 바로 적용되고 StatusRules가 다음 부여부터 읽는다.
public sealed partial class RoomWorkshopWindow
{
    private DraftStore.Draft statusDraft;
    private string statusRow="overview";
    private RectTransform statusList;                 // StatusKeys가 선택 위치를 확인한다
    private (string path,string text)? statusFocus;   // 찾기·경고가 고른 필드: 다음 Draw에서 그 줄로 스크롤하고 설명 칸에 띄운다
    // 결과 카드 계산에만 쓰는 예시 값. 저장하지 않는다(게임에 영향 없음).
    private static float SampleAtk { get=>PlayerPrefs.GetFloat("Workshop.Status.SampleAtk",100f); set=>PlayerPrefs.SetFloat("Workshop.Status.SampleAtk",Mathf.Max(0f,value)); }
    // fields: 이 행이 소유한 필드([원본값으로]의 대상). shared: 공통 지속시간(defaultDuration)을 함께 보이지만 되돌리지 않는다.
    // internal: WorkshopStatusCheck가 필드 집합을 확인한다. 색은 옛 창의 PageInfo 색이다.
    internal static readonly (string key,string title,Color color,string[] fields,bool shared)[] StatusRows=
    {
        ("overview","한눈에 보기",new Color(.55f,.6f,.7f),new string[0],false),
        ("common","공통 지속시간",new Color(.7f,.7f,.7f),new[]{"defaultDuration"},false),
        ("stun","기절",new Color(1f,.85f,.3f),new[]{"stunDuration","stunMinDuration","stunMaxDuration","stunImmunityDuration"},false),
        ("freeze","빙결",new Color(.45f,.85f,1f),new[]{"freezeDuration","freezeBreakDamage","freezeImmunityDuration"},false),
        ("bleed","출혈",new Color(1f,.35f,.35f),new[]{"bleedAtkRatio","bleedTickInterval","bleedFallbackTickDamage"},true),
        ("poison","중독",new Color(.5f,.9f,.4f),new[]{"poisonTickDamage","poisonTickInterval"},true),
        ("bloodpop","비폭",new Color(.85f,.35f,.75f),new[]{"bloodPopThreshold","bloodPopDamage","bloodPopRadius","bloodPopFuse","bloodPopHitDuration"},true),
        ("vulnerable","취약",new Color(1f,.6f,.25f),new[]{"vulnerableDamageBonus"},true),
        ("hitstun","경직",new Color(.6f,.6f,.65f),new string[0],false),
    };
    private const string SharedDuration="defaultDuration";
    private const string SharedNote="⚠ 출혈·중독·비폭·취약 공용 · 바꾸면 모두 바뀜";
    private static readonly Color Pause=new(.5f,.5f,.5f,.5f);

    private void OpenStatus(string row)
    {
        dataCatalog=null;   // 들어올 때마다 다시 바인딩한다(OpenParameters와 같다)
        var entry=EnsureCatalog().FirstOrDefault(e=>e.source is StatusBalanceSO) ?? throw new InvalidOperationException("상태이상 원본 에셋을 찾을 수 없습니다.");
        statusDraft=DraftStore.Open(entry); statusRow=row ?? statusRow;
        PlayerPrefs.SetString("Workshop.Opened."+entry.Document,DateTime.UtcNow.Ticks.ToString("D19"));
        Navigate(Page.Status);   // Navigate가 상태 줄을 비우므로 안내는 그 뒤에 띄운다
        if(statusDraft.Blocked is string reason) Say(reason+" · 최신 제작판이면 [판 차이 정리]로 지운 뒤 저장하세요.");
    }
    private void SaveStatus(){ string when=DraftStore.Save(statusDraft); Draw(page); Say("DATA 저장됨 · 상태이상 · "+when+" 적용"); }
    // 입력 포커스가 없고, 아무것도 선택되지 않았거나 목록 행이 선택돼 있을 때만 ↑↓로 행을 옮긴다.
    // 상세의 스테퍼 버튼이 선택돼 있으면 uGUI Navigation이 방향키를 쓰므로 비켜 선다.
    private void StatusKeys()
    {
        var keys=Keyboard.current; if(keys==null) return;
        int move=keys.upArrowKey.wasPressedThisFrame ? -1 : keys.downArrowKey.wasPressedThisFrame ? 1 : 0;
        if(move==0 || InputFocused()) return;
        var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        if(selected!=null && (statusList==null || !selected.transform.IsChildOf(statusList))) return;
        int i=Array.FindIndex(StatusRows,r=>r.key==statusRow);
        statusRow=StatusRows[Mathf.Clamp(i+move,0,StatusRows.Length-1)].key; Draw(page);
    }
    // (필드, 짧은 제목, 전문). 허브 ⚠ 배지와 미리보기 열이 함께 쓴다. 제목은 미리보기 열 폭(Micro 8, 150px)에 맞춘다.
    private static IEnumerable<(string field,string title,string text)> StatusWarnings(DraftStore.Draft d)
    {
        float V(string path)=>StatusValue(d,path);
        float duration=V(SharedDuration);
        if(V("stunMaxDuration")<V("stunMinDuration"))
            yield return ("stunMaxDuration","⚠ 기절 최대 < 최소","기절 최대 시간이 최소보다 작습니다. 게임에서는 최대 = 최소로 처리합니다.");
        if(V("bleedTickInterval")>duration)
            yield return ("bleedTickInterval","⚠ 틱이 지속시간보다 김","출혈 지속시간 안에 피해가 한 번도 들어가지 않습니다.");
        if(V("poisonTickInterval")>duration)
            yield return ("poisonTickInterval","⚠ 틱이 지속시간보다 김","중독 지속시간 안에 피해가 한 번도 들어가지 않습니다.");
        if(V("bloodPopFuse")+V("bloodPopHitDuration")>duration)
            yield return ("bloodPopFuse","⚠ 폭발 판정 > 지속시간","폭발 판정이 지속시간보다 깁니다.");
    }
    private static float StatusValue(DraftStore.Draft d,string path)=>d.byPath.TryGetValue(path,out var f) ? (float)Number(d.Value(f)) : 0f;
    private static string N(float v)=>v.ToString("0.##",CultureInfo.InvariantCulture);
    private static int Ticks(float duration,float interval)=>interval<=0f ? 0 : Mathf.FloorToInt(duration/interval+.0001f);
    // 목록 요약(8px, 54px 칸). 옛 Summary()를 칸에 맞게 줄였다.
    private static string StatusSummary(DraftStore.Draft d,string key)
    {
        float V(string path)=>StatusValue(d,path);
        return key switch
        {
            "overview"=>"전체 비교표",
            "common"=>N(V(SharedDuration))+"초",
            "stun"=>N(V("stunDuration"))+"초·내성"+N(V("stunImmunityDuration"))+"초",
            "freeze"=>N(V("freezeDuration"))+"초",
            "bleed"=>N(V("bleedAtkRatio")*100f)+"%/"+N(V("bleedTickInterval"))+"초",
            "poison"=>N(V("poisonTickDamage"))+"/"+N(V("poisonTickInterval"))+"초",
            "bloodpop"=>N(V("bloodPopThreshold"))+"스택→"+N(V("bloodPopDamage")),
            "vulnerable"=>"+"+N(V("vulnerableDamageBonus")*100f)+"%",
            _=>"공격별",
        };
    }
    private static string StatusDescription(string key)=>key switch
    {
        "common"=>"출혈·중독·비폭·취약이 함께 쓰는 지속시간입니다. 수치는 모든 유닛이 함께 씁니다. 공격·스킬에 지속시간을 따로 적으면 그 값이 우선합니다.",
        "stun"=>"행동 완전 불가. 피해는 없습니다.",
        "freeze"=>"행동 불가. 맞으면 깨지면서 고정 피해를 받고 풀립니다.",
        "bleed"=>"건 공격자의 공격력에 비례하는 피해를 일정 간격으로 줍니다(물리 피해).",
        "poison"=>"고정 피해를 일정 간격으로 줍니다.",
        "bloodpop"=>"스택형. 정해진 스택에 닿으면 자신과 주변에 폭발합니다.",
        "vulnerable"=>"받는 물리 피해(출혈 포함)가 늘어납니다. 행동 제한은 없습니다.",
        "hitstun"=>"평타에 묻는 짧은 행동 불가. 공통 수치가 없습니다.",
        _=>"",
    };

    private void DrawStatus()
    {
        WorkshopUI.Label(content,"상태이상",12,33,200,20,15);
        if(!ProjectContentStore.Authoring || statusDraft==null)
        {   // 허브 타일이 막혀 있어 보통은 들어올 수 없다. 작업 공간을 바꾼 직후에만 여기에 온다.
            WorkshopUI.Label(content,"프로젝트 연결 필요 · 저장은 체크아웃에 연결한 기본 작업 공간에서만 됩니다.",12,62,612,22,12);
            return;
        }
        var d=statusDraft;
        string pin="Workshop.Pinned."+d.entry.Document; bool pinned=PlayerPrefs.GetInt(pin)!=0;
        Btn(pinned ? "고정됨" : "고정",566,33,66,18,()=>{PlayerPrefs.SetInt(pin,pinned ? 0 : 1);Draw(page);},pinned,"상태이상/고정");
        statusList=WorkshopUI.Rect(content,"상태이상 목록",ListX,BodyY,ListW,ListH);
        for(int i=0;i<StatusRows.Length;i++)
        {
            var r=StatusRows[i];
            var mine=r.fields.Concat(r.shared ? new[]{SharedDuration} : Array.Empty<string>())
                .Where(d.byPath.ContainsKey).Select(p=>d.byPath[p]).ToArray();
            ListRow(statusList,i,"상태이상/"+r.key,r.title,()=>StatusSummary(d,r.key),r.color,
                ()=>(mine.Any(d.Unsaved),mine.Any(d.Changed)),r.key==statusRow,()=>{statusRow=r.key;Draw(page);});
        }
        int index=Mathf.Max(0,Array.FindIndex(StatusRows,r=>r.key==statusRow));
        var row=StatusRows[index]; statusRow=row.key;
        if(row.key=="overview") DrawStatusOverview(d);
        else
        {
            var detail=Detail("status/"+row.key,"Status detail");
            int height=DrawStatusDetail(detail,d,row);
            EndDetail(detail,height);
            DrawStatusAside(d,row.key);
            HelpBox();
            ShowHelp("필드에 마우스를 올리면 설명이 보입니다.","");
            if(statusFocus.HasValue && detail.Find(statusFocus.Value.path) is RectTransform target)
            {   // 그 줄이 상세 맨 위에 오게 스크롤한다(정수 y)
                var (path,text)=statusFocus.Value;
                detailScrollY=Mathf.Clamp(Mathf.Round(-target.anchoredPosition.y),0,Mathf.Max(0,detail.rect.height-DetailH));
                detail.anchoredPosition=new Vector2(0,detailScrollY);
                ShowHelp(text ?? HelpText(d.byPath[path]),path);
            }
        }
        statusFocus=null;
        Btn("변경 취소",8,ActionsY,72,22,()=>{DraftStore.Revert(d);Draw(page);});
        Btn("원본값으로",84,ActionsY,76,22,()=>{DraftStore.ResetFields(d,row.fields.Where(d.byPath.ContainsKey).Select(p=>d.byPath[p]));Draw(page);},false,"상태이상/원본값으로")
            .interactable=row.fields.Length>0;
        Btn("모든 필드",164,ActionsY,72,22,()=>{OpenParameters();SelectContent(d.entry);});
        if(d.Blocked!=null)
        {
            Btn("판 차이 정리",240,ActionsY,96,22,()=>{DraftStore.DropUnknown(d);Draw(page);},false,"이 판에 없는 항목 지우기");
            WorkshopUI.Label(content,"판 차이 "+d.Unknown.Length+"개",340,ActionsY,76,22,8);
        }
        Btn("시험 F6",420,ActionsY,80,22,StartContentTrial);
        Btn("프로젝트에 저장",504,ActionsY,128,22,SaveStatus,true).interactable=d.Blocked==null;
    }
    private void FocusStatus(string path,string text)
    {
        statusRow=StatusRows.FirstOrDefault(r=>r.fields.Contains(path)).key ?? statusRow;
        statusFocus=(path,text); Draw(page);
    }
    // 허브 찾기 결과: 상태이상 화면을 열고 그 필드 줄로 간다.
    private void FocusStatusField(string path)
    {
        statusFocus=(path,null);
        OpenStatus(StatusRows.FirstOrDefault(r=>r.fields.Contains(path)).key);
    }

    // 상세 열: 설명 → 섹션별 필드 → 미리보기 → 안내. 다음 y(= 내용 높이)를 돌려준다.
    private int DrawStatusDetail(RectTransform detail,DraftStore.Draft d,(string key,string title,Color color,string[] fields,bool shared) row)
    {
        float V(string path)=>StatusValue(d,path);
        var color=row.color;
        Color Faded(float alpha)=>new(color.r,color.g,color.b,alpha);
        int y=Note(detail,2,StatusDescription(row.key))+2;
        void Fields(string title,params string[] paths){ Section(detail,ref y,title); foreach(var p in paths) y=FieldRow(detail,y,d,p); }
        void Shared(){ Section(detail,ref y,"지속시간 · 공통"); y=FieldRow(detail,y,d,SharedDuration,SharedNote); }
        void Guide(string text){ Section(detail,ref y,"안내"); y=Note(detail,y,text); }
        switch(row.key)
        {
            case "common":
                Fields("지속시간",SharedDuration);
                Section(detail,ref y,"이 값을 쓰는 상태이상");
                foreach(var (target,i) in new[]{"bleed","poison","bloodpop","vulnerable"}.Select((k,i)=>(k,i)))
                {
                    var r=Array.Find(StatusRows,s=>s.key==target);
                    WorkshopUI.Button(detail,r.title,2+i*74,y,70,20,()=>Safe(()=>{statusRow=target;Draw(page);}),false,"공통/"+target);
                }
                y+=24;
                Guide("기절·빙결은 각자 따로 지속시간을 씁니다.");
                break;
            case "stun":
                Fields("지속시간","stunDuration","stunMinDuration","stunMaxDuration");
                Fields("내성","stunImmunityDuration");
                Section(detail,ref y,"미리보기");
                y=Timeline(detail,y,()=>(new[]{("기절",V("stunDuration"),Faded(.75f)),("내성",V("stunImmunityDuration"),Pause)},0f,0f,color,
                    "기본 기절 → 자연 종료 → 내성(이 동안 다시 기절하지 않음)"));
                Guide("보스 그로기는 최소·최대를 무시하고, 슈퍼아머 중에는 걸리지 않습니다.");
                break;
            case "freeze":
                Fields("지속시간","freezeDuration");
                Fields("깨질 때","freezeBreakDamage","freezeImmunityDuration");
                Section(detail,ref y,"미리보기");
                y=Timeline(detail,y,()=>(new[]{("빙결",V("freezeDuration"),Faded(.75f))},0f,0f,color,"맞지 않으면 끝까지 얼어 있다가 자연 종료(내성 없음)"));
                y=Timeline(detail,y,()=>(new[]{("빙결",V("freezeDuration")*.4f,Faded(.75f)),("내성",V("freezeImmunityDuration"),Pause)},
                    V("freezeDuration")*.4f,V("freezeDuration")*.4f,new Color(1f,.4f,.4f),"중간에 맞으면 ▼ 깨짐 피해 "+N(V("freezeBreakDamage"))+" + 즉시 해제 → 내성"));
                Guide("무기 전용 빙결 추가 피해는 별개이고, 슈퍼아머 중에는 걸리지 않습니다.");
                break;
            case "bleed":
                Fields("피해","bleedAtkRatio","bleedTickInterval","bleedFallbackTickDamage");
                Shared();
                Section(detail,ref y,"미리보기");
                y=Timeline(detail,y,()=>{
                    float duration=V(SharedDuration), interval=V("bleedTickInterval"), tick=SampleAtk*V("bleedAtkRatio");
                    return (new[]{("출혈",duration,Faded(.35f))},interval,duration,color,"▼ = 피해 "+N(tick)+" (총 "+Ticks(duration,interval)+"회)");
                });
                Guide("걸리는 순간의 공격력을 기억합니다. 이후 공격력이 바뀌어도 그 출혈의 피해는 그대로입니다.");
                break;
            case "poison":
                Fields("피해","poisonTickDamage","poisonTickInterval");
                Shared();
                Section(detail,ref y,"미리보기");
                y=Timeline(detail,y,()=>{
                    float duration=V(SharedDuration), interval=V("poisonTickInterval");
                    return (new[]{("중독",duration,Faded(.35f))},interval,duration,color,"▼ = 피해 "+N(V("poisonTickDamage"))+" (총 "+Ticks(duration,interval)+"회)");
                });
                break;
            case "bloodpop":
                Fields("스택","bloodPopThreshold");
                y=Stacks(detail,y,()=>Mathf.Clamp(Mathf.RoundToInt(V("bloodPopThreshold")),1,40),color);
                Fields("폭발","bloodPopDamage","bloodPopRadius","bloodPopFuse","bloodPopHitDuration");
                Shared();
                Section(detail,ref y,"미리보기");
                y=Timeline(detail,y,()=>(new[]{("예고",V("bloodPopFuse"),new Color(1f,.85f,.3f,.6f)),("판정",V("bloodPopHitDuration"),new Color(1f,.3f,.3f,.8f))},0f,0f,color,
                    N(V("bloodPopThreshold"))+"스택 도달 → 예고 → 폭발 판정(반지름 "+N(V("bloodPopRadius"))+")"));
                break;
            case "vulnerable":
                Fields("효과","vulnerableDamageBonus");
                Shared();
                Guide("물리 피해(출혈 포함)에만 적용됩니다. 중독·비폭·빙결 깨짐에는 적용되지 않습니다. 용사 계열 전용입니다.");
                break;
            case "hitstun":
                y=Note(detail,y,"경직은 공통 수치가 없습니다. 평타·공격마다 지속시간을 따로 넘기므로, 그 공격·무기·소환수 설정에서 조절하세요.");
                y=Note(detail,y,"기절과 따로 둔 이유: '기절 시간 증가' 같은 효과가 경직에는 붙지 않게 하기 위함입니다. 걸릴 때 화면에 글자를 띄우지 않습니다(평타마다 묻기 때문).");
                break;
        }
        return y+4;
    }

    // 시간 막대(298×40): 설명 한 줄(8) + 구간 막대(12, 폭 > 40px이면 안에 "출혈 5초") + 초 눈금(8). 틱(▼)은 최대 300개.
    // 값이 바뀌면 refresher가 자기 자식만 다시 만든다(국소 재구성).
    private int Timeline(RectTransform parent,int y,Func<((string label,float length,Color color)[] spans,float tick,float until,Color tickColor,string note)> plan)
    {
        const int W=DetailW-8;
        var box=WorkshopUI.Rect(parent,"Timeline",2,y,W,40);
        void Build()
        {
            foreach(Transform child in box){ child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var (spans,tick,until,tickColor,note)=plan();
            float total=Mathf.Max(.01f,spans.Sum(s=>Mathf.Max(0f,s.length)));
            int X(float t)=>Mathf.RoundToInt(W*Mathf.Clamp01(t/total));
            WorkshopUI.Label(box,note,0,0,W,12,8);
            WorkshopUI.Box(box,"Track",0,13,W,12,new Color(0,0,0,.25f)).raycastTarget=false;
            float t0=0f;
            foreach(var (label,length,color) in spans)
            {
                if(length<=0f) continue;
                int a=X(t0), b=X(t0+length);
                var part=WorkshopUI.Box(box,label,a,13,Mathf.Max(1,b-a),12,color); part.raycastTarget=false;
                if(b-a>40) WorkshopUI.Label(part.transform,label+" "+N(length)+"초",2,0,b-a-4,12,8).overflowMode=TextOverflowModes.Truncate;
                t0+=length;
            }
            int drawn=Mathf.Min(Ticks(until,tick),300);
            for(int i=1;i<=drawn;i++) WorkshopUI.Box(box,"Tick",Mathf.Clamp(X(i*tick)-1,0,W-2),9,2,16,tickColor).raycastTarget=false;
            int step=total>20f ? 5 : total>8f ? 2 : 1;
            for(int s=0;s<=Mathf.FloorToInt(total);s+=step)
            {
                int x=X(s);
                WorkshopUI.Box(box,"Mark",Mathf.Min(x,W-1),25,1,3,new Color(1,1,1,.35f)).raycastTarget=false;
                WorkshopUI.Label(box,s+"초",Mathf.Clamp(x-6,0,W-20),29,24,11,8).overflowMode=TextOverflowModes.Overflow;
            }
        }
        Build(); refreshers.Add(Build);
        return y+42;
    }
    // 비폭 스택 칸(최대 40칸). 칸 폭은 정수(258/스택-1)라 픽셀 격자에서 고르다. 마지막 칸은 폭발 색이다.
    private int Stacks(RectTransform parent,int y,Func<int> threshold,Color color)
    {
        var box=WorkshopUI.Rect(parent,"Stacks",2,y,DetailW-8,16);
        void Build()
        {
            foreach(Transform child in box){ child.gameObject.SetActive(false); Destroy(child.gameObject); }
            int count=threshold(), w=Mathf.Max(1,258/count-1);
            for(int i=0;i<count;i++)
                WorkshopUI.Box(box,"Stack",i*(w+1),2,w,12,i==count-1 ? new Color(1f,.3f,.3f) : new Color(color.r,color.g,color.b,.7f)).raycastTarget=false;
            WorkshopUI.Label(box,"→ 폭발",count*(w+1)+4,2,40,12,8).overflowMode=TextOverflowModes.Overflow;
        }
        Build(); refreshers.Add(Build);
        return y+18;
    }

    // 미리보기 열(158×148): (출혈) 예시 공격력 y4 · 결과 카드 2×2(75×30) y26 · 확인할 것 y94 · 경고 최대 3줄 y109.
    private void DrawStatusAside(DraftStore.Draft d,string key)
    {
        float V(string path)=>StatusValue(d,path);
        var aside=Aside();
        if(key=="bleed") SampleAtkStepper(aside,4,4,56);
        (string title,Func<string> value)[] cards=key switch
        {
            "stun"=>new (string,Func<string>)[]{("공격 지정 기절",()=>N(V("stunMinDuration"))+"~"+N(Mathf.Max(V("stunMinDuration"),V("stunMaxDuration")))+"초"),
                ("재기절까지",()=>N(V("stunDuration")+V("stunImmunityDuration"))+"초")},
            "freeze"=>new (string,Func<string>)[]{("깨짐 피해",()=>N(V("freezeBreakDamage"))),("내성",()=>N(V("freezeImmunityDuration"))+"초")},
            "bleed"=>new (string,Func<string>)[]{("1회 피해",()=>N(BleedTick())),("초당 피해",()=>N(BleedTick()/Mathf.Max(.01f,V("bleedTickInterval")))),
                ("총 피해",()=>N(BleedTick()*Ticks(V(SharedDuration),V("bleedTickInterval")))),
                ("취약 시 총",()=>N(BleedTick()*Ticks(V(SharedDuration),V("bleedTickInterval"))*(1f+V("vulnerableDamageBonus"))))},
            "poison"=>new (string,Func<string>)[]{("1회 피해",()=>N(V("poisonTickDamage"))),("초당 피해",()=>N(V("poisonTickDamage")/Mathf.Max(.01f,V("poisonTickInterval")))),
                ("총 피해",()=>N(V("poisonTickDamage")*Ticks(V(SharedDuration),V("poisonTickInterval"))))},
            "bloodpop"=>new (string,Func<string>)[]{("폭발 피해",()=>N(V("bloodPopDamage"))),("범위 지름",()=>N(V("bloodPopRadius")*2f)+" 유닛")},
            "vulnerable"=>new (string,Func<string>)[]{("물리 100→",()=>N(100f*(1f+V("vulnerableDamageBonus")))),("물리 250→",()=>N(250f*(1f+V("vulnerableDamageBonus"))))},
            _=>Array.Empty<(string,Func<string>)>(),
        };
        float BleedTick()=>SampleAtk*V("bleedAtkRatio");
        for(int i=0;i<cards.Length;i++)
        {
            var (title,value)=cards[i];
            var card=WorkshopUI.Frame(aside,"결과/"+title,4+i%2*79,26+i/2*34,75,30).rectTransform;
            WorkshopUI.Label(card,title,3,2,71,12,8);
            var number=WorkshopUI.Label(card,"",3,13,71,16,12);
            void Show()=>number.text=value();
            Show(); refreshers.Add(Show);
        }
        WorkshopUI.Label(aside,"확인할 것",4,94,150,14);
        var warnings=WorkshopUI.Rect(aside,"경고 목록",4,109,150,36);
        void Warn()
        {
            foreach(Transform child in warnings){ child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var list=StatusWarnings(d).ToArray();
            if(list.Length==0){ WorkshopUI.Label(warnings,"없음",0,0,150,12,8); return; }
            int shown=list.Length>3 ? 2 : list.Length;
            for(int i=0;i<shown;i++)
            {
                var (field,title,text)=list[i];
                var button=WorkshopUI.Button(warnings,title,0,i*12,150,12,()=>Safe(()=>FocusStatus(field,text)),false,"경고/"+field);
                var caption=button.GetComponentInChildren<TMP_Text>(); PixelText.Configure(caption,8); caption.alignment=TextAlignmentOptions.MidlineLeft;
            }
            if(list.Length>3) WorkshopUI.Label(warnings,"외 "+(list.Length-2)+"개",0,24,150,12,8);
        }
        Warn(); refreshers.Add(Warn);
    }
    // 예시 공격력 스테퍼(라벨 + [−][값][+]). 저장하지 않으므로 Safe·DraftStore를 거치지 않고 카드만 다시 계산한다.
    private void SampleAtkStepper(RectTransform parent,int x,int y,int labelWidth,string caption="예시 공격력")
    {
        WorkshopUI.Label(parent,caption,x,y,labelWidth,20,8);
        refreshers.Add(WorkshopUI.Stepper(parent,"예시 공격력",x+labelWidth+2,y,92,()=>SampleAtk,v=>{SampleAtk=(float)v;Refresh();},1,0,100000));
        foreach(var s in parent.Find("예시 공격력").GetComponentsInChildren<Selectable>())
            WorkshopUI.Hover(s,()=>ShowHelp("결과 카드 계산에만 씁니다. 게임에 영향 없음","예시 공격력"));
    }

    // 한눈에 보기: 상세·미리보기 열을 합친 영역(168~632)에 비교표, 예시 공격력, 도움말. 행을 누르면 그 상태이상으로 간다.
    private static readonly (string title,int width)[] OverviewColumns={("상태이상",54),("지속",46),("간격",38),("1회 피해",68),("초당",42),("총 피해",62),("특이사항",140)};
    private void DrawStatusOverview(DraftStore.Draft d)
    {
        float V(string path)=>StatusValue(d,path);
        const int X=DetailX, Y=BodyY;
        int[] at=new int[OverviewColumns.Length];
        for(int i=1;i<at.Length;i++) at[i]=at[i-1]+OverviewColumns[i-1].width+2;
        WorkshopUI.Box(content,"Overview head",X,Y,462,16,new Color(0,0,0,.3f)).raycastTarget=false;
        for(int i=0;i<at.Length;i++) WorkshopUI.Label(content,OverviewColumns[i].title,X+at[i]+4,Y,OverviewColumns[i].width-4,16,8);
        string Common()=>N(V(SharedDuration))+"초(공통)";
        var rows=new (string key,Func<string[]> cells)[]
        {
            ("stun",()=>new[]{N(V("stunDuration"))+"초","-","-","-","-","시간 지정 "+N(V("stunMinDuration"))+"~"+N(V("stunMaxDuration"))+"초 · 내성 "+N(V("stunImmunityDuration"))+"초"}),
            ("freeze",()=>new[]{N(V("freezeDuration"))+"초","-","깨질 때 "+N(V("freezeBreakDamage")),"-",N(V("freezeBreakDamage")),"맞으면 깨짐 · 내성 "+N(V("freezeImmunityDuration"))+"초"}),
            ("bleed",()=>{
                float tick=SampleAtk*V("bleedAtkRatio"), interval=Mathf.Max(.01f,V("bleedTickInterval")); int n=Ticks(V(SharedDuration),interval);
                return new[]{Common(),N(interval)+"초",N(V("bleedAtkRatio")*100f)+"% = "+N(tick),N(tick/interval),N(tick*n)+" ("+n+"회)","공격력 "+N(SampleAtk)+" 기준 · 물리"}; }),
            ("poison",()=>{
                float tick=V("poisonTickDamage"), interval=Mathf.Max(.01f,V("poisonTickInterval")); int n=Ticks(V(SharedDuration),interval);
                return new[]{Common(),N(interval)+"초",N(tick),N(tick/interval),N(tick*n)+" ("+n+"회)","고정 피해"}; }),
            ("bloodpop",()=>new[]{Common(),"-","폭발 "+N(V("bloodPopDamage")),"-","-",N(V("bloodPopThreshold"))+"스택 · 반지름 "+N(V("bloodPopRadius"))+" · 예고 "+N(V("bloodPopFuse"))+"초"}),
            ("vulnerable",()=>new[]{Common(),"-","-","-","-","받는 물리 피해 +"+N(V("vulnerableDamageBonus")*100f)+"%"}),
            ("hitstun",()=>new[]{"공격별","-","-","-","-","공격마다 따로 정합니다"}),
        };
        for(int r=0;r<rows.Length;r++)
        {
            var (key,cells)=rows[r];
            var info=Array.Find(StatusRows,s=>s.key==key);
            var button=WorkshopUI.Button(content,"",X,Y+16+r*22+1,462,20,()=>Safe(()=>{statusRow=key;Draw(page);}),false,"표/"+key);
            WorkshopUI.NoNavigation(button);
            WorkshopUI.Box(button.transform,"Stripe",2,4,3,12,info.color).raycastTarget=false;
            WorkshopUI.Label(button.transform,info.title,at[0]+8,0,OverviewColumns[0].width-8,20,8);
            var labels=new TMP_Text[at.Length-1];
            for(int c=1;c<at.Length;c++) labels[c-1]=WorkshopUI.Label(button.transform,"",at[c]+4,0,OverviewColumns[c].width-4,20,8);
            void Show(){ var text=cells(); for(int c=0;c<labels.Length;c++) labels[c].text=text[c]; }
            Show(); refreshers.Add(Show);
        }
        SampleAtkStepper(content,X,232,120,"예시 공격력 · 게임에 영향 없음");
        var help=WorkshopUI.Label(content,
            "수치는 모든 유닛이 함께 씁니다. 공격·스킬에 지속시간을 따로 적으면 그 값이 우선합니다.\n"+
            "'(공통)' 항목은 공통 지속시간 하나를 같이 씁니다. 총 피해 = 1회 피해 × 지속시간 동안 들어가는 횟수(다시 걸리면 갱신).\n"+
            "행을 누르면 그 상태이상을 고칩니다.",X,256,464,40,8);
        help.textWrappingMode=TextWrappingModes.Normal; help.overflowMode=TextOverflowModes.Truncate; help.alignment=TextAlignmentOptions.TopLeft;
    }
}
