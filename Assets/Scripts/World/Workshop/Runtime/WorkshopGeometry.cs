using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class WorkshopGeometry
{
    public static bool IsEditablePiece(Transform root, Transform t)
    {
        if (t == null || t == root || t.parent == null || !t.IsChildOf(root)) return false;
        // "Workshop encounter" holds fixed enemies placed by the workshop and saved into a prefab.
        return t.parent.name is "DecorateObject" or "TrapObject" or "Tutorial Enemies" or "Workshop encounter";
    }

    // Every anchor after the first one facing the same way.
    public static List<RoomAnchor> ExtraAnchors(Transform root)
    {
        var seen = new HashSet<Vector2Int>();
        return root.GetComponentsInChildren<RoomAnchor>().Where(anchor => !seen.Add(anchor.direction)).ToList();
    }

    public static void ApplyTiles(WorkshopRoom doc, Transform root, WorkshopCatalog library)
    {
        if (doc.clearTiles)
            for (int layer = 0; layer < WorkshopCatalog.Layers.Length; layer++)
                WorkshopCatalog.Map(root, layer)?.ClearAllTiles();
        foreach (var group in doc.cells.GroupBy(c => c.layer))
        {
            var map = WorkshopCatalog.Map(root,group.Key);
            if (map == null) throw new System.IO.InvalidDataException("방에 타일 레이어가 없습니다.");
            var cells = group.ToArray();
            map.SetTiles(cells.Select(c => (Vector3Int)c.at).ToArray(),cells.Select(c => library.Tile(c.tile)).ToArray());
            var styled=cells.Where(c=>c.style>0).Select(c=>{
                var style=doc.styles[c.style-1];
                var at=(Vector3Int)c.at;
                var tile=library.Tile(c.tile);
                return new TileChangeData(at,tile,style.color,tile is Tile ? style.matrix : map.GetTransformMatrix(at));
            }).ToArray();
            if(styled.Length>0) map.SetTiles(styled,true);
            if (library.tiles.Any(entry => entry.tile is WorkshopTerrainTile && map.ContainsTile(entry.tile)))
                map.RefreshAllTiles();
        }
    }

    public static void ApplyToGame(WorkshopRoom doc, RoomInstance room, WorkshopCatalog library,
        System.Func<GameObject,Transform,GameObject> createPiece=null)
    {
        ApplyTiles(doc,room.transform,library);
        foreach (var e in doc.exits)
        {
            var anchor = room.GetComponentsInChildren<RoomAnchor>().FirstOrDefault(a => a.direction == e.direction);
            if (anchor == null)
            {
                var go = new GameObject("Workshop door");
                go.transform.SetParent(room.transform,false);
                anchor = go.AddComponent<RoomAnchor>();
                anchor.direction = e.direction;
            }
            anchor.transform.position = room.transform.TransformPoint(e.local);
        }
        // Rooms collect active anchors when they initialize, so a disabled extra never becomes a door.
        if (doc.singleDoors)
            foreach (var extra in ExtraAnchors(room.transform)) extra.gameObject.SetActive(false);
        Transform enemies = library.FindRoom(doc.template).tutorialOrder>0 ? room.transform.Find("Tutorial Enemies") : null;
        foreach (var p in doc.pieces)
        {
            if (!string.IsNullOrEmpty(p.source))
            {
                var target = WorkshopCatalog.Resolve(room.transform,p.source);
                if (p.removed) target.gameObject.SetActive(false);
                else target.position = room.transform.TransformPoint(p.local);
                continue;
            }
            if (p.removed) continue;
            var item = library.FindPiece(p.catalog);
            Transform parent = room.transform;
            if (item.enemy)
            {
                if (enemies == null)
                {
                    enemies = new GameObject("Workshop encounter").transform;
                    enemies.SetParent(room.transform,false);
                    enemies.gameObject.SetActive(false);
                }
                parent = enemies;
            }
            var go = createPiece!=null ? createPiece(item.prefab,parent) : Object.Instantiate(item.prefab,parent);
            go.transform.position = room.transform.TransformPoint(p.local);
        }
        var combat = room.GetComponent<NormalRoomEvent>();
        if(doc.reward>0) room.SetAuthoredReward((RoomInstance.NormalRewardType)(doc.reward-1));
        if(doc.waves.Count>0 && combat!=null)
            combat.ConfigureWaves(doc.waves.Select(id=>library.FindCluster(id).asset).ToArray());
        if (enemies != null && combat != null) combat.SetWorkshopEnemies(enemies);
    }
}
