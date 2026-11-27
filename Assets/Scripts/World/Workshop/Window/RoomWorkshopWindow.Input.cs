#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Keyboard shortcuts and mouse input on the map.
public sealed partial class RoomWorkshopWindow
{
    private void Update()
    {
        var keys=Keyboard.current;
        if(keys!=null && keys.ctrlKey.isPressed && keys.shiftKey.isPressed && keys.sKey.wasPressedThisFrame && ProjectContentStore.Authoring)
        {Safe(SaveAllDocuments);return;}
        if(page==Page.Parameters && keys!=null && keys.ctrlKey.isPressed && keys.sKey.wasPressedThisFrame) Safe(SaveParameters);
        if(page==Page.Dialogue && keys!=null && keys.ctrlKey.isPressed && keys.sKey.wasPressedThisFrame) Safe(SaveDialogue);
        if(page==Page.Translations && keys!=null && keys.ctrlKey.isPressed && keys.sKey.wasPressedThisFrame) Safe(SaveTranslation);
        if(page==Page.Art && keys!=null && keys.ctrlKey.isPressed && keys.sKey.wasPressedThisFrame) Safe(SaveArt);
        if(page==Page.Balance && keys!=null && keys.ctrlKey.isPressed && keys.sKey.wasPressedThisFrame) Safe(SaveBalance);
        if(page==Page.Game && routeDraft==null && keys!=null && keys.ctrlKey.isPressed && keys.sKey.wasPressedThisFrame)
            Safe(()=>{floorSettings.Save();Say("층 설정을 저장했습니다.");});
        if(page==Page.Desk && pendingDelete!=null && keys!=null && keys.escapeKey.wasPressedThisFrame) {pendingDelete=null;Draw(page);return;}
        // The coverage of the working layer belongs to the map page; the comparison and passage views hide it.
        if(preview!=null) preview.ShowCoverage(page==Page.Map);
        if(preview==null || viewport==null || page!=Page.Map || UnityNote.SceneLoader.IsLoading) return;
        Safe(preview.UpdateExitPassages);
        Safe(()=>preview.UpdateCoverage(layer));
        if(shownCoverage!=(preview.CoverageLayer,preview.CoverageCount)) UpdateDetail();
        preview.TickFeedback();
        var kb=Keyboard.current;
        bool modal=savePanel || leavePrompt || helpOpen || pendingStampDelete!=null;
        if(kb!=null && kb.ctrlKey.isPressed && kb.sKey.wasPressedThisFrame && !modal) {Safe(Save);return;}
        var focused=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        // Tab moves between the X and Y boxes of the object position panel.
        if(applyPiece!=null && pieceFields.x!=null && kb!=null && kb.tabKey.wasPressedThisFrame)
        {
            var next=focused==pieceFields.x.gameObject ? pieceFields.y : pieceFields.x;
            next.Select(); next.ActivateInputField(); return;
        }
        if(focused!=null && focused.TryGetComponent<TMP_InputField>(out var editing) && editing.isFocused) return;
        if(kb!=null && kb.escapeKey.wasPressedThisFrame)
        {
            if(modal) {savePanel=false; leavePrompt=false; helpOpen=false; pendingStampDelete=null; Draw(page); return;}
            if(drawer || encounterOpen || applyPiece!=null) {drawer=false; encounterOpen=false; Draw(page); return;}
            if(before==null) {Safe(RequestLeave); return;}
        }
        if(kb!=null && kb.slashKey.wasPressedThisFrame && helpOpen) {helpOpen=false; Draw(page); return;}
        // L folds the material strip, Shift+L opens or closes the material list (stamp library with the stamp tool).
        // Checked before the open-drawer early return below, so the same key also closes the list.
        if(kb!=null && !modal && before==null && kb.lKey.wasPressedThisFrame && !kb.ctrlKey.isPressed)
        {Safe(()=>{if(kb.shiftKey.isPressed) ToggleMaterialList(); else ToggleStrip();}); return;}
        if(modal || drawer || encounterOpen || applyPiece!=null) {preview.Hover(null,Vector3.zero,false,Color.clear); preview.HideGhost(); ghostKey=null; return;}
        if(kb!=null)
        {
            bool ctrl=kb.ctrlKey.isPressed;
            if(ctrl && kb.zKey.wasPressedThisFrame) {Safe(()=>History(kb.shiftKey.isPressed));return;}
            if(ctrl && kb.yKey.wasPressedThisFrame) {Safe(()=>History(true));return;}
            if(tool==Tool.Select && kb.deleteKey.wasPressedThisFrame && before==null) {Safe(Remove);return;}
            if(HandleShortcuts(kb)) return;
            if(kb.f6Key.wasPressedThisFrame) {Safe(Play);return;}
        }
        var mouse=Mouse.current; if(mouse==null) return;
        var screen=PixelViewport.PointerForCanvas(canvas,mouse.position.ReadValue());
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,screen,PixelViewport.CanvasCamera(canvas),out var pt);
        var uv=Rect.PointToNormalized(viewport.rect,pt);
        bool over=viewport.rect.Contains(pt);

