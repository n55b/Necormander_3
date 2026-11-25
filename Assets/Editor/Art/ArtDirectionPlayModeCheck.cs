using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class ArtDirectionPlayModeCheck
{
    private const string Request = "Verification.ArtDirection";
    public static void Run()
    {
        SessionState.SetBool(Request,true);
        SessionState.SetBool("Verification.CombatPolishRoom",true);
        PresentationRegressionCheck.Run();
    }
    internal static bool TakeRequest()
    {
        bool requested = SessionState.GetBool(Request,false);
        SessionState.EraseBool(Request);
        return requested;
    }
    private static void Check(bool ok,string label)
    {
        if (!ok) throw new InvalidOperationException("[ArtDirection] " + label);
        Debug.Log("[ArtDirection] PASS " + label);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TestParticle { public Vector4 motion,time; }
    private static IEnumerator VerifyDialogue(Action<bool,Vector3> click,Vector3 point)
    {
        var ui=Object.FindFirstObjectByType<DialogueUI>();
        Check(ui!=null,"village dialogue UI exists");
        var body=(TMP_Text)new SerializedObject(ui).FindProperty("bodyText").objectReferenceValue;
        int completed=0;
        try
        {
            ui.Play("bm_intro",()=>completed++);
            yield return null;
            string line=body.text;
            Check(ui.IsPlaying && line.Length>0 && body.maxVisibleCharacters<body.textInfo.characterCount,
                "authored localized line starts with partial glyph visibility");
            click(true,point);
            yield return null;
            click(false,point);
            yield return null;
            Check(ui.IsPlaying && body.text==line && body.maxVisibleCharacters>=body.textInfo.characterCount,
                "one advance reveals every glyph of the current line");
        }
        finally {ui.StopDialogue();}
        Check(completed==1,"dialogue close releases input and calls completion once");
    }
    private static IEnumerator VerifyUiParticles(PlayerStateUI hud,Camera camera)
    {
        var canvas=hud.GetComponentInParent<Canvas>().rootCanvas;
        var backdrop=new GameObject("Verify clipped particles",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
        var rect=(RectTransform)backdrop.transform;
        rect.SetParent(canvas.transform,false);
        float unit=1f/PixelViewport.CanvasPixelsPerUnit(canvas,rect);
        rect.sizeDelta=new Vector2(40,40)*unit;
        var bg=backdrop.GetComponent<Image>();bg.color=Color.black;bg.raycastTarget=false;
        var crop=new GameObject("4 pixel mask",typeof(RectTransform),typeof(RectMask2D));
        var origin=(RectTransform)crop.transform;
        origin.SetParent(rect,false);
        origin.sizeDelta=Vector2.one*4*unit;
        var mask=crop.GetComponent<RectMask2D>();
        var read=new Texture2D(25,25,TextureFormat.RGB24,false);
        UISparkBurst burst=null;
        try
        {
            for(int test=0;test<2;test++)
            {
                mask.enabled=test==1;
                UISparkBurst.Play(ref burst,origin);
                Check(burst!=null && !burst.raycastTarget,"UI emitter enters canvas without taking input");
                Canvas.ForceUpdateCanvases();
                int firstFrame=Time.frameCount;
                while(Time.frameCount<firstFrame+2) yield return null;
                int outside=0;
                for(int sample=0;sample<4;sample++)
                {
                    var center=PixelViewport.CanvasToGamePoint(canvas,origin.position);
                    var prev=RenderTexture.active;
                    try
                    {
                        RenderTexture.active=camera.targetTexture;
                        read.ReadPixels(new Rect(Mathf.RoundToInt(center.x)-12,Mathf.RoundToInt(center.y)-12,25,25),0,0);
                        read.Apply();
                    }
                    finally {RenderTexture.active=prev;}
                    var pixels=read.GetPixels32();int visible=0;
                    var background=pixels[0];
                    for(int y=0;y<25;y++)
                    for(int x=0;x<25;x++)
                    {
                        if(Mathf.Abs(x-12)<=3 && Mathf.Abs(y-12)<=3) continue;
                        var c=pixels[y*25+x];
                        if(Mathf.Abs(c.r-background.r)+Mathf.Abs(c.g-background.g)+Mathf.Abs(c.b-background.b)>80) visible++;
                    }
                    outside=Mathf.Max(outside,visible);
                    int sampledFrame=Time.frameCount;
                    while(Time.frameCount==sampledFrame) yield return null;
                }
                Check(test==0 ? outside>0 : outside==0,$"UI particle raster, mask={mask.enabled}, outside pixels={outside}");
                PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/UIBurst-"+test);
            }
        }
        finally {Object.Destroy(backdrop);Object.Destroy(read);}
    }
    private static IEnumerator VerifyTerrainBounce()
    {
        if(!SystemInfo.supportsComputeShaders) yield break;
        var flow=Resources.Load<ComputeShader>("WorldAtmosphere/ParticleFlow");
        var mask=new Texture2D(4,4,TextureFormat.R8,false,true);
        var open=new byte[16];
        for(int y=0;y<4;y++) {open[4*y]=255;open[4*y+1]=255;}
        mask.LoadRawTextureData(open); mask.Apply();
        var particles=new TestParticle[2];
        particles[0]=new TestParticle {motion=new Vector4(1.5f,1.5f,3,0),time=new Vector4(0,2,1,0)};
        particles[1]=new TestParticle {motion=particles[0].motion,time=new Vector4(0,2,0,0)};
        using var buffer=new ComputeBuffer(2,32);
        try
        {
            int kernel=flow.FindKernel("Advance");
            buffer.SetData(particles); flow.SetBuffer(kernel,"_Particles",buffer);
            flow.SetTexture(kernel,"_FloorMask",mask);flow.SetMatrix("_FloorUV",Matrix4x4.Scale(new Vector3(.25f,.25f,1)));
            flow.SetInt("_Capacity",2);flow.SetInt("_HasFloorMask",1);flow.SetFloat("_Now",.5f);flow.Dispatch(kernel,1,1,1);
            var read=UnityEngine.Rendering.AsyncGPUReadback.Request(buffer);
            while(!read.done) yield return null;
            Check(!read.hasError,"terrain particle GPU readback");
            var result=read.GetData<TestParticle>();
            Check(result[0].motion.x<2 && result[0].motion.z<0 && result[1].motion.x>2 && result[1].motion.z>0,
                "solid particle rebounds while decorative dust crosses the same wall");
        }
        finally { Object.Destroy(mask); }
    }
    private static IEnumerator VerifyFloor(WorldFloorMask field)
    {
        var ground=field.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        Vector3Int cell=default;
        bool found=false;
        var rect=ground.cellBounds;
        foreach(var candidate in rect.allPositionsWithin)
        {
            if(!ground.HasTile(candidate)) continue;
            int x=candidate.x-rect.xMin,y=candidate.y-rect.yMin;
            if(field.Texture.GetPixel(x,y).r>.5f) {cell=candidate;found=true;break;}
        }
        Check(found,"fixture contains open ground");
        var tile=ground.GetTile(cell);
        int sx=cell.x-rect.xMin,sy=cell.y-rect.yMin;
        try
        {
            ground.SetTile(cell,null);
            for(int n=0;n<5;n++) yield return null;
            var read=UnityEngine.Rendering.AsyncGPUReadback.Request(field.Texture,0);
            while(!read.done) yield return null;
            Check(!read.hasError && read.GetData<byte>()[sy*field.Texture.width+sx]==0,"solid or missing cell reaches GPU after a sparse edit");
        }
        finally {ground.SetTile(cell,tile);}
        for(int n=0;n<5;n++) yield return null;
        var restored=UnityEngine.Rendering.AsyncGPUReadback.Request(field.Texture,0);
        while(!restored.done) yield return null;
        Check(!restored.hasError && restored.GetData<byte>()[sy*field.Texture.width+sx]==255,"restored floor reopens the GPU cell");
        var gate=new GameObject("Verify moving barrier");
        gate.SetActive(false);
        gate.transform.position=ground.GetCellCenterWorld(cell);
        var openView=new GameObject("Open");openView.transform.SetParent(gate.transform,false);
        var closedView=new GameObject("Closed");closedView.transform.SetParent(gate.transform,false);
        closedView.AddComponent<BoxCollider2D>().size=Vector2.one*.15f;
        closedView.SetActive(false);
        var door=gate.AddComponent<DoorUpController>();
        var setup=new SerializedObject(door);
        setup.FindProperty("openDoor").objectReferenceValue=openView;
        setup.FindProperty("closeDoor").objectReferenceValue=closedView;
        setup.ApplyModifiedPropertiesWithoutUndo();
        try
        {
            gate.SetActive(true);
            door.CloseDoor();
            for(int n=0;n<5;n++) yield return null;
            var closed=UnityEngine.Rendering.AsyncGPUReadback.Request(field.Texture,0);
            while(!closed.done) yield return null;
            Check(!closed.hasError && closed.GetData<byte>()[sy*field.Texture.width+sx]==0,"closing a door blocks its GPU occupancy cell");
            door.OpenDoor();
            for(int n=0;n<5;n++) yield return null;
            var opened=UnityEngine.Rendering.AsyncGPUReadback.Request(field.Texture,0);
            while(!opened.done) yield return null;
            Check(!opened.hasError && opened.GetData<byte>()[sy*field.Texture.width+sx]==255,"opening the same door releases its GPU occupancy cell");
        }
        finally {Object.Destroy(gate);}
    }

    internal static IEnumerator VerifyClearedRoom(Camera camera)
    {
        var player=GameManager.Instance.PLAYERCONTROLLER;
        var map=MapGenerator.Instance;
        var room=map.AllRooms.First(r=>r.roomType==RoomType.Normal && !r.isCleared);
        var waves=room.GetComponent<NormalRoomEvent>();
        var roster=typeof(NormalRoomEvent).GetField("_activeEnemies",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var enemies=(System.Collections.Generic.List<GameObject>)roster.GetValue(waves);
        var inventory=InventoryManager.Instance;
        int startGold=inventory.GOLD, kills=0, arrivalFrames=0;
        bool sawFinalSlow = false;
        float nextStrike=0, deadline=Time.realtimeSinceStartup+45;
        var playerHealth=player.GetComponentInChildren<CharacterHealth>();
        playerHealth.Invincible=true;
        player.GetComponent<Rigidbody2D>().position=room.transform.position + (Vector3)room.centerOffset;
        int finalSounds = 0;
        void CountFinalSound(FeedbackCue cue, AudioClip clip) { if (cue == FeedbackCue.LastEnemyImpact && clip != null) finalSounds++; }
        PolishFeedback.SoundPlayed += CountFinalSound;
        try
        {
            room.ForceEnter(); room.StartCombatEvent();
            while (!room.isCleared && Time.realtimeSinceStartup<deadline)
            {
                if (!sawFinalSlow && kills > 0 && Mathf.Abs(Time.timeScale - CameraSettingsSO.Instance.finalBlowTimeScale) < .002f)
                {
                    sawFinalSlow = true;
                    PresentationRegressionCheck.SaveFrame(camera.targetTexture, "RoomFinishingBlow");
                }
                var ring=room.transform.Find("Enemy arrival");
                if (ring!=null && ring.gameObject.activeInHierarchy && arrivalFrames<12)
                {
                    if (arrivalFrames == 0) Check(ring.GetComponentInChildren<Collider2D>()==null,"spawn warning has no damage collider");
                    PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/Arrival-"+arrivalFrames.ToString("D2"));
                    arrivalFrames++;
                }
                if(Time.time>=nextStrike)
                {
                    nextStrike=Time.time+.22f;
                    for(int i=enemies.Count-1;i>=0;i--)
                    {
                        if (i>=enemies.Count || enemies[i]==null) continue;
                        var hp=enemies[i].GetComponentInChildren<CharacterHealth>();
                        if(hp==null || hp.IsDead || hp.Invincible) continue;
                        hp.TakeDamage(new DamageInfo(hp.MaxHP*9,DamageType.Fixed,player.gameObject,category:DamageCategory.BasicAttack,bypassGuard:true));
                        if (hp.IsDead) kills++;
                    }
                }
                yield return null;
            }
        }
        finally { PolishFeedback.SoundPlayed -= CountFinalSound; }
        Check(finalSounds == 1, "one final impact sound for the whole room");
        Check(room.isCleared && arrivalFrames>0 && kills>0,"seeded room clears after enemy arrival and "+kills+" kills");
        Check(sawFinalSlow, "final enemy triggers room slow motion before its corpse retires");
        var chest=Object.FindObjectsByType<RoomRewardBox>(FindObjectsSortMode.None)
            .OrderBy(box=>(box.transform.position-room.transform.position).sqrMagnitude).FirstOrDefault();
        if(chest!=null) Check(chest.Interact(player.gameObject),"cleared room chest accepts first interaction");
        float until=Time.realtimeSinceStartup+6;
        while(inventory.GOLD==startGold && Time.realtimeSinceStartup<until) yield return null;
        Check(inventory.GOLD==startGold+RewardManager.NormalClearGold,"room pays its exact clear currency once");
        var drop=Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
            .FirstOrDefault(item=>item.Item!=null && item.IsAvailable);
        Check(drop!=null,"stage clear produced a usable item");
        drop.OnFocused(player.gameObject);
        for (int n=0; n<20; n++)
        {
            yield return null;
            if(n%2==0) PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/ClearLoot-"+n.ToString("D2"));
        }
        var tip=drop.GetComponentInChildren<Tooltip>(true);
        Check(tip!=null && tip.name.text==drop.Item.DisplayName,"stage clear item displays the awarded name");
        drop.OnLostFocus(player.gameObject);
        player.GetComponent<Rigidbody2D>().position = (Vector2)room.transform.position + room.centerOffset + new Vector2(-2.2f, -.9f);
        var finale = VerifyBossFinish(camera, room, player);
        try { while (finale.MoveNext()) yield return finale.Current; }
        finally { (finale as IDisposable)?.Dispose(); playerHealth.Invincible = false; }
    }

    private static IEnumerator VerifyBossFinish(Camera camera, RoomInstance room, PlayerController player)
    {
        var data = AssetDatabase.FindAssets("t:EnemyMinionDataSO").Select(guid =>
            AssetDatabase.LoadAssetAtPath<EnemyMinionDataSO>(AssetDatabase.GUIDToAssetPath(guid)))
            .First(d => d.tier == EnemyTier.Boss && !d.isPhaseVariant && d.minionPrefab.GetComponent<BoneMasterController>() != null);
        var host = new GameObject("Boss finishing blow check");
        var encounter = host.AddComponent<BossRoomEvent>();
        var so = new SerializedObject(encounter);
        so.FindProperty("specificBossData").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();
        int bossCues = 0, roomCues = 0;
        void Count(FeedbackCue cue, AudioClip clip)
        {
            if (cue == FeedbackCue.BossDeath) bossCues++;
            if (cue == FeedbackCue.LastEnemyImpact) roomCues++;
        }
        PolishFeedback.SoundPlayed += Count;
        BoneMasterController boss = null;
        try
        {
            encounter.OnPlayerEnter(room);
            float deadline = Time.unscaledTime + 18;
            while (boss == null && Time.unscaledTime < deadline)
            {
                yield return null;
                boss = Object.FindFirstObjectByType<BoneMasterController>();
            }
            Check(boss != null, "boss-room event spawns Bone Master");
            var hp = boss.GetComponentInChildren<CharacterHealth>();
            float nextHit = Time.unscaledTime + .7f;
            float atDeath = 0, stopEnd = 0;
            bool stopped = false, slowed = false, recovered = false;
            while (Time.unscaledTime < deadline)
            {
                if (bossCues == 0 && hp != null && !hp.Invincible && !hp.IsDead && Time.unscaledTime >= nextHit)
                {
                    nextHit = Time.unscaledTime + .3f;
                    hp.TakeDamage(new DamageInfo(hp.MaxHP * 9, DamageType.Fixed, player.gameObject,
                        category: DamageCategory.BasicAttack, bypassGuard: true));
                    if (bossCues > 0) atDeath = Time.unscaledTime;
                }
                if (bossCues > 0)
                {
                    float scale = PolishFeedback.Catalog.slowMotionScale;
                    stopped |= Time.timeScale > 0 && Time.timeScale < scale;
                    if (!slowed && Mathf.Abs(Time.timeScale - scale) < .002f)
                    {
                        slowed = true;
                        stopEnd = Time.unscaledTime;
                        PresentationRegressionCheck.SaveFrame(camera.targetTexture, "BossFinishingBlow");
                    }
                    if (slowed && Mathf.Approximately(Time.timeScale, 1)) { recovered = true; break; }
                }
                yield return null;
            }
            Check(bossCues == 1 && roomCues == 0, "boss phase change is silent and the death plays only the boss finale");
            float duration = Time.unscaledTime - stopEnd;
            Check(stopped && slowed && recovered && duration >= PolishFeedback.Catalog.slowMotionSeconds - .08f,
                $"boss hit stop, {PolishFeedback.Catalog.slowMotionScale:F2}x slow and full recovery in {Time.unscaledTime-atDeath:F2}s");
        }
        finally
        {
            PolishFeedback.SoundPlayed -= Count;
            Object.Destroy(host);
            if (boss != null) Object.Destroy(boss.gameObject);
        }
    }

    private static IEnumerator VerifyPanelIdle(PlayerStateUI hud)
    {
        var panel = new GameObject("Idle frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(hud.transform, false);
        panel.SetActive(false);
        panel.AddComponent<UISkinPart>().role = UISkinRole.InfoPanel;
        panel.SetActive(true);
        try
        {
            Canvas.ForceUpdateCanvases();
            var image = panel.GetComponent<Image>();
            var before = image.canvasRenderer.GetMesh().colors32;
            var until = Time.unscaledTime + 3.7f;
            while (Time.unscaledTime < until)
            {
                yield return null;
                if (!before.SequenceEqual(image.canvasRenderer.GetMesh().colors32))
                    throw new InvalidOperationException("Passive panel changed color while idle");
            }
        }
        finally { Object.Destroy(panel); }
    }

    internal static IEnumerator VerifyActorPolish(Camera camera, PlayerController player)
    {
        var manager = GameManager.Instance.dataManager;
        var data = manager.ENEMY_MINION_DATA.First(d => d != null && d.minionPrefab != null &&
            d.minionPrefab.GetComponent<Rigidbody2D>() != null && d.minionPrefab.GetComponent<VisualSquashStretch>() != null);
        var origin = player.transform.position;
        var bossData = AssetDatabase.FindAssets("t:EnemyMinionDataSO")
            .Select(id => AssetDatabase.LoadAssetAtPath<EnemyMinionDataSO>(AssetDatabase.GUIDToAssetPath(id)))
            .First(d => d.tier == EnemyTier.Boss && !d.isPhaseVariant && d.minionPrefab != null);
        var boss = manager.CreateUnit(bossData, origin + Vector3.right * 2);
        try
        {
            var renderer = boss.GetComponent<BaseEntity>().SpriteRenderer;
            var properties = new MaterialPropertyBlock();
            float peak = 0, until = Time.time + .75f;
            while (Time.time < until)
            {
                renderer.GetPropertyBlock(properties);
                peak = Mathf.Max(peak, properties.GetVector("_PixelMotionOffset").y * PixelViewport.Height / (2 * camera.orthographicSize));
                yield return null;
            }
            renderer.GetPropertyBlock(properties);
            Check(peak > 28 && Mathf.Abs(properties.GetVector("_PixelMotionOffset").y) < .06f,
                $"actual boss descends {peak:F1}px and settles at its ground contact");
        }
        finally { Object.Destroy(boss); }
        var unit = manager.CreateUnit(data, origin + Vector3.right * .8f);
        var actor = unit.GetComponent<BaseEntity>();
        var bar = unit.GetComponentInChildren<WorldHPBar>(true)?.GetComponent<CanvasGroup>();
        var body = unit.GetComponent<Rigidbody2D>();
        var hp = unit.GetComponentInChildren<CharacterHealth>();
        var pose = unit.GetComponent<VisualSquashStretch>();
        var field = WorldSparkField.Current;
        try
        {
            yield return null;
            Check(bar != null && bar.alpha < .1f, "the enemy health bar is hidden during its aerial reveal");
            actor.enabled = false;
            var agent = unit.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled=false;
            var renderer=actor.SpriteRenderer;
            var block=new MaterialPropertyBlock();
            float top = 0, side = 0, stretch = 0, airCompression = 1, compression = 1;
            float end = Time.time + .48f;
            while (Time.time < end)
            {
                renderer.GetPropertyBlock(block);
                top = Mathf.Max(top, block.GetVector("_PixelMotionOffset").y * PixelViewport.Height / (2 * camera.orthographicSize));
                Vector4 displacement = block.GetVector("_PixelMotionOffset") * (PixelViewport.Height / (2 * camera.orthographicSize));
                side = Mathf.Max(side, Mathf.Abs(displacement.x));
                float y = block.GetVector("_FeedbackScale").y;
                if (displacement.y > 5 && y > 0) airCompression = Mathf.Min(airCompression, y);
                if (y > 0) { stretch = Mathf.Max(stretch, y); compression = Mathf.Min(compression, y); }
                yield return null;
            }
            renderer.GetPropertyBlock(block);
            Check(bar.alpha > .99f, "the enemy health bar returns after landing");
            Check(top > 18 && side > 2 && stretch > 1.12f && airCompression < .93f && compression < .9f &&
                Mathf.Abs(block.GetVector("_PixelMotionOffset").y) < .06f,
                $"midair entry rises {top:F1}px, crosses {side:F1}px, airborne squash {airCompression:F2}, stretch {stretch:F2}, deepest squash {compression:F2}");
            float Strike(float gap)
            {
                body.position=(Vector2)player.transform.position + Vector2.right * gap;
                Physics2D.SyncTransforms();
                hp.SetHP(hp.MaxHP);
                var contact=new DamageInfo(2f,DamageType.Fixed,player.gameObject,knockbackForce:2f, category:DamageCategory.BasicAttack,
                    bypassGuard:true) {playerBasicAttack=new PlayerBasicAttackRoll(null)};
                hp.TakeDamage(contact);
                return body.linearVelocity.magnitude;
            }
            float normal = Strike(1.4f);
            float close = Strike(.10f);
            renderer.GetPropertyBlock(block);
            Check(renderer.sharedMaterial.HasProperty("_HitFlash") && block.GetFloat("_HitFlash")>.5f,
                "white flash on the actual enemy body material "+renderer.sharedMaterial.name);
            Check(close>normal*1.02f && close<=normal*1.16f,"contact impulse measured normal="+normal+" close="+close);
            body.linearVelocity=Vector2.zero;
            body.position=(Vector2)player.transform.position+Vector2.right*1.25f;
            var originBounds=unit.GetComponent<Collider2D>().bounds.size;
            for(int frame=0;frame<18;frame++)
            {
                if(frame==5) pose.PlayAttack();
                if(frame==10) pose.PlayLand();
                yield return null;
                PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/Actor-"+frame.ToString("D2"));
            }
            renderer.GetPropertyBlock(block);
            Check(block.GetFloat("_HitFlash")<.001f && (unit.GetComponent<Collider2D>().bounds.size-originBounds).sqrMagnitude<.00001f,
                "enemy pose keeps collider size and releases white flash");
            var skills=player.GetComponent<PlayerSkillController>();
            var companion=AssetDatabase.FindAssets("t:MainMinionDataSO")
                .Select(id=>AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(id)))
                .First(entry=>entry != null && entry.basicAnim != null && entry.basicAnim.visual != null);
            int before=field.Emitted;
            var restored=SummonFieldAgent.Spawn(skills,companion,skills.ActiveSlot,false);
            Check(field.Emitted==before,"restoring a pet does not replay summoning");
            Object.Destroy(restored.gameObject);
            var summoned=SummonFieldAgent.Spawn(skills,companion,skills.ActiveSlot,true);
            try
            {
                Check(summoned.IsVisualVisible && field.Emitted>before,"new active pet emits field particles");
                for(int frame=0;frame<10;frame++)
                {
                    yield return null;
                    PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/Pet-"+frame.ToString("D2"));
                }
            }
            finally { Object.Destroy(summoned.gameObject); }
        }
        finally {Object.Destroy(unit);}
    }

    private static IEnumerator VerifyCombatPolish(Camera camera, PlayerController player, GameObject target, Action<bool,Vector3> click)
    {
        var melee = player.GetComponent<MeleeCombatController>();
        var center = (Vector2)target.transform.position + Vector2.up * 80;
        var first = new GameObject("Contact candidate") { layer = Layers.Enemy };
        var second = new GameObject("Other candidate") { layer = Layers.Enemy };
        var wall = new GameObject("Contact wall") { layer = Layers.Wall };
        var hp = first.AddComponent<CharacterHealth>();
        var shape = first.AddComponent<CircleCollider2D>();
        shape.radius = .12f;
        first.AddComponent<CircleCollider2D>().radius = .1f;
        second.AddComponent<CharacterHealth>();
        second.AddComponent<CircleCollider2D>().radius = .12f;
        wall.AddComponent<BoxCollider2D>().size = new Vector2(.04f, 1);
        second.SetActive(false);
        wall.SetActive(false);
        Vector2 Aim(Vector2 pointer)
        {
            Physics2D.SyncTransforms();
            melee.ResolveAim(center, pointer, Vector2.zero, 1f, out var aim, out _);
            return aim;
        }
        try
        {
            foreach (var dir in new[] {Vector2.left, Vector2.right, Vector2.up, Vector2.down})
            {
                first.transform.position = center + dir * .32f;
                Check(Vector2.Dot(Aim(center - dir * 3), dir) > .98f,"single contact reaches " + dir + " across two hurtboxes");
            }
            first.transform.position = center;
            Check(Vector2.Dot(Aim(center),melee.CurrentAttackDir) > .98f,"overlapping centers use the last attack direction");
            first.transform.position = center + Vector2.right * .34f;
            second.transform.position = center + Vector2.left * .85f;
            second.SetActive(true);
            Check(Aim(center + Vector2.left * 3).x < -.98f,"two reachable actors preserve cursor direction");
            second.SetActive(false);
            wall.transform.position = center + Vector2.right * .13f;
            wall.SetActive(true);
            Check(Aim(center + Vector2.left * 3).x < -.98f,"walls exclude contact assist");
            wall.SetActive(false);
            hp.Invincible = true;
            Check(Aim(center + Vector2.left * 3).x < -.98f,"invulnerable actors cannot claim contact assist");
        }
        finally
        {
            Object.Destroy(first); Object.Destroy(second); Object.Destroy(wall);
        }
        var dummy = target.GetComponentInChildren<CharacterHealth>();
        int contacts = 0;
        void Hit(float amount) { if(amount > 0) contacts++; }
        var body = player.GetComponent<Rigidbody2D>();
        var saved = body.position;
        Vector3 wrongSide = target.transform.position + Vector3.left * 4;
        dummy.OnDamageTaken += Hit;
        try
        {
            body.position = (Vector2)target.transform.position + Vector2.left * .28f;
            Physics2D.SyncTransforms();
            for(int i=0;i<8;i++) yield return null;
            PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/Contact-00");
            click(true,wrongSide);
            for(int i=1;i<=33;i++)
            {
                yield return null;
                if (i==3) click(false,wrongSide);
                if (i%3==0) PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/Contact-"+i.ToString("D2"));
            }
            Check(contacts>0,"real melee click past a lone contact lands on the scarecrow");
        }
        finally {dummy.OnDamageTaken-=Hit; click(false,wrongSide); body.position=saved;}
        var item = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:ItemSO")[0])));
        var drop = GroundItem.Drop(item, player.transform.position + Vector3.up * .8f, false);
        try
        {
            drop.OnFocused(player.gameObject);
            var tooltip = drop.GetComponentInChildren<Tooltip>(true);
            Check(tooltip != null && tooltip.name.text == item.DisplayName,"ground reward shows the bound item name");
            var cameraManager = CameraManager.Instance;
            float defaultLens = camera.orthographicSize;
            foreach (string caption in new[] {"질풍대시 - 회복", "Stage clear reward", "Enemy Skeleton Homing Magician"})
            {
                bool zoomed = caption.StartsWith("Enemy", StringComparison.Ordinal);
                if (zoomed) cameraManager.SetPixelZoom(2);
                item.itemName = caption;
                drop.OnFocused(player.gameObject);
                for (int n=0;n<4;n++) yield return null;
                tooltip.name.ForceMeshUpdate();
                var glyphs=tooltip.name.textInfo;
                float peak=0;
                for (int n=0;n<glyphs.characterCount;n++)
                {
                    var glyph=glyphs.characterInfo[n];
                    if (!glyph.isVisible) continue;
                    var bottom=camera.WorldToViewportPoint(tooltip.name.transform.TransformPoint(glyph.bottomLeft));
                    var top=camera.WorldToViewportPoint(tooltip.name.transform.TransformPoint(glyph.topLeft));
                    peak=Mathf.Max(peak,(top.y-bottom.y)*PixelViewport.Height);
                }
                Check(peak>=4 && peak<=12,"world reward glyph height " + peak.ToString("0.0") + " px for " + caption);
                var corners = new Vector3[4];
                ((RectTransform)tooltip.transform).GetWorldCorners(corners);
                var plate = tooltip.GetComponent<Image>();
                var mesh = plate.canvasRenderer.GetMesh();
                Check(plate.sprite == null && plate.color.a > .5f && plate.color.a < .85f && mesh != null && mesh.vertexCount == 12,
                    "focused drop uses a single translucent cut-corner plate");
                var vertices = mesh.vertices;
                Vector3 Project(int i) => camera.WorldToScreenPoint(plate.transform.TransformPoint(vertices[i]));
                var inset = Project(0) - Project(4);
                Check(Mathf.Abs(inset.x - 1f) < .05f && Mathf.Abs(inset.y + 1f) < .05f,
                    "drop plate corner is one logical pixel at the current zoom");
                foreach (var corner in corners)
                {
                    Vector3 point = camera.WorldToViewportPoint(corner);
                    Check(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1,
                        "focused reward frame fits the logical viewport");
                }
                if (zoomed)
                {
                    Check(Mathf.Abs(camera.orthographicSize-defaultLens)>.1f && glyphs.characterCount==caption.Length,
                        "long ground reward keeps native glyphs at a different lens");
                    cameraManager.SetZoom(false);
                }
                PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/Reward-"+(zoomed?"zoom":caption[0]=='S'?"en":"ko"));
            }
        }
        finally { if(drop!=null) Object.Destroy(drop.gameObject); Object.Destroy(item); }
    }

    internal static IEnumerator VerifyFinalEquipment(Camera camera, PlayerController player)
    {
        const string directory = "Assets/SOData/Equipment/Weapons/";
        var final = AssetDatabase.LoadAssetAtPath<EquipmentSO>(directory + "Serendipity.asset");
        var previous = AssetDatabase.FindAssets("t:EquipmentSO", new[] { directory })
            .Select(id => AssetDatabase.LoadAssetAtPath<EquipmentSO>(AssetDatabase.GUIDToAssetPath(id)))
            .First(weapon => weapon.upgradeOptions.Contains(final));
        var inventory = EquipmentInventoryManager.Instance;
        var equipped = inventory.EquippedEquipment;
        var prefab = AssetDatabase.LoadAssetAtPath<EnhanceShopNPC>("Assets/Prefabs/Interactables/EnhanceShopNPC.prefab");
        var shop = Object.Instantiate(prefab, player.transform.position + Vector3.right * .6f, Quaternion.identity);
        try
        {
            inventory.EquipEquipment(EquipmentInstance.Roll(previous));
            shop.OnFocused(player.gameObject);
            var tip = shop.GetComponentInChildren<Tooltip>();
            Check(tip.name.text.EndsWith("G"), "available upgrade shows its actual cost");
            Check(inventory.UpgradeTo(final), "shop fixture upgrades to Serendipity through the equipment manager");
            for (int i = 0; i < 12; i++) yield return null;
            tip.name.ForceMeshUpdate();
            var info = tip.name.textInfo;
            Check(tip.name.text == final.equipmentName + "\n최종 장비" && info.lineCount == 2 && !tip.price.gameObject.activeInHierarchy,
                "terminal upgrade replaces price with two distinct lines while focused");
            var plate = (RectTransform)tip.transform;
            var corners = new Vector3[4];
            plate.GetWorldCorners(corners);
            foreach (var glyph in info.characterInfo.Take(info.characterCount).Where(c => c.isVisible))
            {
                var lo = tip.name.transform.TransformPoint(glyph.bottomLeft);
                var hi = tip.name.transform.TransformPoint(glyph.topRight);
                float height = (camera.WorldToViewportPoint(hi).y - camera.WorldToViewportPoint(lo).y) * PixelViewport.Height;
                Check(height > 0 && height <= 12 + 2 * tip.name.font.atlasPadding + .01f && lo.x >= corners[0].x && lo.y >= corners[0].y &&
                    hi.x <= corners[2].x && hi.y <= corners[2].y, $"terminal equipment glyph {glyph.character}: {height:F2}px bounds {lo:F3} .. {hi:F3} in {corners[0]:F3} .. {corners[2]:F3}");
            }
            PopupSystem.ShowInteractionIcon(shop.GetComponent<Collider2D>(), 0);
            var icon = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                .First(r => r.name.StartsWith("Interaction F Icon", StringComparison.Ordinal) && r.bounds.min.y >= corners[2].y &&
                    Mathf.Abs(r.bounds.center.x - plate.position.x) < .1f);
            Check(icon.bounds.min.y >= corners[2].y && corners[0].y > shop.GetComponent<SpriteRenderer>().bounds.max.y,
                "interaction key, nameplate and merchant occupy separate rows");
            yield return null;
            PresentationRegressionCheck.SaveFrame(camera.targetTexture, "ArtDirection/FinalEquipment");
        }
        finally
        {
            shop.OnLostFocus(player.gameObject);
            Object.Destroy(shop.gameObject);
            inventory.EquipEquipment(equipped);
        }
    }

    internal static IEnumerator Verify(Camera camera, Action<bool,Vector3> click)
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var target = GameObject.Find("Enemy Scarecrow");
        var health = target.GetComponentInChildren<CharacterHealth>();
        var field = WorldSparkField.Current;
        var library = Resources.Load<ParticleLibrary>("WorldAtmosphere/Particles");
        var look = WorldAtmosphere.Current;
        Check(look != null && look.ProfileSource == "마을","village camera resolves its own atmosphere");
        var terrain = Object.FindFirstObjectByType<WorldFloorMask>();
        Check(terrain != null && terrain.Texture != null && terrain.Texture.filterMode==FilterMode.Point,"uploaded point-filtered floor mask");
        var quality = library.density;
        var simulation = library.simulation;
        var original = player.transform.position;
        var oldRate = Time.captureFramerate;
        var random = UnityEngine.Random.state;
        var lamp = new GameObject("Verification torch",typeof(Light2D));
        var light = lamp.GetComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.pointLightOuterRadius=3.2f; light.intensity=1.8f; light.color=new Color(1,.67f,.38f);
        lamp.transform.position = target.transform.position + new Vector3(1.2f,1.3f);
        lamp.AddComponent<GroundShadowLight>();
        var paths = new[]{"Assets/Prefabs/Enemy/Monster Melee_Doll.prefab","Assets/Prefabs/Enemy/Elite/Archer Elite.prefab"};
        var cast = new GameObject[paths.Length];
        var hud = Object.FindFirstObjectByType<PlayerStateUI>();
        var testLabel = new GameObject("Glyph playback check",typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));
        testLabel.transform.SetParent(hud.transform,false);
        var text=testLabel.GetComponent<TextMeshProUGUI>();
        text.rectTransform.sizeDelta=new Vector2(150,32);
        text.raycastTarget=false;
        PixelText.Configure(text,12);
        var animator=testLabel.AddComponent<TMPTextEffectPlayer>();
        var settings=CameraSettingsSO.Instance;
        float fogBefore=settings.mistStrength, bloomBefore=settings.bloomStrength;
        bool motionBefore=UISkinSettings.Active.motionEnabled;
        int hits=0,finishers=0,swings=0;
        int phase=0,missesWithDamage=0;
        void Hit(CharacterHealth victim,CharacterStat source,DamageInfo info,bool critical)
        {
            if (victim!=health || !DamageRules.IsPlayerSourced(info.category)) return;
            hits++;
            if (phase==2) missesWithDamage++;
            if (info.feedbackComboFinish) finishers++;
        }
        var melee=player.GetComponent<MeleeCombatController>();
        void Swing(int step) => swings++;
        try
        {
            Time.captureFramerate=30;
            UnityEngine.Random.InitState(907);
            var idle = VerifyPanelIdle(hud);
            while (idle.MoveNext()) yield return idle.Current;
            animator.SetText("<fx=Warning>경고</fx> <wave>Critical 128</wave>");
            for(int i=0;i<12;i++) yield return null;
            int rebuilds=animator.MeshGenerations;
            for(int i=0;i<15;i++) yield return null;
            Check(text.text=="경고 Critical 128" && rebuilds>0 && animator.MeshGenerations==rebuilds,"mixed-language motion plays without another full mesh generation");
            var spans=new System.Collections.Generic.List<TextEffectRange>();
            var rich=TextEffectParser.Parse("<fx=Warning>A<rainbow><sprite=0>B</rainbow></fx>",spans);
            Check(rich=="A<sprite=0>B" && spans.Count==2 && spans[0].length==rich.Length && spans[1].startIndex==1,"nested motion tags preserve inline icon indices");
            // Assign as a localization event would, without calling the motion player.
            text.text = "<type=0.2><fx=Enchantment><color=#62DDB7>서리</color></fx> 서린 문장.</type>";
            for (int i = 0; i < 2; i++) yield return null;
            byte AlphaAt(int at)
            {
                var ch = text.textInfo.characterInfo[at];
                return text.textInfo.meshInfo[ch.materialReferenceIndex].colors32[ch.vertexIndex].a;
            }
            int last = text.textInfo.characterCount - 1;
            Check(!text.text.Contains("<type") && text.text.Contains("<color=") && last > 4 && AlphaAt(last) == 0,
                "global typewriter reserves the whole localized line and hides unrevealed glyphs");
            animator.PlayPreset("Focus");
            for (int i = 0; i < 3; i++) yield return null;
            Check(AlphaAt(last) == 0, "button focus preserves the in-progress reveal");
            animator.FinishReveal();
            for (int i = 0; i < 3; i++) yield return null;
            Check(AlphaAt(last) > 0, $"skip reveals the final glyph immediately (alpha={AlphaAt(last)}, glyph={text.textInfo.characterInfo[last].character}, visible={text.textInfo.characterInfo[last].isVisible}, generations={animator.MeshGenerations})");
            text.text = "Plain replacement";
            for (int i = 0; i < 3; i++) yield return null;
            Check(text.textInfo.characterCount == 17 && AlphaAt(16) > 0, "new plain text clears the previous span");
            animator.SetText("<fx=Missing>획득</fx>");
            Check(text.text=="획득","unknown preset preserves the text");
            animator.SetText("<fx=Wave><b>Reward</b> 획득</fx>");
            text.rectTransform.sizeDelta=new Vector2(62,40);
            for(int i=0;i<5;i++) yield return null;
            Check(text.textInfo.characterCount>5 && text.text.Contains("<b>"),"rich text and a resized mixed-language label preserve glyphs");
            UISkinSettings.Active.motionEnabled=false;
            animator.SetText("<fx=Warning>경고 Warning</fx>");
            for(int i=0;i<4;i++) yield return null;
            Check(text.text=="경고 Warning","reduced-motion warning retains its message");
            UISkinSettings.Active.motionEnabled=motionBefore;
            testLabel.SetActive(false);
            testLabel.SetActive(true);
            animator.SetText("다음 Target");
            for(int i=0;i<3;i++) yield return null;
            Check(text.textInfo.characterCount==9,"pooled label displays the next text");
            testLabel.SetActive(false);
            for(int i=0;i<cast.Length;i++)
            {
                cast[i]=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]),target.transform.position + new Vector3(i==0?-2.5f:2.5f,1.1f),Quaternion.identity);
                var actorBody=cast[i].GetComponent<BaseEntity>();
                actorBody.enabled=false;
                if(cast[i].TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent)) agent.enabled=false;
                var sr=actorBody.SpriteRenderer;
                float density=sr.transform.TransformVector(Vector3.right).magnitude * 32f / sr.sprite.pixelsPerUnit;
                Check(Mathf.Abs(density-1)<.01f,cast[i].name+" has one native pixel per texel at base zoom");
            }
            player.transform.position=target.transform.position+Vector3.left*1.1f;
            for (int i=0;i<24;i++) yield return null;
            DamageEventBus.OnAttackHitResolved+=Hit;
            melee.OnAttackExecuted+=Swing;
            string folder=Path.Combine(ProjectVerification.Output,"ArtDirection");
            Directory.CreateDirectory(folder);
            Debug.Log($"[ArtDirection] controls blocked={player.IsInputBlocked}, attacking={melee.IsAttacking}, state={player.GetPlayerState()}, scale={Time.timeScale}");
            for (int frame=0;frame<210;frame++)
            {
                if (frame==12 || frame==53) click(true,target.transform.position);
                if (frame==15 || frame==124) click(false,target.transform.position);
                if (frame==145) player.GetComponent<Rigidbody2D>().position = (Vector2)target.transform.position + Vector2.left*4.5f;
                if (frame==165) { phase=2; click(true,player.transform.position+Vector3.left*3); }
                if (frame==168) click(false,player.transform.position+Vector3.left*3);
                if (frame==207) phase=3;
                if (frame%3==0) PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/Combat"+(frame/3).ToString("D3"));
                yield return null;
            }
            Check(swings>=3 && hits>=2 && finishers>0 && missesWithDamage==0,$"actual combat input: {swings} swings, {hits} hits, {finishers} finishers, {missesWithDamage} false confirmations");
            foreach (var variant in new[]{"Ambient","HeavyFog"})
            {
                lamp.SetActive(false);
                settings.mistStrength=variant=="HeavyFog" ? .8f : 0;
                settings.bloomStrength=variant=="HeavyFog" ? bloomBefore : 0;
                player.GetComponent<Rigidbody2D>().position=(Vector2)target.transform.position+Vector2.left*1.1f;
                for(int n=0;n<42;n++) yield return null;
                UnityEngine.Random.InitState(907);
                phase=0;
                int initialHits=hits, initialFinishers=finishers;
                Directory.CreateDirectory(Path.Combine(folder,variant));
                for(int frame=0;frame<210;frame++)
                {
                    if(frame==12 || frame==53) click(true,target.transform.position);
                    if(frame==15 || frame==124) click(false,target.transform.position);
                    if(frame==145) player.GetComponent<Rigidbody2D>().position=(Vector2)target.transform.position+Vector2.left*4.5f;
                    if(frame==165) {phase=2;click(true,player.transform.position+Vector3.left*3);}
                    if(frame==168) click(false,player.transform.position+Vector3.left*3);
                    if(frame%3==0) PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/"+variant+"/Combat"+(frame/3).ToString("D3"));
                    yield return null;
                }
                Check(hits>initialHits && finishers>initialFinishers && missesWithDamage==0,variant+" input replay with confirmed finisher and miss");
            }
            phase=0;
            settings.mistStrength=fogBefore; settings.bloomStrength=bloomBefore;
            lamp.SetActive(true);
            foreach (WorldAtmosphere.View view in Enum.GetValues(typeof(WorldAtmosphere.View)))
            {
                look.Preview=view;
                for(int n=0;n<4;n++) yield return null;
                PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/Pass-"+view);
            }
            look.Preview=WorldAtmosphere.View.Composite;
            var comparison=AtmospherePassComparison.Capture(camera,look);
            while(comparison.MoveNext()) yield return comparison.Current;
            var portal=FloorPortalGraphic.Capture(player);
            Check(portal!=null,"portal has a capturable player silhouette");
            try
            {
                portal.SetProgress(.7f); yield return null; yield return null;
                Check(look.ProfileSource=="포탈","active portal owns atmosphere");
            }
            finally {if(portal!=null) Object.Destroy(portal.gameObject);}
            for(int n=0;n<6;n++) yield return null;
            Check(look.ProfileSource=="마을","cancelled portal restores village atmosphere");
            var floorCheck=VerifyFloor(terrain);
            while(floorCheck.MoveNext()) yield return floorCheck.Current;
            var bounce=VerifyTerrainBounce();while(bounce.MoveNext()) yield return bounce.Current;
            var ui=VerifyUiParticles(hud,camera);while(ui.MoveNext()) yield return ui.Current;
            var polish=VerifyCombatPolish(camera,player,target,click); while(polish.MoveNext()) yield return polish.Current;
            var actorPolish=VerifyActorPolish(camera,player); while(actorPolish.MoveNext()) yield return actorPolish.Current;
            var dialogue=VerifyDialogue(click,player.transform.position);while(dialogue.MoveNext()) yield return dialogue.Current;
            for(int mode=0;mode<=1;mode++)
            {
                library.simulation=(ParticleLibrary.Simulation)mode;
                foreach (ParticleLibrary.Density q in Enum.GetValues(typeof(ParticleLibrary.Density)))
                {
                    library.density=q;
                    yield return null;
                    int emitted=field.Emitted, dropped=field.BudgetDrops;
                    WorldSparkField.Emit(WorldSparkField.Kind.Pollen,target.transform.position,Vector2.up,200,907);
                    int decor=field.Emitted-emitted;
                    WorldSparkField.Emit(WorldSparkField.Kind.EnemyHit,target.transform.position,Vector2.right,7,907);
                    Check(decor<=library.FrameBudget/2 && field.Emitted-emitted-decor==7 && field.BudgetDrops>dropped,$"{q} backend {mode}: reserved hit capacity under saturation");
                    for(int i=0;i<4;i++) yield return null;
                    PresentationRegressionCheck.SaveFrame(camera.targetTexture,$"ArtDirection/World-{mode}-{q}");
                }
            }
        }
        finally
        {
            DamageEventBus.OnAttackHitResolved-=Hit;
            melee.OnAttackExecuted-=Swing;
            library.density=quality; library.simulation=simulation;
            UISkinSettings.Active.motionEnabled=motionBefore;
            settings.mistStrength=fogBefore; settings.bloomStrength=bloomBefore;
            look.Preview=WorldAtmosphere.View.Composite;
            Time.captureFramerate=oldRate;
            UnityEngine.Random.state=random;
            player.transform.position=original;
            foreach(var go in cast) if(go!=null) Object.Destroy(go);
            Object.Destroy(lamp); Object.Destroy(testLabel);
        }
    }
}
