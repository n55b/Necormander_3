using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

/// <summary>신규 연출 프리팹 배선 도구. 기존 기획 수치는 다시 실행해도 덮어쓰지 않는다.</summary>
public static class FeedbackSetup
{
    public const string Folder="Assets/Prefabs/Effect/Feedback";
    public const string CatalogPath="Assets/Resources/FeedbackCatalog.asset";
    [MenuItem("Tools/연출/프리팹 배선 확인 및 누락 연결")]
    public static void Setup()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var burst=AssetDatabase.LoadAssetAtPath<FeedbackBurst>(Folder+"/Feedback Burst.prefab")??BuildBurst();
        var overlay=AssetDatabase.LoadAssetAtPath<FeedbackOverlay>(Folder+"/Feedback Overlay.prefab")??BuildOverlay();
        var catalog=AssetDatabase.LoadAssetAtPath<FeedbackCatalog>(CatalogPath);
        bool creating = catalog==null;
        if(creating)
        {
            catalog=ScriptableObject.CreateInstance<FeedbackCatalog>();
            var matches=Regex.Matches(File.ReadAllText("Docs/이펙트_후보_2026-10-26.txt"),@"(?m)^(\d{2})\s+★?\s*([^:：\r\n]+)[:：](.*)$")
                .Cast<Match>().ToDictionary(m=>int.Parse(m.Groups[1].Value));
            catalog.effects=Enum.GetValues(typeof(FeedbackCue)).Cast<FeedbackCue>().Select(c=>Default(c,burst,matches.TryGetValue((int)c,out var m)?m:null)).ToArray();
            catalog.overlayPrefab=overlay;
            AssetDatabase.CreateAsset(catalog,CatalogPath);
        }
        WirePrefab("Assets/Prefabs/Player Melee.prefab",go=>
        {
            var feedback=go.GetComponent<PlayerFeedback>()??go.AddComponent<PlayerFeedback>();
            Set(feedback,"player",go.GetComponent<PlayerController>()); Set(feedback,"combat",go.GetComponent<MeleeCombatController>());
            Set(feedback,"dodge",go.GetComponent<MeleeDodgeController>()); Set(feedback,"skills",go.GetComponent<PlayerSkillController>());
        });
        WirePrefab("Assets/Prefabs/GameManager.prefab",go=>
        {
            if(go.GetComponentInChildren<FeedbackOverlay>(true)==null)
                PrefabUtility.InstantiatePrefab(overlay.gameObject,go.transform);
        });
        Surface("Assets/Prefabs/Interactables/FloorProceedPortalPrefab.prefab",FeedbackSurface.Kind.Portal);
        foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            if(path.Contains("/Grass/")) Surface(path,FeedbackSurface.Kind.Foliage);
            else if(path.EndsWith("/Torchlight.prefab")) Surface(path,FeedbackSurface.Kind.Ambient);
            else if(path.EndsWith("/GroundItem.prefab")||path.EndsWith("/GroundMinion.prefab")) Surface(path,FeedbackSurface.Kind.Drop);
        }
        foreach(var guid in creating ? AssetDatabase.FindAssets("t:MainMinionDataSO") : Array.Empty<string>())
        {
            var data=AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if(data.feedbackTier!=0) continue;
            data.feedbackTier=data.name.Contains("Champion")||data.name.Contains("Warlord")?3:data.name.Contains("Knight")||data.name.Contains("Warrior")?1:0;
            data.feedbackColor=data.name.Contains("Warrior")||data.name.Contains("Warlord")?new Color(.4f,.9f,1f):new Color(.95f,.82f,.45f);
            EditorUtility.SetDirty(data);
        }
        var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/Renderer2D.asset");
        if(!renderer.rendererFeatures.Any(f=>f is FeedbackDistortionFeature))
        {
            var feature=ScriptableObject.CreateInstance<FeedbackDistortionFeature>(); feature.name="타격 국소 굴절 (전체 연출 설정 76번)";
            var material=new Material(Shader.Find("Necromancer/Feedback Distortion"));
            AssetDatabase.CreateAsset(material,Folder+"/Feedback Distortion.mat");
            Set(feature,"material",material);
            AssetDatabase.AddObjectToAsset(feature,renderer); renderer.rendererFeatures.Add(feature); EditorUtility.SetDirty(renderer);
        }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Validate();
    }
    private static FeedbackRecipe Default(FeedbackCue cue,FeedbackBurst burst,Match text)
    {
        var r=new FeedbackRecipe {cue=cue,title=text!=null?text.Groups[2].Value:cue.ToString(),description=text!=null?text.Groups[3].Value:"개별 켜기/강도 조절",
            prefab=burst,color=new Color(.5f,.88f,1f),shape=FeedbackShape.Rays};
        int id=(int)cue;
        if(id<=8) { r.scaleWithTier=true; r.color=new Color(1f,.88f,.58f); r.minimumTier=id==1||id==7?0:1; }
        if(id>=17&&id<=24) { r.color=new Color(.75f,.7f,.6f,.85f); r.radius=.4f; r.count=4; r.duration=.26f; r.shape=FeedbackShape.Dots; }
        if(id>=25&&id<=32) { r.color=new Color(.35f,.85f,1f); r.scaleWithTier=true; }
        if(id>=41&&id<=48) { r.radius=.22f; r.count=3; r.duration=.16f; r.color=new Color(1,.65f,.38f); r.interval=.025f; }
        if(id>=49&&id<=56) { r.color=new Color(1f,.65f,.28f); r.radius=1.1f; r.duration=.25f; }
        if(id>=57&&id<=64) { r.color=new Color(.95f,.86f,.45f); r.duration=.5f; }
        switch(cue)
        {
            case FeedbackCue.DirectionHit: r.count=5; r.radius=.5f; break;
            case FeedbackCue.ComboFinish: r.shape=FeedbackShape.Cross; r.count=2; r.radius=.9f; break;
            case FeedbackCue.CriticalEcho: r.shape=FeedbackShape.Cross; r.count=4; r.delay=.055f; r.color=Color.white; break;
            case FeedbackCue.MaterialHit: r.shape=FeedbackShape.Dots; r.count=4; r.radius=.4f; break;
            case FeedbackCue.WeaponTrail: r.shape=FeedbackShape.Arc; r.count=1; r.radius=.7f; r.duration=.14f; break;
            case FeedbackCue.HeavyDust: r.shape=FeedbackShape.Dots; r.minimumTier=2; r.count=8; r.radius=.9f; break;
            case FeedbackCue.MultiHit: r.count=3; r.radius=.32f; r.interval=.015f; break;
            case FeedbackCue.KillHit: r.shape=FeedbackShape.Cross; r.count=2; r.radius=.75f; break;
            case FeedbackCue.DashStart: r.shape=FeedbackShape.Rays; r.count=7; r.radius=.65f; r.duration=.18f; break;
            case FeedbackCue.DashEnd: r.shape=FeedbackShape.Ring; r.count=1; r.radius=.6f; break;
            case FeedbackCue.DashReady: r.shape=FeedbackShape.Cross; r.count=2; r.radius=.2f; r.color=new Color(.5f,1,1); break;
            case FeedbackCue.DashTrail: r.shape=FeedbackShape.Slash; r.count=1; r.radius=.22f; r.color=new Color(.4f,.85f,1,.5f); r.interval=.025f; break;
            case FeedbackCue.MinionCommand: case FeedbackCue.MinionReturn: case FeedbackCue.SoulReturn:
                r.shape=FeedbackShape.Link; r.count=1; r.radius=.45f; r.duration=.35f; break;
            case FeedbackCue.MinionAppear: r.shape=FeedbackShape.Inward; r.count=9; r.duration=.35f; r.radius=.7f; break;
            case FeedbackCue.MinionReady: r.shape=FeedbackShape.Arc; r.count=1; r.radius=.3f; break;
            case FeedbackCue.MarionetteHit: r.shape=FeedbackShape.Slash; r.count=2; r.radius=.6f; r.color=new Color(.8f,.45f,1); r.interval=.015f; break;
            case FeedbackCue.MinionFamily: r.shape=FeedbackShape.Arc; r.count=2; r.minimumTier=2; break;
            case FeedbackCue.Evolve: r.shape=FeedbackShape.Inward; r.count=12; r.radius=1.3f; r.duration=.8f; r.scaleWithTier=false; break;
            case FeedbackCue.Heal: r.shape=FeedbackShape.Cross; r.count=2; r.color=new Color(.5f,1,.55f); r.drift=Vector2.up; r.duration=.65f; break;
            case FeedbackCue.LowHealth: r.prefab=null; r.color=new Color(.65f,.05f,.12f,.09f); break;
            case FeedbackCue.ProjectileTrail: case FeedbackCue.ReflectedTrail: case FeedbackCue.HomingTrail:
                r.shape=FeedbackShape.Slash; r.count=1; r.width=.02f; r.radius=.15f;
                if(cue!=FeedbackCue.ProjectileTrail) r.color=new Color(.45f,.85f,1,.75f); break;
            case FeedbackCue.GroundHit: r.shape=FeedbackShape.Dots; r.duration=.35f; r.color=new Color(.5f,.4f,.35f,.7f); break;
            case FeedbackCue.PierceHit: r.shape=FeedbackShape.Slash; r.count=2; break;
            case FeedbackCue.ProjectileExpire: r.shape=FeedbackShape.Inward; r.duration=.2f; break;
            case FeedbackCue.AimLock: r.shape=FeedbackShape.Cross; r.count=2; r.radius=.4f; r.duration=.12f; r.color=Color.white; break;
            case FeedbackCue.AttackRelease: r.shape=FeedbackShape.Rays; r.count=6; r.radius=.5f; r.duration=.1f; break;
            case FeedbackCue.ChargeStart: r.shape=FeedbackShape.Arc; r.count=1; r.radius=.9f; break;
            case FeedbackCue.ChargeWall: r.shape=FeedbackShape.Rays; r.count=10; r.radius=1.3f; break;
            case FeedbackCue.BossLand: r.shape=FeedbackShape.Ring; r.count=1; r.radius=2; r.duration=.32f; break;
            case FeedbackCue.CounterSuccess: r.shape=FeedbackShape.Cross; r.count=4; r.color=new Color(.5f,1,1); break;
            case FeedbackCue.BossVanish: r.shape=FeedbackShape.Inward; r.count=12; r.duration=.3f; break;
            case FeedbackCue.BossDeath: r.shape=FeedbackShape.Rays; r.count=14; r.radius=2; r.duration=.7f; break;
            case FeedbackCue.ChestOpen: r.shape=FeedbackShape.Rays; r.count=9; r.drift=Vector2.up*.5f; r.delay=.2f; r.radius=.9f; break;
            case FeedbackCue.DropLand: r.shape=FeedbackShape.Ring; r.count=1; r.radius=.3f; r.duration=.25f; break;
            case FeedbackCue.RarityGlow: r.shape=FeedbackShape.Arc; r.count=1; r.radius=.4f; r.duration=.8f; r.intensity=.5f; break;
            case FeedbackCue.DropGlimmer: r.shape=FeedbackShape.Cross; r.count=2; r.radius=.14f; r.width=.03f; r.duration=.45f; r.intensity=.8f; r.color=new Color(1f,.88f,.55f,.9f); break;
            case FeedbackCue.Pickup: r.shape=FeedbackShape.Inward; r.count=5; r.duration=.45f; break;
            case FeedbackCue.StackUp: case FeedbackCue.StackMax: r.shape=FeedbackShape.Cross; r.count=2; break;
            case FeedbackCue.Upgrade: r.shape=FeedbackShape.Ring; r.count=1; r.radius=1.2f; r.duration=.65f; break;
            case FeedbackCue.Purchase: r.shape=FeedbackShape.Dots; r.count=6; r.drift=Vector2.up*.4f; break;
            case FeedbackCue.PortalAmbient: r.shape=FeedbackShape.Inward; r.count=6; r.radius=.65f; r.duration=1.2f; r.intensity=.5f; break;
            case FeedbackCue.PortalArrive: r.shape=FeedbackShape.Ring; r.count=1; r.radius=.8f; r.duration=.4f; break;
            case FeedbackCue.RoomClear: r.shape=FeedbackShape.Ring; r.count=1; r.radius=1.5f; r.duration=.6f; break;
            case FeedbackCue.DoorOpen: r.shape=FeedbackShape.Dots; r.count=6; r.color=new Color(.7f,.7f,.7f); break;
            case FeedbackCue.WaterRipple: r.shape=FeedbackShape.Ring; r.count=1; r.radius=.45f; r.duration=.8f; r.intensity=.4f; break;
            case FeedbackCue.Foliage: r.shape=FeedbackShape.Rays; r.count=3; r.radius=.35f; r.color=new Color(.55f,.85f,.35f); break;
            case FeedbackCue.AmbientDust: r.shape=FeedbackShape.Dots; r.count=3; r.drift=Vector2.up*.8f; r.duration=1.8f; r.intensity=.35f; break;
            case FeedbackCue.PropBreak: r.shape=FeedbackShape.Rays; r.count=10; r.radius=1.2f; r.color=new Color(.85f,.55f,.3f); break;
            case FeedbackCue.CameraKick: case FeedbackCue.HitStop: case FeedbackCue.SlowMotion: r.prefab=null; break;
            case FeedbackCue.Distortion: r.prefab=null; r.minimumTier=3; r.radius=1.5f; r.duration=.2f; break;
            case FeedbackCue.SkillReady: r.shape=FeedbackShape.Cross; r.count=2; r.radius=.3f; r.color=new Color(.5f,1,1); break;
            case FeedbackCue.HurtDirection: r.prefab=null; r.color=new Color(.65f,.05f,.12f,.25f); r.duration=.25f; break;
            case FeedbackCue.ImpactSound:
                r.prefab=null; r.sound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Sound/SFX/Hit/Enemy_Hit_1.wav"); r.volume=.16f; break;
            case FeedbackCue.SoundVariation: r.prefab=null; break;
        }
        return r;
    }
    private static FeedbackBurst BuildBurst()
    {
        var root=new GameObject("Feedback Burst"); var burst=root.AddComponent<FeedbackBurst>();
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/Feedback/Slash Streak.mat");
        var lines=new LineRenderer[16];
        for(int i=0;i<lines.Length;i++)
        {
            var go=new GameObject($"빛 조각 {i+1:00}"); go.transform.SetParent(root.transform,false);
            var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=material; line.useWorldSpace=false;
            line.positionCount=2; line.widthMultiplier=1f/32f; line.sortingOrder=5000; line.numCapVertices=0;
            line.startColor=line.endColor=Color.white; line.enabled=false; lines[i]=line;
        }
        var icon=new GameObject("원본 크기 아이콘").AddComponent<SpriteRenderer>(); icon.transform.SetParent(root.transform,false); icon.sortingOrder=5001; icon.enabled=false;
        Set(burst,"icon",icon); SetArray(burst,"lines",lines);
        var saved=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/Feedback Burst.prefab"); Object.DestroyImmediate(root); return saved.GetComponent<FeedbackBurst>();
    }
    private static FeedbackOverlay BuildOverlay()
    {
        var root=new GameObject("Feedback Overlay",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(FeedbackOverlay));
        var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=80;
        var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(960,540); scaler.matchWidthOrHeight=.5f;
        var overlay=root.GetComponent<FeedbackOverlay>(); Set(overlay,"canvasRect",root.transform);
        var motes=new Image[16]; var edges=new Image[4];
        for(int i=0;i<motes.Length;i++) { motes[i]=ImageChild(root.transform,$"아이콘 비행 {i+1:00}"); motes[i].enabled=false; }
        for(int i=0;i<4;i++)
        {
            edges[i]=ImageChild(root.transform,new[]{"위 피격","오른쪽 피격","아래 피격","왼쪽 피격"}[i]);
            var rect=edges[i].rectTransform;
            rect.anchorMin=i==0?new Vector2(0,.975f):i==1?new Vector2(.985f,0):Vector2.zero;
            rect.anchorMax=i==2?new Vector2(1,.025f):i==3?new Vector2(.015f,1):Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero; edges[i].enabled=false;
        }
        SetArray(overlay,"motes",motes); SetArray(overlay,"edges",edges);
        string[] names={"bagAnchor","healthAnchor","skillAnchor","goldAnchor"};
        var points=new[]{new Vector2(.95f,.1f),new Vector2(.5f,.085f),new Vector2(.55f,.16f),new Vector2(.95f,.2f)};
        for(int i=0;i<names.Length;i++) { var rect=new GameObject(names[i],typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(root.transform,false); rect.anchorMin=rect.anchorMax=points[i]; rect.anchoredPosition=Vector2.zero; Set(overlay,names[i],rect); }
        var saved=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/Feedback Overlay.prefab"); Object.DestroyImmediate(root); return saved.GetComponent<FeedbackOverlay>();
    }
    private static Image ImageChild(Transform parent,string name)
    {
        var image=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<Image>(); image.transform.SetParent(parent,false);
        image.rectTransform.sizeDelta=new Vector2(10,10); image.raycastTarget=false; return image;
    }
    private static void Surface(string path,FeedbackSurface.Kind kind) => WirePrefab(path,go=>
    {
        if(go.GetComponent<FeedbackSurface>()!=null) return;
        var component=go.AddComponent<FeedbackSurface>(); var so=new SerializedObject(component); so.FindProperty("kind").enumValueIndex=(int)kind; so.ApplyModifiedPropertiesWithoutUndo();
    });
    private static void WirePrefab(string path,Action<GameObject> action)
    {
        var root=PrefabUtility.LoadPrefabContents(path);
        try { action(root); PrefabUtility.SaveAsPrefabAsset(root,path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static void Set(Object target,string field,Object value) { var so=new SerializedObject(target); so.FindProperty(field).objectReferenceValue=value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetArray(Object target,string field,Object[] values) { var so=new SerializedObject(target); var p=so.FindProperty(field); p.arraySize=values.Length; for(int i=0;i<values.Length;i++) p.GetArrayElementAtIndex(i).objectReferenceValue=values[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
    [MenuItem("Tools/연출/전체 연출 배선 검증")]
    public static void Validate()
    {
        var c=AssetDatabase.LoadAssetAtPath<FeedbackCatalog>(CatalogPath);
        Require(c!=null&&c.effects.Length==66,"66개 효과 설정");
        Require(c.effects.Select(r=>r.cue).Distinct().Count()==66,"중복 없는 번호");
        foreach(var r in c.effects) Require(!string.IsNullOrEmpty(r.title)&&r.duration>0,"효과 이름/수명 "+r.cue);
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab").GetComponent<PlayerFeedback>();
        Require(player!=null,"플레이어 관찰자");
        foreach(var field in new[]{"player","combat","dodge","skills"}) Require(new SerializedObject(player).FindProperty(field).objectReferenceValue!=null,"플레이어 참조 "+field);
        var overlay=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameManager.prefab").GetComponentInChildren<FeedbackOverlay>(true);
        Require(overlay!=null,"화면 프리팹 실제 연결");
        Require(overlay.GetComponentsInChildren<Image>(true).All(i=>!i.raycastTarget),"연출 UI 클릭 통과");
        Require(overlay.GetComponentsInChildren<GraphicRaycaster>(true).Length==0,"연출 UI 입력 미개입");
        var burst=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Feedback Burst.prefab");
        Require(burst.GetComponentsInChildren<LineRenderer>(true).Length==16,"선 프리팹 16개");
        Require(burst.GetComponentsInChildren<Transform>(true).All(t=>t.localScale==Vector3.one),"원본 그림 1배");
        Debug.Log("[FeedbackSetup] PASS: 66개 설정, 성장 4단계, 실제 프리팹 배선, 입력 비차단, 원본 배율 검증");
    }
    private static void Require(bool value,string label) { if(!value) throw new Exception("[FeedbackSetup] 실패: "+label); }
}
