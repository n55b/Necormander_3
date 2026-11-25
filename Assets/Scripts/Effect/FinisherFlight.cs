using UnityEngine;

// The enemy dies and grants loot at the hit. Only its last rendered pose travels.
public sealed class FinisherFlight : MonoBehaviour
{
    private const int MaxVisible = 6;
    private static int visible;
#if NECRO_RENDER_BENCH
    internal static int FlightsStarted { get; private set; }
#endif
    private Vector2 point, velocity;
    private Vector3 artOffset;
    private float age, lifetime, spin;
    private int bounces;
    private SpriteRenderer picture;
    private AirborneShadow shadow;
    private ContactFilter2D solids;
    private readonly RaycastHit2D[] hit = new RaycastHit2D[1];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCount() => visible = 0;

    public static SpriteRenderer Play(CharacterHealth health, DamageInfo blow, bool strong)
    {
        if (!Application.isPlaying || visible >= MaxVisible || !DamageRules.IsPlayerSourced(blow.category) ||
            blow.category == DamageCategory.Debuff || !PolishFeedback.Enabled(FeedbackCue.KillHit)) return null;
        var entity = health.GetComponentInParent<BaseEntity>();
        if (entity == null || entity.team != Team.Enemy || entity.MinionData != null && entity.MinionData.tier == EnemyTier.Boss) return null;
        var actor = entity.GetComponentInChildren<YSortableObject>();
        var source = actor != null ? actor.Renderer : null;
        if (source == null || !source.enabled || source.forceRenderingOff || source.sprite == null) return null;
        var go = new GameObject("Finishing hit",typeof(SpriteRenderer),typeof(FinisherFlight));
        var fx = go.GetComponent<FinisherFlight>();
        fx.picture=go.GetComponent<SpriteRenderer>();
        fx.picture.sprite=source.sprite;
        fx.picture.sharedMaterial=source.sharedMaterial;
        fx.picture.color=source.color;
        fx.picture.flipX=source.flipX;
        fx.picture.flipY=source.flipY;
        fx.picture.sortingLayerName="FlyingObject";
        go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
        go.transform.localScale=source.transform.lossyScale;
        fx.point=actor.ShadowGroundPosition;
        fx.shadow=go.AddComponent<AirborneShadow>();
        fx.shadow.SetFloor(fx.point);
        fx.artOffset=source.transform.position-(Vector3)fx.point;
        Vector2 origin=blow.hitFrom ?? (blow.attacker!=null ? (Vector2)blow.attacker.transform.position : fx.point-Vector2.up);
        Vector2 dir=(fx.point-origin).normalized;
        if (dir.sqrMagnitude < .1f) dir=Vector2.up;
        fx.velocity=dir*(strong ? 26 : 13);
        fx.lifetime=strong ? .94f : .52f;
        fx.spin=Mathf.Sign(dir.x)*(strong ? 650 : 350);
        fx.solids=new ContactFilter2D{useTriggers=false};
        fx.solids.SetLayerMask(Layers.WallMask | Layers.ObjectMask);
        visible++;
#if NECRO_RENDER_BENCH
        FlightsStarted++;
#endif
        source.forceRenderingOff=true;
        return source;
    }

    private void Update()
    {
        float dt=Time.deltaTime;
        if (dt<=0) return;
        age+=dt;
        if (age>=lifetime) { Destroy(gameObject); return; }
        float remaining=dt;
        for (int pass=0;pass<3 && remaining>.0001f;pass++)
        {
            float speed=velocity.magnitude;
            if (speed<.05f) break;
            Vector2 dir=velocity/speed;
            if (Physics2D.CircleCast(point,.075f,dir,solids,hit,speed*remaining)==0)
            { point+=velocity*remaining; break; }
            var contact=hit[0];
            point+=dir*Mathf.Max(0, contact.distance-.008f)+contact.normal*.014f;
            remaining-=contact.distance/speed;
            velocity=Vector2.Reflect(velocity,contact.normal)*.85f;
            spin *= -.85f;
            if (++bounces<=4) WorldSparkField.Emit(WorldSparkField.Kind.Wall,contact.point,contact.normal,6);
        }
        shadow.SetFloor(point);
        float progress=age/lifetime;
        var pose=(Vector3)point+artOffset+Vector3.up*(Mathf.Sin(progress*Mathf.PI)*.35f);
        transform.position = pose;
        transform.Rotate(0,0,spin*dt);
        var color=picture.color;
        color.a=Mathf.Clamp01((1-progress)*5);
        picture.color=color;
    }
    private void OnDestroy() => visible=Mathf.Max(0,visible-1);
}
