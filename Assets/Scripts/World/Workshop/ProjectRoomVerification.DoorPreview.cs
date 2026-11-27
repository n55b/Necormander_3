#if UNITY_EDITOR || PROJECT_CONTENT_QA
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public sealed partial class ProjectRoomVerification
{
    private sealed class DoorSample
    {
        internal readonly Dictionary<(int layer, Vector3Int delta), (TileBase tile, Sprite sprite, Matrix4x4 transform, Color color)> cells = new();
        internal readonly HashSet<Vector3Int> walkable = new();
        internal Vector3Int landing;

        internal void Compare(RoomAnchor door, Tilemap walls, Tilemap ground, Tilemap shadow, Tilemap blocked)
        {
            var maps = new[] {ground,walls,shadow,blocked};
            var mouth = walls.WorldToCell(door.transform.position);
            if (walls.WorldToCell(door.ArrivalPoint(walls))-mouth != landing)
                throw new InvalidOperationException("Workshop landing differs for "+door.direction);
            foreach (var pair in cells)
            {
                var map=maps[pair.Key.layer];
                var cell=mouth+pair.Key.delta;
                var tile=map.GetTile(cell);
                if (tile!=pair.Value.tile || (tile!=null && (map.GetTransformMatrix(cell)!=pair.Value.transform ||
                    map.GetColor(cell)!=pair.Value.color || (tile is not AnimatedTile && map.GetSprite(cell)!=pair.Value.sprite))))
                    throw new InvalidOperationException($"Workshop mismatch {door.direction} layer {pair.Key.layer} at {cell} / {pair.Key.delta}: {tile} versus {pair.Value.tile}");
            }
            // The ordinary floor opens all linked doors. Isolate the selected one for a
            // like-for-like flood fill using the authored layers at the other doorways.
            var room=door.GetComponentInParent<RoomInstance>();
            var otherCuts=new HashSet<Vector3Int>(room.anchors.Where(a=>a!=door && a.isUsed)
                .SelectMany(a=>a.PassageCells(walls,false)));
            otherCuts.ExceptWith(door.PassageCells(walls,false));
            var reached=new HashSet<Vector3Int>();
            var queue=new Queue<Vector3Int>();
            void Visit(Vector3Int at)
            {
                if(reached.Contains(at-mouth)) return;
                bool walk;
                if(otherCuts.Contains(at))
                {
                    var local=room.wallTilemap.WorldToCell(walls.GetCellCenterWorld(at));
                    walk=room.groundTilemap.HasTile(local) && !room.wallTilemap.HasTile(local) &&
                        (room.unsteppableTilemap==null || !room.unsteppableTilemap.HasTile(local));
                }
                else walk=ground.HasTile(at) && !walls.HasTile(at) && !blocked.HasTile(at);
                if(walk) { reached.Add(at-mouth); queue.Enqueue(at); }
            }
            Visit(mouth+landing);
            while(queue.Count>0)
            {
                var at=queue.Dequeue();
                Visit(at+Vector3Int.left); Visit(at+Vector3Int.up);
                Visit(at+Vector3Int.right); Visit(at+Vector3Int.down);
            }
            if(!reached.SetEquals(walkable))
                throw new InvalidOperationException($"Door {door.direction}: preview {walkable.Count} walkable, ordinary dungeon {reached.Count}");
            Debug.Log($"[Door preview] PASS {door.direction}: {cells.Count} layer cells, landing {landing}, {reached.Count} reachable tiles");
        }
    }

    private static IEnumerator ReadWorkshopDoors(WorkshopRoom doc, WorkshopCatalog.Template template, Dictionary<Vector2Int,DoorSample> samples)
    {
        if(!WorkshopTrial.Open()) throw new InvalidOperationException("Workshop entry failed.");
        yield return null;
        while(UnityNote.SceneLoader.IsLoading || Button("레벨")==null) yield return null;
        Click("레벨");
        string entry="프로젝트 편집 "+doc.id;
        for(int i=0;i<24 && Button(entry)==null;i++) Click("다음");
        Click(entry); Click("출구");
        var original=WorkshopTrial.Draft.Serialize();
        var saved=ProjectRooms.Snapshot(doc.id);
        int undoCount=WorkshopTrial.Undo.Count;
        Click("전체 보기");
        var camera=GameObject.Find("Workshop camera").GetComponent<Camera>();
        var cameraPosition=camera.transform.position;
        var cameraSize=camera.orthographicSize;
        var names=new[]{"동","북","서","남"};
        var dirs=new[]{Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down};
        var anchors=template.room.GetComponentsInChildren<RoomAnchor>();
        int first=Array.FindIndex(dirs,d=>anchors.Any(a=>a.direction==d));
        Click(names[first]); Click("연결 미리보기");
        Transform Drawing(string pane)
        {
            var frame=GameObject.Find(pane+" render").GetComponent<RawImage>().texture;
            return FindObjectsByType<Camera>(FindObjectsSortMode.None).Single(c=>c.targetTexture==frame).transform.parent;
        }
        foreach(int i in Enumerable.Range(0,4).Concat(new[]{first}))
        {
            var direction=dirs[i];
            var anchor=anchors.FirstOrDefault(a=>a.direction==direction);
            if(Button(names[i]).interactable!=(anchor!=null)) throw new InvalidOperationException("Invalid door directions");
            if(!anchor) continue;
            Click(names[i]);
            var drawing=Drawing("연결 후");
            var art=drawing.GetChild(0);
            var maps=Enumerable.Range(0,4).Select(layer=>WorkshopCatalog.Map(art,layer)).ToArray();
            var before=Drawing("연결 전").GetChild(0);
            var beforeWall=WorkshopCatalog.Map(before,1);
            var mouth=maps[1].WorldToCell(art.TransformPoint(template.room.transform.InverseTransformPoint(anchor.transform.position)));
            var overlay=drawing.GetComponentsInChildren<Tilemap>().Single(t=>t.name=="Passage overlay");
            var sample=new DoorSample();
            int gold=0;
            for(int y=-7;y<=7;y++)
            for(int x=-7;x<=7;x++)
            {
                var delta=new Vector3Int(x,y);
                var cell=mouth+delta;
                for(int layer=0;layer<4;layer++)
                {
                    var m=maps[layer];
                    sample.cells.Add((layer,delta),(m.GetTile(cell),m.GetSprite(cell),m.GetTransformMatrix(cell),m.GetColor(cell)));
                }
                if(overlay.HasTile(cell))
                {
                    if(WorkshopPreview.ArrivalColor.Equals((Color32)overlay.GetColor(cell))) { gold++; sample.landing=delta; }
                }
                if(direction==Vector2Int.right && beforeWall.HasTile(cell) && !maps[1].HasTile(cell))
                {
                    var markings=before.GetComponentsInChildren<Tilemap>().Single(m=>m.name=="Passage overlay");
                    if(!markings.HasTile(cell) || !WorkshopPreview.RemovedColor.Equals((Color32)markings.GetColor(cell)))
                        throw new InvalidOperationException("Removed wall lacks a workshop marking.");
                }
            }
            foreach(var at in overlay.cellBounds.allPositionsWithin)
                if(overlay.HasTile(at)) sample.walkable.Add(at-mouth);
            if(gold!=1 || sample.walkable.Count==0) throw new InvalidOperationException("Missing arrival or accessible cells");
            samples[direction]=sample;
        }
        Click("방 전체"); Click("출입구 확대"); Click("표시 숨기기");
        if(Drawing("연결 후").GetComponentsInChildren<Tilemap>().Any(m=>m.name=="Passage overlay"))
            throw new InvalidOperationException("Clean art view still includes an overlay.");
        Click("표시 보기");
        yield return null; yield return null;
#if UNITY_EDITOR
        if(Application.isBatchMode)
        {
            var render=PixelArtUpscaleRig.RenderCamera.targetTexture;
            var prev=RenderTexture.active;
            var capture=new Texture2D(render.width,render.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=render;
                capture.ReadPixels(new Rect(0,0,render.width,render.height),0,0);
                var folder=System.IO.Path.Combine(Application.dataPath,"../Logs/Verification");
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"workshop-door-preview.png"),capture.EncodeToPNG());
            }
            finally { RenderTexture.active=prev; Destroy(capture); }
        }
#endif
        Click("편집으로");
        var savedAfter=ProjectRooms.Snapshot(doc.id);
        if(original!=WorkshopTrial.Draft.Serialize() || undoCount!=WorkshopTrial.Undo.Count ||
            saved.Count!=savedAfter.Count || saved.Any(p=>!savedAfter.TryGetValue(p.Key,out string value) || value!=p.Value))
            throw new InvalidOperationException("Preview changed the authored document or undo history.");
        if(camera.transform.position!=cameraPosition || !Mathf.Approximately(camera.orthographicSize,cameraSize))
            throw new InvalidOperationException("Preview did not restore the editor viewport.");
        Click("메뉴");
        yield return null;
        while(UnityNote.SceneLoader.IsLoading) yield return null;
    }
}
#endif
