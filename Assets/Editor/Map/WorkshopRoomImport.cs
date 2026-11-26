using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WorkshopRoomImport
{
    private const string CatalogPath=ProjectRooms.CatalogPath;

    [MenuItem("Tools/Level Workshop/Legacy/Import room as new prefab variant")]
    private static void ImportRoom()
    {
        var nativeRooms=Path.GetFullPath(Path.Combine(Application.persistentDataPath,"..","..","Necromander","Level Workshop","LevelWorkshop","Rooms"));
        var path=EditorUtility.OpenFilePanel("제작실에서 저장한 방 선택",Directory.Exists(nativeRooms)?nativeRooms:WorkshopFiles.Folder,"json");
        if(string.IsNullOrEmpty(path)) return;
        try
        {
            var catalog=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(CatalogPath);
            var doc=WorkshopFiles.Read(path,catalog);
            var template=catalog.FindRoom(doc.template);
            string summary=$"{doc.title}\n원본 {template.title}\n타일 {doc.cells.Count}칸 · 출구 {doc.exits.Count}개 · 배치 변경 {doc.pieces.Count}개";
            if(!EditorUtility.DisplayDialog("새 방 변형 만들기",summary,"저장 위치 선택","취소")) return;
            string output=EditorUtility.SaveFilePanelInProject("새 방 프리팹",template.title+" Workshop","prefab","새 변형을 보관할 폴더를 선택하세요.");
            if(string.IsNullOrEmpty(output)) return;
            output=AssetDatabase.GenerateUniqueAssetPath(output);
            var saved=SaveVariant(doc,catalog,output);
            Selection.activeObject=saved; EditorGUIUtility.PingObject(saved);
        }
        catch(Exception e) { EditorUtility.DisplayDialog("방 가져오기",e.Message,"확인"); }
    }
    private static GameObject SaveVariant(WorkshopRoom doc,WorkshopCatalog catalog,string path)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var source=catalog.FindRoom(doc.template);
            var clone=(GameObject)PrefabUtility.InstantiatePrefab(source.room.gameObject,scene);
            var room=clone.GetComponent<RoomInstance>();
            WorkshopGeometry.ApplyToGame(doc,room,catalog,(prefab,parent)=>(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent));
            foreach(var component in clone.GetComponentsInChildren<Component>(true))
                if(component!=null && PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            var saved=PrefabUtility.SaveAsPrefabAsset(clone,path,out bool ok);
            if(!ok) throw new IOException("방 프리팹 저장에 실패했습니다.");
            return saved;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    [MenuItem("Tools/Level Workshop/Verify original room documents")]
    public static void VerifySources()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(CatalogPath);
        foreach(var template in catalog.rooms)
        {
            var draft=WorkshopRoom.Create(template);
            draft.Validate(catalog);
            var json=draft.Serialize();
            var decoded=JsonUtility.FromJson<WorkshopRoom>(json);
            decoded.Validate(catalog);
            if(decoded.Serialize()!=json || WorkshopCatalog.Map(template.room.transform,0)==null)
                throw new InvalidDataException(template.title);
            foreach(var t in template.room.GetComponentsInChildren<Transform>(true))
                if(WorkshopGeometry.IsEditablePiece(template.room.transform,t) &&
                    WorkshopCatalog.Resolve(template.room.transform,WorkshopCatalog.PathOf(template.room.transform,t))!=t)
                    throw new InvalidDataException(template.title+" / "+t.name);
        }
        if(Application.isBatchMode) VerifyVariant(catalog);
        foreach(int seed in new[]{907,613,2718}) VerifyLayout(catalog,seed);
        Debug.Log($"[Workshop] Verified {catalog.rooms.Length} source documents, {catalog.tiles.Length} tiles and {catalog.pieces.Length} placeable prefabs.");
    }
    private static void VerifyVariant(WorkshopCatalog catalog)
    {
        var source=catalog.DefaultRoom;
        var doc=WorkshopRoom.Create(source);
        var map=WorkshopCatalog.Map(source.room.transform,0);
        var cell=map.cellBounds.min;
        foreach(var candidate in map.cellBounds.allPositionsWithin)
            if(map.HasTile(candidate)) {cell=candidate;break;}
        var old=JsonUtility.FromJson<WorkshopRoom>($"{{\"version\":1,\"id\":\"{doc.id}\",\"title\":\"old\",\"template\":\"{doc.template}\",\"baseRevision\":\"{doc.baseRevision}\",\"cells\":[],\"exits\":[],\"pieces\":[]}}");
        old.Validate(catalog);
        if(old.version!=2 || old.waves.Count!=0) throw new InvalidDataException("v1 migration");
        doc.Paint(0,(Vector2Int)cell,"");
        var styledCell=cell+Vector3Int.right;
        var flip=Matrix4x4.Scale(new Vector3(-1,1,1));
        var tint=new Color32(96,144,184,255);
        doc.Paint(0,(Vector2Int)styledCell,catalog.tiles.First(t=>t.tile is UnityEngine.Tilemaps.Tile).id,doc.StyleFor(flip,tint));
        doc.waves.Add(catalog.clusters.First().id);
        doc.Validate(catalog);
        var anchor=source.room.GetComponentsInChildren<RoomAnchor>().First();
        var local=source.room.transform.InverseTransformPoint(anchor.transform.position)+Vector3.right*.25f;
        doc.SetDoor(anchor.direction,local);
        var enemy=catalog.pieces.First(p=>p.enemy);
        doc.pieces.Add(new WorkshopRoom.Piece{id=Guid.NewGuid().ToString("N"),catalog=enemy.id,local=Vector3.zero});
        string path=AssetDatabase.GenerateUniqueAssetPath("Assets/WorkshopVerification.prefab");
        try
        {
            SaveVariant(doc,catalog,path);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            var variant=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var moved=variant.GetComponentsInChildren<RoomAnchor>().First(a=>a.direction==anchor.direction);
            var group=variant.transform.Find("Workshop encounter");
            var stamped=WorkshopCatalog.Map(variant.transform,0);
            var combat=variant.GetComponent<NormalRoomEvent>();
            if(combat.AuthoredWaves.Count!=1 || combat.AuthoredWaves[0]!=catalog.clusters[0].asset ||
                stamped.GetTransformMatrix(styledCell)!=flip || !((Color32)stamped.GetColor(styledCell)).Equals(tint))
                throw new InvalidDataException("Saved variant lost an encounter or stamp setting.");
            if(PrefabUtility.GetPrefabAssetType(variant)!=PrefabAssetType.Variant ||
                WorkshopCatalog.Map(variant.transform,0).HasTile(cell) || !map.HasTile(cell) ||
                (variant.transform.InverseTransformPoint(moved.transform.position)-local).sqrMagnitude>.00001f ||
                group==null || group.childCount!=1 || variant.GetComponent<NormalRoomEvent>().UsesRandomEnemies)
                throw new InvalidDataException("Saved variant did not preserve tile, door or fixed enemy overrides.");
        }
        finally { AssetDatabase.DeleteAsset(path); }
    }
    private static void VerifyLayout(WorkshopCatalog catalog,int seed)
    {
        var planner=new DungeonLayoutPlanner(catalog.generation,catalog.roomPool);
        var mode=seed==613 ? DungeonLayoutMode.EliteArena : seed==2718 ? DungeonLayoutMode.Boss : DungeonLayoutMode.Normal;
        var random=new System.Random(seed);
        for(int i=0;i<15;i++)
            if(planner.TryPlan(mode,mode==DungeonLayoutMode.Boss?3:1,null,random,"Workshop",out _,out _)) return;
        throw new InvalidDataException($"Floor {mode}, seed {seed}");
    }
}
