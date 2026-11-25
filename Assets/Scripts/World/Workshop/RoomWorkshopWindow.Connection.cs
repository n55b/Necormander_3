#if LEVEL_WORKSHOP && !UNITY_EDITOR
using TMPro;

public sealed partial class RoomWorkshopWindow
{
    private void OpenConnection()
    {
        var panel=WorkshopUI.Frame(content,"Project connection",8,38,622,278).rectTransform;
        WorkshopUI.Label(panel,"프로젝트 체크아웃 폴더",15,22,580,28,14);
        var box=WorkshopUI.Box(panel,"프로젝트 경로",15,73,580,34,WorkshopUI.Ink);
        var path=box.gameObject.AddComponent<TMP_InputField>();
        path.textViewport=box.rectTransform;
        path.textComponent=WorkshopUI.Label(box.transform,"",5,0,569,34);
        path.text=ProjectDataConnection.Checkout ?? "";
        WorkshopUI.Button(panel,ProjectContentStore.Authoring ? "현재 문서 저장 후 연결" : "연결",15,144,173,26,()=>Safe(()=>{
            if(ProjectContentStore.Authoring)
            {
                SaveAllDocuments();
                if(pendingConflict!=null)return;
            }
            else if(Doc!=null && dirty)Save();
            ProjectDataConnection.Connect(path.text);
            WorkshopWorkspace.Switch("");
            WorkshopTrial.Draft=null;WorkshopTrial.Undo.Clear();WorkshopTrial.Redo.Clear();
            floorSettings=null;balance=null;dataEntry=null;dataField=null;dataCatalog=null;
            conversation=null;translation=null;translationTable=null;artEntries=null;art=null;
            nextArt=null;DisposeArt();catalog.dialogueTable.Invalidate();openFloors.Clear();
            Navigate(Page.Desk);Say("프로젝트 DATA에 연결했습니다.");
        }));
        WorkshopUI.Button(panel,"닫기",200,144,92,26,()=>Draw(page));
        path.ActivateInputField();
    }
}
#endif
