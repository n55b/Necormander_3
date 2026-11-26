using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow
{
    // 시험·게임 오버로 LevelWorkshop 씬이 다시 열려도 남는 화면 상태. remembered*(지도)와 같은 방식이다.
    private static class WorkshopSession
    {
        internal static bool FromContent;      // 콘텐츠 화면에서 시험을 시작했다. 시험이 실제로 시작된 뒤에만 켜고 Start()가 바로 소비한다
        internal static Page Page=Page.Desk;   // 그때 보던 화면
        internal static string Item;           // 선택 항목 키: 모든 수치는 entry.id
        internal static int ListPage;          // 모든 수치 dataPage
        internal static float Scroll;          // 상세 content y(논리 px, 정수)
        internal static string Focus;          // FocusKey(부모 이름 + 이름)
        internal static string Lost;           // 시험으로 창과 함께 사라진 Title 페이지 이름들. 돌아와서 알린다
    }
    private float detailScrollY;           // 상세 content y. Scroll의 moved 콜백이 갱신한다
    private bool restoringScroll;          // 시험 복귀 중: Detail()이 WorkshopSession.Scroll을 쓴다

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        WorkshopSession.FromContent=false; WorkshopSession.Page=Page.Desk; WorkshopSession.Item=WorkshopSession.Focus=WorkshopSession.Lost=null;
        WorkshopSession.ListPage=0; WorkshopSession.Scroll=0; QuitApproved=false;
    }

    // 콘텐츠 화면에서 시험을 시작한 직후 부른다(S7 StartContentTrial). PlayArena는 씬 로드를 요청만 하므로 같은 프레임에 기록해도 된다.
    private void RememberContent(string item)
    {
        var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        WorkshopSession.FromContent=true; WorkshopSession.Page=page; WorkshopSession.Item=item;
        WorkshopSession.ListPage=dataPage; WorkshopSession.Scroll=detailScrollY;
        WorkshopSession.Focus=selected!=null && selected.transform.IsChildOf(content) ? FocusKey(selected.transform) : null;
        if(Doc!=null) RememberMap();          // 방을 열어 둔 채 콘텐츠를 시험해도 지도 상태가 남는다
    }
    private void RestoreContent()
    {
        restoringScroll=true;                  // Detail()이 WorkshopSession.Scroll을 한 번 적용한다
        switch(WorkshopSession.Page)
        {
            case Page.Parameters:
                OpenParameters();
                var entry=Array.Find(dataCatalog,e=>e.id==WorkshopSession.Item);
                if(entry!=null){ SelectContent(entry); dataPage=WorkshopSession.ListPage; Draw(page); }
                break;
            default: Draw(WorkshopSession.Page); break;
        }
        restoringScroll=false;                 // Detail()을 쓰지 않는 화면에서 남지 않게
        Refocus(WorkshopSession.Focus);
    }
    private void RememberMap()
    {
        rememberedSelection=selection.ToArray(); rememberedStart=start; rememberedTools=(tool,layer,enemyIndex,propIndex,dir);
        rememberedTile=palette?.Length>0 ? palette[materialIndex].id : null; rememberedCell=floorCell; rememberedFloor=roomFloor;
    }
    private void RestoreMap()
    {
        selection.UnionWith(rememberedSelection); start=rememberedStart; floorCell=rememberedCell; roomFloor=rememberedFloor;
        (tool,layer,enemyIndex,propIndex,dir)=rememberedTools;
    }
    // 이름에 '/'가 들어가므로(상태이상/poison) Transform 경로 대신 부모 이름 + 이름으로 찾는다.
    private static string FocusKey(Transform t)=>t.parent.name+"\n"+t.name;
    // 입력칸은 되살리지 않는다: Select가 편집을 다시 시작하고, 입력칸 onEndEdit(선택 해제 중)에서 부른 Draw라면 EventSystem이 중첩 선택을 오류로 막는다.
    private void Refocus(string key)
    {
        var events=UnityEngine.EventSystems.EventSystem.current;
        if(key==null || events==null || events.alreadySelecting) return;
        foreach(var s in content.GetComponentsInChildren<Selectable>())
            if(s.interactable && s is not TMPro.TMP_InputField && FocusKey(s.transform)==key){ s.Select(); return; }
    }
}
