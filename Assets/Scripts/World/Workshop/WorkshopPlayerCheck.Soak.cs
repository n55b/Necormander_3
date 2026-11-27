#if LEVEL_WORKSHOP_QA
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public sealed partial class WorkshopPlayerCheck
{
    private IEnumerator CheckRepeatedTrials()
    {
        const int warmRounds = 20;
        bool snapshots = Environment.GetCommandLineArgs().Contains("-workshopHeapSnapshots");
        yield return Click("레벨 제작실");
        yield return Click("레벨");
        yield return Click("새 방 만들기");
        yield return Wait(() => WorkshopTrial.Draft != null && GameObject.Find("Map") != null);
        string document = WorkshopTrial.Draft.Serialize();
        string csv = Path.Combine(output, "rounds.csv");
        File.WriteAllText(csv, "round,enterMs,returnMs,cameras,tilemaps,previewTargets,sparkBuffers,hitSubscribers,pooledEffects,unityAllocatedBytes,managedUsedBytes\n");
        string memory = Path.Combine(output, "retained.csv");
        File.WriteAllText(memory, "round,managedBytesAfterGC,materials,meshes,renderTextures\n");
        File.WriteAllText(Path.Combine(output, "machine.txt"), $"{Application.unityVersion}\n{SystemInfo.processorType}\n{SystemInfo.graphicsDeviceName}\n{SystemInfo.graphicsDeviceType}\n{Screen.width}x{Screen.height}\n");
        int[] first = null;
        var enter = new double[warmRounds];
        var back = new double[warmRounds];
        var mapWindows = new WeakReference[warmRounds + 1];
        for (int round = 0; round <= warmRounds; round++)
        {
            double start = 0;
            yield return Click("플레이", () => start = Time.realtimeSinceStartupAsDouble);
            Step("battle ready / round " + round);
            yield return Wait(() => SceneManager.GetActiveScene().name == "BattleScene" && GameManager.Instance != null &&
                GameManager.Instance.IsPlayerReady && !UnityNote.SceneLoader.IsLoading);
            double enterMs = (Time.realtimeSinceStartupAsDouble - start) * 1000;
            Require(WorkshopTrial.Active && GameManager.Instance.mapGenerator.AllRooms.Count > 0, "playable trial " + round);
            mapWindows[round] = ObserveTrialInput();
            yield return Click("편집으로  F6", () => start = Time.realtimeSinceStartupAsDouble);
            Step("workbench ready / round " + round);
            yield return Wait(() => SceneManager.GetActiveScene().name == WorkshopTrial.SceneName && !WorkshopTrial.Active &&
                !UnityNote.SceneLoader.IsLoading && GameObject.Find("Workshop camera") != null && GameObject.Find("플레이") != null);
            double returnMs = (Time.realtimeSinceStartupAsDouble - start) * 1000;
            yield return new WaitForEndOfFrame();
            int[] counts = {
                CountSceneObjects<Camera>(), CountSceneObjects<Tilemap>(),
                Resources.FindObjectsOfTypeAll<RenderTexture>().Count(rt => rt.name == "Workshop map" && rt.IsCreated()),
                FindObjectsByType<WorldSparkField>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(field => field.HasParticleBuffer),
                DamageEventBus.HitSubscriberCount,
                SceneEffectPool<FeedbackBurst>.RetainedCount + SceneEffectPool<ImpactFeedback>.RetainedCount + SceneEffectPool<PooledHitEffect>.RetainedCount
            };
            File.AppendAllText(csv, FormattableString.Invariant($"{round},{enterMs:F2},{returnMs:F2},{string.Join(",", counts)},{Profiler.GetTotalAllocatedMemoryLong()},{Profiler.GetMonoUsedSizeLong()}\n"));
            first ??= counts;
            Require(counts.SequenceEqual(first) && counts[2] == 1 && counts[5] == 0, "workbench resource counts stable " + round);
            Require(document == WorkshopTrial.Draft.Serialize(), "same draft " + round);
            Require(ReferenceEquals(GameManager.Instance, null), "battle manager released " + round);
            Require(ReferenceEquals(InventoryManager.Instance, null), "battle inventory released " + round);
            if (round % 10 == 0)
            {
                // Collection and enumeration are outside the input-to-ready timing window.
                long retained = GC.GetTotalMemory(true);
                File.AppendAllText(memory, $"{round},{retained},{Resources.FindObjectsOfTypeAll<Material>().Length},{Resources.FindObjectsOfTypeAll<Mesh>().Length},{Resources.FindObjectsOfTypeAll<RenderTexture>().Length}\n");
                // Exclude the most recent scene, which can still be on the engine's coroutine stack.
                Require(!mapWindows.Take(round).Any(reference => reference.IsAlive), "older map windows collected " + round);
                if (snapshots && (round == 0 || round == warmRounds))
                {
                    bool? saved = null;
                    Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(Path.Combine(output, $"workbench-{round}.snap"),
                        (_, ok) => saved = ok, Unity.Profiling.Memory.CaptureFlags.ManagedObjects | Unity.Profiling.Memory.CaptureFlags.NativeObjects);
                    yield return Wait(() => saved.HasValue);
                    Require(saved == true, "workbench heap snapshot " + round);
                }
            }
            if (round > 0)
            {
                enter[round - 1] = enterMs;
                back[round - 1] = returnMs;
            }
        }
        Array.Sort(enter);
        Array.Sort(back);
        int p95 = Mathf.CeilToInt(warmRounds * .95f) - 1;
        File.WriteAllText(Path.Combine(output, "summary.txt"), FormattableString.Invariant($"warmRounds={warmRounds}\nenterP95Ms={enter[p95]:F2}\nreturnP95Ms={back[p95]:F2}\n"));
        if (!snapshots) Require(enter[p95] <= 1000 && back[p95] <= 1000, "warm transitions within one second at p95");
        yield return CheckLargestRoom();
        Finish(true);
    }

    private WeakReference ObserveTrialInput()
    {
        var map = FindFirstObjectByType<MapUIManager>();
        Require(map != null && map.isActiveAndEnabled, "active battle map");
        var input = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();
        if (input != null && input.user.valid) InputUser.PerformPairingWithDevice(mouse, input.user);
        return new WeakReference(map);
    }

    private IEnumerator CheckLargestRoom()
    {
        yield return Click("Legacy");
        // Compare populated ground cells rather than serialized tilemap bounds, which can contain empty margins.
        var largest = WorkshopTrial.Library.rooms.Select(room => {
            var map = WorkshopCatalog.Map(room.room.transform, 0);
            int tiles = map == null ? 0 : map.GetTilesRangeCount(map.cellBounds.min, map.cellBounds.max - Vector3Int.one);
            return (room, tiles);
        }).OrderByDescending(item => item.tiles).First();
        string id = largest.room.id;
        Button FindRow() => FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name.EndsWith(" " + id, StringComparison.Ordinal));
        int pages = (WorkshopTrial.Library.rooms.Length + 5) / 6;
        for (int i = 1; FindRow() == null && i < pages; i++) yield return Click("다음");
        Require(FindRow() != null, "largest room on Legacy shelf");
        yield return Click(FindRow().name);
        Require(WorkshopTrial.Draft.template == id, "opened largest ground map");
        string original = WorkshopTrial.Draft.Serialize();
        // A room opens at 1x, which shows only 30x17 cells; the 32x32 stamp needs the whole room in view.
        yield return Click("전체 보기");
        var camera = GameObject.Find("Workshop camera").GetComponent<Camera>();
        var ground = WorkshopCatalog.Map(camera.transform.parent, 0);
        var center = ground.WorldToCell(camera.ViewportToWorldPoint(new Vector3(.5f,.5f,30)));
        Vector2 Point(Vector3Int cell)
        {
            var view = camera.WorldToViewportPoint(ground.GetCellCenterWorld(cell));
            if (view.x <= 0 || view.x >= 1 || view.y <= 0 || view.y >= 1) throw new InvalidOperationException("Stamp outside map viewport");
            return new Vector2(7 + view.x * WorkshopPreview.Width, 360 - 54 - (1 - view.y) * WorkshopPreview.Height);
        }
        var lo = center - new Vector3Int(15,15,0);
        var hi = lo + new Vector3Int(31,31,0);
        int expected = 0;
        Vector3Int occupied = default;
        foreach (var cell in new BoundsInt(lo, new Vector3Int(32,32,1)).allPositionsWithin)
            if (ground.HasTile(cell)) { occupied=cell; expected++; }
        Require(expected > 0, "visible maximum stamp contains tiles");
        yield return Click("칠하기");
        yield return CheckBrushFrame(camera, ground, center, Point(center));
        yield return Click("도장");
        yield return Drag(Point(lo), Point(hi));
        var at = Point(center);
        double start = Time.realtimeSinceStartupAsDouble;
        yield return Press(at);
        double maxStampMs = (Time.realtimeSinceStartupAsDouble - start) * 1000;
        Require(WorkshopTrial.Draft.cells.Count == expected, "maximum 32x32 stamp preserves occupied cells");
        var single = Point(occupied);
        var targets = Enumerable.Range(0,50).Select(i => Point(center + new Vector3Int(i % 10 - 5,i / 10 - 2,0))).ToArray();
        yield return Click("실행 취소");
        yield return Click("다시 고르기");
        yield return Drag(single, single);
        start = Time.realtimeSinceStartupAsDouble;
        foreach (var target in targets) yield return Press(target);
        double paintSeconds = Time.realtimeSinceStartupAsDouble - start;
        Require(WorkshopTrial.Draft.cells.Count == 50 && WorkshopTrial.Undo.Count == 50, "50 separate stamp placements");
        start = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < 50; i++) yield return Click("실행 취소");
        double undoSeconds = Time.realtimeSinceStartupAsDouble - start;
        Require(WorkshopTrial.Draft.Serialize() == original && WorkshopTrial.Redo.Count == 50, "50 undos restore original room");
        Require(File.ReadAllText(WorkshopFiles.FileFor(WorkshopTrial.Draft.id) + ".draft") == original, "automatic recovery matches final undo");
        File.WriteAllText(Path.Combine(output, "editing.txt"), FormattableString.Invariant($"room={largest.room.title}\ngroundTiles={largest.tiles}\nmaxStampCells={expected}\nmaxStampInputMs={maxStampMs:F2}\nplacements50Seconds={paintSeconds:F3}\nundos50Seconds={undoSeconds:F3}\n"));
    }

    private IEnumerator CheckBrushFrame(Camera camera, Tilemap ground, Vector3Int cell, Vector2 logical)
    {
        var viewport = camera.WorldToViewportPoint(ground.GetCellCenterWorld(cell));
        var frame = camera.targetTexture;
        var pixel = new Rect(Mathf.Floor(viewport.x * frame.width), Mathf.Floor(viewport.y * frame.height), 1, 1);
        var sample = new Texture2D(1, 1, TextureFormat.RGB24, false);
        Color32 Read()
        {
            var previous = RenderTexture.active;
            RenderTexture.active = frame;
            sample.ReadPixels(pixel, 0, 0);
            RenderTexture.active = previous;
            return sample.GetPixel(0, 0);
        }
        yield return new WaitForEndOfFrame();
        var before = Read();
        int sent = Time.frameCount;
        InputSystem.QueueStateEvent(mouse, new MouseState { position=PixelViewport.GameToScreenPoint(logical,PixelArtUpscaleRig.Output.DisplayRect) });
        yield return null;
        yield return new WaitForEndOfFrame();
        var after = Read();
        Destroy(sample);
        Require(Time.frameCount == sent + 1 && !after.Equals(before), "brush visible on next rendered map frame");
    }

    private static int CountSceneObjects<T>() where T : Component =>
        FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
}
#endif
