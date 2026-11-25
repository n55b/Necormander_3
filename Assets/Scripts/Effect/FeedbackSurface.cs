using UnityEngine;

/// <summary>포탈·풀·횃불·드롭 프리팹에 저작하는 관찰자. 충돌/위치는 건드리지 않는다.</summary>
public sealed class FeedbackSurface : MonoBehaviour
{
    public enum Kind { [InspectorName("포탈")] Portal, [InspectorName("풀과 잎")] Foliage,
        [InspectorName("환경 먼지")] Ambient, [InspectorName("드롭 아이템")] Drop }
    [SerializeField] private Kind kind;
    [SerializeField, Min(.1f)] private float visibleDistance=12f;
    [SerializeField, Min(.1f)] private float touchDistance=1f;
    [SerializeField] private Vector3 offset;
    private float next;
    private bool wasNear;
    private GroundItem pickup;
    private SpriteRenderer visualRenderer;
    private float dropRevealStart;
    private bool revealingDrop;
    private void Start()
    {
        pickup=GetComponent<GroundItem>();
        next=Time.time+PolishFeedback.VisualRange(0,.8f);
        if(kind==Kind.Drop)
        {
            var sr=visualRenderer=GetComponentInChildren<SpriteRenderer>();
            var burst=PolishFeedback.Play(FeedbackCue.DropLand,transform.position+offset,Vector2.zero,sprite:sr!=null?sr.sprite:null);
            if (sr == null || !sr.enabled) return;
            if (burst == null || !burst.AnimateDrop(this, sr))
            {
                dropRevealStart = Time.unscaledTime;
                revealingDrop = true;
                SetDropAlpha(0f);
            }
        }
    }
    internal void DropLanded()
    {
        if (pickup != null && pickup.IsAvailable)
        {
            WorldAtmosphere.Current?.Accent(transform.position);
            WorldSparkField.Emit(WorldSparkField.Kind.Treasure, transform.position, Vector2.up, 5);
            PolishFeedback.Play(FeedbackCue.RarityGlow, transform.position + offset, Vector2.up,
                tint: pickup.Item != null ? pickup.Item.TierColor : Color.cyan);
        }
    }
    private void OnDisable()
    {
        if (revealingDrop) SetDropAlpha(1f);
        revealingDrop = false;
    }

    private void SetDropAlpha(float alpha)
    {
        var color = visualRenderer.color;
        color.a = alpha;
        visualRenderer.color = color;
    }
    private void Update()
    {
        if (revealingDrop)
        {
            float progress = (Time.unscaledTime - dropRevealStart) / .14f;
            SetDropAlpha(Mathf.SmoothStep(0f, 1f, progress));
            revealingDrop = progress < 1f;
        }
        var player=PolishFeedback.Player; var settings=PolishFeedback.Catalog;
        if(player==null||settings==null||!settings.enabled||Time.deltaTime<=0) return;
        var delta=player.transform.position-transform.position;
        if(delta.sqrMagnitude>visibleDistance*visibleDistance) return;
        bool near=delta.sqrMagnitude<touchDistance*touchDistance;
        if(kind==Kind.Foliage)
        {
            if(near&&!wasNear) PolishFeedback.Play(FeedbackCue.Foliage,transform.position+offset,delta);
            wasNear=near; return;
        }
        if(Time.time<next) return;
        next=Time.time+settings.ambientInterval;
        if(kind==Kind.Portal) PolishFeedback.Play(FeedbackCue.PortalAmbient,transform.position+offset,Vector2.up);
        else if(kind==Kind.Ambient) PolishFeedback.Play(FeedbackCue.AmbientDust,transform.position+offset,Vector2.up);
        else if(pickup!=null&&pickup.IsAvailable)
        {
            if (pickup.Item != null)
            {
                Vector3 center = visualRenderer != null ? visualRenderer.bounds.center : transform.position;
                PolishFeedback.Play(FeedbackCue.DropGlimmer, center + offset + new Vector3(.25f,.25f,0))?.FollowOrigin(transform);
            }
        }
    }
}
