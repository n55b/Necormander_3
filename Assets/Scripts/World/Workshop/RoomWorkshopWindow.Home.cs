using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// 제작실 첫 화면: 작업 종류 카드, 최근 작업, 찾기(Ctrl+F). 영역 소속과 헤더 탭은 등록표(Pages.cs)의 Area·Tab 칸이 정한다.
// 카드 id는 영역 이름이다(검사가 누르는 고정 이름). 게임 콘텐츠·공유 카드는 그 영역의 탭과 이름이 같지만 한 화면에 함께 나오지 않는다.
public sealed partial class RoomWorkshopWindow
{
    private enum Area { Level, Content, Text, Art, Share }
    private static readonly (string caption,string summary)[] areas=
    {
        ("레벨","방 목록 · 방 편집 · 층 · 튜토리얼 · 마을"),
        ("게임 콘텐츠","플레이어 · 소환수 · 몬스터 · 아이템 · 장비 · 상태이상"),
        ("글","대사 편집 · UI·보상 번역"),
        ("아트","ARTIST 원본 · 기준 팔레트"),
        ("공유","방 파일 · 제작 묶음 · 작업 공간 · Git 충돌"),
    };
    private bool areaMenuOpen;
    private string findQuery="";
    private TMP_InputField findInput;

    // 글·아트는 DATA·ARTIST에 바로 저장하므로 체크아웃에 연결한 기본 작업 공간에서만 연다. 게임 콘텐츠 허브는 비연결 안내를 스스로 띄운다.
    private static bool AreaOpen(Area area)=>area is not (Area.Text or Area.Art) || ProjectContentStore.Authoring;
    private Page FirstPage(Area area)=>(Page)Array.FindIndex(pages,p=>p.Area==area && p.Tab.caption!=null);
    private void OpenArea(Area area)
    {
        if(!AreaOpen(area)) throw new InvalidOperationException("프로젝트에 연결한 기본 작업 공간에서 편집합니다.");
        Navigate(FirstPage(area));
    }

    // 헤더: 첫 화면은 저장 위치·프로젝트 연결, 영역 화면은 [처음 화면]·영역 전환·그 영역의 탭.
    private void DrawHeader()
    {
        var area=pages[(int)page].Area;
        if(area==null)
        {
            Btn(ProjectContentStore.Authoring ? "DATA 제작실" : "로컬 제작실",7,5,86,20,()=>
                Say(ProjectContentStore.Authoring ? ProjectContentStore.Root : WorkshopWorkspace.Current));
#if LEVEL_WORKSHOP && !UNITY_EDITOR
            Btn("프로젝트 연결",99,5,95,20,OpenConnection);
#endif
            return;
        }
        Btn("처음 화면",7,5,58,20,()=>Navigate(Page.Home));
        Btn(areas[(int)area.Value].caption+" ▼",68,5,76,20,()=>{areaMenuOpen=!areaMenuOpen;Draw(page);},areaMenuOpen,"영역");
        int x=148;
        // 탭이 없는 화면(상태이상·모든 수치·방 편집·Git 등)은 그 영역의 첫 탭을 켜 둔다. 옛 헤더가 콘텐츠 하위 화면에서 '게임 콘텐츠'를 켠 것과 같다.
        var lit=pages[(int)page].Tab.caption!=null ? page : FirstPage(area.Value);
        for(int i=0;i<pages.Length;i++)
        {
            var tab=pages[i].Tab;
            if(pages[i].Area!=area || tab.caption==null) continue;
            var target=(Page)i;
            Btn(tab.caption,x,5,tab.width,20,()=>Navigate(target),lit==target,tab.id);
            x+=tab.width+3;
        }
    }
    // 영역 전환 목록. 그리는 순서는 미저장 목록과 같은 자리(페이지 위)다.
    private void DrawAreaMenu()
    {
        WorkshopUI.Blocker(content,Color.clear,()=>{areaMenuOpen=false;Draw(page);});
        var box=WorkshopUI.Frame(content,"Area menu",68,28,140,8+areas.Length*22).rectTransform;
        for(int i=0;i<areas.Length;i++)
        {
            var area=(Area)i;
            var item=WorkshopUI.Button(box,areas[i].caption,4,4+i*22,132,20,()=>Safe(()=>{areaMenuOpen=false;OpenArea(area);}),
                pages[(int)page].Area==area,"영역/"+areas[i].caption);
            item.interactable=AreaOpen(area);
        }
    }

