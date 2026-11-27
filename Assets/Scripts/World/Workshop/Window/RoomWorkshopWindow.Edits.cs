#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Saving, undo and redo, test play and the strokes that change the room document.
public sealed partial class RoomWorkshopWindow
{
    private void Recover(string snapshot = null) => WriteRecovery(Doc.id,snapshot ?? Doc.Serialize());
    private void FinishMapEdits()
    {
        applyPiece?.Invoke();
        if(nameField!=null && nameField.isFocused) nameField.DeactivateInputField();
        EndStroke();
    }
    private void Save() => QuickSave();
    private void Play()
    {
        EndStroke();
        string warning=RoomWarnings();
        if(warning!=null && !playWarned) { playWarned=true; Say("확인 · "+warning+" · 한 번 더 누르면 그대로 시험합니다."); return; }
        playWarned=false; rememberedSelection=selection.ToArray(); rememberedStart=start; rememberedTools=(tool,layer,enemyIndex,propIndex,dir); rememberedTile=palette.Length>0 ? palette[materialIndex].id : null;
        WorkshopTrial.View=preview.Center; WorkshopTrial.Zoom=preview.Zoom;
        WorkshopTrial.Play(start);
    }
    private void History(bool redo)
    {
        EndStroke();
        var from=redo ? WorkshopTrial.Redo : WorkshopTrial.Undo;
        var to=redo ? WorkshopTrial.Undo : WorkshopTrial.Redo;
        if(from.Count==0) { Say(redo ? "다시 실행할 것이 없습니다." : "더 되돌릴 것이 없습니다."); return; }
        string restored=from.Peek();
        var room=JsonUtility.FromJson<WorkshopRoom>(restored);
        var current=Doc.Serialize();
        WriteRecovery(room.id,restored);
        from.Pop(); to.Push(current); WorkshopTrial.Draft=room;
        dirty=true; Rebuild();
        preview.FlashDifference(JsonUtility.FromJson<WorkshopRoom>(current),room);
    }
    private void RememberView()
    {
        WorkshopTrial.View=page==Page.Passage ? passagePreviousCenter : preview.Center;
        WorkshopTrial.Zoom=page==Page.Passage ? passagePreviousZoom : preview.Zoom;
    }
    private void Rebuild()
    {
        // Removed objects pop away and new ones hop in (편집 효과); the snapshot is null while effects are off.
        var shown=preview?.SnapshotPieces();
        RememberView();
        OpenMap(false,palette.Length>0 ? palette[materialIndex].id : null);
        preview.ReactToRebuild(shown);
    }
    private void EndStroke()
    {
        fillRegion=null; ghostKey=null;
        if(before==null) return;
        string old=before; before=null; lastCell=null; bool dropped=moving; moving=false;
        sampling=false; selectingArea=false; preview?.HideArea();
        var updated=Doc.Serialize();
        if(old==updated) return;
        WorkshopTrial.Undo.Push(old); WorkshopTrial.Redo.Clear(); dirty=true; playWarned=false;
        Recover(updated); UpdateDetail();
        if(dropped && preview!=null) foreach(string key in selection) preview.Land(key);
    }
    private void Remove()
    {
        if(selection.Count==0) return;
        before=Doc.Serialize();
        foreach(string key in selection) EditPiece(key).removed=true;
        selection.Clear(); EndStroke(); Rebuild();
    }
    private WorkshopRoom.Piece EditPiece(string key)
    {
        var p=Doc.pieces.Find(e=>e.id==key);
        if(p==null)
        {
            p=new WorkshopRoom.Piece {id=key,source=key};
            Doc.pieces.Add(p);
        }
        return p;
    }
}
#endif
