#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

// Picking placed objects, selection outlines, the drag area and door markers.
internal sealed partial class WorkshopPreview
{
    private Transform area;
    private readonly List<Transform> edges = new();
    private readonly List<string> highlighted = new();
    private static bool Visible(SpriteRenderer sprite) => sprite.enabled && sprite.gameObject.activeInHierarchy && sprite.color.a > 0;

    public Bounds PieceBounds(string key)
    {
        var bounds = new Bounds(pieces[key].position, Vector3.one * .4f);
        bool found = false;
        foreach (var sprite in artwork[key])
        {
            if (!Visible(sprite)) continue;
            if (found) bounds.Encapsulate(sprite.bounds);
            else { bounds = sprite.bounds; found = true; }
        }
        return bounds;
    }

    public string Pick(Vector3 world, string after = null)
    {
        var hits = new List<(string key, (int layer, int order, float depth, int childOrder) rank)>();
        foreach (var piece in pieces)
        {
            if (!piece.Value.gameObject.activeInHierarchy) continue;
            bool drawn = false, hit = false;
            (int, int, float, int) rank = default;
            foreach (var sprite in artwork[piece.Key])
            {
                if (!Visible(sprite)) continue;
                drawn = true;
                if (!ContainsXY(sprite.bounds, world)) continue;
                var group = WorldDepthOrder.FindEffectiveGroup(sprite);
                WorldDepthOrder.ReadOrder(sprite, group, out int layer, out int order);
                var candidate = (SortingLayer.GetLayerValueFromID(layer), order,
                    -(group != null ? group.transform.position.y : sprite.bounds.center.y), sprite.sortingOrder);
                if (!hit || candidate.CompareTo(rank)>0) rank=candidate;
                hit=true;
            }
            if (hit) hits.Add((piece.Key, rank));
            else if (!drawn && ContainsXY(PieceBounds(piece.Key), world))
                hits.Add((piece.Key,(int.MinValue, 0, -piece.Value.position.y, 0)));
        }
        if (hits.Count==0) return null;
        hits.Sort((a,b) => a.rank!=b.rank ? b.rank.CompareTo(a.rank) : string.CompareOrdinal(b.key,a.key));
        int current=after!=null ? hits.FindIndex(item=>item.key==after) : -1;
        return hits[(current+1)%hits.Count].key;
    }

    private static bool ContainsXY(Bounds bounds, Vector3 point) =>
        point.x >= bounds.min.x && point.x <= bounds.max.x && point.y >= bounds.min.y && point.y <= bounds.max.y;

    public void HighlightSelection(IEnumerable<string> keys)
    {
        highlighted.Clear();
        highlighted.AddRange(keys);
        PositionOutlines();
    }

    public void PositionOutlines()
    {
        int edge = 0;
        float pixel = 1f / Density;
        foreach (string key in highlighted)
        {
            var b = PieceBounds(key);
            var size = b.size + Vector3.one * (2 * pixel);
            var center = b.center;
            Line(center + Vector3.up * size.y / 2, size.x + pixel, pixel);
            Line(center - Vector3.up * size.y / 2, size.x + pixel, pixel);
            Line(center + Vector3.right * size.x / 2, pixel, size.y);
            Line(center - Vector3.right * size.x / 2, pixel, size.y);
        }
        for (int i = edge; i < edges.Count; i++) edges[i].gameObject.SetActive(false);
        PositionGuide(pixel);

        void Line(Vector3 center, float width, float height)
        {
            if (edge == edges.Count)
            {
                var outline=AddMarker(stage.transform, WorkshopUI.Mint, 1);
                outline.name="Selection outline";
                edges.Add(outline);
            }
            var line = edges[edge++];
            line.gameObject.SetActive(true);
            line.position = center;
            line.localScale = new Vector3(width, height, 1);
        }
    }
    public void Area(Vector3 from,Vector3 to)
    {
        if(area==null) area=AddMarker(stage.transform,new Color(.57f,.92f,.79f,.28f),1);
        area.gameObject.SetActive(true);
        area.position=(from+to)*.5f;
        area.localScale=new Vector3(Mathf.Max(.1f,Mathf.Abs(to.x-from.x)),Mathf.Max(.1f,Mathf.Abs(to.y-from.y)),1);
    }
    public void HideArea() { if(area!=null) area.gameObject.SetActive(false); }
    public Vector3? DoorLocal(Vector2Int direction) => doors.TryGetValue(direction,out var node) ? (Vector3?)Local(node.position) : null;
    public void MarkDoor(Vector2Int direction,Vector3 local)
    {
        if (!doors.TryGetValue(direction,out var node))
            doors[direction]=node=AddMarker(art,new Color(.25f,.95f,.75f),.55f);
        node.position=World(local);
    }
}
#endif
