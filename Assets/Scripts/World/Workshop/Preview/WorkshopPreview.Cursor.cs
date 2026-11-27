#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

// Cursor preview: the brush box, the test start and what the next click will place.
internal sealed partial class WorkshopPreview
{
    public void Hover(Tilemap map,Vector3 point,bool over,Color color)
    {
        if(!over || map==null)
        {
            if(brush!=null) brush.gameObject.SetActive(false);
            return;
        }
        if(brush==null) brush=AddMarker(stage.transform,Color.white,1);
        brush.gameObject.SetActive(true);
        var cell=map.WorldToCell(point);
        var p=map.GetCellCenterWorld(cell);
        brush.position=p;
        var extent=map.CellToWorld(cell+Vector3Int.one)-map.CellToWorld(cell);
        brush.localScale=new Vector3(Mathf.Abs(extent.x),Mathf.Abs(extent.y),1f);
        brush.GetComponent<SpriteRenderer>().color=color;
    }
    public void MarkStart(Vector3? local)
    {
        if(startMarker!=null) startMarker.gameObject.SetActive(local.HasValue);
        if(!local.HasValue) return;
        if(startMarker==null) startMarker=AddMarker(art,new Color32(252,203,103,255),.55f);
        startMarker.position=World(local.Value);
    }

    // ---------- Cursor preview: what the next click will place ----------

    private Tilemap ghostMap;
    private Transform ghostPiece;
    private GameObject ghostPrefab;

    public void HideGhost()
    {
        if (ghostMap!=null) ghostMap.gameObject.SetActive(false);
        if (ghostPiece!=null) ghostPiece.gameObject.SetActive(false);
    }

    // Translucent copies of the tiles a click would paint, on a sibling of the working layer.
    public void GhostTiles(Tilemap map, IList<TileChangeData> cells)
    {
        if (ghostPiece!=null) ghostPiece.gameObject.SetActive(false);
        if (map==null || cells.Count==0) { if (ghostMap!=null) ghostMap.gameObject.SetActive(false); return; }
        if (ghostMap==null)
        {
            var node=new GameObject("Brush preview",typeof(Tilemap),typeof(TilemapRenderer)) { layer=Layer };
            ghostMap=node.GetComponent<Tilemap>();
            var renderer=node.GetComponent<TilemapRenderer>();
            renderer.sortingLayerName="UI"; renderer.sortingOrder=14970; renderer.sharedMaterial=unlit;
        }
        var ghost=ghostMap.transform;
        if (ghost.parent!=map.transform.parent) ghost.SetParent(map.transform.parent,false);
        ghost.localPosition=map.transform.localPosition; ghost.localRotation=map.transform.localRotation; ghost.localScale=map.transform.localScale;
        ghostMap.tileAnchor=map.tileAnchor; ghostMap.orientation=map.orientation;
        ghostMap.gameObject.SetActive(true);
        ghostMap.ClearAllTiles();
        ghostMap.SetTiles(cells as TileChangeData[] ?? cells.ToArray(),true);
    }

    // A translucent copy of an enemy or prop at the spot a click would place it.
    public void GhostPiece(GameObject prefab, Vector3 local)
    {
        if (ghostMap!=null) ghostMap.gameObject.SetActive(false);
        if (prefab==null) { if (ghostPiece!=null) ghostPiece.gameObject.SetActive(false); return; }
        if (ghostPiece==null || ghostPrefab!=prefab)
        {
            if (ghostPiece!=null) Object.Destroy(ghostPiece.gameObject);
            ghostPiece=Copy(prefab.transform,art,null,true);
            ghostPiece.name="Piece preview";
            foreach (var sprite in ghostPiece.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var color=sprite.color; color.a*=.5f; sprite.color=color;
            }
            ghostPrefab=prefab;
        }
        ghostPiece.gameObject.SetActive(true);
        ghostPiece.position=World(local);
    }

    // A box over a block of cells, used for the eraser at its brush size.
    public void HoverCells(Tilemap map, Vector3Int low, Vector3Int high, Color color)
    {
        if (map==null) { if (brush!=null) brush.gameObject.SetActive(false); return; }
        if (brush==null) brush=AddMarker(stage.transform,Color.white,1);
        brush.gameObject.SetActive(true);
        var a=map.CellToWorld(low); var b=map.CellToWorld(high+new Vector3Int(1,1,0));
        brush.position=(a+b)*.5f;
        brush.localScale=new Vector3(Mathf.Abs(b.x-a.x),Mathf.Abs(b.y-a.y),1);
        brush.GetComponent<SpriteRenderer>().color=color;
    }
}
#endif
