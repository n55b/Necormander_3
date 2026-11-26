using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Entry = ProjectParameterBindings.Entry;

// 게임 콘텐츠 허브: 도메인 타일, 찾기, 최근·고정 칩. 타일 id는 검사가 누르는 고정 이름이다(옛 작업대 버튼 이름 포함).
public sealed partial class RoomWorkshopWindow
{
    // group은 걸러 낼 DATA 영역 접두. 소유 화면이 생기기 전까지 그 영역만 걸러 낸 모든 수치를 연다.
    private static readonly (string caption,string id,string group,Page open)[] contentTiles=
    {
        ("플레이어","플레이어 수치",null,Page.Balance),("소환수","소환수","BALANCE/MINION",Page.Parameters),
        ("몬스터","몬스터","BALANCE/ENEMY",Page.Parameters),("아이템","아이템","BALANCE/ITEMS",Page.Parameters),
        ("장비","장비","BALANCE/EQUIPMENT",Page.Parameters),("상태이상","상태이상","BALANCE/STATUS",Page.Status),
        ("화면·연출","화면·연출","PRESENTATION/",Page.Parameters),("사운드","사운드","AUDIO/",Page.Parameters),
        ("대사","대사 편집",null,Page.Dialogue),("번역","UI·보상 번역",null,Page.Translations),("아트","아트 원본",null,Page.Art),
        ("플레이테스트","플레이테스트",null,Page.Content),("모든 수치","콘텐츠 수치",null,Page.Parameters),
    };
    private const string PlaytestTile="플레이테스트";
    private string contentQuery="";
    private TMP_InputField contentFind;

    // 캐시가 있으면 그대로, 없으면 만들고 바인딩한다. OpenParameters는 dataCatalog=null 뒤 불러 새 변형을 잡는다.
    private Entry[] EnsureCatalog()
    {
        if(dataCatalog!=null) return dataCatalog;
#if UNITY_EDITOR
        var sources=ProjectParameterBindings.All();
#else
        var sources=ProjectParameterBindings.CreatorCatalog ?? Array.Empty<Entry>();
#endif
        ProjectParameterBindings.Bind(sources);
        return dataCatalog=sources.Concat(ProjectContentVariants.Entries).ToArray();
    }
    // 칩·미저장 목록 제목. 싱글턴 화면의 에셋 파일 이름("StatusBalance")이 한글 화면에 뜨지 않게 타일 이름을 쓴다.
    private static string ContentTitle(Entry e)=>e.source is StatusBalanceSO ? "상태이상" : e.Title;
    private static string TileOf(Entry e)=>contentTiles.FirstOrDefault(t=>t.group!=null && e.group.StartsWith(t.group,StringComparison.Ordinal)).id ?? "콘텐츠 수치";
    private void OpenTile((string caption,string id,string group,Page open) tile)
    {
        if(tile.open==Page.Status){ OpenStatus(null); return; }
        if(tile.open!=Page.Parameters){ Navigate(tile.open); return; }
        // 모든 수치는 이전 항목을 그대로 열지만, 영역 타일이 남긴 영역 필터는 지운다(그대로면 '모든 수치'가 한 영역만 보인다).
        if(tile.group==null && contentTiles.Any(t=>t.group!=null && t.group==dataSearch)){ dataSearch=""; dataPage=0; }
        OpenParameters(tile.group);
    }
    // 소유 화면으로 연다: 상태이상 원본은 상태이상 화면, 그 밖은 모든 수치.
    private void OpenContent(string document)
    {
        if(EnsureCatalog().Any(e=>e.Document==document && e.source is StatusBalanceSO)){ OpenStatus(null); return; }
        OpenParameters();
        var entry=Array.Find(dataCatalog,e=>e.Document==document);
        if(entry!=null) SelectContent(entry);
    }
    private void OpenFind()
    {
        Navigate(Page.Content);
        if(contentFind!=null){ contentFind.Select(); contentFind.ActivateInputField(); }
    }
    private IEnumerable<Entry> Documents()=>EnsureCatalog().GroupBy(e=>e.Document).Select(g=>g.First());
    private IEnumerable<Entry> RecentContent()=>Documents()
        .Select(e=>(e,at:PlayerPrefs.GetString("Workshop.Opened."+e.Document,""))).Where(x=>x.at.Length>0)
        .OrderByDescending(x=>x.at,StringComparer.Ordinal).Select(x=>x.e).Take(3);
    private IEnumerable<Entry> PinnedContent()=>Documents().Where(e=>PlayerPrefs.GetInt("Workshop.Pinned."+e.Document)!=0).Take(3);
    private void ContentChip(string kind,Entry e,int x,int y,int w,int h)=>
        Btn(ContentTitle(e),x,y,w,h,()=>OpenContent(e.Document),false,kind+"/"+e.Document);

