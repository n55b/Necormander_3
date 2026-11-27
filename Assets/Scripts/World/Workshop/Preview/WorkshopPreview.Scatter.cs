#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Linq;
using UnityEngine;

internal sealed partial class WorkshopPreview
{
    // Adds one placed object to the drawing without rebuilding the room (used while scattering).
    internal void AddPiece(string id, WorkshopCatalog.Piece piece, Vector3 local)
    {
        var node = Copy(piece.prefab.transform, art, null, true);
        pieces[id] = node;
        node.position = World(local);
        artwork[id] = node.GetComponentsInChildren<SpriteRenderer>(true).Where(renderer => renderer.sprite != marker).ToArray();
        if (!artwork[id].Any(Visible) && node.gameObject.activeInHierarchy) AddMarker(node, WorkshopUI.Paper, .4f);
    }
}
#endif
