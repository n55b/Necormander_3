#if NECRO_RENDER_BENCH
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

// Compiled only into the isolated benchmark player by PixelBenchmarkBuild.
public sealed class PixelRenderBenchmark : MonoBehaviour
{
    internal sealed class GenerationSample : IDisposable
    {
        private readonly string name;
        private readonly long start = Stopwatch.GetTimestamp();
        private readonly int frame = Time.frameCount;
        private static int peakFrame = -2;
        private static double peakMs;
        private static string peakName;
        internal GenerationSample(string name) { this.name = name; }
        public void Dispose()
        {
            double ms = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            if (frame != Time.frameCount || ms < 3 || (peakFrame == frame && ms <= peakMs)) return;
            peakFrame = frame;
            peakMs = ms;
            peakName = name;
        }
        internal static string Recent => Time.frameCount - peakFrame <= 1 ? $" work={peakName} {peakMs:F2}ms" : "";
    }

    [Serializable] private class Sample
    {
        public string name;
        public long startQpc,endQpc;
        public int frames,particleRequests,emitted,dropped,raycasts,areaDrops;
        public long allocatedBytes95=-1, allocatedBytesMax=-1;
        public string allocationScope;
        public float p50,p95,p99,worst,firstFrame,medianGPU=-1,gpu95=-1,cpu95=-1;
        public int gpuSamples, above16ms, above33ms, visibleAbove33ms, coveredAbove33ms;
        public float worstVisible, worstCovered;
    }
    [Serializable] private class Report
    {
        public string device,api,cpu;
        public bool allocationCounterVerified;
        public int width,height,seed=907;
        public List<string> sceneResources=new();
        public List<string> slowFrames=new();
        public long ticksPerSecond;
        public List<Sample> sections = new();
    }
    private readonly Report report = new();
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private string output;
    private int seconds;
    private int battleLoadedFrame;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args=Environment.GetCommandLineArgs();
        int i=Array.IndexOf(args,"-pixelBench");
        if(i<0 || i+1>=args.Length) return;
        var go=new GameObject("Pixel render benchmark");
        DontDestroyOnLoad(go);
        var probe=go.AddComponent<PixelRenderBenchmark>();
        probe.output=args[i+1];
        int j=Array.IndexOf(args,"-benchSeconds");
        probe.seconds=j>=0 && j+1<args.Length && int.TryParse(args[j+1],out int duration) ? Mathf.Clamp(duration,2,120) : 60;
    }
#if PIXEL_GPU_TIMER
    [System.Runtime.InteropServices.DllImport("PixelGpuTimer")] private static extern IntPtr GetFrameMarker();
    [System.Runtime.InteropServices.DllImport("PixelGpuTimer")] private static extern int ReadGpuMicroseconds();
    private IntPtr marker;
    private IEnumerator MeasureGpuFrames()
    {
        var end = new WaitForEndOfFrame();
        while (marker != IntPtr.Zero)
        {
            GL.IssuePluginEvent(marker, 1);
            yield return end;
            GL.IssuePluginEvent(marker, 2);
            yield return null;
        }
    }
