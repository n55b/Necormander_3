#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

// Size guide, grid and layer visibility.
internal sealed partial class WorkshopPreview
{
    // ---------- Size guide, grid and layer visibility ----------

    public Rect? Guide;
    private readonly List<Transform> guideEdges = new();
    private Tilemap gridMap;
    private Tile gridTile;
    private Texture2D gridTexture;

    public void SetLayerVisible(int layer, bool visible)
    {
        if (maps[layer]!=null && maps[layer].TryGetComponent<TilemapRenderer>(out var renderer)) renderer.enabled=visible;
    }

    public void ShowGrid(bool visible)
    {
        if (!visible) { if (gridMap!=null) gridMap.gameObject.SetActive(false); return; }
        if (gridMap==null)
        {
            var node=new GameObject("Grid overlay",typeof(Tilemap),typeof(TilemapRenderer)) { layer=Layer };
            node.transform.SetParent(Ground.transform.parent,false);
            node.transform.localPosition=Ground.transform.localPosition;
            node.transform.localRotation=Ground.transform.localRotation;
            node.transform.localScale=Ground.transform.localScale;
            gridMap=node.GetComponent<Tilemap>();
            gridMap.tileAnchor=Ground.tileAnchor;
            var renderer=node.GetComponent<TilemapRenderer>();
            renderer.sortingLayerName="UI"; renderer.sortingOrder=14990; renderer.sharedMaterial=unlit;
            const int size=16;
            gridTexture=new Texture2D(size,size,TextureFormat.RGBA32,false) { filterMode=FilterMode.Point, wrapMode=TextureWrapMode.Clamp };
            var pixels=new Color32[size*size];
            for (int y=0;y<size;y++) for (int x=0;x<size;x++)
                pixels[y*size+x]=new Color32(255,255,255,(byte)(x==0 || y==size-1 ? 46 : 0));
            gridTexture.SetPixels32(pixels); gridTexture.Apply(false,true);
            gridTile=ScriptableObject.CreateInstance<Tile>();
            gridTile.sprite=Sprite.Create(gridTexture,new Rect(0,0,size,size),Vector2.one*.5f,size,0,SpriteMeshType.FullRect);
            gridTile.flags=TileFlags.None; gridTile.colliderType=Tile.ColliderType.None;
        }
        gridMap.gameObject.SetActive(true);
        gridMap.ClearAllTiles();
        var bounds=ContentBounds();
        var lo=Ground.WorldToCell(bounds.min)-new Vector3Int(6,6,0);
        var hi=Ground.WorldToCell(bounds.max)+new Vector3Int(6,6,0);
        if ((long)(hi.x-lo.x+1)*(hi.y-lo.y+1)>40000) return;
        var scale=Ground.layoutGrid.cellSize;
        var matrix=Matrix4x4.Scale(new Vector3(scale.x,scale.y,1));
        var cells=new List<TileChangeData>();
        for (int y=lo.y;y<=hi.y;y++) for (int x=lo.x;x<=hi.x;x++)
            cells.Add(new TileChangeData(new Vector3Int(x,y,0),gridTile,Color.white,matrix));
        gridMap.SetTiles(cells.ToArray(),true);
    }

    private void PositionGuide(float pixel)
    {
        int edge=0;
        if (Guide.HasValue)
        {
            var g=Guide.Value;
            Vector3 a=World(new Vector3(g.xMin,g.yMin)), b=World(new Vector3(g.xMax,g.yMax));
            float w=b.x-a.x, h=b.y-a.y;
            Line(new Vector3((a.x+b.x)*.5f,a.y),w,pixel); Line(new Vector3((a.x+b.x)*.5f,b.y),w,pixel);
            Line(new Vector3(a.x,(a.y+b.y)*.5f),pixel,h); Line(new Vector3(b.x,(a.y+b.y)*.5f),pixel,h);
        }
        for (int i=edge;i<guideEdges.Count;i++) guideEdges[i].gameObject.SetActive(false);
        void Line(Vector3 center,float width,float height)
        {
            if (edge==guideEdges.Count)
            {
                var line=AddMarker(stage.transform,new Color(.57f,.92f,.79f,.38f),1);
                line.name="Size guide"; line.GetComponent<SpriteRenderer>().sortingOrder=14980;
                guideEdges.Add(line);
            }
            var node=guideEdges[edge++];
            node.gameObject.SetActive(true);
            node.position=center;
            node.localScale=new Vector3(Mathf.Abs(width),Mathf.Abs(height),1);
        }
    }
}
#endif
