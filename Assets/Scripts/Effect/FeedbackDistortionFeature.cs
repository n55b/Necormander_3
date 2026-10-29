using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>강한 적중 때만 실행하는 국소 굴절. 판정/카메라 Transform은 변경하지 않는다.</summary>
public sealed class FeedbackDistortionFeature : ScriptableRendererFeature
{
    [SerializeField] private Material material;
    private DistortionPass pass;
    private struct Pulse { public Vector3 position; public float started; }
    private static readonly Pulse[] pulses = new Pulse[8];
    private static readonly Vector4[] rings = new Vector4[8];
    private static readonly int RingsId = Shader.PropertyToID("_Rings");
    private static readonly int RingCountId = Shader.PropertyToID("_RingCount");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static int cursor;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { cursor=0; for(int i=0;i<pulses.Length;i++) pulses[i].started=-100f; }
    public static void Add(Vector3 position) { pulses[cursor++%8]=new Pulse {position=position,started=Time.time}; }
    public override void Create() { pass=new DistortionPass(material); }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        if(material==null||!Application.isPlaying||data.cameraData.cameraType!=CameraType.Game||!PolishFeedback.Enabled(FeedbackCue.Distortion)) return;
        var recipe=PolishFeedback.Catalog.Find(FeedbackCue.Distortion);
        var camera=data.cameraData.camera; int count=0;
        for(int i=0;i<8;i++)
        {
            float age=Time.time-pulses[i].started;
            if(age<0||age>=recipe.duration) continue;
            var pos=camera.WorldToViewportPoint(pulses[i].position);
            if(pos.z<=0) continue;
            float t=age/recipe.duration;
            float radius=recipe.radius/(camera.orthographicSize*2)*t;
            // 화면 밖에서도 링이 화면까지 닿을 때는 유지한다. 닿지 않을 때만 전체 화면 패스를 생략한다.
            if(!CanReachViewport(pos,camera.aspect,radius)) continue;
            rings[count++]=new Vector4(pos.x,pos.y,radius,(1-t)*recipe.intensity*PolishFeedback.Catalog.intensity*.002f);
        }
        if(count==0) return;
        material.SetVectorArray(RingsId,rings); material.SetInt(RingCountId,count);
        material.SetFloat(AspectId,camera.aspect);
        renderer.EnqueuePass(pass);
    }
    internal static bool CanReachViewport(Vector2 position,float aspect,float radius)
    {
        float dx=Mathf.Max(0f,Mathf.Max(-position.x,position.x-1f))*aspect;
        float dy=Mathf.Max(0f,Mathf.Max(-position.y,position.y-1f));
        float extent=radius+.025f; // shader의 smoothstep 띠 폭
        return dx*dx+dy*dy<=extent*extent;
    }
    protected override void Dispose(bool disposing) { pass?.Dispose(); }
    private sealed class DistortionPass : ScriptableRenderPass
    {
        private readonly Material material;
        private RTHandle scratch;
        public DistortionPass(Material source) { material=source; renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing; requiresIntermediateTexture=true; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var resources=frame.Get<UniversalResourceData>();
            if(resources.isActiveTargetBackBuffer) return;
            var source=resources.activeColorTexture;
            var desc=graph.GetTextureDesc(source); desc.name="국소 타격 굴절"; desc.clearBuffer=false;
            var destination=graph.CreateTexture(desc);
            graph.AddBlitPass(new UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils.BlitMaterialParameters(source,destination,material,0),"타격 굴절");
            resources.cameraColor=destination;
        }
        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var descriptor=data.cameraData.cameraTargetDescriptor; descriptor.depthBufferBits=0; descriptor.msaaSamples=1;
            RenderingUtils.ReAllocateHandleIfNeeded(ref scratch,descriptor,FilterMode.Point,TextureWrapMode.Clamp,name:"타격 굴절 복사");
            var cmd=CommandBufferPool.Get("타격 굴절");
            var source=data.cameraData.renderer.cameraColorTargetHandle;
            Blitter.BlitCameraTexture(cmd,source,scratch,material,0); Blitter.BlitCameraTexture(cmd,scratch,source);
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
        public void Dispose() { scratch?.Release(); }
    }
}
