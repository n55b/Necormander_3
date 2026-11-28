using System;

public sealed partial class RoomWorkshopWindow
{
    // 페이지 하나 = 한 줄. 새 페이지는 Page에 값을 더하고 RegisterPages에 한 줄을 넣는다. 상태는 자기 partial 파일에 둔다.
    private struct PageEntry
    {
        public Action Draw;      // content를 처음부터 만든다
        public Action Save;      // Ctrl+S: 지금 보는 항목만. Map은 UpdateMap이 직접 처리한다
        public Func<bool> Dirty; // Title이 있는 페이지만 모두 저장에 센다
        public string Title;     // 미저장 목록에 보이는 이름
        public Action Reset;     // 프로젝트 연결 때 상태 비우기
        public Func<DraftStore.Draft> Draft; // Ctrl+Z/Y의 대상 문서
        public Action Trial;     // F6: 시험장에서 시험
        public Action Keys;      // 페이지 전용 키·포인터. Map은 기존 지도 입력 전체
        public Area? Area;       // 소속 작업 영역. null은 첫 화면이다. 헤더는 이 영역의 탭만 보인다
        public (string caption,int width,string id) Tab;   // 헤더 탭(caption이 null이면 탭 없음). id는 검사가 누르는 고정 이름(null이면 caption)
        public Action Open;      // 탭·허브 타일이 이 화면을 여는 방법(초안 준비 뒤 Navigate). null이면 Navigate만. 있으면 탭은 연결한 기본 작업 공간에서만 누른다
    }
    private PageEntry[] pages;
    private string deskControl;

    // 배열 인덱스 = (int)Page라서 모두 저장 순서와 헤더 탭 순서가 고정된다. 영역의 첫 탭이 카드·영역 전환으로 여는 화면이다.
    private void RegisterPages()
    {
        pages=new PageEntry[Enum.GetValues(typeof(Page)).Length];
        pages[(int)Page.Desk]        =new PageEntry{Draw=()=>DrawLibrary(deskControl),Area=Area.Level,Tab=("방 목록",56,"내 작업")};
        pages[(int)Page.Legacy]      =new PageEntry{Draw=()=>DrawLibrary(null),Area=Area.Level,Tab=("Legacy",54,null)};
        pages[(int)Page.Shared]      =new PageEntry{Draw=()=>DrawLibrary(null),Area=Area.Share,Tab=("공유",45,null)};
        pages[(int)Page.Project]     =new PageEntry{Draw=()=>DrawLibrary(null),Area=Area.Level};
        pages[(int)Page.Map]         =new PageEntry{Draw=()=>{if(pendingConflict!=null)DrawSaveConflict();else DrawMap();},Keys=UpdateMap,Area=Area.Level};
        pages[(int)Page.Compare]     =new PageEntry{Draw=DrawComparison,Area=Area.Level};
        pages[(int)Page.Passage]     =new PageEntry{Draw=DrawPassage,Area=Area.Level};
        pages[(int)Page.Git]         =new PageEntry{Draw=DrawGit,Area=Area.Share};
        // 층 설정(floorSettings·openFloors)은 작업 공간별로 맡겨 두므로 Reset이 없다. 연결 때만 Connection.cs가 비운다.
        pages[(int)Page.Game]        =new PageEntry{Draw=DrawFloor,Save=SaveFloorShortcut,Area=Area.Level,Tab=("게임 설정",62,null)};
        pages[(int)Page.Balance]     =new PageEntry{Draw=DrawBalance,Save=SaveBalance,Title="플레이어 수치",Dirty=()=>balance?.Dirty==true,Reset=()=>balance=null,Area=Area.Content};
        pages[(int)Page.Parameters]  =new PageEntry{Draw=DrawParameters,Save=SaveParameters,Reset=ResetParameters,Draft=()=>paramDraft,Trial=StartContentTrial,Area=Area.Content};
        pages[(int)Page.Dialogue]    =new PageEntry{Draw=DrawDialogue,Save=SaveDialogue,Title="대사 편집",Dirty=()=>conversation!=null&&dialogueDirty,
            Reset=()=>{conversation=null;dialogueDirty=false;catalog.dialogueTable.Invalidate();},Area=Area.Text,Tab=("대사 편집",62,null)};
        pages[(int)Page.Translations]=new PageEntry{Draw=DrawTranslations,Save=SaveTranslation,Title="UI·보상 번역",
            Dirty=()=>creatingTranslation||translation!=null&&translationDraft!=translation.Value,
            Reset=()=>{translation=null;translationTable=null;creatingTranslation=false;},Area=Area.Text,Tab=("UI·보상 번역",76,null)};
        pages[(int)Page.Art]         =new PageEntry{Draw=DrawArt,Save=SaveArt,Title="아트 원본",Dirty=()=>art!=null&&nextArt!=null,
            Reset=()=>{artEntries=null;art=null;nextArt=null;DisposeArt();},Area=Area.Art,Tab=("아트 원본",62,null)};
        pages[(int)Page.Content]     =new PageEntry{Draw=DrawContent,Area=Area.Content,Tab=("게임 콘텐츠",72,null)};
        pages[(int)Page.Status]      =new PageEntry{Draw=DrawStatus,Save=SaveStatus,Draft=()=>statusDraft,Trial=StartContentTrial,Keys=StatusKeys,
            Reset=()=>{statusDraft=null;statusFocus=null;},Area=Area.Content,Tab=("상태이상",58,"탭/상태이상"),Open=()=>OpenStatus(null)};
        pages[(int)Page.Home]        =new PageEntry{Draw=DrawHome,Reset=()=>findQuery=""};
        // 게임 콘텐츠 탭은 전용 화면이 있는 도메인만 둔다(허브 타일과 id가 겹치지 않게 "탭/"). 전용 화면이 생기면 Tab·Open을 더한다.
        pages[(int)Page.Items]       =new PageEntry{Draw=DrawItems,Save=SaveItem,Draft=()=>itemDraft,Trial=StartContentTrial,Keys=ItemKeys,Reset=ResetItems,Area=Area.Content,
            Tab=("아이템",50,"탭/아이템"),Open=()=>OpenItems(null)};
    }
    private void SaveFloorShortcut(){ if(routeDraft!=null) return; floorSettings.Save(); Say("층 설정을 저장했습니다."); }
    private void ResetParameters(){ dataEntry=null; dataField=null; dataCatalog=null; paramDraft=null; }
    private void ResetPages(){ foreach(var entry in pages) entry.Reset?.Invoke(); ForgetHistory(); }
}
