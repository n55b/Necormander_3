using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Instantiated only by the isolated art check. No feature is serialized into the shipping renderer.
internal sealed class AtmospherePassComparison : ScriptableRendererFeature
{
    private sealed class DrawPass : ScriptableRenderPass
    {
        internal Material material;
        internal Mesh quad;
        internal int executions;
        private sealed class Draw { internal DrawPass pass; }
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
        {
            using var builder=graph.AddRasterRenderPass<Draw>("Compare atmosphere after transparents",out var data);
            data.pass=this;
            builder.SetRenderAttachment(frame.Get<UniversalResourceData>().activeColorTexture,0,AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((Draw d,RasterGraphContext ctx)=>
            {
                ctx.cmd.DrawMesh(d.pass.quad,Matrix4x4.identity,d.pass.material,0,0);
                d.pass.executions++;
            });
        }
        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context,ref RenderingData data)
        {
            var commands=CommandBufferPool.Get("Compare atmosphere after transparents");
            commands.DrawMesh(quad,Matrix4x4.identity,material,0,0);
            context.ExecuteCommandBuffer(commands);
            CommandBufferPool.Release(commands);
            executions++;
        }
    }
    private Camera target;
    private DrawPass pass;
    public override void Create() => pass??=new DrawPass{renderPassEvent=RenderPassEvent.AfterRenderingTransparents};
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
    {
        if(data.cameraData.camera==target && pass.material!=null) renderer.EnqueuePass(pass);
    }
    internal static IEnumerator Capture(Camera camera,WorldAtmosphere atmosphere)
    {
        var rendererData=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline))
            .Select(AssetDatabase.LoadAssetAtPath<Renderer2DData>).FirstOrDefault(d=>d!=null);
        if(rendererData==null) throw new System.InvalidOperationException("2D renderer data unavailable.");
        var surface=atmosphere.transform.Find("World mist and bloom").GetComponent<MeshRenderer>();
        var feature=CreateInstance<AtmospherePassComparison>();
        feature.target=camera;
        feature.Create();
        feature.pass.material=surface.sharedMaterial;
        feature.pass.quad=surface.GetComponent<MeshFilter>().sharedMesh;
        try
        {
            atmosphere.Preview=WorldAtmosphere.View.EmissionMask;
            for(int i=0;i<4;i++) yield return null;
            PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/SortingLayer-Emission");
            surface.forceRenderingOff=true;
            rendererData.rendererFeatures.Add(feature);
            rendererData.SetDirty();
            for(int i=0;i<8;i++) yield return null;
            if(feature.pass.executions<=0) throw new System.InvalidOperationException("Comparison render pass did not execute.");
            PresentationRegressionCheck.SaveFrame(camera.targetTexture,"ArtDirection/AfterTransparents-Emission");
            Debug.Log("[ArtDirection] PASS after-transparents comparison on the world camera");
        }
        finally
        {
            rendererData.rendererFeatures.Remove(feature);
            rendererData.SetDirty();
            surface.forceRenderingOff=false;
            atmosphere.Preview=WorldAtmosphere.View.Composite;
            DestroyImmediate(feature);
        }
    }
}
