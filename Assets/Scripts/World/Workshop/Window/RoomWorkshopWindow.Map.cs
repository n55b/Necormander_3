#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Opening and closing a room: the map screen, the palette and the status line under the map.
public sealed partial class RoomWorkshopWindow
{
    // Puts an object moved by the position panel back where the document has it. Cleared before it runs,
    // so a failure here can never leave every later redraw failing too.
    private void RevertPiecePreview()
    {
        var revert=revertPiece; revertPiece=null; pieceFields=default;
        try { revert?.Invoke(); }
        catch(Exception error) { Debug.LogWarning("[Workshop] 배치물 위치 되돌리기 실패 · "+error.Message); }
    }
    private void CloseMap()
    {
        RevertPiecePreview();
        EndStroke();
        comparison?.Dispose();comparison=null;
        if (preview!=null)
        {
            RememberView();
            preview.Dispose(); preview=null;
        }
    }

    private void New(WorkshopCatalog.Template r) => Begin(WorkshopRoom.Create(r),true);
    private void Begin(WorkshopRoom room,bool changed)
    {
        // Saving writes a prefab, which needs the editor. The Windows creator never opens the room map.
        if (!PrefabMode) throw new InvalidOperationException(RoomsNeedEditor);
        notice="";
        savePanel=false; leavePrompt=false; pendingDelete=null;
        room.Validate(catalog);
        PlayerPrefs.SetString("Workshop.Opened."+room.id,DateTime.UtcNow.Ticks.ToString("D19"));
        WorkshopTrial.Draft=room; selection.Clear(); stamp.Clear(); sampling=false; encounterOpen=false; start=null; tool=Tool.Select;
        WorkshopTrial.Undo.Clear(); WorkshopTrial.Redo.Clear();
        dirty=changed;
        if(changed) Recover();
        OpenMap(true);
    }
    private void OpenMap(bool home,string tileId=null)
    {
        comparison?.Dispose(); comparison=null;
        preview?.Dispose();
        preview=new WorkshopPreview(catalog.FindRoom(Doc.template),Doc,catalog);
        preview.ResizeFrame(MapWidth,WorkshopPreview.Height);
        preview.ListenForEdits();
        if (!home) { preview.Center=WorkshopTrial.View; preview.Zoom=WorkshopTrial.Zoom; preview.SetView(); }
        RefreshSelection();
        preview.MarkStart(start);
        ApplyViewSettings(home);
        SyncPalette();
        int saved=Array.FindIndex(palette,t=>t.id==tileId);
        if(saved>=0) SelectMaterial(saved);
        Draw(Page.Map);
    }
    private void SyncPalette()
    {
        var map=preview.Map(layer);
        var local=map!=null ? new UnityEngine.Tilemaps.TileBase[map.GetUsedTilesCount()] : Array.Empty<UnityEngine.Tilemaps.TileBase>();
        if(local.Length>0) map.GetUsedTilesNonAlloc(local);
        palette=catalog.tiles.Where(t => tool==Tool.Auto ? t.tile is WorkshopTerrainTile : t.tile is not WorkshopTerrainTile)
            .OrderBy(t => RecentRank(t.id)).ThenByDescending(t => local.Contains(t.tile)).ThenBy(t => t.tile.name,StringComparer.Ordinal)
            .ThenBy(t => t.id,StringComparer.Ordinal).ToArray();
        int preferred=tool==Tool.Auto ? Array.FindIndex(palette, t=>((WorkshopTerrainTile)t.tile).PaintLayer==layer) : 0;
        SelectMaterial(Mathf.Max(0,preferred));
    }
    private void SelectMaterial(int index)
    {
        materialIndex=index;
        if(tool==Tool.Auto && palette.Length>0)
            layer=((WorkshopTerrainTile)palette[index].tile).PaintLayer;
    }
    private string MapHint() => tool switch
    {
        Tool.Select => "선택  ·  물체를 눌러 이동  /  빈 곳을 끌어 여러 개 선택",
        Tool.Auto => "자동 타일  ·  재료를 고르고 지도 위를 끌어 경계 잇기",
        Tool.Paint => "칠하기  ·  지도 위를 끌어 칠하기 · Shift 직선 · [ ] 붓 크기",
        Tool.Rect => "사각형  ·  끌어서 사각형 범위를 한 번에 칠하기",
        Tool.Fill => "채우기  ·  누른 곳과 이어진 같은 칸을 한 번에 칠하기",
        Tool.Erase => "지우개  ·  선택한 레이어에서 지울 곳을 끌기",
        Tool.Sample => "스포이드  ·  지도에서 타일을 눌러 같은 재료로 칠하기",
        Tool.Door => "출구  ·  방향을 고른 뒤 지도에서 연결할 위치 누르기",
        Tool.Enemy => "적 배치  ·  적을 고른 뒤 걸을 수 있는 바닥 누르기",
        Tool.Prop => scatter ? "흩뿌리기  ·  바닥을 문질러 소품 뿌리기 (통로 제외)" : "소품 배치  ·  소품을 고른 뒤 배치할 위치 누르기",
        Tool.Start => "시작점  ·  바닥을 누른 뒤 F6으로 그 위치에서 시험하기",
        Tool.Stamp when stamp.Count==0 => "복사 도장  ·  복사할 타일 범위를 끌어 선택하기",
        _ => "복사 도장  ·  지도에서 복사한 모양을 찍을 위치 누르기"
    };
    // Built once per opened room; kept while panels are redrawn so the picture never blinks and the name keeps its caret.
    private void DrawMapFrame()
    {
        viewport=WorkshopUI.Rect(content,"Map",MapLeft,54,MapWidth,WorkshopPreview.Height);
        var image=viewport.gameObject.AddComponent<RawImage>(); image.texture=preview.Frame;
        drawnPreview=preview;
        NameField();
        cursorCell=null;
        cursorLabel=WorkshopUI.Label(content,"",237,31,85,20);
        // Saving writes a prefab; registration in RoomPrefabData stays manual.
        Btn("비교",345,5,40,20,OpenComparison);
        Btn("저장",389,5,96,20,OpenSavePanel).name="저장";
        Btn("플레이",493,5,83,20,Play,true);
    }
    private void DrawMapPanels()
    {
        Btn("단축키 ?",268,5,70,20,()=>{helpOpen=!helpOpen;Draw(page);},helpOpen).name="단축키";
        if(catalog.FindRoom(Doc.template).room.TryGetComponent<NormalRoomEvent>(out _))
            Btn("전투 설정",413,31,72,20,()=>{encounterOpen=!encounterOpen; drawer=false; Draw(page);},encounterOpen);
        var source=catalog.FindRoom(Doc.template);
        WorkshopUI.Label(content,IsSavedPrefab ? WorkshopPrefabs.Label(WorkshopPrefabs.TagOf(source))+" · "+
            (WorkshopPrefabs.IsRegistered(catalog,source) ? "게임 등록됨" : "미등록") : "저장 전 · 새 방",324,31,87,20).color=WorkshopUI.Mint;
        DrawQuickStrip();
        DrawToolbox();
        Btn(SnapLabel,268,333,62,22,CycleSnap,snapMode>0).name="자석";
        Btn(showGrid ? "격자 켬" : "격자",334,333,60,22,()=>{showGrid=!showGrid;preview.ShowGrid(showGrid);Draw(page);},showGrid).name="격자";
        zoomOut=Btn("−",398,333,24,22,()=>{StepZoom(Vector2.one*.5f,-1);Draw(page);});
        zoomOut.interactable=preview.CanZoomOut;
        zoomIn=Btn("+",426,333,24,22,()=>{StepZoom(Vector2.one*.5f,1);Draw(page);});
        zoomIn.interactable=preview.CanZoomIn;
        Btn("원위치",455,333,60,22,()=>{preview.Home();Draw(page);});
        Btn("전체 보기",519,333,76,22,()=>{preview.Overview();Draw(page);},preview.Zoom==0);
        Btn("효과",599,333,31,22,ToggleJuice,WorkshopPreview.Juice).name="편집 효과";
        UpdateDetail();
        if(drawer)
        {
            if(tool==Tool.Stamp) DrawStampLibrary(); else DrawPalette();
        }
        if(encounterOpen) DrawEncounter();
        if(helpOpen) DrawHelp();
    }
    private void Cycle(int delta)
    {
        int Wrap(int n,int length)=>(n+delta+length)%length;
        if(tool==Tool.Enemy) enemyIndex=Wrap(enemyIndex,enemies.Length);
        else if(tool==Tool.Prop) propIndex=Wrap(propIndex,props.Length);
        else if(palette.Length>0) SelectMaterial(Wrap(materialIndex,palette.Length));
        Draw(page);
    }
    private void UpdateDetail()
    {
        if(undoButton!=null) undoButton.interactable=WorkshopTrial.Undo.Count>0;
        if(redoButton!=null) redoButton.interactable=WorkshopTrial.Redo.Count>0;
        if(removeButton!=null) removeButton.interactable=selection.Count>0;
        if(detail!=null && preview!=null)
        {
            string target=selection.Count==1 ? preview.PieceName(selection.First()) :
                selection.Count>1 ? selection.Count+"개 선택" : "선택 없음";
            // Wall and unsteppable show their filled cells on the map; the count sits beside the selection.
            if(preview.CoverageLayer==layer) target+="  ·  "+preview.CoverageCount+"칸";
            shownCoverage=(preview.CoverageLayer,preview.CoverageCount);
            detail.text=(dirty ? "저장 필요" : "저장됨")+"  ·  "+preview.ZoomLabel+"\n"+target;
        }
    }
}
#endif