    private void DrawContent()
    {
        bool authoring=ProjectContentStore.Authoring;
        WorkshopUI.Label(content,"게임 콘텐츠",12,36,200,22,15);
        contentFind=null;
        if(authoring)
        {
            WorkshopUI.Label(content,"찾기",360,36,30,20);
            contentFind=Input("콘텐츠 찾기",contentQuery,392,36,180,20);
            contentFind.onEndEdit.AddListener(value=>{ value=value.Trim(); if(value==contentQuery) return; contentQuery=value; Draw(page); });
            WorkshopUI.Label(content,"Ctrl+F",578,36,50,20,8);
        }
        if(authoring && contentQuery.Length>0){ DrawFindResults(); return; }
        for(int i=0;i<contentTiles.Length;i++)
        {
            var tile=contentTiles[i];
            bool last=i==contentTiles.Length-1;
            int x=last ? 12 : 12+i%6*103, y=last ? 152 : 62+i/6*44;
            var button=Btn(tile.caption,x,y,98,40,()=>OpenTile(tile),false,tile.id);
            button.interactable=authoring && tile.id!=PlaytestTile;
            int unsaved=authoring ? UnsavedIn(tile.id) : 0;
            string badge=tile.id==PlaytestTile ? "P6" : unsaved>0 ? "● "+unsaved : null;
            if(badge!=null){ var mark=WorkshopUI.Label(button.transform,badge,60,1,36,12,8); mark.alignment=TextAlignmentOptions.TopRight; }
            // 상태이상 경고 수. 이번 세션에 연 상태이상 초안이 있을 때만 센다(세션 첫 허브·시험 복귀 직후에는 그리지 않는다).
            var status=authoring && tile.open==Page.Status ? DraftStore.All.FirstOrDefault(d=>d.entry.source is StatusBalanceSO) : null;
            int warnings=status!=null ? StatusWarnings(status).Count() : 0;
            if(warnings>0){ var warn=WorkshopUI.Label(button.transform,"⚠ "+warnings,60,27,36,12,8); warn.alignment=TextAlignmentOptions.BottomRight; }
        }
        if(authoring)
        {
            WorkshopUI.Label(content,"최근",118,152,34,24);
            WorkshopUI.Label(content,"고정",118,180,34,24);
            int k=0; foreach(var e in RecentContent()) ContentChip("최근",e,154+k++*118,152,114,24);
            k=0; foreach(var e in PinnedContent()) ContentChip("고정",e,154+k++*118,180,114,24);
            return;
        }
        WorkshopUI.Label(content,"프로젝트 연결 필요 · 저장은 체크아웃에 연결한 기본 작업 공간에서만 됩니다.",12,214,612,22,12);
        int bx=12;
#if LEVEL_WORKSHOP && !UNITY_EDITOR
        Btn("프로젝트 연결",bx,240,110,24,OpenConnection); bx+=118;
#endif
        if(WorkshopWorkspace.ActiveId!="")
            Btn("기본 작업 공간으로 전환",bx,240,170,24,()=>ActivateWorkspace(WorkshopWorkspace.List().First(e=>e.id=="")));
    }
    // 그 타일이 소유한 미저장 문서 수: DraftStore 초안 + 같은 이름의 Title 페이지(플레이어·대사·번역·아트).
    private int UnsavedIn(string tile)=>DraftStore.Unsaved.Count(d=>TileOf(d.entry)==tile)+pages.Count(p=>p.Title==tile && p.Dirty());
    private void DrawFindResults()
    {
        string q=contentQuery;
        bool Has(string text)=>text!=null && text.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0;
        var hits=new List<(string key,string caption,Action open)>();
        foreach(var tile in contentTiles.Where(t=>t.id!=PlaytestTile && (Has(t.caption) || Has(t.id))))
            hits.Add((tile.id,tile.caption+"   화면",()=>OpenTile(tile)));
        foreach(var e in Documents().Where(e=>Has(e.Title) || Has(e.source.name) || Has(e.group) || Has(ContentTitle(e))))
            hits.Add((e.Document,ContentTitle(e)+"   "+e.group,()=>OpenContent(e.Document)));
        // 소유 화면의 필드(상태이상): 그 필드가 든 행을 열고 그 줄로 스크롤한다. 모든 SO의 필드 찾기는 P2에서 색인과 함께 한다.
        var balance=Documents().FirstOrDefault(e=>e.source is StatusBalanceSO);
        if(balance!=null)
            foreach(var f in ProjectParameters.Fields(balance.source,labels:true).Where(f=>Has(f.label) || Has(f.path)))
                hits.Add(("상태이상/"+f.path,f.label+"   상태이상",()=>FocusStatusField(f.path)));
        if(hits.Count==0) WorkshopUI.Label(content,"일치하는 항목이 없습니다.",12,66,612,22,12);
        for(int i=0;i<Math.Min(10,hits.Count);i++)
        {
            var hit=hits[i];
            Btn(hit.caption,12,62+i*23,612,21,hit.open,false,"찾기/"+hit.key).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
        }
        if(hits.Count>10) WorkshopUI.Label(content,"외 "+(hits.Count-10)+"개 · 검색어를 더 입력하세요.",12,295,400,18,8);
    }
}
