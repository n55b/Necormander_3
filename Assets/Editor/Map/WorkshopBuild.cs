using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class WorkshopBuild
{
    private const string PreviousStartScene = "Workshop.PreviousPlayModeStartScene";
    private const string PlayMenu = "Tools/제작실/플레이 모드에서 열기";
    private const string CatalogPath=ProjectRooms.CatalogPath;
    internal const string Rooms="Assets/Prefabs/Map/Room Map Prefabs";

    [InitializeOnLoadMethod]
    private static void RestoreStartSceneAfterLaunch()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode && state != PlayModeStateChange.EnteredEditMode) return;
            string previous = SessionState.GetString(PreviousStartScene, "unset");
            if (previous == "unset") return;
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null :
                AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(previous));
            SessionState.EraseString(PreviousStartScene);
        };
    }

    [MenuItem(PlayMenu, true)]
    internal static bool CanOpenInPlayMode() =>
        !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
        (EditorApplication.isPlaying ? EditorApplication.isPlayingOrWillChangePlaymode &&
            !UnityNote.SceneLoader.IsLoading && SceneManager.GetActiveScene().path != WorkshopTrial.ScenePath :
            !EditorApplication.isPlayingOrWillChangePlaymode);

    [MenuItem(PlayMenu)]
    internal static void OpenInPlayMode()
    {
        if (!CanOpenInPlayMode()) return;
        if (EditorApplication.isPlaying)
        {
            WorkshopTrial.Open();
            return;
        }
        var workshop = AssetDatabase.LoadAssetAtPath<SceneAsset>(WorkshopTrial.ScenePath);
        if (workshop == null) { Debug.LogError("제작실 씬을 찾을 수 없습니다. " + WorkshopTrial.ScenePath); return; }
        var previous = EditorSceneManager.playModeStartScene;
        SessionState.SetString(PreviousStartScene, previous == null ? string.Empty :
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(previous)));
        EditorSceneManager.playModeStartScene = workshop;
        EditorApplication.EnterPlaymode();
    }
    [MenuItem("Tools/제작실/원본 카탈로그 다시 굽기")]
    public static void Bake()
    {
        var library=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(CatalogPath);
        if (library==null)
        {
            library=ScriptableObject.CreateInstance<WorkshopCatalog>();
            AssetDatabase.CreateAsset(library,CatalogPath);
        }
        library.generation=AssetDatabase.LoadAssetAtPath<MapGenerationDataSO>("Assets/SOData/Map/MapGenerationData.asset");
        library.playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab");
        library.dialogueTable=AssetDatabase.LoadAssetAtPath<DialogueTableSO>("Assets/SOData/Dialogue/DialogueTable.asset");
        library.editableFonts=new[] {("Small","Galmuri9"),("Body","Galmuri11"),("Micro","Galmuri7"),
            ("Emphasis","Galmuri11-Bold"),("Large","Galmuri14")}.Select(pair=>new WorkshopCatalog.EditableFont {
                bitmap=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Resources/PixelFonts/"+pair.Item1+".asset"),
                source=AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/"+pair.Item2+".ttf")
            }).ToArray();
        var pool=AssetDatabase.LoadAssetAtPath<RoomPrefabDataSO>("Assets/SOData/Map/RoomPrefabData.asset");
        library.roomPool=pool;
        var manager=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameManager.prefab").GetComponentInChildren<DataManager>(true);
        library.elites=MapGenerator.EligibleFloorElites(manager).ToArray();
        library.clusters=manager.ENEMY_CLUSTERS.Where(c=>c!=null && c.enemies!=null &&
            c.enemies.Exists(e=>e.enemyData!=null && e.count>0)).Distinct()
            .OrderBy(c=>c.name,StringComparer.Ordinal).Select(c=>new WorkshopCatalog.Cluster {
                id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c)), asset=c
            }).ToArray();
        var templates=new List<WorkshopCatalog.Template>();
        var tileSet=new HashSet<TileBase>();
        foreach (var id in AssetDatabase.FindAssets("t:Prefab",new[]{Rooms}).OrderBy(AssetDatabase.GUIDToAssetPath,StringComparer.Ordinal))
        {
            string path=AssetDatabase.GUIDToAssetPath(id);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var room=prefab.GetComponent<RoomInstance>();
            if (room==null) continue;
            var entry=pool.roomEntries.FirstOrDefault(e=>e.prefabs.Contains(prefab));
            templates.Add(new WorkshopCatalog.Template { id=id,title=prefab.name,room=room,
                revision=Revision(path),inPool=entry!=null,
                type=entry?.roomType ?? room.roomType, tutorialOrder=library.generation.tutorialRooms.IndexOf(prefab)+1 });
            foreach (var map in prefab.GetComponentsInChildren<Tilemap>(true))
            {
                var tiles=new TileBase[map.GetUsedTilesCount()]; map.GetUsedTilesNonAlloc(tiles);
                foreach(var tile in tiles) if(tile!=null) tileSet.Add(tile);
            }
        }
        var village=EditorSceneManager.OpenPreviewScene("Assets/Scenes/VillageScene.unity");
        try
        {
            library.villageActors=village.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<NPCBase>(true))
                .Select(npc=>new SerializedObject(npc).FindProperty("workshopId").stringValue)
                .Where(id=>!string.IsNullOrEmpty(id)).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
        }
        finally { EditorSceneManager.ClosePreviewScene(village); }
        library.rooms=templates.ToArray();
        foreach (var id in AssetDatabase.FindAssets("t:WorkshopTerrainTile"))
            tileSet.Add(AssetDatabase.LoadAssetAtPath<WorkshopTerrainTile>(AssetDatabase.GUIDToAssetPath(id)));
        library.tiles=tileSet.Select(t=>{
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t,out string guid,out long local);
            return new WorkshopCatalog.TileEntry { id=guid+"-"+local,tile=t };
        }).OrderBy(t=>t.id,StringComparer.Ordinal).ToArray();
        library.pieces=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs/Enemy","Assets/Prefabs/Map/Objects","Assets/Prefabs/Traps"})
            .OrderBy(AssetDatabase.GUIDToAssetPath,StringComparer.Ordinal).Select(id=>{
                string path=AssetDatabase.GUIDToAssetPath(id);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                // 허수아비는 전투 없는 시험장에 바로 서 있어야 하므로 소품으로 둔다(적 배치물은 전투 방의 비활성 묶음에만 들어간다).
                bool dummy=prefab.name==WorkshopCatalog.DummyName;
                return new WorkshopCatalog.Piece { id=id,prefab=prefab,enemy=path.StartsWith("Assets/Prefabs/Enemy/") && !dummy };
            }).Where(p=>!p.enemy || (p.prefab.name.StartsWith("Monster ") && p.prefab.GetComponentInChildren<BaseEntity>(true)!=null))
            .OrderBy(p=>p.prefab.name==WorkshopCatalog.DummyName)   // 소품 팔레트 맨 뒤: 기본 소품(props[0])과 검사 기대가 바뀌지 않게
            .ToArray();
        library.assets=WorkshopCatalog.BakeAssets();
        library.pouchPreview=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PouchUI.prefab");
        // 자동 재기준이 이 굽기를 부르므로 사용자가 저장하지 않은 다른 에셋까지 저장하지 않는다.
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
        if (!File.Exists(WorkshopTrial.ScenePath))
        {
            if (Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var root=new GameObject("Level Workshop"); SceneManager.MoveGameObjectToScene(root,scene);
            root.AddComponent<RoomWorkshopWindow>().catalog=library;
            var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(events,scene);
            EditorSceneManager.SaveScene(scene,WorkshopTrial.ScenePath); EditorSceneManager.CloseScene(scene,true);
            if(previous.IsValid()) SceneManager.SetActiveScene(previous);
        }
        Debug.Log($"[Workshop] {templates.Count} templates, {library.tiles.Length} tiles, {library.pieces.Length} pieces");
    }
    // Hierarchy paths in a draft refer to this exact authored base and its nested prefabs.
    internal static string Revision(string roomPath)
    {
        using var hash=SHA256.Create();
        foreach (var dependency in AssetDatabase.GetDependencies(roomPath).Where(p=>p.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p,StringComparer.Ordinal))
        {
            // Git checks prefabs out as LF or CRLF depending on the machine. Hash the LF form so every checkout agrees.
            var bytes=System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(dependency).Replace("\r\n","\n"));
            hash.TransformBlock(bytes,0,bytes.Length,null,0);
        }
        hash.TransformFinalBlock(Array.Empty<byte>(),0,0);
        return BitConverter.ToString(hash.Hash).Replace("-","");
    }
    [MenuItem("Tools/제작실/Windows 제작판 빌드")]
    public static void BuildWindows() => Build(false);
    public static void BuildVerificationPlayer() => Build(true);
    public static void BuildMemoryCheckPlayer() => Build(true, true);

    private static void Build(bool verification, bool memoryCheck = false)
    {
        Bake();
        var options=new BuildPlayerOptions {
            scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).Append(WorkshopTrial.ScenePath).Distinct().ToArray(),
            locationPathName="Builds/LevelWorkshop/"+(memoryCheck?"MemoryQA":verification?"QA":"Windows")+"/NecromanderWorkshop.exe",target=BuildTarget.StandaloneWindows64,
            options=memoryCheck ? BuildOptions.Development : BuildOptions.None,
            // URP's per-camera sampler cache keeps a key for every preview camera ever rendered.
            extraScriptingDefines=verification ? new[]{"LEVEL_WORKSHOP","LEVEL_WORKSHOP_QA","UNIVERSAL_PROFILING_NO_ALLOC"}
                : new[]{"LEVEL_WORKSHOP","UNIVERSAL_PROFILING_NO_ALLOC"}
        };
        string product=PlayerSettings.productName, company=PlayerSettings.companyName;
        try
        {
            PlayerSettings.companyName="Necromander";
            PlayerSettings.productName=verification ? "Level Workshop QA" : "Level Workshop";
            if(BuildPipeline.BuildPlayer(options).summary.result!=BuildResult.Succeeded)
                throw new InvalidOperationException("Workshop player build failed.");
        }
        finally
        {
            PlayerSettings.productName=product; PlayerSettings.companyName=company;
        }
    }
}
