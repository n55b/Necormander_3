using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Keeps the workshop catalog in step with the room prefabs so a deleted, renamed or re-created
// prefab never leaves a stale entry behind (the cause of the empty-room crashes in the workshop lists).
internal sealed class WorkshopCatalogWatcher : AssetPostprocessor
{
    private const string Rooms = "Assets/Prefabs/Map/Room Map Prefabs";
    private static bool queued;

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool touched = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
            .Any(path => path.StartsWith(Rooms + "/", StringComparison.Ordinal) &&
                         path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
        if (touched) Queue();
    }

    internal static void Queue()
    {
        if (queued) return;
        queued = true;
        EditorApplication.delayCall += () =>
        {
            queued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Queue(); return; }
            Rebuild("방 프리팹 변경");
        };
    }

    [InitializeOnLoadMethod]
    private static void CheckBeforePlay()
    {
        // The workshop saves prefabs during play and needs the catalog rebuilt at once to keep editing.
        WorkshopPrefabs.RebuildCatalog = WorkshopBuild.Bake;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode && IsStale()) Rebuild("플레이 시작 전 점검");
        };
    }

    internal static bool IsStale()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(WorkshopCatalog.AssetPath);
        if (catalog == null || catalog.rooms == null) return true;
        var current = AssetDatabase.FindAssets("t:Prefab", new[] { Rooms })
            .Where(guid =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                return prefab != null && prefab.GetComponent<RoomInstance>() != null;
            })
            .OrderBy(guid => guid, StringComparer.Ordinal);
        var known = catalog.rooms.Where(room => room != null && room.room != null)
            .Select(room => room.id).OrderBy(guid => guid, StringComparer.Ordinal);
        return !current.SequenceEqual(known) || catalog.rooms.Any(room => room == null || room.room == null);
    }

    private static void Rebuild(string reason)
    {
        try
        {
            WorkshopBuild.Bake();
            Debug.Log("[Workshop] 카탈로그 자동 갱신 · " + reason);
        }
        catch (Exception error)
        {
            Debug.LogWarning("[Workshop] 카탈로그 자동 갱신 실패 · " + error.Message);
        }
    }
}
