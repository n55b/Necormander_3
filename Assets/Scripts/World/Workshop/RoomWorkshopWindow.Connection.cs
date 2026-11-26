#if LEVEL_WORKSHOP && !UNITY_EDITOR
using System.Linq;
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
        void Connect()
        {
            ProjectDataConnection.Connect(path.text);
            WorkshopWorkspace.Switch("");
            WorkshopTrial.Draft=null;WorkshopTrial.Undo.Clear();WorkshopTrial.Redo.Clear();
            floorSettings=null;openFloors.Clear();ResetPages();DraftStore.Clear();
            Navigate(Page.Desk);Say("프로젝트 DATA에 연결했습니다.");
        }
        WorkshopUI.Button(panel,ProjectContentStore.Authoring ? "현재 문서 저장 후 연결" : "연결",15,144,173,26,()=>Safe(()=>{
            if(ProjectContentStore.Authoring)
            {
                // 모두 저장이 확인 역할을 한다. 방 충돌이나 판 차이로 저장하지 못한 초안이 남으면 연결하지 않는다.
                SaveAllDocuments();
                if(pendingConflict!=null || UnsavedTitles().Any())return;
                Connect();
            }
            else
            {
                if(Doc!=null && dirty)Save();
                ConfirmUnsaved(Connect);   // 작업 공간 사본에서도 기본 작업 공간의 초안이 남아 있을 수 있다
            }
        }));
        WorkshopUI.Button(panel,"닫기",200,144,92,26,()=>Draw(page));
        path.ActivateInputField();
    }
}
#endif
