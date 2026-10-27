using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

/// <summary>배선·기존 연출 회귀 검사와 격리된 Play Mode 수명/예산/일시정지 검사.</summary>
public static class FeedbackValidation
{
    private const string Key="FeedbackValidation.Running";
    private static int stage,index,peak;
    private static double next;
    private static FeedbackCatalog catalog;
    private static string snapshot;
    private static Camera camera;
    private static HitStopManager timeEffects;
    private static FeedbackOverlay overlay;
    private static FeedbackCue[] cues;
    private static Color32[] beforeDistortion;
    public static void Run()
    {
        try
        {
            FeedbackSetup.Validate();
            DesignerInspectorCheck.Run();
            FeedbackCheck.Run();
            var shader=Shader.Find("Necromancer/Feedback Distortion");
            if(UnityEditor.ShaderUtil.ShaderHasError(shader)) throw new Exception("굴절 셰이더 오류: "+string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(Key,true);
            EditorApplication.update-=Tick;
            EditorApplication.update+=Tick;
            EditorApplication.EnterPlaymode();
        }
        catch(Exception error) { Fail(error); }
    }
    [InitializeOnLoadMethod]
    private static void Resume() { if(SessionState.GetBool(Key,false)) EditorApplication.update+=Tick; }
    private static void Tick()
    {
        if(!EditorApplication.isPlaying) return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(stage==0)
            {
                catalog=PolishFeedback.Catalog; snapshot=EditorJsonUtility.ToJson(catalog);
                camera=new GameObject("격리 연출 검사 카메라").AddComponent<Camera>(); camera.tag="MainCamera";
                camera.orthographic=true; camera.orthographicSize=4f; camera.transform.position=new Vector3(0,0,-10);
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.06f,.09f,.11f);
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                overlay=Object.Instantiate(catalog.overlayPrefab);
                timeEffects=new GameObject("격리 시간 검사").AddComponent<HitStopManager>(); timeEffects.Initialize();
                cues=Enum.GetValues(typeof(FeedbackCue)).Cast<FeedbackCue>().ToArray();
                stage=1;
            }
            if(now<next) return;
            if(stage==1)
            {
                if(index<cues.Length)
                {
                    var cue=cues[index++]; PolishFeedback.Play(cue,new Vector3(index%6-3,index/6f%4-2),Vector2.right,3);
                    if(cue==FeedbackCue.Heal||cue==FeedbackCue.SkillReady||cue==FeedbackCue.Pickup) overlay.Pulse(cue,Vector3.zero);
                    peak=Mathf.Max(peak,FeedbackBurst.ActiveCount); next=now+.03; return;
                }
                next=now+2.5; stage=2; return;
            }
            if(stage==2)
            {
                Require(FeedbackBurst.ActiveCount==0,"66종 재생 후 활성 효과 잔류 없음");
                catalog.Find(FeedbackCue.DirectionHit).interval=0; catalog.burstsPerFrame=2; catalog.simultaneousBursts=3;
                for(int i=0;i<10;i++) PolishFeedback.Play(FeedbackCue.DirectionHit,Vector3.zero);
                Require(FeedbackBurst.ActiveCount==2,"프레임당 2개 제한"); stage=3; next=now+.8; return;
            }
            if(stage==3)
            {
                Require(FeedbackBurst.ActiveCount==0,"풀 반환"); catalog.burstsPerFrame=10;
                for(int i=0;i<10;i++) PolishFeedback.Play(FeedbackCue.DirectionHit,Vector3.zero);
                Require(FeedbackBurst.ActiveCount==3,"동시 3개 제한");
                catalog.enabled=false; stage=4; next=now+.1; return;
            }
            if(stage==4)
            {
                Require(FeedbackBurst.ActiveCount==0,"전체 끄기 후 활성 효과 정리");
                Require(PolishFeedback.Play(FeedbackCue.DirectionHit,Vector3.zero)==null,"전체 끄기 생성 차단");
                EditorJsonUtility.FromJsonOverwrite(snapshot,catalog);
                timeEffects.DoSlowMotion(.08f,.35f); Require(Mathf.Approximately(Time.timeScale,.35f),"감속 적용");
                stage=5; next=now+.2; return;
            }
            if(stage==5)
            {
                Require(Mathf.Approximately(Time.timeScale,1),"감속 자동 복구");
                timeEffects.DoHitStop(.04f); Time.timeScale=0; stage=6; next=now+.2; return;
            }
            if(stage==6)
            {
                Require(Time.timeScale==0,"정지 효과 종료가 UI 일시정지를 풀지 않음"); Time.timeScale=1;
                DrawPreview(); stage=7; next=now+.08; return;
            }
            if(stage==7)
            {
                var target=new RenderTexture(1200,800,24); camera.targetTexture=target; camera.Render();
                var old=RenderTexture.active; RenderTexture.active=target;
                var image=new Texture2D(1200,800,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1200,800),0,0); image.Apply();
                File.WriteAllBytes("Logs/feedback-preview.png",image.EncodeToPNG());
                camera.targetTexture=null; RenderTexture.active=old; Object.Destroy(image); target.Release(); Object.Destroy(target);
                for(int i=-9;i<=9;i++)
                {
                    var line=new GameObject("굴절 확인 격자").AddComponent<LineRenderer>();
                    line.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/Feedback/Slash Streak.mat");
                    line.positionCount=2; line.widthMultiplier=.025f;
                    line.SetPosition(0,new Vector3(i*.12f,-1,0)); line.SetPosition(1,new Vector3(i*.12f,1,0));
                    line.startColor=line.endColor=Color.white;
                }
                catalog.Find(FeedbackCue.Distortion).enabled=false;
                beforeDistortion=ReadCamera();
                catalog.Find(FeedbackCue.Distortion).enabled=true;
                FeedbackDistortionFeature.Add(Vector3.zero);
                // 첫 셰이더 컴파일에 걸린 실제 시간이 0.2초 수명을 소진하지 않도록, 중간 프레임을 고정한다.
                Time.timeScale=0;
                var pulses=(Array)typeof(FeedbackDistortionFeature).GetField("pulses",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
                for(int i=0;i<pulses.Length;i++)
                {
                    var pulse=pulses.GetValue(i);
                    pulse.GetType().GetField("started").SetValue(pulse,Time.time-.08f);
                    pulse.GetType().GetField("position").SetValue(pulse,Vector3.zero);
                    pulses.SetValue(pulse,i);
                }
                stage=8; next=now+.06; return;
            }
            if(stage==8)
            {
                var after=ReadCamera(); int changed=0;
                for(int i=0;i<after.Length;i++) if(!after[i].Equals(beforeDistortion[i])) changed++;
                Require(changed>0,"국소 굴절 실제 픽셀 변화");
                Debug.Log($"[FeedbackValidation] PASS: 66 cues, peak {peak}, lifetime cleanup, frame/active caps, disable, pooling, slow motion restore, pause ownership; distortion changed {changed} pixels. Preview: Logs/feedback-preview.png");
                Finish(0);
            }
        }
        catch(Exception error) { Fail(error); }
    }
    private static void DrawPreview()
    {
        var render=typeof(FeedbackBurst).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance);
        var recipes=new[]{FeedbackCue.DirectionHit,FeedbackCue.ComboFinish,FeedbackCue.MinionAppear,FeedbackCue.BossLand,FeedbackCue.Heal,FeedbackCue.ProjectileTrail,FeedbackCue.ChestOpen,FeedbackCue.Evolve};
        for(int i=0;i<recipes.Length;i++)
        {
            var r=catalog.Find(recipes[i]); var position=new Vector3((i%4-1.5f)*2.5f,i<4?1.8f:-1.8f);
            var burst=FeedbackBurst.Spawn(r.prefab,position,Quaternion.identity);
            burst.Begin(r,position+Vector3.up,null,3,null,null);
            burst.enabled=false; render.Invoke(burst,new object[]{.3f,true});
        }
    }
    private static Color32[] ReadCamera()
    {
        var texture=new RenderTexture(1200,800,24); camera.targetTexture=texture; camera.Render();
        var old=RenderTexture.active; RenderTexture.active=texture;
        var image=new Texture2D(1200,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1200,800),0,0); image.Apply();
        var pixels=image.GetPixels32();
        RenderTexture.active=old; camera.targetTexture=null;
        Object.Destroy(image); texture.Release(); Object.Destroy(texture); return pixels;
    }
    private static void Require(bool pass,string name) { if(!pass) throw new Exception(name); }
    private static void Fail(Exception error) { Debug.LogException(error); Finish(1); }
    private static void Finish(int code)
    {
        if(catalog!=null&&snapshot!=null) EditorJsonUtility.FromJsonOverwrite(snapshot,catalog);
        Time.timeScale=1; SessionState.SetBool(Key,false); EditorApplication.update-=Tick;
        EditorApplication.Exit(code);
    }
}
