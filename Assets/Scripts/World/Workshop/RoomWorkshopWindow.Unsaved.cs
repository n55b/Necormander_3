using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 헤더 '미저장 n', 목록 팝업, 나가기 확인 모달, 플레이 종료 처리.
// 세는 것: DraftStore 초안 + Title이 있는 등록표 페이지 중 Dirty()인 것. 방 초안(.draft 자동 복구)과 층 설정(무대 플레이가 저장)은 세지 않는다.
public sealed partial class RoomWorkshopWindow
{
    private Button unsavedButton;
    private bool unsavedOpen;
    private Action pendingConfirm;           // null이 아니면 모달이 떠 있다. Draw 끝에서 다시 그리므로 다른 Draw가 지워도 남는다
    internal static bool QuitApproved;       // 모달에서 진행을 골랐거나, 검사 플레이어가 끝낼 때(Finish가 켠다)

    private static string DraftTitle(DraftStore.Draft d)=>ContentTitle(d.entry);
    private IEnumerable<string> UnsavedTitles()=>DraftStore.Unsaved.Select(DraftTitle)
        .Concat(pages.Where(p=>p.Title!=null && p.Dirty()).Select(p=>p.Title));

    // Draw의 헤더에서 부른다. Authoring이고 지도가 아니면 항상 만들고 0이면 숨긴다(제자리 편집으로 0→n이 되면 RefreshUnsaved가 켠다).
    private void DrawUnsavedButton()
    {
        unsavedButton=null;
        if(!ProjectContentStore.Authoring || page==Page.Map) return;
        unsavedButton=Btn("미저장",523,5,59,20,()=>{unsavedOpen=!unsavedOpen;Draw(page);},unsavedOpen,"미저장");
        RefreshUnsaved();
    }
    private void RefreshUnsaved()
    {
        if(unsavedButton==null) return;
        int count=UnsavedTitles().Count();
        unsavedButton.gameObject.SetActive(count>0);
        unsavedButton.GetComponentInChildren<TMP_Text>().text="미저장 "+count;
    }
    private void DrawUnsavedList()
    {
        var rows=DraftStore.Unsaved.Select(d=>(title:DraftTitle(d),key:d.entry.Document,open:(Action)(()=>OpenDraft(d))))
            .Concat(Enumerable.Range(0,pages.Length).Where(i=>pages[i].Title!=null && pages[i].Dirty())
                .Select(i=>(title:pages[i].Title,key:pages[i].Title,open:(Action)(()=>Navigate((Page)i))))).ToArray();
        WorkshopUI.Blocker(content,Color.clear,()=>{unsavedOpen=false;Draw(page);});
        int shown=Mathf.Min(rows.Length,6), more=rows.Length-shown;
        int height=26+shown*22+(more>0 ? 16 : 0)+30;
        var box=WorkshopUI.Frame(content,"Unsaved list",400,30,232,height).rectTransform;
        WorkshopUI.Label(box,"저장하지 않은 변경",8,4,216,18);
        for(int i=0;i<shown;i++)
        {
            var row=rows[i];
            WorkshopUI.Button(box,row.title,8,26+i*22,216,20,()=>Safe(()=>{unsavedOpen=false;row.open();}),false,"미저장/"+row.key);
        }
        if(more>0) WorkshopUI.Label(box,"외 "+more+"개",8,26+shown*22,216,16,8);
        WorkshopUI.Button(box,"모두 저장 Ctrl+Shift+S",8,height-26,216,20,()=>Safe(()=>{unsavedOpen=false;SaveAllDocuments();}),true,"미저장/모두 저장");
    }
    private void OpenDraft(DraftStore.Draft d)=>OpenContent(d.entry.Document);

    private void ConfirmUnsaved(Action proceed)
    {
        if(!UnsavedTitles().Any()){ proceed(); return; }
        pendingConfirm=proceed; Draw(page);
    }
    private void DrawConfirm()
    {
        var unsaved=UnsavedTitles().ToArray();
        WorkshopUI.Blocker(content,new Color(0,0,0,.5f));          // 뒤의 탭·메뉴를 막는다
        var box=WorkshopUI.Frame(content,"Unsaved confirm",170,110,300,120).rectTransform;
        WorkshopUI.Label(box,"저장하지 않은 변경 "+unsaved.Length+"개",12,8,276,20,12);
        WorkshopUI.Label(box,string.Join(" · ",unsaved.Take(3))+(unsaved.Length>3 ? " 외 "+(unsaved.Length-3)+"개" : ""),12,32,276,18,8);
        if(ProjectContentStore.Authoring)
            WorkshopUI.Button(box,"모두 저장 후 계속",12,82,110,24,()=>Safe(()=>{SaveAllDocuments(); if(!UnsavedTitles().Any()) Proceed();}),true,"확인/모두 저장");
        WorkshopUI.Button(box,"저장 안 함",128,82,80,24,()=>Safe(()=>{DraftStore.Clear(); Proceed();}),false,"확인/저장 안 함");
        WorkshopUI.Button(box,"취소",214,82,74,24,()=>{pendingConfirm=null; Draw(page);},false,"확인/취소");
    }
    // 진행 동작(Leave·종료·연결)은 씬을 바꾸거나 페이지를 스스로 비우므로 여기서 ResetPages를 부르지 않는다.
    private void Proceed(){ var go=pendingConfirm; pendingConfirm=null; go(); }
#if LEVEL_WORKSHOP && !UNITY_EDITOR
    private bool WantsToQuit()
    {
        if(QuitApproved || !UnsavedTitles().Any()) return true;
        ConfirmUnsaved(()=>{ QuitApproved=true; Application.Quit(); });
        return false;
    }
#endif
#if UNITY_EDITOR
    // 편집기는 플레이 종료를 막을 수 없다. 사라지는 미저장 문서를 로그로 남긴다.
    private void WarnUnsavedOnExit(UnityEditor.PlayModeStateChange state)
    {
        if(state!=UnityEditor.PlayModeStateChange.ExitingPlayMode || pages==null) return;
        var lost=UnsavedTitles().ToArray();
        if(lost.Length>0) Debug.LogWarning("[Workshop] 저장하지 않은 변경을 버리고 플레이를 끝냅니다: "+string.Join(", ",lost));
    }
#endif
}
