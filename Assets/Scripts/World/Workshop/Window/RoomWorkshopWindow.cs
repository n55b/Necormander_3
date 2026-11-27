#if UNITY_EDITOR || LEVEL_WORKSHOP
// Workshop screens are compiled only in the editor and the Windows creator, never in the game build.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow : MonoBehaviour
{
    public WorkshopCatalog catalog;
    private enum Page { Desk, Map, Game, Balance, Parameters, Dialogue, Translations, Art, Compare, Passage }
    private enum Tool { Select, Paint, Erase, Sample, Door, Enemy, Prop, Start, Stamp, Auto, Rect, Fill }
    private Page page;
    private Tool tool;
    private Canvas canvas;
    // workspace is the whole screen. content is where new widgets go: the workspace itself, or on the map page
    // the "Map panels" layer, which is the only part rebuilt while the same room stays open (see RedrawMapPanels).
    private RectTransform workspace, content, viewport, mapPanels;
    private WorkshopPreview drawnPreview;
    private (int layer,int cells) shownCoverage=(-1,0);
    private TMP_Text feedback, detail, cursorLabel;
    private Button undoButton, redoButton, removeButton;
    private WorkshopPreview preview;
    private WorkshopPreview comparison;
    private WorkshopRoom Doc => WorkshopTrial.Draft;
    private string before, notice="";
    private int layer, materialIndex, enemyIndex, propIndex, dir, listPage;
    private Vector2Int? lastCell;
    private Vector3? start;
    private bool moving;
    private Vector2 lastPointer;
    private Vector3Int? cursorCell;
    private Button zoomIn, zoomOut;
    private readonly string[] layerNames={"바닥","벽","그림자","통행 불가"};
    private readonly Vector2Int[] directions={Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down};
    private readonly string[] compass={"동","북","서","남"};
    private WorkshopCatalog.Piece[] enemies, props;
    private WorkshopCatalog.TileEntry[] palette;
    // Every unsaved room lives in its draft slot until it is saved as a prefab.
    private static void WriteRecovery(string id,string json) => WorkshopDrafts.Write(id,json);
    private static bool dirty;
    private static string[] rememberedSelection=Array.Empty<string>();
    private static Vector3? rememberedStart;
    private static (Tool,int,int,int,int) rememberedTools;
    private static string rememberedTile;

    private void Start()
    {
        WorkshopTrial.Library=catalog;
        enemies=catalog.pieces.Where(p=>p.enemy).ToArray();
        props=catalog.pieces.Where(p=>!p.enemy).ToArray();
        int archer=Array.FindIndex(enemies,e=>e.prefab.name.Contains("Range"));
        enemyIndex=Mathf.Max(0,archer);
        canvas=WorkshopUI.Canvas("Workshop canvas",25000);
        canvas.transform.SetParent(transform,false);
        // Root canvases get their logical transform from the frame camera.
        if (WorkshopTrial.ReturnToFloor)
        {
            WorkshopTrial.ReturnToFloor=false; Draw(Page.Game);
        }
        else if (Doc!=null)
        {
            selection.UnionWith(rememberedSelection); start=rememberedStart;
            (tool,layer,enemyIndex,propIndex,dir)=rememberedTools;
            OpenMap(false,rememberedTile);
        }
        else Draw(Page.Desk);
    }
    private void OnDestroy()
    {
        DisposeArt();
        comparison?.Dispose();
        preview?.Dispose();
        foreach(var texture in thumbnails.Values) Destroy(texture);
        ReleaseStampThumbnails();
        if(canvas!=null) Destroy(canvas.gameObject);
    }
    private void Draw(Page next)
    {
        var previousControl=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        string deskControl=next==Page.Desk && page==Page.Desk && workspace!=null && previousControl!=null &&
            previousControl.transform.IsChildOf(workspace) ? previousControl.name : null;
        var typing=previousControl!=null ? previousControl.GetComponent<TMP_InputField>() : null;
        if(typing!=null && !typing.isFocused) typing=null;
        RevertPiecePreview();
        applyPiece=null;
        bool sameRoom=next==Page.Map && page==Page.Map && workspace!=null && viewport!=null && preview!=null && preview==drawnPreview;
        page=next;
        // The same room stays open: keep the map picture and the name box, rebuild only the panels around them.
        if (sameRoom) { RedrawMapPanels(); RestoreTyping(typing); return; }
        if (workspace!=null) { workspace.gameObject.SetActive(false); Destroy(workspace.gameObject); }
        workspace=content=WorkshopUI.Box(canvas.transform,"Workspace",0,0,640,360,WorkshopUI.Ink).rectTransform;
        mapPanels=null; drawnPreview=null; viewport=null;
        WorkshopUI.Frame(content,"Header",0,0,640,30);
        if (page==Page.Map)
        {
            Btn("← 목록",7,5,70,20,RequestLeave).name="목록으로 돌아가기";
            WorkshopUI.Label(content,"방 편집",84,4,80,22,15);
        }
        else
        {
            Btn(PrefabMode ? "방 제작실" : "DATA 제작실",7,5,86,20,()=>
                Say(PrefabMode ? WorkshopPrefabs.Root : ProjectContentStore.Authoring ? ProjectContentStore.Root : "프로젝트 DATA에 연결되지 않았습니다."));
            Btn(PrefabMode ? "내 방" : "내 작업",99,5,61,20,()=>Navigate(Page.Desk),page==Page.Desk);
            Btn("게임 설정",164,5,66,20,()=>Navigate(Page.Game),page==Page.Game);
        }
        Btn("메뉴",586,5,46,20,Leave);
#if LEVEL_WORKSHOP && !UNITY_EDITOR
        if(page==Page.Desk) Btn("프로젝트 연결",344,5,95,20,OpenConnection);
#endif
        if (ProjectContentStore.Authoring && page!=Page.Map && page!=Page.Game && page!=Page.Desk)
            Btn("플레이어 수치",447,5,106,20,()=>Navigate(Page.Balance),page==Page.Balance);
        if (page==Page.Map)
        {
            try { DrawMapFrame(); }
            catch(Exception error) { Debug.LogException(error); notice="화면 일부를 그리지 못했습니다 · "+error.Message; }
            RedrawMapPanels();
            return;
        }
        // One broken entry must not leave the page half-built without its status line.
        try
        {
            if(page==Page.Game) DrawFloor(); else if(page==Page.Balance) DrawBalance();
            else if(page==Page.Parameters) DrawParameters();
            else if(page==Page.Dialogue) DrawDialogue();
            else if(page==Page.Translations) DrawTranslations();
            else if(page==Page.Art) DrawArt(); else if(page==Page.Passage) DrawPassage(); else if(page==Page.Compare) DrawComparison(); else DrawLibrary(deskControl);
        }
        catch(Exception error)
        {
            Debug.LogException(error);
            notice="화면 일부를 그리지 못했습니다 · "+error.Message;
        }
        DrawStatusLine();
        RestoreTyping(typing);
    }
    // The map page in two layers. The frame (map picture, name box, save and play) is built once per room;
    // the panels (toolbox, bottom bar, drawers, dialogs, status line) are rebuilt on every Draw(page).
    private void RedrawMapPanels()
    {
        if (mapPanels!=null) { mapPanels.gameObject.SetActive(false); Destroy(mapPanels.gameObject); }
        content=mapPanels=WorkshopUI.Rect(workspace,"Map panels",0,0,640,360);
        if (nameField!=null && !nameField.isFocused && nameField.text!=Doc.title) nameField.SetTextWithoutNotify(Doc.title);
        if (viewport!=null && viewport.TryGetComponent<RawImage>(out var image)) image.texture=preview.Frame;
        try
        {
            DrawMapPanels();
            if (savePanel) DrawSavePanel(); else if (leavePrompt) DrawLeavePrompt();
        }
        catch(Exception error)
        {
            Debug.LogException(error);
            notice="화면 일부를 그리지 못했습니다 · "+error.Message;
        }
        DrawStatusLine();
    }
    private void DrawStatusLine()
    {
        string hint = page == Page.Passage ? "이동 범위는 착지점에서 상하좌우로 이어진 바닥 타일 기준입니다." :
            !string.IsNullOrEmpty(notice) ? notice : page == Page.Map ? MapHint() :
            page == Page.Desk ? (PrefabMode ? "저장 위치  ·  "+WorkshopPrefabs.Root+"/<태그>/Variants" :
                ProjectContentStore.Authoring ? "저장 위치  ·  프로젝트 DATA" : "프로젝트 DATA에 연결하면 콘텐츠를 편집할 수 있습니다.") : "";
        feedback=WorkshopUI.Label(content,hint,9,page==Page.Map ? 328 : 335,page==Page.Map ? 256 : 623,page==Page.Map ? 15 : 19);
        if (page==Page.Map)
            WorkshopUI.Label(content,"휠 확대 · 우클릭 끌기 이동 · M 자석 · ? 단축키",9,344,256,15);
    }
    // A redraw while someone is typing puts the caret back in the rebuilt box of the same name.
    private void RestoreTyping(TMP_InputField typed)
    {
        if (typed==null || workspace==null) return;
        string name=typed.name; int caret=typed.caretPosition;
        var again=workspace.GetComponentsInChildren<TMP_InputField>(true).FirstOrDefault(f=>f.name==name && f!=typed);
        if (again==null || !again.interactable || !again.gameObject.activeInHierarchy) return;
        again.Select(); again.ActivateInputField();
        StartCoroutine(PlaceCaret(again,caret));
    }
    private static System.Collections.IEnumerator PlaceCaret(TMP_InputField field,int caret)
    {
        yield return null;
        if (field==null || !field.isFocused) yield break;
        field.caretPosition=field.selectionAnchorPosition=field.selectionFocusPosition=Mathf.Clamp(caret,0,field.text.Length);
    }
    private Button Btn(string title,int x,int y,int w,int h,Action action,bool selectedTab=false) =>
        WorkshopUI.Button(content,title,x,y,w,h,()=>Safe(action),selectedTab);
    // A button that steps through values: click goes forward, right-click goes back.
    private Button Stepper(string title,int x,int y,int w,int h,Action<int> step)
    {
        var button=Btn(title,x,y,w,h,()=>step(1));
        button.gameObject.AddComponent<WorkshopRightClick>().action=()=>Safe(()=>step(-1));
        return button;
    }
    private static int StepWrap(int value,int count) => (value%count+count)%count;
    private void Safe(Action action)
    {
        try { action(); }
        catch(Exception ex) { Say(ex.Message); Debug.LogWarning("[Workshop] "+ex.Message); }
    }
    private void Say(string message) { notice=message; if(feedback!=null) feedback.text=message; }
    private void Navigate(Page next)
    {
        CloseMap(); listPage=0; drawer=false; encounterOpen=false; notice="";
        pendingDelete=null; savePanel=false; leavePrompt=false; Draw(next);
    }
    private void Leave()
    {
        CloseMap(); WorkshopTrial.Draft=null; WorkshopTrial.Undo.Clear(); WorkshopTrial.Redo.Clear();
        UnityNote.SceneLoader.Load("StartScene");
    }
    // Rooms are edited as prefabs inside the Unity editor. The Windows creator keeps the content tools only.
    private void DrawLibrary(string deskControl)
    {
        if (PrefabMode) DrawRoomList(deskControl);
        else DrawDesk(deskControl);
    }
    private void SaveAllDocuments()
    {
        if(!ProjectContentStore.Authoring)return;
        if(page==Page.Map && Doc!=null) FinishMapEdits();
        // The room is already kept in its draft slot; saving it as a prefab needs a name and tag.
        if(balance!=null) balance.Save();
        if(floorSettings!=null)
        {
            if(routeDraft!=null) floorSettings.ApplyTutorialOrder(routeDraft,catalog);
            floorSettings.Save(); routeDraft=null;
        }
        if(dataEntry!=null)SaveParameters();
        if(conversation!=null && dialogueDirty)SaveDialogue();
        if(creatingTranslation || translation!=null && translationDraft!=translation.Value)SaveTranslation();
        if(art!=null && nextArt!=null)
        {
            if(!artReady)throw new IOException("아트 미리보기가 끝나면 저장하세요.");
            SaveArt();
        }
        Draw(page);Say("열린 제작 문서를 모두 저장했습니다.");
    }
}
#endif