    private void DrawHome()
    {
        bool authoring=ProjectContentStore.Authoring;
        WorkshopUI.Label(content,"무엇을 만들까요?",12,36,300,22,15);
        WorkshopUI.Label(content,"찾기",360,37,30,20);
        findInput=Input("콘텐츠 찾기",findQuery,392,37,180,20);
        findInput.onEndEdit.AddListener(value=>{ value=value.Trim(); if(value==findQuery) return; findQuery=value; Draw(page); });
        WorkshopUI.Label(content,"Ctrl+F",578,37,50,20,8);
        if(findQuery.Length>0){ DrawFindResults(); return; }
        var recent=RecentLog().ToArray();
        UnityEngine.UI.Button first=null;
        for(int i=0;i<areas.Length;i++)
        {
            var area=(Area)i; int x=8+i*126;
            WorkshopUI.Frame(content,"Card",x,62,120,184);
            var card=Btn(areas[i].caption,x+4,66,112,76,()=>OpenArea(area),false,areas[i].caption);
            card.interactable=AreaOpen(area);
            first??=card;
            var title=card.GetComponentInChildren<TMP_Text>();
            title.rectTransform.anchoredPosition=new Vector2(6,-4); title.rectTransform.sizeDelta=new Vector2(100,22);
            title.alignment=TextAlignmentOptions.MidlineLeft; PixelText.Configure(title,15);
            var summary=WorkshopUI.Label(card.transform,AreaOpen(area) ? areas[i].summary : "프로젝트 연결 필요",6,28,100,44,8);
            summary.textWrappingMode=TextWrappingModes.Normal; summary.alignment=TextAlignmentOptions.TopLeft;
            int unsaved=UnsavedInArea(area);
            if(unsaved>0){ var mark=WorkshopUI.Label(card.transform,"● "+unsaved,70,4,38,14,8); mark.alignment=TextAlignmentOptions.TopRight; }
            WorkshopUI.Label(content,"최근",x+6,148,100,18,8).color=WorkshopUI.Mint;
            var items=recent.Where(r=>r.area==area).Take(2).ToArray();
            for(int k=0;k<items.Length;k++)
            {
                var item=items[k];
                var open=Btn(item.title,x+4,168+k*24,112,22,()=>OpenRecent(item.area,item.key),false,areas[i].caption+"/최근/"+item.key);
                PixelText.Configure(open.GetComponentInChildren<TMP_Text>(),8);
                open.interactable=area==Area.Level || authoring;
            }
            if(items.Length==0)
                WorkshopUI.Label(content,area==Area.Share ? "작업 공간 · "+WorkspaceTitle() : "아직 연 항목이 없습니다.",x+6,170,108,18,8);
        }
        WorkshopUI.Label(content,"최근 작업",12,256,60,24);
        int n=0;
        foreach(var item in recent.Take(4))
        {
            var open=Btn(item.title,76+n++*139,256,135,24,()=>OpenRecent(item.area,item.key),false,"최근 작업/"+item.key);
            PixelText.Configure(open.GetComponentInChildren<TMP_Text>(),8);
            open.interactable=item.area==Area.Level || authoring;
        }
        if(n==0) WorkshopUI.Label(content,"카드를 골라 작업을 시작하면 여기에 이어서 할 항목이 나타납니다.",76,256,548,24,8);
        if(!authoring)
            WorkshopUI.Label(content,"게임 콘텐츠·글·아트는 프로젝트 체크아웃에 연결한 기본 작업 공간에서 저장합니다.",12,288,612,22,8);
        // 키보드 시작점은 레벨 카드. 찾기 칸의 onEndEdit(선택 해제 중)에서 다시 그리면 중첩 선택이 막히므로 건너뛴다.
        var events=UnityEngine.EventSystems.EventSystem.current;
        if(events!=null && !events.alreadySelecting) first.Select();
    }
    private static string WorkspaceTitle()=>WorkshopWorkspace.ActiveId=="" ? "기본" : WorkshopWorkspace.List().FirstOrDefault(e=>e.id==WorkshopWorkspace.ActiveId)?.title ?? "기본";
    // 영역의 미저장 문서 수: Title 페이지(대사·번역·아트·플레이어 수치) + 게임 콘텐츠의 DraftStore 초안.
    private int UnsavedInArea(Area area)=>pages.Count(p=>p.Area==area && p.Title!=null && p.Dirty())+(area==Area.Content ? DraftStore.Unsaved.Count() : 0);