#endif
    private IEnumerator Start()
    {
#if PIXEL_GPU_TIMER
        marker=GetFrameMarker();
        StartCoroutine(MeasureGpuFrames());
#endif
        Application.runInBackground=true;
        QualitySettings.vSyncCount=0;
        Application.targetFrameRate=-1;
        report.ticksPerSecond=Stopwatch.Frequency;
        long beforeSample=GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(new byte[8192]);
        report.allocationCounterVerified=GC.GetAllocatedBytesForCurrentThread()-beforeSample>=8192;
        report.device=SystemInfo.graphicsDeviceName; report.api=SystemInfo.graphicsDeviceType.ToString(); report.cpu=SystemInfo.processorType;
        report.width=Screen.width;report.height=Screen.height;
        UnityEngine.Random.InitState(report.seed);
        yield return null;
        while(UnityNote.SceneLoader.IsLoading) yield return null;
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-floorSoak") >= 0)
        {
            yield return MeasureFloors();
            Application.Quit();
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-flowProbe") >= 0)
        {
            yield return VerifyFlows();
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Application.Quit();
            yield break;
        }
        UnityNote.SceneLoader.Load("VillageScene");
        yield return null;
        while(UnityNote.SceneLoader.IsLoading || GameManager.Instance==null || !GameManager.Instance.IsPlayerReady) yield return null;
        var player=GameManager.Instance.PLAYERCONTROLLER;
        bool windowsOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-windowOnly") >= 0;
        if (windowsOnly || Array.IndexOf(Environment.GetCommandLineArgs(), "-windowQA") >= 0)
            yield return VerifyWindows();
        if (windowsOnly)
        {
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Application.Quit();
            yield break;
        }
        CharacterHealth victim=null;
        Vector3 center=Vector3.zero;
        void BindTrainingRoom()
        {
            player=GameManager.Instance.PLAYERCONTROLLER;
            victim=GameObject.Find("Enemy Scarecrow").GetComponentInChildren<CharacterHealth>();
            center=victim.transform.position;
            player.GetComponent<Rigidbody2D>().position=center+Vector3.left*1.35f;
            var playerHealth=player.GetComponentInChildren<CharacterHealth>();
            if(playerHealth!=null) playerHealth.Invincible=true;
            var inputs=player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if(inputs!=null) inputs.DeactivateInput();
        }
        BindTrainingRoom();
        var library=Resources.Load<ParticleLibrary>("WorldAtmosphere/Particles");
        var initialQuality=library.density; var initialBackend=library.simulation;
        var catalog=PolishFeedback.Catalog;
        bool enabledBefore=catalog.enabled;
        var atmosphere=WorldAtmosphere.Current;
        bool atmosphereWasEnabled = atmosphere != null && atmosphere.enabled;
        var camera=CameraSettingsSO.Instance;
        float mist=camera.mistStrength,glow=camera.bloomStrength;
        float nextHit=0, nextBurst=0;
        Action<int> combat=i=>
        {
            if(Time.unscaledTime>=nextHit)
            {
                nextHit=Time.unscaledTime+.26f;
                if(victim!=null && victim.MaxHP>0) victim.SetHP(victim.MaxHP);
                victim.TakeDamage(new DamageInfo(16,DamageType.Physical,player.gameObject,category:DamageCategory.BasicAttack));
            }
            if(Time.unscaledTime<nextBurst) return;
            nextBurst=Time.unscaledTime+.045f;
            WorldSparkField.Emit(WorldSparkField.Kind.Projectile,center,Vector2.left,12,(uint)i+1);
            WorldSparkField.Emit(WorldSparkField.Kind.EnemyHit,center,Vector2.right,12,(uint)i+2);
        };
        try
        {
            bool crowdProbe=Array.IndexOf(Environment.GetCommandLineArgs(),"-crowdProbe")>=0;
            bool crowdMatrix=Array.IndexOf(Environment.GetCommandLineArgs(),"-crowdMatrix")>=0;
            if(!crowdProbe && !crowdMatrix)
            {
                yield return Measure("first-hit",3,combat);
                catalog.enabled=false;camera.mistStrength=0;camera.bloomStrength=0;
                if (atmosphere != null) atmosphere.enabled = false;
                yield return Measure("effects-disabled",seconds,combat);
                catalog.enabled=enabledBefore;
                yield return Measure("atmosphere-off",Mathf.Min(seconds,10),combat);
                camera.mistStrength=mist;camera.bloomStrength=glow;
                if (atmosphere != null) atmosphere.enabled = atmosphereWasEnabled;
                yield return Measure("atmosphere-on",Mathf.Min(seconds,10),combat);
                foreach(ParticleLibrary.Simulation backend in Enum.GetValues(typeof(ParticleLibrary.Simulation)))
                    foreach(ParticleLibrary.Density density in Enum.GetValues(typeof(ParticleLibrary.Density)))
                    {
                        UnityNote.SceneLoader.Load("VillageScene");
                        yield return null;
                        while(UnityNote.SceneLoader.IsLoading || GameManager.Instance==null || !GameManager.Instance.IsPlayerReady) yield return null;
                        BindTrainingRoom();
                        UnityEngine.Random.InitState(report.seed);
                        nextHit=0;nextBurst=0;
                        library.simulation=backend;library.density=density;
                        yield return Measure(backend+"-"+density,seconds,combat);
                    }
            }
            int destinationFloor=1;
            var transitionStart=Stopwatch.GetTimestamp();
            void SetSeed(UnityEngine.SceneManagement.Scene scene,UnityEngine.SceneManagement.LoadSceneMode mode)
            {
                if(scene.name!="BattleScene") return;
                UnityEngine.SceneManagement.SceneManager.sceneLoaded-=SetSeed;
                GameManager.Instance.currentFloor=destinationFloor;
                GameManager.Instance.debugEliteArena=false;
                GameManager.Instance.testMode_DisableAutoBattle=true;
                TutorialFlow.IsRunning=false;
                MapGenerator.Instance.SetNextLayoutSeed(report.seed);
            }
            UnityEngine.SceneManagement.SceneManager.sceneLoaded+=SetSeed;
            UnityNote.SceneLoader.Load("BattleScene");
            yield return null;
            while(UnityNote.SceneLoader.IsLoading || MapGenerator.Instance==null || !MapGenerator.Instance.IsMapGenerationCompleted) yield return null;
            report.sections.Add(new Sample {name="dungeon-entry",startQpc=transitionStart,endQpc=Stopwatch.GetTimestamp()});
            yield return Measure("dungeon",3,i=>{});
            CaptureResources("dungeon");
            player=GameManager.Instance.PLAYERCONTROLLER;
            var combatRoom=MapGenerator.Instance.AllRooms.First(r=>r.roomType==RoomType.Normal);
            var waves=combatRoom.GetComponent<NormalRoomEvent>();
            player.GetComponent<Rigidbody2D>().position=combatRoom.transform.position+(Vector3)combatRoom.centerOffset;
            var roomHealth=player.GetComponentInChildren<CharacterHealth>();
            if(roomHealth!=null) roomHealth.Invincible=true;
            combatRoom.ForceEnter();
            combatRoom.StartCombatEvent();
            var rosterField=typeof(NormalRoomEvent).GetField("_activeEnemies",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var enemies=rosterField.GetValue(waves) as List<GameObject>;
            float ready=Time.realtimeSinceStartup+12;
            while(enemies.Count==0 && Time.realtimeSinceStartup<ready) yield return null;
            if (crowdMatrix)
            {
                yield return CrowdMatrix(enemies, player);
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
                Application.Quit();
                yield break;
            }
            int maxActors=0,confirmed=0;
            float strikeAt=0;
            yield return Measure("dungeon-crowd",seconds,i=>
            {
                maxActors=Mathf.Max(maxActors,enemies.Count);
                if(enemies.Count==0 || Time.unscaledTime<strikeAt) return;
                strikeAt=Time.unscaledTime+.25f;
                var enemy=enemies[confirmed%enemies.Count];
                if(enemy==null) return;
                var hp=enemy.GetComponentInChildren<CharacterHealth>();
                if(hp==null || hp.IsDead || hp.Invincible) return;
                hp.SetHP(hp.MaxHP);
                var contact=new DamageInfo(Mathf.Max(1,hp.MaxHP*.16f),DamageType.Fixed,player.gameObject,category:DamageCategory.BasicAttack,bypassGuard:true);
                contact.feedbackComboFinish=confirmed%4==3;
                hp.TakeDamage(contact);
                if(hp.CurHP<hp.MaxHP) confirmed++;
            });
            report.sceneResources.Add($"active dungeon encounter peakActors={maxActors} successfulHits={confirmed}");
            if(crowdProbe) { Application.Quit(); yield break; }
            var mapSettings=Resources.FindObjectsOfTypeAll<MapGenerationDataSO>().First();
            int authoredBossFloor=mapSettings.floorBosses.First(e=>e!=null && e.boss!=null).floor;
            foreach(int floor in new[]{1,authoredBossFloor})
            {
                destinationFloor=floor;
                UnityEngine.SceneManagement.SceneManager.sceneLoaded+=SetSeed;
                transitionStart=Stopwatch.GetTimestamp();
                UnityNote.SceneLoader.Load("BattleScene");
                yield return null;
                while(UnityNote.SceneLoader.IsLoading || MapGenerator.Instance==null || !MapGenerator.Instance.IsMapGenerationCompleted) yield return null;
                bool bossFloor=floor==authoredBossFloor;
                report.sections.Add(new Sample{name=bossFloor?"boss-entry":"dungeon-reentry",startQpc=transitionStart,endQpc=Stopwatch.GetTimestamp()});
                yield return Measure(bossFloor?"boss-room":"dungeon-warm",2,i=>{});
                CaptureResources(bossFloor?"boss":"dungeon-warm");
            }
            var room=MapGenerator.Instance.AllRooms.First(r=>r.roomType==RoomType.Boss);
            var bossEvent=room.GetComponent<BossRoomEvent>();
            player=GameManager.Instance.PLAYERCONTROLLER;
            player.GetComponent<Rigidbody2D>().position=room.transform.position+(Vector3)room.centerOffset;
            room.ForceEnter();
            bossEvent.OnPlayerEnter(room);
            float strikeAfter=0;
            bool clear=false;
            int strikes=0;
            string lastBoss="awaiting spawn";
            var actorField=typeof(BossRoomEvent).GetField("_activeBoss",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            void Clear() => clear=true;
            bossEvent.OnBossCombatClear.AddListener(Clear);
            try
            {
                yield return Measure("boss-finisher",28,i=>
                {
                    if(clear || Time.time<strikeAfter) return;
                    strikeAfter=Time.time+.5f;
                    var boss=actorField.GetValue(bossEvent) as GameObject;
                    if(boss==null) return;
                    var health=boss.GetComponentInChildren<CharacterHealth>();
                    lastBoss=boss.name;
                    if(health==null || health.Invincible || health.IsDead) return;
                    strikes++;
                    var hit=new DamageInfo(health.MaxHP*8,DamageType.Fixed,player.gameObject,category:DamageCategory.BasicAttack,bypassGuard:true);
                    hit.feedbackComboFinish=true; health.TakeDamage(hit);
                },()=>clear);
            }
            finally {if(bossEvent!=null) bossEvent.OnBossCombatClear.RemoveListener(Clear);}
            report.sceneResources.Add($"boss clear={clear} strikes={strikes} last={lastBoss} timeScale={Time.timeScale}");
        }
        finally
        {
            if (atmosphere != null) atmosphere.enabled = atmosphereWasEnabled;
            library.density=initialQuality; library.simulation=initialBackend; catalog.enabled=enabledBefore;
            camera.mistStrength=mist;camera.bloomStrength=glow;
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
        }
        Application.Quit();
    }
    private void CaptureResources(string name)
    {
        var frame=PixelArtUpscaleRig.Output.Frame;
        int targets=Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t=>t.name==frame.name && t.IsCreated());
        int masks=Resources.FindObjectsOfTypeAll<Texture2D>().Count(t=>t.name=="Room floor mask");
        int fields=FindObjectsByType<WorldSparkField>(FindObjectsSortMode.None).Length;
        report.sceneResources.Add($"{name} seed={MapGenerator.Instance.LastLayoutSeed} frameTargets={targets} floorMasks={masks} particleFields={fields}");
    }
    private IEnumerator MeasureFloors()
    {
        var arguments = Environment.GetCommandLineArgs();
        bool collectBeforeSnapshot = Array.IndexOf(arguments, "-naturalGc") < 0;
        int index = Array.IndexOf(arguments, "-floorCount");
        int floorCount = index >= 0 && index + 1 < arguments.Length && int.TryParse(arguments[index + 1], out int requested)
            ? Mathf.Clamp(requested, 1, 100) : 6;
        bool firstBattle = true;
        bool checkPouch = floorCount > 1 && Array.IndexOf(arguments, "-areaBurst") >= 0;
        SaveData previousPouch = null;
        SaveData startingPouch = null;
        string floorJournal = floorCount > 1 ? output + ".floors.jsonl" : null;
        if (floorJournal != null) File.WriteAllText(floorJournal, string.Empty);
        void Seed(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name != "BattleScene") return;
            battleLoadedFrame = Time.frameCount;
            TutorialFlow.IsRunning = false;
            if (firstBattle) GameManager.Instance.currentFloor = 1;
            firstBattle = false;
            MapGenerator.Instance.SetNextLayoutSeed(report.seed);
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += Seed;
        try
        {
            var navIds = new HashSet<int>();
            var roomLifetimes = new List<WeakReference>();
            for (int floor = 1; floor <= floorCount; floor++)
            {
                if (floor > 1 && Array.IndexOf(arguments, "-areaBurst") >= 0)
                    yield return MeasureFloorTravel(floor);
                else
                {
                    bool started = floor == 1 ? UnityNote.SceneLoader.Load("BattleScene") : GameManager.Instance.GoToNextFloor();
                    if (!started) throw new InvalidOperationException($"Floor {floor} transition rejected");
                }
                yield return null;
                while (UnityNote.SceneLoader.IsLoading || MapGenerator.Instance == null ||
                       !MapGenerator.Instance.IsMapGenerationCompleted || !GameManager.Instance.IsPlayerReady) yield return null;
                if (GameManager.Instance.currentFloor != floor) throw new InvalidOperationException($"Expected floor {floor}");
                if (previousPouch != null)
                {
                    var restored = CapturePouch();
                    bool matches = previousPouch.gold == restored.gold &&
                        previousPouch.pouchItemNames.Where(item => !string.IsNullOrEmpty(item))
                            .SequenceEqual(restored.pouchItemNames.Where(item => !string.IsNullOrEmpty(item)));
                    report.sceneResources.Add($"floor-{floor} checkpointRestored={matches} gold={restored.gold} items={restored.pouchItemNames.Count}");
                }
                if (checkPouch && floor == 1) startingPouch = CapturePouch();
                yield return Measure($"floor-{floor}", seconds, _ => {});
                var nav = Resources.FindObjectsOfTypeAll<UnityEngine.AI.NavMeshData>();
                int oldNav = nav.Count(data => navIds.Contains(data.GetInstanceID()));
                var surface = FindFirstObjectByType<NavMeshPlus.Components.NavMeshSurface>();
                if (surface != null && surface.navMeshData != null) navIds.Add(surface.navMeshData.GetInstanceID());
                var persistent = gameObject.scene.GetRootGameObjects();
                report.sceneResources.Add($"floor={floor} rooms={MapGenerator.Instance.AllRooms.Count} nav={nav.Length} oldRuntimeNav={oldNav} " +
                    $"persistentRoots={persistent.Length} worldEntries={WorldDepthOrder.Entries.Count} agents={CharacterStatus.ActiveEnemies.Count} " +
                    $"materials={Resources.FindObjectsOfTypeAll<Material>().Length} meshes={Resources.FindObjectsOfTypeAll<Mesh>().Length} " +
                    $"textures={Resources.FindObjectsOfTypeAll<Texture2D>().Length} allocated={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()} " +
                    $"{(collectBeforeSnapshot ? "managedLive" : "managedOccupied")}={GC.GetTotalMemory(collectBeforeSnapshot)}");
                if (floorCount > 1)
                {
                    int retained = 0;
                    // Allow one floor for deferred destruction and temporary references on the native stack.
                    for (int previous = 0; previous < roomLifetimes.Count - 1; previous++)
                        if (roomLifetimes[previous].IsAlive) retained++;
                    bool handoffLoaded = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Floor handoff").isLoaded;
                    report.sceneResources.Add($"floor-{floor} oldRoomWrappers={retained} handoffLoaded={handoffLoaded}");
                    roomLifetimes.Add(new WeakReference(MapGenerator.Instance.AllRooms[0]));
                }
                var casters = FindObjectsByType<UnityEngine.Rendering.Universal.ShadowCaster2D>(FindObjectsSortMode.None);
                report.sceneResources.Add($"floor-{floor} activeCasters={casters.Count(c => c.enabled)} totalCasters={casters.Length}");
                CaptureResources($"floor-{floor}");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-clearRooms") >= 0)
                {
                    if (floor == 1) yield return CheckMovingLight(casters);
                    yield return ClearFloorEncounter(floor, startingPouch);
                }
                if (checkPouch && floor < floorCount) previousPouch = CapturePouch();
                if (floorJournal == null) File.WriteAllText(output, JsonUtility.ToJson(report, true));
                else
                {
                    // Release diagnostic strings and samples before the next floor's live-heap snapshot.
                    File.AppendAllText(floorJournal, JsonUtility.ToJson(report) + Environment.NewLine);
                    report.sceneResources.Clear();
                    report.sections.Clear();
                    report.slowFrames.Clear();
                }
            }
            if (floorJournal != null)
            {
                foreach (string line in File.ReadLines(floorJournal))
                {
                    var floorReport = JsonUtility.FromJson<Report>(line);
                    report.sceneResources.AddRange(floorReport.sceneResources);
                    report.sections.AddRange(floorReport.sections);
                    report.slowFrames.AddRange(floorReport.slowFrames);
                }
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
                File.Delete(floorJournal);
            }
        }
        finally { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Seed; }
    }

    private static SaveData CapturePouch()
    {
        var checkpoint = new SaveData { gold = GameManager.Instance.inventoryManager.GOLD };
        ItemPouch.Instance.SaveToData(checkpoint);
        return checkpoint;
    }

    private IEnumerator MeasureFloorTravel(int floor)
    {
        int fadeId = Shader.PropertyToID("_ScenePaletteFade");
        var frames = new List<float>(2048);
        var row = new Sample { name = $"travel-{floor-1}-{floor}" };
        float lastFade = Shader.GetGlobalFloat(fadeId);
        string worstPhase = null;
        long previous = row.startQpc = Stopwatch.GetTimestamp();
        if (!GameManager.Instance.GoToNextFloor()) throw new InvalidOperationException($"Floor {floor} travel rejected");
        do
        {
            float progress = MapGenerator.Instance != null ? MapGenerator.Instance.GenerationProgress : -1f;
            string phase = UnityNote.SceneLoader.FloorIsCovered && UnityNote.SceneLoader.TravelStage == "unused asset release" ? "unused asset release" :
                GameManager.Instance == null || GameManager.Instance.currentFloor != floor ? UnityNote.SceneLoader.TravelStage :
                MapGenerator.Instance == null ? "scene activation" :
                !MapGenerator.Instance.IsMapGenerationCompleted ? MapGenerator.Instance.GenerationProgress switch
                {
                    < .1f => "layout/instantiate", < .4f => "tile transfer", < .6f => "doors",
                    < .7f => "special rooms", < .8f => "wall collision", < .9f => "navigation",
                    < .92f => "room binding", < .95f => "terrain shadows", < .97f => "tile animations", _ => "floor mask"
                } :
                !GameManager.Instance.IsPlayerReady ? "room preparation" : "reveal";
            yield return null;
            long now = Stopwatch.GetTimestamp();
            float ms = (float)((now-previous)*1000d/Stopwatch.Frequency);
            float fade = Shader.GetGlobalFloat(fadeId);
            if (lastFade >= .999f && fade >= .999f) row.worstCovered = Mathf.Max(row.worstCovered, ms);
            else row.worstVisible = Mathf.Max(row.worstVisible, ms);
            if (ms > row.worst) { row.worst = ms; worstPhase = phase; }
            if (ms > 16.7f)
            {
                row.above16ms++;
                float next = MapGenerator.Instance != null ? MapGenerator.Instance.GenerationProgress : -1f;
                report.sceneResources.Add($"{row.name} frame={frames.Count} phase={phase} ms={ms:F2} map={progress:F2}->{next:F2} framesSinceSceneLoad={Time.frameCount - battleLoadedFrame}{GenerationSample.Recent}");
            }
            if (ms > 33.3f) row.above33ms++;
            frames.Add(ms);
            lastFade = fade;
            previous = now;
        } while (UnityNote.SceneLoader.IsLoading);
        row.endQpc = previous;
        row.frames = frames.Count;
        row.firstFrame = frames[0];
        frames.Sort();
        row.p95 = frames[Mathf.CeilToInt(frames.Count*.95f)-1];
        report.sections.Add(row);
        report.sceneResources.Add($"{row.name} worst phase={worstPhase}");
    }

    private IEnumerator CheckMovingLight(UnityEngine.Rendering.Universal.ShadowCaster2D[] casters)
    {
        var caster = Array.Find(casters, c => !c.enabled && c.shapePath != null && c.shapePath.Length >= 3);
        if (caster == null) yield break;
        var lampObject = new GameObject("Floor verification lamp");
        try
        {
            lampObject.transform.position = caster.transform.TransformPoint(caster.shapePath[0]);
            var lamp = lampObject.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
            lamp.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Point;
            lamp.pointLightOuterRadius = 3f;
            lamp.shadowsEnabled = true;
            lampObject.AddComponent<GroundShadowLight>();
            yield return null; yield return null;
            bool near = caster.enabled;
            lampObject.transform.position += Vector3.right * 1000f;
            yield return null; yield return null;
            report.sceneResources.Add($"movingLamp restoredNear={near} suspendedFar={!caster.enabled}");
        }
        finally { Destroy(lampObject); }
    }

    private IEnumerator ClearFloorEncounter(int floor, SaveData startingPouch)
    {
        var rooms = MapGenerator.Instance.AllRooms;
        var room = rooms.FirstOrDefault(r => r.roomType == RoomType.Normal)
            ?? rooms.First(r => r.roomType == RoomType.Boss);
        var player = GameManager.Instance.PLAYERCONTROLLER;
        player.GetComponentInChildren<CharacterHealth>().Invincible = true;
        EnemyClusterSO stressWave = null;
        var encounter = room.GetComponent<NormalRoomEvent>();
        bool area = Array.IndexOf(Environment.GetCommandLineArgs(), "-areaBurst") >= 0;
        string prefix = floor == 1 ? "" : $"f{floor}-";
        // The save contract preserves the starter. Match floor one while testing every reward across travel.
        if (startingPouch != null && floor > 1) ItemPouch.Instance.LoadFromData(startingPouch);
        if ((floor == 1 || area) && Array.IndexOf(Environment.GetCommandLineArgs(), "-roomBurst") >= 0)
        {
            var unit = GameManager.Instance.dataManager.ENEMY_CLUSTERS.Where(c => c != null)
                .SelectMany(c => c.enemies).First(e => e.enemyData != null);
            unit.count = 64;
            stressWave = ScriptableObject.CreateInstance<EnemyClusterSO>();
            stressWave.enemies.Add(unit);
            encounter.ConfigureWaves(new[] { stressWave });
        }
        player.GetComponent<Rigidbody2D>().position = room.transform.position + (Vector3)room.centerOffset;
        room.ForceEnter();
        if (stressWave != null)
        {
            int previousCount = 0, peak = 0;
            yield return Measure(prefix + "authored-room-64", 15, _ =>
            {
                int current = encounter.SpawnedEnemyCount;
                peak = Mathf.Max(peak, current - previousCount);
                previousCount = current;
            }, () => previousCount >= 64);
            report.sceneResources.Add($"{prefix}authoredBurst count={previousCount} peakPerFrame={peak} budget={encounter.SpawnBudget}");
        }
        if (stressWave != null && Array.IndexOf(Environment.GetCommandLineArgs(), "-areaBurst") >= 0)
        {
            yield return MeasureAreaAttack(room, player, prefix);
            Destroy(stressWave);
            yield break;
        }
        float deadline = Time.realtimeSinceStartup + 35f;
        int hits = 0;
        var targets = new List<CharacterStatus>();
        while (!room.isCleared && Time.realtimeSinceStartup < deadline)
        {
            // Damage can remove enemies from the active registry during iteration.
            targets.Clear();
            targets.AddRange(CharacterStatus.ActiveEnemies);
            foreach (var target in targets)
            {
                if (target == null) continue;
                var hp = target.GetComponent<CharacterHealth>();
                if (hp == null || hp.IsDead || hp.Invincible) continue;
                hp.TakeDamage(new DamageInfo(hp.MaxHP * 8, DamageType.Fixed, player.gameObject,
                    category: DamageCategory.BasicAttack, bypassGuard: true));
                hits++;
            }
            yield return new WaitForSecondsRealtime(.25f);
        }
        report.sceneResources.Add($"clearedFloor={floor} clear={room.isCleared} hits={hits}");
        if (stressWave != null) Destroy(stressWave);
    }

    private IEnumerator MeasureAreaAttack(RoomInstance room, PlayerController player, string prefix)
    {
        var targets = CharacterStatus.ActiveEnemies.Select(s => s.GetComponent<CharacterHealth>()).ToArray();
        var status = player.GetComponentInChildren<CharacterStatus>();
        var prefab = status.BenchmarkExplosion;
        Vector3 center = player.transform.position;
        int startingGold = GameManager.Instance.inventoryManager.GOLD;
        var startingItems = new HashSet<GroundItem>(FindObjectsByType<GroundItem>(FindObjectsSortMode.None));
        var sparks = WorldSparkField.Current;
        var manager = FloatingTextManager.Instance;
        foreach (var section in new[] { "area-hit", "area-repeat-hit", "area-trigger-hit", "area-kill-with-drops" })
        {
            bool lethal = section == "area-kill-with-drops";
            bool trigger = section == "area-trigger-hit";
            var previousHP = targets.Select(h => h.CurHP).ToArray();
            int contacts = 0, immediate = 0, numbers = 0, emitted = 0, drops = 0;
            int labelsBefore = 0, sparksBefore = 0, visibleBefore = 0, lastContact = -1;
            int targetsWithSparks = 0;
            int flashes = 0, flightsBefore = 0, flights = 0;
            double pulseMs = 0, dropMs = 0;
            var box = Instantiate(prefab, center, Quaternion.identity);
            box.destroyOnFinish = false;
            box.SetManualHitOnly(true);
            box.GetComponent<CircleCollider2D>().radius = 14;
            box.Init(new DamageInfo(lethal ? 100000 : 1, DamageType.Fixed, player.gameObject,
                category: DamageCategory.Skill, bypassGuard: true), Layers.EnemyMask, isAlly: true,
                onHitEnemy: target =>
                {
                    contacts++;
                    lastContact = Time.frameCount;
                    if (target.Stat.Visual.LastHitFlashFrame == lastContact) flashes++;
                    flights = FinisherFlight.FlightsStarted - flightsBefore;
                    immediate = manager.DamageLabelsShown - labelsBefore;
                    emitted = sparks.Emitted - sparksBefore;
                    targetsWithSparks = sparks.VisibleEnemyHits - visibleBefore;
                });
            for (int i = 0; i < targets.Length && section != "area-repeat-hit"; i++)
            {
                var actor = targets[i].GetComponentInParent<BaseEntity>();
                var pt = center + new Vector3(Mathf.Cos(i*2.4f), Mathf.Sin(i*2.4f)) * (1 + i%4 * .35f);
                if (actor.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var nav) && nav.isActiveAndEnabled && nav.isOnNavMesh)
                    nav.Warp(pt);
                actor.transform.position = pt;
                if (actor.TryGetComponent<Rigidbody2D>(out var body)) body.position = pt;
            }
            Physics2D.SyncTransforms();
            // Let the navigation/physics step for the test's repositioning finish before timing the attack.
            yield return new WaitForSecondsRealtime(.15f);
            yield return Measure(prefix + section, 2, frame =>
            {
                if (lastContact >= 0 && Time.frameCount == lastContact + 2)
                    numbers = manager.DamageLabelsShown - labelsBefore;
                if (frame != 0) return;
                labelsBefore = manager.DamageLabelsShown;
                flightsBefore = FinisherFlight.FlightsStarted;
                sparksBefore = sparks.Emitted;
                visibleBefore = sparks.VisibleEnemyHits;
                if (trigger) { box.SetManualHitOnly(false); return; }
                long start = Stopwatch.GetTimestamp();
                box.PulseDamageOverlapping();
                long afterHit = Stopwatch.GetTimestamp();
                pulseMs = (afterHit - start) * 1000.0 / Stopwatch.Frequency;
                if (lethal) drops = RewardProcessor.DropItems(GameManager.Instance.dataManager, center, 8);
                dropMs = (Stopwatch.GetTimestamp() - afterHit) * 1000.0 / Stopwatch.Frequency;
            });
            int affected = 0;
            for (int i = 0; i < targets.Length; i++)
                if (lethal ? targets[i] == null || targets[i].IsDead : targets[i] != null && targets[i].CurHP < previousHP[i])
                    affected++;
            report.sceneResources.Add($"{prefix}{section} targets={targets.Length} contacts={contacts} affected={affected} immediate={immediate} numbers={numbers} sparks={emitted} drops={drops}");
            report.sceneResources.Add($"{prefix}{section} sparkTargets={targetsWithSparks} pulseMs={pulseMs:F2} dropMs={dropMs:F2}");
            report.sceneResources.Add($"{prefix}{section} hitFlashes={flashes} finishingFlights={flights}");
            Destroy(box.gameObject);
        }
        float deadline = Time.realtimeSinceStartup + 10;
        while (!room.isCleared && Time.realtimeSinceStartup < deadline) yield return null;
        var chest = FindFirstObjectByType<RoomRewardBox>();
        if (chest != null) chest.Interact(player.gameObject);
        while (chest != null && Time.realtimeSinceStartup < deadline) yield return null;
        var items = FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
            .Where(item => !startingItems.Contains(item) && item.IsAvailable).ToArray();
        int picked = 0;
        foreach (var item in items)
        {
            var data = item.Item;
            int before = ItemPouch.Instance.CountOf(data);
            if (item.Interact(player.gameObject) && !item.Interact(player.gameObject) && ItemPouch.Instance.CountOf(data) == before + 1 && !item.IsAvailable) picked++;
        }
        report.sceneResources.Add($"{prefix}area clear={room.isCleared} items={items.Length} picked={picked} gold={GameManager.Instance.inventoryManager.GOLD-startingGold}");
    }

    private IEnumerator VerifyFlows()
    {
        var scenes = new[] {"VillageScene","BattleScene","BattleScene","VillageScene","BattleScene"};
        var labels = new[] {"menu-village","first-dungeon","floor-portal","reward-return","dungeon-revisit"};
        for (int index=0; index<scenes.Length; index++)
        {
            string sceneName=scenes[index], label=labels[index];
            long start=Stopwatch.GetTimestamp(), previous=start;
            var sample = new Sample {name=label,startQpc=start};
            var frames = new List<float>();
            bool activated=false, mapDone=false, ready=false;
            int fadeId = Shader.PropertyToID("_ScenePaletteFade");
            float previousFade = Shader.GetGlobalFloat(fadeId);
            string phase="fade and stream", worstPhase=phase;
            void Mark(string stage)
            {
                phase=stage;
                report.sceneResources.Add($"{label} {stage} at {(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency:F2} ms");
            }
            void Loaded(UnityEngine.SceneManagement.Scene scene,UnityEngine.SceneManagement.LoadSceneMode mode)
            {
                if(scene.name!=sceneName) return;
                activated=true;
                Mark("scene active");
                if(sceneName=="BattleScene" && MapGenerator.Instance!=null)
                {
                    TutorialFlow.IsRunning=false;
                    GameManager.Instance.currentFloor=1;
                    MapGenerator.Instance.SetNextLayoutSeed(report.seed);
                }
            }
            UnityEngine.SceneManagement.SceneManager.sceneLoaded+=Loaded;
            try
            {
                if(!UnityNote.SceneLoader.Load(sceneName,index==2 ? FadeSignal.층이동 : FadeSignal.씬전환))
                    throw new InvalidOperationException("Transition rejected: "+label);
                do
                {
                    yield return null;
                    long now=Stopwatch.GetTimestamp();
                    float wall=(float)((now-previous)*1000d/Stopwatch.Frequency);
                    previous=now;
                    frames.Add(wall);
                    if(wall>sample.worst) {sample.worst=wall; worstPhase=phase;}
                    if(wall>16.7f) sample.above16ms++;
                    if(wall>33.3f) sample.above33ms++;
                    float currentFade = Shader.GetGlobalFloat(fadeId);
                    if (previousFade >= .999f && currentFade >= .999f)
                    {
                        sample.worstCovered = Mathf.Max(sample.worstCovered, wall);
                        if (wall > 33.3f) sample.coveredAbove33ms++;
                    }
                    else
                    {
                        sample.worstVisible = Mathf.Max(sample.worstVisible, wall);
                        if (wall > 33.3f) sample.visibleAbove33ms++;
                    }
                    previousFade = currentFade;
                    if(activated && !mapDone && MapGenerator.Instance!=null && MapGenerator.Instance.IsMapGenerationCompleted)
                    { mapDone=true; Mark("map ready"); }
                    if(activated && !ready && GameManager.Instance!=null && GameManager.Instance.IsPlayerReady)
                    { ready=true; Mark("player ready"); }
                } while(UnityNote.SceneLoader.IsLoading);
            }
            finally { UnityEngine.SceneManagement.SceneManager.sceneLoaded-=Loaded; }
            if(!activated || !ready || (sceneName=="BattleScene" && !mapDone)) throw new InvalidOperationException("Transition incomplete: "+label);
            Mark("input released");
            sample.endQpc=Stopwatch.GetTimestamp(); sample.frames=frames.Count; frames.Sort();
            sample.p99=frames[Mathf.CeilToInt(frames.Count*.99f)-1];
            report.sections.Add(sample);
            report.sceneResources.Add($"{label} worst phase={worstPhase}");
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            for(int n=0;n<10;n++) yield return null;
        }
    }

    private IEnumerator CrowdMatrix(List<GameObject> wave, PlayerController player)
    {
        // The natural wave stays registered with its room while the controlled encounter runs.
        yield return new WaitForSecondsRealtime(2);
        var sources = wave.Where(e => e != null).Select(e => e.GetComponent<BaseEntity>()?.MinionData)
            .Where(d => d != null).Distinct().ToArray();
        if (sources.Length == 0) throw new InvalidOperationException("Empty encounter roster");
        report.sceneResources.Add($"authored wave actors={wave.Count}");
        foreach (var enemy in wave) if (enemy != null) enemy.SetActive(false);
        var manager = GameManager.Instance.dataManager;
        var center = player.transform.position;
        bool focusHits = Array.IndexOf(Environment.GetCommandLineArgs(), "-crowdFocusHits") >= 0;
        bool stress = Array.IndexOf(Environment.GetCommandLineArgs(), "-largeCrowd") >= 0;
        int[] counts = stress ? new[] {32,64} : focusHits ? new[] {16} : new[] {1,4,8,16};
        foreach (int count in counts)
        {
            var active = new CharacterHealth[count];
            CharacterHealth Spawn(int slot)
            {
                float angle = slot * 2.39996f;
                Vector3 pos = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * (1.3f + .25f * (slot % 3));
                if (UnityEngine.AI.NavMesh.SamplePosition(pos,out var point,2f,UnityEngine.AI.NavMesh.AllAreas)) pos=point.position;
                return manager.CreateUnit(sources[slot % sources.Length],pos).GetComponentInChildren<CharacterHealth>();
            }
            void Remove(int slot)
            {
                if (active[slot] != null) Destroy(active[slot].GetComponentInParent<BaseEntity>().gameObject);
                active[slot]=null;
            }
            for (int slot=0;slot<count;slot++) active[slot]=Spawn(slot);
            foreach (string phase in focusHits ? new[] {"hits"} : new[] {"movement","spawn","hits","drops"})
            {
                float next=0;
                int events=0;
                yield return Measure($"crowd-{count}-{phase}",seconds,frame =>
                {
                    if (Time.unscaledTime<next || phase=="movement") return;
                    next=Time.unscaledTime+(phase=="hits" ? .25f : 3f);
                    if (phase=="spawn")
                    {
                        for (int slot=0;slot<count;slot++) { Remove(slot); active[slot]=Spawn(slot); }
                    }
                    else
                    {
                        for (int slot=0;slot<count;slot++)
                        {
                            var hp=active[slot];
                            if(hp==null || hp.IsDead) hp=active[slot]=Spawn(slot);
                            hp.SetHP(hp.MaxHP);
                            hp.TakeDamage(new DamageInfo(phase=="drops" ? hp.MaxHP*8 : Mathf.Max(1,hp.MaxHP*.16f),
                                DamageType.Fixed,player.gameObject,category:DamageCategory.BasicAttack,bypassGuard:true));
                        }
                    }
                    events++;
                });
                report.sceneResources.Add($"actors={count} phase={phase} bursts={events}");
            }
            for (int slot=0;slot<count;slot++) Remove(slot);
            yield return null;
        }
    }

    private IEnumerator VerifyWindows()
    {
        string folder=Path.Combine(Path.GetDirectoryName(output),"WindowChecks");
        Directory.CreateDirectory(folder);
        var canvas=FindFirstObjectByType<PlayerStateUI>().GetComponentInParent<Canvas>().rootCanvas;
        var controls=new UnityEngine.UI.Button[4];
        var clicked=new bool[4];
        var anchors=new[]{Vector2.zero, Vector2.right, Vector2.up, Vector2.one};
        var colors=new[]{Color.red,Color.green,Color.blue,Color.yellow};
        for(int i=0;i<controls.Length;i++)
        {
            var go=new GameObject("Corner "+i,typeof(RectTransform),typeof(CanvasRenderer),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));
            var rect=(RectTransform)go.transform;
            rect.SetParent(canvas.transform,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=anchors[i];
            rect.sizeDelta=Vector2.one*(12f/Mathf.Max(.001f,PixelViewport.CanvasPixelsPerUnit(canvas,rect)));
            go.GetComponent<UnityEngine.UI.Image>().color=colors[i];
            controls[i]=go.GetComponent<UnityEngine.UI.Button>();
            int index=i; controls[i].onClick.AddListener(()=>clicked[index]=true);
            controls[i].transition=UnityEngine.UI.Selectable.Transition.None;
        }
        var inputSettings = UnityEngine.InputSystem.InputSystem.settings;
        var previousFocus = inputSettings.backgroundBehavior;
        inputSettings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
        var input=GameManager.Instance.PLAYERCONTROLLER.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if(input!=null && input.user.valid) UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(mouse,input.user);
        var sizes=new[]{new Vector2Int(640,360),new Vector2Int(1280,720),new Vector2Int(1366,768),new Vector2Int(1920,1080)};
        var results=new List<string>();
        var player=GameManager.Instance.PLAYERCONTROLLER;
        var item=ScriptableObject.CreateInstance<ItemSO>();
        item.itemName="스테이지 보상 · Stage Reward";
        var drop=GroundItem.Drop(item, player.transform.position + Vector3.up * .8f, false);
        var label=drop.GetComponentInChildren<Tooltip>(true).name;
        try
        {
            foreach(var size in sizes)
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
                for(int i=0;i<100;i++) {yield return null; if(i>=5 && Screen.width==size.x && Screen.height==size.y) break;}
                var display=PixelArtUpscaleRig.Output.DisplayRect;
                int clicks=0, dispatched=0,painted=0;
                var module=UnityEngine.EventSystems.EventSystem.current.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule;
                for(int i=0;i<controls.Length;i++)
                {
                    var rect=(RectTransform)controls[i].transform;
                    Vector2 logical=PixelViewport.CanvasToGamePoint(canvas,rect.TransformPoint(rect.rect.center));
                    Vector2 physical=PixelViewport.GameToScreenPoint(logical,display);
                    var state=new UnityEngine.InputSystem.LowLevel.MouseState {position=physical};
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,state);
                    yield return null; yield return null;
                    var point=module!=null ? module.point.action.ReadValue<Vector2>() : Vector2.negativeInfinity;
                    var hits=new List<UnityEngine.EventSystems.RaycastResult>();
                    UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=point},hits);
                    if (hits.Exists(hit=>hit.gameObject==controls[i].gameObject)) clicks++;
                    else UnityEngine.Debug.LogWarning($"[WindowInput] {size} corner={i} focus={Application.isFocused} physical={physical} logical={logical} delivered={point} device={mouse.enabled} canvas={canvas.name} event={module} hits={string.Join(",",hits.Select(h=>h.gameObject.name))}");
                    clicked[i]=false;
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
                    yield return null; yield return null;
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left,false));
                    yield return null; yield return null;
                    if(clicked[i]) dispatched++;
                }
                drop.OnFocused(player.gameObject);
                yield return null;
                yield return new WaitForEndOfFrame();
                label.ForceMeshUpdate();
                var camera=PixelViewport.WorldCamera;
                float height=0;
                for(int n=0;n<label.textInfo.characterCount;n++)
                {
                    var glyph=label.textInfo.characterInfo[n];
                    if(!glyph.isVisible) continue;
                    float top=camera.WorldToViewportPoint(label.transform.TransformPoint(glyph.topLeft)).y;
                    float bottom=camera.WorldToViewportPoint(label.transform.TransformPoint(glyph.bottomLeft)).y;
                    height=Mathf.Max(height,(top-bottom)*PixelViewport.Height);
                }
                if(height<4 || height>12) throw new InvalidOperationException($"Window {size} reward glyph height {height} px");
                report.sceneResources.Add($"reward {size.x}x{size.y} glyphHeight={height:F2} logical pixels");
                var screen=ScreenCapture.CaptureScreenshotAsTexture();
                var frame=PixelArtUpscaleRig.Output.Frame;
                var previous=RenderTexture.active;
                RenderTexture.active=frame;
                var native=new Texture2D(frame.width,frame.height,TextureFormat.RGB24,false);
                native.ReadPixels(new Rect(0,0,frame.width,frame.height),0,0); native.Apply();
                RenderTexture.active=previous;
                for(int i=0;i<controls.Length;i++)
                {
                    int x=anchors[i].x>0 ? screen.width-3 : 2, y=anchors[i].y>0 ? screen.height-3 : 2;
                    Color actual=screen.GetPixel(x,y), reference=colors[i];
                    Color logical=native.GetPixel(anchors[i].x>0 ? frame.width-2 : 1,anchors[i].y>0 ? frame.height-2 : 1);
                    // A real press can tint the entire logical frame. Require the same colored pixel in the physical window.
                    float channelError=Mathf.Max(Mathf.Abs(actual.r-reference.r),Mathf.Abs(actual.g-reference.g),Mathf.Abs(actual.b-reference.b));
                    float sourceError=Mathf.Max(Mathf.Abs(actual.r-logical.r),Mathf.Abs(actual.g-logical.g),Mathf.Abs(actual.b-logical.b));
                    if(channelError<.30f && sourceError<.025f) painted++;
                }
                string prefix=Path.Combine(folder,size.x+"x"+size.y);
                File.WriteAllBytes(prefix+"-window.png",screen.EncodeToPNG());
                File.WriteAllBytes(prefix+"-logical.png",native.EncodeToPNG());
                results.Add($"{size.x}x{size.y} actual={screen.width}x{screen.height} frame={frame.width}x{frame.height} rect={display} cornerColors={painted}/4 inputRaycasts={clicks}/4 clicks={dispatched}/4 logicalTargets={Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t=>t.name==frame.name && t.IsCreated())}");
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results);
                Destroy(native); Destroy(screen);
            }
        }
        finally
        {
            Destroy(drop.gameObject);
            Destroy(item);
            foreach(var control in controls) Destroy(control.gameObject);
            UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            inputSettings.backgroundBehavior = previousFocus;
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        }
        for(int i=0;i<12;i++) yield return null;
    }

    private IEnumerator Measure(string name,int length,Action<int> act,Func<bool> finished=null)
    {
        UnityEngine.Debug.Log($"[RenderBudget] Begin {name} in {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        var samples=new List<float>(Mathf.Max(2048,length*600));
        var gpu=new List<float>();
        var cpu=new List<float>();
        ulong previousTimestamp=0;
        var field=WorldSparkField.Current;
        var row=new Sample{name=name,startQpc=Stopwatch.GetTimestamp()};
        int areaDrops=field.AreaDrops;
        var allocations=new List<long>();
        using var allocation=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory,"GC Allocated In Frame");
        int requests=field.Requested,emitted=field.Emitted,drops=field.BudgetDrops,casts=field.CollisionQueries;
        var markers=new List<(string name,Unity.Profiling.ProfilerRecorder sample)>();
        if((name=="dungeon-crowd" || name.StartsWith("crowd-",StringComparison.Ordinal)) && UnityEngine.Debug.isDebugBuild)
        {
            var requested=new HashSet<string>{"GC.Collect","GarbageCollectIncremental","Camera.Render","RenderPipelineManager.DoRenderLoop_Internal",
                "Physics2D.Simulate","Update.ScriptRunBehaviourUpdate","BehaviourUpdate","Canvas.BuildBatch","UI.CanvasManager",
                "Gfx.WaitForPresentOnGfxThread","Shader.CreateGPUProgram","NavMesh.Update","Animator.Update"};
            var handles=new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            foreach(var handle in handles)
            {
                var description=Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(handle);
                if(requested.Remove(description.Name)) markers.Add((description.Name,Unity.Profiling.ProfilerRecorder.StartNew(description.Category,description.Name)));
            }
        }
        float recordedWorst=0;
        long prev=row.startQpc;
        while(prev-row.startQpc < (long)length*Stopwatch.Frequency && (samples.Count==0 || finished==null || !finished()))
        {
            long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
            act(samples.Count);
            FrameTimingManager.CaptureFrameTimings();
            yield return null;
            long now=Stopwatch.GetTimestamp();
            bool profilerAllocation=allocation.Valid && allocation.Count>0;
            long measuredAllocation=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
            if(profilerAllocation || report.allocationCounterVerified) allocations.Add(profilerAllocation ? allocation.LastValue : measuredAllocation);
            row.allocationScope=profilerAllocation ? "Unity GC Allocated In Frame" : report.allocationCounterVerified ? "main thread bytes between updates" : "unavailable";
            float wall=(float)((now-prev)*1000d/Stopwatch.Frequency);
            samples.Add(wall);prev=now;
            if (wall>16.7f) row.above16ms++;
            if (wall>33.3f) row.above33ms++;
            if(markers.Count>0 && wall>recordedWorst && wall>16.7f)
            {
                recordedWorst=wall;
                long bytes=allocations.Count>0 ? allocations[allocations.Count-1] : -1;
                var line=new System.Text.StringBuilder($"frame={samples.Count} wall={wall:F2}ms allocated={bytes}B");
                foreach(var mark in markers)
                    if(mark.sample.Valid && mark.sample.Count>0) line.Append($" | {mark.name}={mark.sample.LastValue/1000000d:F3}ms");
                report.slowFrames.Add(line.ToString());
            }
            bool hasTiming=FrameTimingManager.GetLatestTimings(1,timings)>0 && timings[0].frameStartTimestamp!=previousTimestamp;
            if (hasTiming)
            {
                previousTimestamp=timings[0].frameStartTimestamp;
                if (timings[0].cpuFrameTime>0) cpu.Add((float)timings[0].cpuFrameTime);
            }
#if PIXEL_GPU_TIMER
            int us=ReadGpuMicroseconds();
            if(us>0) gpu.Add(us*.001f);
#else
            if(FrameTimingManager.GetLatestTimings(1,timings)>0 && timings[0].gpuFrameTime>0) gpu.Add((float)timings[0].gpuFrameTime);
#endif
        }
        foreach(var mark in markers) {var recorder=mark.sample;recorder.Dispose();}
        row.endQpc=prev;row.frames=samples.Count;row.firstFrame=samples[0];samples.Sort();gpu.Sort();
        float P(float quantile)=>samples[Mathf.CeilToInt(quantile*samples.Count)-1];
        row.p50=P(.50f);row.p95=P(.95f);row.p99=P(.99f);row.worst=samples[samples.Count-1];
        if(gpu.Count>0) { row.gpuSamples=gpu.Count; row.medianGPU=gpu[gpu.Count/2];row.gpu95=gpu[Mathf.CeilToInt(gpu.Count*.95f)-1]; }
        if(cpu.Count>0) { cpu.Sort(); row.cpu95=cpu[Mathf.CeilToInt(cpu.Count*.95f)-1]; }
        if(allocations.Count>0) { allocations.Sort();row.allocatedBytes95=allocations[Mathf.CeilToInt(allocations.Count*.95f)-1];row.allocatedBytesMax=allocations[allocations.Count-1]; }
        row.areaDrops=field.AreaDrops-areaDrops;
        row.particleRequests=field.Requested-requests;row.emitted=field.Emitted-emitted;row.dropped=field.BudgetDrops-drops;row.raycasts=field.CollisionQueries-casts;
        report.sections.Add(row);
        UnityEngine.Debug.Log($"[RenderBudget] End {name} frames={row.frames}");
        File.WriteAllText(output,JsonUtility.ToJson(report,true));
    }
}
#endif
