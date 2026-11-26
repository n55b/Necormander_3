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
        public Action Keys;      // 페이지 전용 키·포인터. Map은 기존 지도 입력 전체
    }
    private PageEntry[] pages;
    private string deskControl;

    // 배열 인덱스 = (int)Page라서 모두 저장 순서가 고정된다.
    private void RegisterPages()
    {
        pages=new PageEntry[Enum.GetValues(typeof(Page)).Length];
        pages[(int)Page.Desk]        =new PageEntry{Draw=()=>DrawLibrary(deskControl)};
        pages[(int)Page.Legacy]      =new PageEntry{Draw=()=>DrawLibrary(null)};
        pages[(int)Page.Shared]      =new PageEntry{Draw=()=>DrawLibrary(null)};
        pages[(int)Page.Project]     =new PageEntry{Draw=()=>DrawLibrary(null)};
        pages[(int)Page.Map]         =new PageEntry{Draw=()=>{if(pendingConflict!=null)DrawSaveConflict();else DrawMap();},Keys=UpdateMap};
        pages[(int)Page.Compare]     =new PageEntry{Draw=DrawComparison};
        pages[(int)Page.Passage]     =new PageEntry{Draw=DrawPassage};
        pages[(int)Page.Git]         =new PageEntry{Draw=DrawGit};
        // 층 설정(floorSettings·openFloors)은 작업 공간별로 맡겨 두므로 Reset이 없다. 연결 때만 Connection.cs가 비운다.
        pages[(int)Page.Game]        =new PageEntry{Draw=DrawFloor,Save=SaveFloorShortcut};
        pages[(int)Page.Balance]     =new PageEntry{Draw=DrawBalance,Save=SaveBalance,Title="플레이어 수치",Dirty=()=>balance?.Dirty==true,Reset=()=>balance=null};
        pages[(int)Page.Parameters]  =new PageEntry{Draw=DrawParameters,Save=SaveParameters,Reset=ResetParameters,Draft=()=>paramDraft};
        pages[(int)Page.Dialogue]    =new PageEntry{Draw=DrawDialogue,Save=SaveDialogue,Title="대사 편집",Dirty=()=>conversation!=null&&dialogueDirty,
            Reset=()=>{conversation=null;dialogueDirty=false;catalog.dialogueTable.Invalidate();}};
        pages[(int)Page.Translations]=new PageEntry{Draw=DrawTranslations,Save=SaveTranslation,Title="UI·보상 번역",
            Dirty=()=>creatingTranslation||translation!=null&&translationDraft!=translation.Value,
            Reset=()=>{translation=null;translationTable=null;creatingTranslation=false;}};
        pages[(int)Page.Art]         =new PageEntry{Draw=DrawArt,Save=SaveArt,Title="아트 원본",Dirty=()=>art!=null&&nextArt!=null,
            Reset=()=>{artEntries=null;art=null;nextArt=null;DisposeArt();}};
        pages[(int)Page.Content]     =new PageEntry{Draw=DrawContent,Reset=()=>contentQuery=""};
    }
    private void SaveFloorShortcut(){ if(routeDraft!=null) return; floorSettings.Save(); Say("층 설정을 저장했습니다."); }
    private void ResetParameters(){ dataEntry=null; dataField=null; dataCatalog=null; paramDraft=null; }
    private void ResetPages(){ foreach(var entry in pages) entry.Reset?.Invoke(); }
}
