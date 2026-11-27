#if UNITY_EDITOR || LEVEL_WORKSHOP
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow
{
    private Vector2 passagePreviousCenter;
    private int passagePreviousZoom;
    private bool showPassageOverlay, passageOverview;
    private (Vector3 focus, int removed, int added, int reachable) passageResult;

    private void OpenPassage()
    {
        EndStroke();
        passagePreviousCenter = preview.Center;
        passagePreviousZoom = preview.Zoom;
        showPassageOverlay = true;
        passageOverview = false;
        comparison = new WorkshopPreview(catalog.FindRoom(Doc.template), Doc, catalog, true);
        preview.ResizeFrame(310,176);
        comparison.ResizeFrame(310,176);
        preview.HighlightSelection(System.Array.Empty<string>());
        preview.Hover(null,Vector3.zero,false,Color.clear);
        preview.MarkStart(null);
        preview.ShowDoorMarkers(false);
        comparison.ShowDoorMarkers(false);
        SelectPassage(dir);
    }

    private void SelectPassage(int directionIndex)
    {
        dir = directionIndex;
        passageResult = comparison.PreviewPassage(preview, directions[dir], catalog.generation.floorTile);
        FramePassage();
    }

    private void FramePassage()
    {
        if (passageOverview) preview.Overview();
        else { preview.Center = passageResult.focus; preview.Zoom = WorkshopPreview.FocusZoom; preview.SetView(); }
        comparison.MatchView(preview);
        preview.ShowOverlay(showPassageOverlay);
        comparison.ShowOverlay(showPassageOverlay);
        Draw(Page.Passage);
    }

    private void ClosePassage()
    {
        comparison.Dispose(); comparison = null;
        preview.ShowOverlay(false);
        preview.ShowDoorMarkers(true);
        preview.ResizeFrame(MapWidth,WorkshopPreview.Height);
        if (passagePreviousZoom == 0) preview.Overview();
        preview.Center = passagePreviousCenter; preview.Zoom = passagePreviousZoom; preview.SetView();
        RefreshSelection(); preview.MarkStart(start);
        Draw(Page.Map);
    }

    private void DrawPassage()
    {
        WorkshopUI.Label(content,"출입구 연결 미리보기",9,35,180,24,14);
        for (int i=0; i<directions.Length; i++)
        {
            int index=i;
            Btn(compass[i],205+i*33,36,30,21,()=>SelectPassage(index),dir==i).interactable=preview.HasDoor(directions[i]);
        }
        Btn(passageOverview ? "출입구 확대" : "방 전체",370,36,90,21,()=>{passageOverview=!passageOverview;FramePassage();});
        Btn(showPassageOverlay ? "표시 숨기기" : "표시 보기",466,36,90,21,()=>{showPassageOverlay=!showPassageOverlay;FramePassage();});
        void Pane(string label, WorkshopPreview drawing, int x)
        {
            WorkshopUI.Label(content,label,x,60,310,23,12);
            var image=WorkshopUI.Rect(content,label+" render",x,85,310,176).gameObject.AddComponent<RawImage>();
            image.texture=drawing.Frame; image.raycastTarget=false;
        }
        Pane("연결 전",preview,7); Pane("연결 후",comparison,323);
        void Key(string label, Color32 color, int x, int y)
        {
            color.a=255;
            WorkshopUI.Box(content,"색상",x,y+6,8,8,color);
            WorkshopUI.Label(content,label,x+13,y,145,21);
        }
        Key("걷힐 칸 "+passageResult.removed,WorkshopPreview.RemovedColor,9,267);
        Key("바닥 보충 "+passageResult.added,WorkshopPreview.AddedColor,162,267);
        Key("열리는 통로",WorkshopPreview.PassageColor,326,267);
        Key("착지점",WorkshopPreview.ArrivalColor,482,267);
        Key("이동 가능 "+passageResult.reachable+"칸",WorkshopPreview.ReachColor,326,294);
        Btn("편집으로",513,295,109,23,ClosePassage);
    }
}
#endif
