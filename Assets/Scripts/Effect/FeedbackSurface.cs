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
    private SpriteRenderer dropRenderer;
    private SpriteRenderer visualRenderer;
    private bool restoreDrop;
    private void Start()
    {
        pickup=GetComponent<GroundItem>();
        next=Time.time+PolishFeedback.VisualRange(0,.8f);
        if(kind==Kind.Drop)
        {
            var sr=visualRenderer=GetComponentInChildren<SpriteRenderer>();
            var burst=PolishFeedback.Play(FeedbackCue.DropLand,transform.position+offset,Vector2.zero,sprite:sr!=null?sr.sprite:null);
            if(burst!=null&&sr!=null&&sr.enabled) { dropRenderer=sr; restoreDrop=true; sr.enabled=false; StartCoroutine(RestoreDrop()); }
        }
    }
    private System.Collections.IEnumerator RestoreDrop()
    {
        var recipe=PolishFeedback.Catalog.Find(FeedbackCue.DropLand);
        float remaining=recipe.delay+recipe.duration;
        while(remaining>0&&PolishFeedback.Enabled(FeedbackCue.DropLand)) { remaining-=Time.deltaTime; yield return null; }
        RestoreIcon();
    }
    private void RestoreIcon() { if(restoreDrop&&dropRenderer!=null) dropRenderer.enabled=true; restoreDrop=false; }
    private void OnDisable() { StopAllCoroutines(); RestoreIcon(); }
    private void Update()
    {
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
            PolishFeedback.Play(FeedbackCue.RarityGlow,transform.position+offset,Vector2.up,
                tint:pickup.Item!=null?pickup.Item.TierColor:new Color(.45f,.95f,1f));
            if (pickup.Item != null)
            {
                Vector3 center = visualRenderer != null ? visualRenderer.bounds.center : transform.position;
                PolishFeedback.Play(FeedbackCue.DropGlimmer, center + offset + new Vector3(.25f,.25f,0));
            }
        }
    }
}