    // 최근 작업: 한 줄 = 영역 번호 \t 키 \t 제목. 앞이 최근이고 같은 항목은 한 줄만 둔다. 개인 화면 기록이라 PlayerPrefs에 둔다.
    private const string RecentKey="Workshop.Recent";
    private static void RememberRecent(Area area,string key,string title)
    {
        string head=(int)area+"\t"+key+"\t";
        var rest=PlayerPrefs.GetString(RecentKey,"").Split('\n').Where(l=>l.Length>0 && !l.StartsWith(head,StringComparison.Ordinal));
        PlayerPrefs.SetString(RecentKey,string.Join("\n",rest.Prepend(head+title.Replace('\t',' ').Replace('\n',' ')).Take(16)));
    }
    private static IEnumerable<(Area area,string key,string title)> RecentLog()=>PlayerPrefs.GetString(RecentKey,"").Split('\n')
        .Select(l=>l.Split('\t')).Where(p=>p.Length==3 && int.TryParse(p[0],out int a) && Enum.IsDefined(typeof(Area),a))
        .Select(p=>((Area)int.Parse(p[0]),p[1],p[2]));
    private void OpenRecent(Area area,string key)
    {
        switch(area)
        {
            case Area.Level:
                var row=RoomRows(false).FirstOrDefault(r=>r.key==key);
                if(row.run!=null){ row.run(); return; }
                var template=ProjectContentStore.Authoring ? catalog.rooms.FirstOrDefault(r=>r.id==key) : null;   // 게임 원본 편집으로 연 방
                if(template==null) throw new InvalidOperationException("방을 찾을 수 없습니다.");
                Begin(ProjectRooms.Template(template),false); return;
            case Area.Content: OpenContent(key); return;
            case Area.Text:
                if(conversation!=null && dialogueDirty) throw new InvalidOperationException("대사를 저장하거나 변경 취소를 눌러 주세요.");
                Navigate(Page.Dialogue); OpenDialogue(key); return;
            case Area.Art:
                if(art!=null && nextArt!=null) throw new InvalidOperationException("아트 원본을 저장하거나 비교를 취소해 주세요.");
                artEntries??=WorkshopArtSource.Entries();
                var entry=Array.Find(artEntries,e=>e.source==key) ?? throw new InvalidOperationException("아트 원본을 찾을 수 없습니다.");
                Navigate(Page.Art); OpenArt(entry); return;
        }
    }
    private void OpenFind()
    {
        Navigate(Page.Home);
        if(findInput!=null){ findInput.Select(); findInput.ActivateInputField(); }
    }

    // 찾기: 영역·방 이름은 언제나, 게임 콘텐츠(화면·문서·상태이상 필드)는 연결한 기본 작업 공간에서만.
    private void DrawFindResults()
    {
        string q=findQuery;
        bool Has(string text)=>text!=null && text.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0;
        var hits=new List<(string key,string caption,Action open)>();
        for(int i=0;i<areas.Length;i++)
        {
            var area=(Area)i;
            if(AreaOpen(area) && (Has(areas[i].caption) || Has(areas[i].summary))) hits.Add(("영역/"+areas[i].caption,areas[i].caption+"   영역",()=>OpenArea(area)));
        }
        foreach(var row in RoomRows(false).Where(r=>Has(r.label))) hits.Add(("방/"+row.key,row.label+"   방",row.run));
        if(ProjectContentStore.Authoring)
        {
            foreach(var tile in contentTiles.Where(t=>t.id!=PlaytestTile && (Has(t.caption) || Has(t.id))))
                hits.Add((tile.id,tile.caption+"   화면",()=>OpenTile(tile)));
            foreach(var e in Documents().Where(e=>Has(e.Title) || Has(e.source.name) || Has(e.group) || Has(ContentTitle(e))))
                hits.Add((e.Document,ContentTitle(e)+"   "+e.group,()=>OpenContent(e.Document)));
            // 소유 화면의 필드(상태이상): 그 필드가 든 행을 열고 그 줄로 스크롤한다. 모든 SO의 필드 찾기는 P2에서 색인과 함께 한다.
            var balance=Documents().FirstOrDefault(e=>e.source is StatusBalanceSO);
            if(balance!=null)
                foreach(var f in ProjectParameters.Fields(balance.source,labels:true).Where(f=>Has(f.label) || Has(f.path)))
                    hits.Add(("상태이상/"+f.path,f.label+"   상태이상",()=>FocusStatusField(f.path)));
        }
        if(hits.Count==0) WorkshopUI.Label(content,"일치하는 항목이 없습니다.",12,66,612,22,12);
        for(int i=0;i<Math.Min(10,hits.Count);i++)
        {
            var hit=hits[i];
            Btn(hit.caption,12,62+i*23,612,21,hit.open,false,"찾기/"+hit.key).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
        }
        if(hits.Count>10) WorkshopUI.Label(content,"외 "+(hits.Count-10)+"개 · 검색어를 더 입력하세요.",12,295,400,18,8);
    }
}
