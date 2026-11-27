#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

// Zoom steps, panning and the camera that renders the map texture.
internal sealed partial class WorkshopPreview
{
    // Zoom is a step index: 0 fits everything, 1..8 pick a scale from Steps (1 = one texel per screen pixel).
    public static readonly float[] Steps = { .25f, .5f, .75f, 1f, 1.5f, 2f, 3f, 4f };
    public const int HomeZoom = 2, FocusZoom = 4;
    public float CellWidth { get; private set; }
    public float Scale => Density / nativeDensity;
    public string ZoomLabel => (Zoom == 0 ? "맞춤 " : "") + Mathf.RoundToInt(Scale * 100) + "%";
    public bool CanZoomIn => Scale < Steps[^1] - .001f;
    public bool CanZoomOut => Zoom != 0;

    public void Home()
    {
        Zoom=HomeZoom;
        Center=Local(ContentBounds().center);
        SetView();
        PositionOutlines();
    }

    public void Focus(Vector3 local)
    {
        Center=local;
        if (Zoom==0) Zoom=HomeZoom;
        SetView();
        PositionOutlines();
    }

    // One wheel notch or +/- press moves to the next finer or coarser scale; below the fitted view it fits.
    public void ZoomStep(Vector2 uv, int delta)
    {
        float scale=Scale;
        if (delta>0)
        {
            int next=System.Array.FindIndex(Steps,s=>s>scale+.001f);
            if (next>=0) ZoomAt(uv,next+1);
            return;
        }
        int previous=System.Array.FindLastIndex(Steps,s=>s<scale-.001f);
        if (Zoom==0 || previous<0 || Steps[previous]<FitDensity(ContentBounds())/nativeDensity-.001f) Overview();
        else ZoomAt(uv,previous+1);
    }

    // Tiles, exits, the test start, the size guide and placed objects: everything the author needs in view.
    private Bounds ContentBounds()
    {
        bool any=false; var result=new Bounds();
        void Add(Bounds b) { if (any) result.Encapsulate(b); else { result=b; any=true; } }
        foreach (var map in maps)
        {
            if (map==null || map.GetUsedTilesCount()==0) continue;
            map.CompressBounds();
            var cells=map.cellBounds;
            var a=map.CellToWorld(cells.min); var b=map.CellToWorld(cells.max);
            Add(new Bounds((a+b)*.5f,new Vector3(Mathf.Abs(b.x-a.x),Mathf.Abs(b.y-a.y),0)));
        }
        foreach (var door in doors.Values) Add(new Bounds(door.position,Vector3.one*CellWidth*2));
        if (startMarker!=null && startMarker.gameObject.activeSelf) Add(new Bounds(startMarker.position,Vector3.one*CellWidth*2));
        if (Guide.HasValue)
        {
            var g=Guide.Value;
            Add(new Bounds(World(g.center),new Vector3(g.width,g.height,0)*art.lossyScale.x));
        }
        foreach (var piece in pieces)
            if (piece.Value.gameObject.activeInHierarchy) Add(PieceBounds(piece.Key));
        return any ? result : new Bounds(art.position,Vector3.one*CellWidth*20);
    }

    private float FitDensity(Bounds bounds) =>
        Mathf.Min((Frame.width-16)/Mathf.Max(.01f,bounds.size.x),(Frame.height-16)/Mathf.Max(.01f,bounds.size.y));

    // Several room grids have a half-scale parent. Their 16-texel cells occupy half a world unit.
    private float nativeDensity, overviewDensity;
    public float DragThreshold => 2f / Density;
    private float Density => Zoom == 0 ? overviewDensity : nativeDensity * Steps[Mathf.Clamp(Zoom, 1, Steps.Length) - 1];
    public void Overview()
    {
        var bounds=ContentBounds();
        Center=Local(bounds.center);
        overviewDensity=Mathf.Min(nativeDensity*Steps[^1],FitDensity(bounds));
        Zoom=0;
        SetView();
        PositionOutlines();
    }
    public void ZoomAt(Vector2 uv, int zoom)
    {
        var anchor = Local(Point(uv));
        Zoom = Mathf.Clamp(zoom, 1, Steps.Length);
        SetView();
        Center += (Vector2)(anchor - Local(Point(uv)));
        SetView();
        PositionOutlines();
    }
    public void Pan(Vector2 previousUV, Vector2 currentUV)
    {
        Center += (Vector2)(Local(Point(previousUV)) - Local(Point(currentUV)));
        SetView();
    }
    public void ResizeFrame(int width,int height)
    {
        if (Frame.width==width && Frame.height==height) return;
        camera.targetTexture=null;
        Frame.Release(); Frame.width=width; Frame.height=height; Frame.Create();
        camera.targetTexture=Frame;
        if (Zoom==0) Overview(); else SetView();
    }

    public void MatchView(WorkshopPreview other)
    {
        Center=other.Center; Zoom=other.Zoom;
        overviewDensity=other.overviewDensity;
        SetView();
    }

    public void SetView()
    {
        float density = Density;
        camera.aspect = Frame.width / (float)Frame.height;
        camera.orthographicSize = Frame.height / (2f * density);
        Vector3 p = World(Center);
        Vector3 gridOrigin=Ground.CellToWorld(Vector3Int.zero);
        camera.transform.position = new Vector3(gridOrigin.x + Mathf.Round((p.x-gridOrigin.x)*density)/density,
            gridOrigin.y + Mathf.Round((p.y-gridOrigin.y)*density)/density, -30);
    }
    public Vector3 Point(Vector2 uv) => camera.ViewportToWorldPoint(new Vector3(uv.x,uv.y,30));
}
#endif
