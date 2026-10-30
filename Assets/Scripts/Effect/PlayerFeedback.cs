using UnityEngine;

/// <summary>플레이어 프리팹의 연출 관찰자. 입력/위치/공격 판정을 변경하지 않는다.</summary>
public sealed class PlayerFeedback : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private MeleeDodgeController dodge;
    [SerializeField] private MeleeCombatController combat;
    [SerializeField] private PlayerSkillController skills;
    private Vector3 previous;
    private Vector2 velocity;
    private float nextTrail, nextWater, nextHeartbeat;
    private int charges;
    private bool dashing, ready, minionReady;
    private CharacterHealth health;
    private void OnEnable()
    {
        previous=transform.position;
        DamageEventBus.OnAttackHitResolved+=PolishFeedback.ResolvedHit;
        DamageEventBus.OnEntityDied+=PolishFeedback.Killed;
        DamageEventBus.OnDamageReceived+=Damage;
        RoomInstance.OnAnyRoomCleared+=Cleared;
        if(combat!=null) combat.OnAttackExecuted+=Attack;
    }
    private void OnDisable()
    {
        DamageEventBus.OnAttackHitResolved-=PolishFeedback.ResolvedHit;
        DamageEventBus.OnEntityDied-=PolishFeedback.Killed;
        DamageEventBus.OnDamageReceived-=Damage;
        RoomInstance.OnAnyRoomCleared-=Cleared;
        if(combat!=null) combat.OnAttackExecuted-=Attack;
    }
    private void Attack(int step)
    { PolishFeedback.Play(FeedbackCue.WeaponTrail,transform.position,combat.CurrentAttackDir,PolishFeedback.WeaponTier)?.FollowOrigin(transform); }
    private void Damage(CharacterHealth target,DamageInfo info)
    {
        if(target!=health) return;
        if(info.attacker!=null) FeedbackOverlay.Instance?.Hurt(info.attacker.transform.position-transform.position);
    }
    private void Cleared(RoomInstance room)
    {
        PolishFeedback.Play(FeedbackCue.RoomClear,transform.position,Vector2.up);
    }
    private void LateUpdate()
    {
        if(player==null||PolishFeedback.Catalog==null) return;
        health=player.Stat!=null?player.Stat.Health:null;
        if(health!=null)
        {
            float fraction = health.MaxHP > 0 ? health.CurHP / health.MaxHP : 1;
            FeedbackOverlay.Instance?.SetHealth(fraction);
            if (!health.IsDead && !player.IsInputBlocked && !PlayerController.IsGameplayPaused
                && fraction < PolishFeedback.Catalog.lowHealthRatio && Time.time >= nextHeartbeat)
            {
                nextHeartbeat = Time.time + PolishFeedback.Catalog.lowHealthInterval;
                PolishFeedback.PlaySound(FeedbackCue.LowHealth, transform.position);
            }
        }
        Vector3 position=transform.position;
        Vector2 delta=position-previous;
        previous=position;
        if(Time.deltaTime<=0||player.IsInputBlocked||delta.sqrMagnitude>4) { velocity=Vector2.zero; return; }
        Vector2 current=delta/Time.deltaTime;
        bool dash=dodge!=null&&dodge.IsDashing;
        if(dash&&!dashing) PolishFeedback.Play(FeedbackCue.DashStart,position,current.normalized,playSound:false);
        if(!dash&&dashing) PolishFeedback.Play(FeedbackCue.DashEnd,position,velocity.normalized);
        if(!dash&&!dashing)
        {
            if(current.magnitude>.4f&&velocity.magnitude<.1f) PolishFeedback.Play(FeedbackCue.MoveStart,position,-current.normalized);
            if(current.magnitude<.1f&&velocity.magnitude>.4f) PolishFeedback.Play(FeedbackCue.MoveBrake,position,velocity.normalized);
            if(current.sqrMagnitude>1&&velocity.sqrMagnitude>1&&Vector2.Dot(current.normalized,velocity.normalized)<.4f) PolishFeedback.Play(FeedbackCue.MoveTurn,position,-velocity.normalized);
        }
        if(dash&&Time.time>nextTrail) { nextTrail=Time.time+PolishFeedback.Catalog.trailInterval; PolishFeedback.Play(FeedbackCue.DashTrail,position,-current.normalized); }
        if(dodge!=null)
        {
            if(dodge.CurrentCharges>charges) PolishFeedback.Play(FeedbackCue.DashReady,position,Vector2.up);
            charges=dodge.CurrentCharges;
        }
        bool skillReady=skills!=null&&skills.MainSummon!=null&&skills.GetMainSummonCooldownRemaining()<=0;
        if(skillReady&&!ready) { PolishFeedback.Play(FeedbackCue.SkillReady,position,Vector2.up)?.FollowOrigin(transform); FeedbackOverlay.Instance?.Pulse(FeedbackCue.SkillReady,position); }
        ready=skillReady;
        bool basicReady=combat!=null&&combat.MinionFeedbackReady;
        if(basicReady&&!minionReady) PolishFeedback.Play(FeedbackCue.MinionReady,position,Vector2.up,PolishFeedback.MinionTier())?.FollowOrigin(transform);
        minionReady=basicReady;
        if(Time.time>nextWater&&MapGenerator.Instance!=null)
        {
            nextWater=Time.time+PolishFeedback.Catalog.waterInterval;
            if(MapGenerator.Instance.TryGetNearbyWater(position,out var water)) PolishFeedback.Play(FeedbackCue.WaterRipple,water,Vector2.up);
        }
        velocity=current; dashing=dash;
    }
}
