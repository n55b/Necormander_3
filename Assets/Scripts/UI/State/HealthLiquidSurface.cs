using UnityEngine;
using UnityEngine.UI;

// The Image still owns fillAmount and nine-slicing. UV1 measures its visible face in logical pixels.
[RequireComponent(typeof(Image)), DisallowMultipleComponent]
public sealed class HealthLiquidSurface : BaseMeshEffect
{
    private Image fill;
    private bool motion;

    protected override void OnEnable()
    {
        base.OnEnable();
        fill=GetComponent<Image>();
        motion=UISkinSettings.MotionOn;
        EnableCoordinates();
    }

    protected override void OnCanvasHierarchyChanged()
    {
        base.OnCanvasHierarchyChanged();
        EnableCoordinates();
    }

    private void EnableCoordinates()
    {
        var canvas=graphic != null ? graphic.canvas : null;
        if (canvas!=null) canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
    }

    private void Update()
    {
        bool next=UISkinSettings.MotionOn;
        if (next==motion) return;
        motion=next;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || fill==null || mesh.currentVertCount==0) return;
        var rect=fill.rectTransform;
        var bounds=rect.rect;
        float step=PixelViewport.CanvasPixelsPerUnit(fill.canvas,rect);
        float height=Mathf.Max(1,Mathf.Round(bounds.height*step));
        var vertex=new UIVertex();
        for (int i=0;i<mesh.currentVertCount;i++)
        {
            mesh.PopulateUIVertex(ref vertex,i);
            vertex.uv1=new Vector4((vertex.position.x-bounds.xMin)*step,
                (vertex.position.y-bounds.yMin)*step,height,motion ? 1 : 0);
            mesh.SetUIVertex(vertex,i);
        }
    }
}
