using UnityEngine;
using UnityEngine.UI;

public sealed class RewardCardSurface : RewardPixelSurface
{
    [SerializeField] private Color32 edge = new(77, 71, 80, 255);
    [SerializeField] private Color32 paper = new(46, 43, 50, 255);
    [SerializeField] private Color32 portrait = new(58, 54, 61, 255);

    protected override void Draw(VertexHelper mesh, float w, float h)
    {
        Plate(mesh, 0, 0, w, h, 3, edge);
        Plate(mesh, 1, 1, w - 2, h - 2, 2, paper);
        Plate(mesh, 4, h - 68, w - 8, 64, 2, portrait);
    }
}
