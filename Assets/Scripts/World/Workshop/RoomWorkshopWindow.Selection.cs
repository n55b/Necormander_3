using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

public sealed partial class RoomWorkshopWindow
{
    private readonly HashSet<string> selection = new();
    private readonly Dictionary<string,Vector3> dragOrigins = new();
    private Vector3 dragStart, areaStart;
    private Vector2Int sampleStart;
    private bool selectingArea, sampling, dragStarted;
    private TMP_InputField stampName;
    private readonly struct StampCell
    {
        public readonly Vector2Int Offset;
        public readonly TileChangeData Tile;
        public readonly string Id;

        public StampCell(Vector2Int offset, TileChangeData tile, string id)
            => (Offset, Tile, Id) = (offset, tile, id);
    }
    private static readonly List<StampCell> stamp = new();

    private void RefreshSelection()
    {
        selection.IntersectWith(preview.Pieces.Where(p=>p.Value.gameObject.activeInHierarchy).Select(p=>p.Key));
        preview.HighlightSelection(selection);
        UpdateDetail();
    }

    private void SelectPointer(Vector3 world, Vector3 local, bool first)
    {
        if(first)
        {
            dragStarted=false;
            bool additive=Keyboard.current?.shiftKey.isPressed==true;
            bool cycle=Keyboard.current?.altKey.isPressed==true;
            string nearest = preview.Pick(world,cycle && selection.Count==1 ? selection.First() : null);
            if(nearest==null)
            {
                if(!additive) selection.Clear();
                areaStart=world;
                selectingArea=true;
            }
            else
            {
                if(additive && selection.Contains(nearest)) selection.Remove(nearest);
                else
                {
                    if(!additive && !selection.Contains(nearest)) selection.Clear();
                    selection.Add(nearest);
                }
                moving=selection.Contains(nearest);
                dragStart=local;
                dragOrigins.Clear();
                foreach(var piece in preview.Pieces)
                    if(selection.Contains(piece.Key)) dragOrigins.Add(piece.Key,preview.Local(piece.Value.position));
            }
            RefreshSelection();
        }
        var origin=selectingArea ? areaStart : preview.World(dragStart);
        if (!dragStarted && Vector2.Distance(world,origin)>=preview.DragThreshold) dragStarted=true;
        if (!dragStarted) return;
        if(selectingArea) preview.Area(areaStart,world);
        else if(moving && !first)
        {
            var delta=local-dragStart;
            if(dragOrigins.Values.Any(home=>Mathf.Abs(home.x+delta.x)>512 || Mathf.Abs(home.y+delta.y)>512))
                return;
            foreach(var piece in preview.Pieces)
            {
                if(!dragOrigins.TryGetValue(piece.Key,out var home)) continue;
                var next=home+delta;
                if((piece.Value.position-preview.World(next)).sqrMagnitude<.0000001f) continue;
                piece.Value.position=preview.World(next);
                EditPiece(piece.Key).local=next;
            }
            preview.PositionOutlines();
        }
    }

    private void StampPointer(Tilemap map, Vector2Int cell, bool first)
    {
        if(first && stamp.Count==0) { sampling=true; sampleStart=cell; }
        if(sampling)
        {
            var min=Vector2Int.Min(sampleStart,cell);
            var max=Vector2Int.Max(sampleStart,cell)+Vector2Int.one;
            preview.Area(map.CellToWorld((Vector3Int)min),map.CellToWorld((Vector3Int)max));
            return;
        }
        if(!first) return;
        foreach(var sample in stamp)
        {
            var at=cell+sample.Offset;
            if(Math.Abs(at.x)>1024 || Math.Abs(at.y)>1024) { Say("도장이 편집 범위를 벗어납니다."); return; }
        }
        var positions=new Vector3Int[stamp.Count];
        var tiles=new TileBase[stamp.Count];
        for(int i=0;i<stamp.Count;i++)
        {
            positions[i]=(Vector3Int)(cell+stamp[i].Offset);
            tiles[i]=stamp[i].Tile.tile;
        }
        map.SetTiles(positions,tiles);
        var changes=new TileChangeData[stamp.Count];
        for(int i=0;i<stamp.Count;i++)
        {
            var sample=stamp[i];
            var at=cell+sample.Offset;
            var tile=sample.Tile;
            var matrix=tile.tile is Tile ? tile.transform : map.GetTransformMatrix(positions[i]);
            Doc.Paint(layer,at,sample.Id,Doc.StyleFor(matrix,tile.color));
            changes[i]=new TileChangeData(positions[i],tile.tile,tile.color,matrix);
        }
        map.SetTiles(changes,true);
        if (layer<=1) foreach (var at in positions) WorkshopTerrainTile.RefreshNeighbors(map,at);
    }

