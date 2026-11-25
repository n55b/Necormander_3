using System.Collections;
using UnityEngine;

// One reusable animated leaf per visible crown. The tree owns its lifetime through room changes.
[DisallowMultipleComponent, RequireComponent(typeof(YSortableObject))]
public sealed class TreeLeafFall : MonoBehaviour
{
    private const string ArtPath = "Sprites/Effect/AnimateParticle001";
    private static GameObject art;
    private YSortableObject tree;
    private SpriteRenderer leaf;
    private System.Random random;
    private Coroutine falling;
    private Vector3 releaseOffset;
    private float nextRelease, age, duration, fallDistance, wind, phase;

    private void Awake()
    {
        tree = GetComponent<YSortableObject>();
        random = new System.Random(GetInstanceID());
    }

    private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

    private void OnEnable()
    {
        if (GetComponent<SpriteRenderer>().isVisible) OnBecameVisible();
    }

    private void OnBecameVisible()
    {
        if (isActiveAndEnabled && falling == null)
            falling = StartCoroutine(WhileVisible());
    }

    private void OnDisable() => OnBecameInvisible();

    private void OnBecameInvisible()
    {
        if (falling != null) StopCoroutine(falling);
        falling = null;
        if (leaf != null) leaf.gameObject.SetActive(false);
    }

    private IEnumerator WhileVisible()
    {
        nextRelease = Time.time + Range(.25f, 3f);
        while (true)
        {
            yield return null;
            UpdateLeaf();
        }
    }

    private void UpdateLeaf()
    {
        var canopy = tree.Renderer;
        if (!tree.isActiveAndEnabled)
        {
            if (leaf != null && leaf.gameObject.activeSelf) leaf.gameObject.SetActive(false);
            return;
        }
        if (leaf == null || !leaf.gameObject.activeSelf)
        {
            if (Time.time < nextRelease) return;
            if (!Release(canopy.bounds)) return;
        }

        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / duration);
        float flutter = Mathf.Sin(phase + t * 8f) - Mathf.Sin(phase);
        float drift = wind * t + flutter * .13f;
        leaf.transform.position = transform.position + releaseOffset +
            new Vector3(drift, -fallDistance * (.75f * t + .25f * t * t), 0);
        WorldDepthOrder.ReadOrder(canopy, tree.Group, out int layer, out int order);
        leaf.sortingLayerID = layer;
        leaf.sortingOrder = WorldDepthOrder.AttachmentOrder(layer, order, 1);
        float alpha = Mathf.SmoothStep(0, 1, t / .10f) * Mathf.SmoothStep(0, 1, (1f - t) / .22f);
        leaf.color = new Color(1, 1, 1, alpha);
        if (t >= 1f)
        {
            leaf.gameObject.SetActive(false);
            nextRelease = Time.time + Range(.9f, 3.4f);
        }
    }

    private bool Release(Bounds bounds)
    {
        if (leaf == null)
        {
            if (art == null) art = Resources.Load<GameObject>(ArtPath);
            if (art == null)
            {
                Debug.LogError("Missing leaf animation at " + ArtPath, this);
                enabled = false;
                return false;
            }
            // Keep the imported 32 PPU even on a scaled room or tree.
            leaf = Instantiate(art, transform, true).GetComponent<SpriteRenderer>();
        }
        Vector3 origin = bounds.center;
        origin.x += Range(-.66f, .66f) * bounds.extents.x;
        origin.y += Range(.03f, .45f) * bounds.extents.y;
        releaseOffset = origin - transform.position;
        float floor = tree.GroundPosition.y - Range(.04f, .22f);
        fallDistance = Mathf.Max(.2f, origin.y - floor);
        duration = Range(1.8f, 2.9f);
        wind = (Mathf.PerlinNoise(origin.x * .23f + Time.time * .17f, origin.y * .19f) - .5f) * 1.6f;
        phase = Range(0, Mathf.PI * 2);
        age = 0;
        leaf.flipX = random.Next(2) == 0;
        leaf.gameObject.SetActive(true);
        return true;
    }
}
