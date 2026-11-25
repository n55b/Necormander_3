using UnityEngine;

// The animator selects the authored pose. Each fist follows its own target in world space.
[DefaultExecutionOrder(1500), RequireComponent(typeof(SpriteRenderer))]
public sealed class PlayerHandAimController : MonoBehaviour
{
    [SerializeField, Range(.01f,.15f)] private float strikeResponse = .033f;
    [SerializeField, Range(.035f,.25f)] private float returnResponse = .10f;
    private MeleeCombatController melee;
    private SpriteRenderer source;
    private HandPoseCatalog catalog;
    private readonly Hand left = new(), right = new();
    private Vector3 lastRoot;
    private bool initialized;

    private sealed class Hand
    {
        public SpriteRenderer renderer;
        public Vector3 position, velocity;
        public float angle, angularVelocity;
        public bool visible;
    }

    public Vector3 SummonOrigin(bool preferLeft)
    {
        var hand = preferLeft ? left : right;
        var other = preferLeft ? right : left;
        if (hand.visible) return hand.position;
        return other.visible ? other.position : transform.position;
    }

    internal Vector4 LightSource(bool preferLeft)
    {
        var hand = preferLeft ? left : right;
        var picture = hand.visible ? hand.renderer : (preferLeft ? source : null);
        if (!isActiveAndEnabled || picture == null || !picture.enabled || picture.forceRenderingOff ||
            picture.sprite == null || !picture.gameObject.activeInHierarchy) return Vector4.zero;
        var center = picture.bounds.center;
        return new Vector4(center.x, center.y, picture.color.a, 0);
    }

    private void Awake()
    {
        source = GetComponent<SpriteRenderer>();
        melee = GetComponentInParent<MeleeCombatController>();
        catalog = Resources.Load<HandPoseCatalog>("PlayerHandPoses");
        Create(left, "Left Hand");
        Create(right, "Right Hand");
        var body = melee != null ? melee.GetComponentInChildren<YSortableObject>() : null;
        AirborneShadow.AttachHand(source, body);
        AirborneShadow.AttachHand(left.renderer, body);
        AirborneShadow.AttachHand(right.renderer, body);
    }

    private void Create(Hand hand, string label)
    {
        var child = new GameObject(label);
        child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        hand.renderer = child.AddComponent<SpriteRenderer>();
        hand.renderer.sharedMaterial = source.sharedMaterial;
        hand.renderer.enabled = false;
    }

    private void LateUpdate()
    {
        if (melee == null) return;
        float dt = Time.deltaTime;
        bool attacking = melee.IsAimingAttack;
        float targetAngle = 0;
        if (attacking && melee.CurrentAttackDir.sqrMagnitude > .0001f)
        {
            Vector2 dir = melee.CurrentAttackDir;
            float aim = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
            targetAngle = melee.transform.localScale.x < 0 ? aim : 180f - aim;
        }
        // Keep the authored hit frame and its aim pivot on the combat direction.
        // Each visible hand interpolates toward this pose independently.
        transform.localRotation = Quaternion.Euler(0, 0, targetAngle);
        if (catalog == null || !source.enabled || !catalog.TryGet(source.sprite, out var pose))
        {
            RestoreSource();
            return;
        }
        source.forceRenderingOff = true;
        var root = melee.transform.position;
        bool warp = !initialized || (root - lastRoot).sqrMagnitude > 9f;
        Follow(left, pose.left, pose.leftOffset, warp, dt, attacking ? strikeResponse : returnResponse);
        Follow(right, pose.right, pose.rightOffset, warp, dt, attacking ? strikeResponse * 1.12f : returnResponse * 1.12f);
        lastRoot = root;
        initialized = true;
    }

    private void Follow(Hand hand, Sprite sprite, Vector2 local, bool warp, float dt, float response)
    {
        var renderer = hand.renderer;
        renderer.enabled = sprite != null;
        if (sprite == null) { hand.visible = false; return; }
        var target = transform.TransformPoint(local);
        float rotation = transform.eulerAngles.z;
        if (warp || !hand.visible)
        {
            hand.position = target; hand.velocity = Vector3.zero;
            hand.angle = rotation; hand.angularVelocity = 0;
        }
        else if (dt > 0)
        {
            hand.position = Vector3.SmoothDamp(hand.position, target, ref hand.velocity, response, Mathf.Infinity, dt);
            hand.angle = Mathf.SmoothDampAngle(hand.angle, rotation, ref hand.angularVelocity, response, Mathf.Infinity, dt);
        }
        renderer.transform.rotation = Quaternion.Euler(0, 0, hand.angle);
        hand.visible = true;
        renderer.sprite = sprite;
        renderer.color = source.color;
        renderer.sortingLayerID = source.sortingLayerID;
        renderer.sortingOrder = source.sortingOrder;
        renderer.transform.position = PixelViewport.WorldCamera is { } camera ? hand.position + PixelActorMotion.GetOffset(camera, hand.position, CameraSettingsSO.Instance.playerMotion) : hand.position;
    }

    private void RestoreSource()
    {
        initialized = left.visible = right.visible = false;
        if (source != null) source.forceRenderingOff = false;
        if (left.renderer != null) left.renderer.enabled = right.renderer.enabled = false;
    }
    private void OnDisable() => RestoreSource();
}