    private void DrawStampLibrary()
    {
        WorkshopUI.Frame(content,"Stamp library",7,54,WorkshopPreview.Width,WorkshopPreview.Height);
        WorkshopUI.Label(content,"도장 보관함",18,64,220,22,15);
        Btn("닫기",416,65,57,22,()=>{drawer=false;Draw(page);});
        if(stamp.Count>0)
        {
            var field=WorkshopUI.Box(content,"도장 이름",18,98,282,24,WorkshopUI.ButtonColor);
            var input=stampName=field.gameObject.AddComponent<TMP_InputField>();
            input.textViewport=field.rectTransform;
            input.textComponent=WorkshopUI.Label(field.transform,"",5,0,272,24,12);
            input.characterLimit=64;
            input.SetTextWithoutNotify(layerNames[layer]+" "+(stamp.Max(s=>s.Offset.x)+1)+"×"+(stamp.Max(s=>s.Offset.y)+1));
            Btn("새 도장 저장",308,98,165,24,()=>{
                var doc=new WorkshopStamp {title=input.text.Trim(),layer=layer};
                foreach(var s in stamp) doc.cells.Add(new WorkshopStamp.Sample {
                    offset=s.Offset,tile=s.Id,matrix=s.Tile.transform,color=s.Tile.color
                });
                doc.Save(catalog); palettePage=0; Draw(page); Say("보관함에 도장을 저장했습니다.");
            });
        }
        Directory.CreateDirectory(WorkshopStamp.Folder);
        var files=Directory.GetFiles(WorkshopStamp.Folder,"*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        const int perPage=8;
        int pages=Mathf.Max(1,(files.Length+perPage-1)/perPage);
        palettePage=Mathf.Clamp(palettePage,0,pages-1);
        for(int i=palettePage*perPage;i<Math.Min(files.Length,(palettePage+1)*perPage);i++)
        {
            int x=18+i%2*229,y=140+i%perPage/2*32;
            try
            {
                var saved=WorkshopStamp.Read(files[i],catalog);
                Btn(saved.title+" · "+saved.cells.Count+"칸",x,y,223,27,()=>{
                    if(preview.Map(saved.layer)==null) {Say("이 방에는 도장 레이어가 없습니다.");return;}
                    layer=saved.layer; SyncPalette(); stamp.Clear();
                    foreach(var cell in saved.cells)
                        stamp.Add(new StampCell(cell.offset,new TileChangeData((Vector3Int)cell.offset,
                            catalog.Tile(cell.tile),cell.color,cell.matrix),cell.tile));
                    drawer=false;Draw(page);Say(saved.title+" · 지도에서 배치할 칸을 선택하세요.");
                });
            }
            catch(Exception ex) { WorkshopUI.Label(content,ex.Message,x,y,223,27); }
        }
        if(files.Length==0) WorkshopUI.Label(content,"지도에서 타일을 고른 뒤 도장을 보관하세요.",18,145,445,26);
        if(pages>1)
        {
            Btn("이전",18,283,61,21,()=>{palettePage--;Draw(page);}).interactable=palettePage>0;
            WorkshopUI.Label(content,(palettePage+1)+" / "+pages,91,283,64,21);
            Btn("다음",157,283,61,21,()=>{palettePage++;Draw(page);}).interactable=palettePage+1<pages;
        }
    }

    private void FinishArea(bool over,Vector3 world)
    {
        preview.HideArea();
        if(selectingArea && over && dragStarted)
        {
            Vector3 min=Vector3.Min(areaStart,world), max=Vector3.Max(areaStart,world);
            foreach(var piece in preview.Pieces)
            {
                var bounds=preview.PieceBounds(piece.Key);
                if(piece.Value.gameObject.activeInHierarchy && bounds.max.x>=min.x && bounds.min.x<=max.x && bounds.max.y>=min.y && bounds.min.y<=max.y)
                    selection.Add(piece.Key);
            }
            RefreshSelection();
        }
        selectingArea=false;
        if(!sampling) return;
        sampling=false;
        if(!over) return;
        var map=preview.Map(layer);
        var end=(Vector2Int)map.WorldToCell(world);
        var lo=Vector2Int.Min(sampleStart,end);
        var hi=Vector2Int.Max(sampleStart,end);
        if(hi.x-lo.x>=32 || hi.y-lo.y>=32) { Say("도장은 최대 32×32칸입니다."); return; }
        for(int y=lo.y;y<=hi.y;y++)
        for(int x=lo.x;x<=hi.x;x++)
        {
            var cell=new Vector3Int(x,y,0);
            var tile=map.GetTile(cell);
            if(tile==null) continue;
            var data=new TileChangeData(cell,tile,map.GetColor(cell),map.GetTransformMatrix(cell));
            stamp.Add(new StampCell((Vector2Int)cell-lo,data,catalog.Id(tile)));
        }
        Draw(page);
        Say(stamp.Count==0 ? "선택한 범위에 타일이 없습니다." : "도장의 왼쪽 아래 칸을 눌러 배치하세요.");
    }
}