        // A captured box or object drag can end on the edge, even after the pointer leaves the canvas.
        bool captured=selectingArea || sampling || moving;
        // Rectangles and Shift lines keep the stroke alive outside the map so they can end on its edge.
        captured|=shapeStart.HasValue && (tool==Tool.Rect || lineStroke);
        bool space=kb!=null && kb.spaceKey.isPressed;
        bool panning=mouse.middleButton.isPressed || mouse.rightButton.isPressed || (space && mouse.leftButton.isPressed && before==null);
        bool panStarted=mouse.middleButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || (space && mouse.leftButton.wasPressedThisFrame);
        if(before!=null && mouse.leftButton.wasReleasedThisFrame)
        {
            if(captured && !shapeStart.HasValue) Safe(()=>ApplyPointer(preview.Point(uv),false));
            Safe(()=>FinishShape(preview.Point(uv)));
            Safe(()=>FinishArea(over || captured,preview.Point(uv)));
            Safe(EndStroke);
        }
        else if(!over && !captured) Safe(EndStroke);
        if(over)
        {
            if(panning && !panStarted)
                preview.Pan(Rect.PointToNormalized(viewport.rect,lastPointer),uv);
            float scroll=mouse.scroll.ReadValue().y;
            // One step per notch; high-resolution wheels and touchpads send many small events.
            if(scroll!=0 && Time.unscaledTime>=zoomReady)
            {
                StepZoom(uv,scroll>0 ? 1 : -1);
                zoomReady=Time.unscaledTime+.08f;
            }
        }
        Vector3 world=preview.Point(uv);
        preview.FeedbackOrigin=world;
        var tilemap=preview.Map(layer);
        UpdateCursorPreview(tilemap,world,over);
        if(over && tilemap!=null)
        {
            var cell=tilemap.WorldToCell(world);
            if(!cursorCell.HasValue || cursorCell.Value!=cell)
            {
                cursorLabel.text="X "+cell.x+"  Y "+cell.y; cursorCell=cell;
            }
        }
        else if(cursorCell.HasValue){cursorCell=null;cursorLabel.text="";}
        if((over || captured) && !panning)
        {
            if(mouse.leftButton.wasPressedThisFrame) {before=Doc.Serialize();lastCell=null;}
            if(before!=null && mouse.leftButton.isPressed)
                Safe(()=>ApplyPointer(world,mouse.leftButton.wasPressedThisFrame));
        }
        lastPointer=pt;
    }
    private void ApplyPointer(Vector3 world,bool first)
    {
        var map=preview.Map(layer);

        Vector3 local=SnapLocal(preview.Local(world));
        if(tool==Tool.Select) { SelectPointer(world,local,first); return; }
        if(tool==Tool.Prop && scatter) { ScatterPointer(world,first); return; }
        if(map==null) {Say("이 원본에는 해당 레이어가 없습니다.");return;}
        var cell=(Vector2Int)map.WorldToCell(world);
        if(tool is Tool.Paint or Tool.Erase or Tool.Auto) { PaintStroke(map,cell,first); return; }
        if(tool==Tool.Rect) { RectPointer(map,cell,first); return; }
        if(tool==Tool.Fill) { if(first) Fill(map,cell); return; }
        if(tool==Tool.Stamp) { StampPointer(map,cell,first); return; }
        if(!first) return;
        if(tool==Tool.Sample)
        {
            var tile=map.GetTile((Vector3Int)cell);
            if(tile!=null)
            {
                tool=tile is WorkshopTerrainTile ? Tool.Auto : Tool.Paint;
                SyncPalette();
                SelectMaterial(Mathf.Max(0,Array.FindIndex(palette,t=>t.tile==tile)));
                Draw(page);
            }
        }
        else if(tool==Tool.Door) {Doc.SetDoor(directions[dir],local);preview.MarkDoor(directions[dir],local);}
        else if(tool==Tool.Start)
        {
            if(!preview.Walkable(world))
            {Say("시작점은 걸을 수 있는 바닥에 놓으세요.");return;}
            start=local; preview.MarkStart(start); Say("시험 시작점 지정  ·  F6 플레이");
        }
        else
        {
            var entry=tool==Tool.Enemy ? enemies[enemyIndex] : props[propIndex];
            if(entry.enemy && catalog.FindRoom(Doc.template).room.GetComponent<NormalRoomEvent>()==null)
            {Say("고정 적은 일반 전투가 있는 방에 배치할 수 있습니다.");return;}
            if(entry.enemy && !preview.Walkable(world))
            {Say("적을 배치할 바닥이 필요합니다.");return;}
            string selected=Guid.NewGuid().ToString("N");
            selection.Clear();selection.Add(selected);
            Doc.pieces.Add(new WorkshopRoom.Piece {id=selected,catalog=entry.id,local=local});
            RememberQuick(new QuickMaterial(entry.enemy ? 'e' : 'p',entry.id));
            EndStroke(); Rebuild();
        }
    }
}
#endif
