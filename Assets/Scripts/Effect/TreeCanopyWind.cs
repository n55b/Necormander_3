using UnityEngine;

// Static pose and collision stay at the trunk. The shader offsets source rows in whole texels.
[ExecuteAlways, RequireComponent(typeof(SpriteRenderer))]
public sealed class TreeCanopyWind : MonoBehaviour
{
    public float Phase { get; private set; }

    private void OnEnable()
    {
        var renderer = GetComponent<SpriteRenderer>();
        var sprite = renderer.sprite;
        if (sprite == null) return;
        var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetVector("_SpriteRect", uv);
        block.SetFloat("_TreePPU", sprite.pixelsPerUnit);
        Phase = transform.position.x * .47f + transform.position.y * .29f;
        block.SetFloat("_WindPhase", Phase);
        renderer.SetPropertyBlock(block);
    }
}
