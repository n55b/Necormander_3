using UnityEngine;

/// <summary>연출 호출과 설정 조회만 담당한다. 게임 판정은 호출한 원래 시스템이 소유한다.</summary>
public static class PolishFeedback
{
    private static FeedbackCatalog catalog;
    private static bool loaded;
    private static int frame, bursts, sounds;
    // 연출을 꺼도 전투/보상 추첨의 Unity Random 상태가 바뀌지 않는다.
    private static readonly System.Random visualRandom = new System.Random();
    internal static float VisualRange(float min, float max) => min+(max-min)*(float)visualRandom.NextDouble();
    private static readonly float[] next = new float[81];
    public static FeedbackCatalog Catalog
    {
        get { if(!loaded) { catalog=Resources.Load<FeedbackCatalog>("FeedbackCatalog"); loaded=true; } return catalog; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { catalog=null; loaded=false; frame=-1; System.Array.Clear(next,0,next.Length); }
    public static bool Enabled(FeedbackCue cue)
    {
        var c=Catalog; var r=c!=null?c.Find(cue):null;
        return c!=null && c.enabled && c.intensity>0 && r!=null && r.enabled && r.intensity>0;
    }
    public static FeedbackBurst Play(FeedbackCue cue, Vector3 position, Vector2 direction=default, int tier=0,
        Transform target=null, Sprite sprite=null, Color? tint=null)
    {
        var c=Catalog;
        if(c==null || !Enabled(cue)) return null;
        var r=c.Find(cue);
        if(tier<r.minimumTier) return null;
        if(frame!=Time.frameCount) { frame=Time.frameCount; bursts=0; sounds=0; }
        int id=(int)cue;
        if(Time.time<next[id]) return null;
        if(bursts>=c.burstsPerFrame || FeedbackBurst.ActiveCount>=c.simultaneousBursts) return null;
        next[id]=Time.time+r.interval;
        if(r.sound!=null && sounds<c.soundsPerFrame && SoundManager.Instance!=null)
        {
            sounds++;
            float variation=Enabled(FeedbackCue.SoundVariation)?r.pitchVariation:0;
            SoundManager.Instance.PlaySFX(r.sound,r.volume*c.intensity*(1+VisualRange(-variation,variation)),1+VisualRange(-variation,variation));
        }
        if(cue==FeedbackCue.Distortion) FeedbackDistortionFeature.Add(position);
        if(r.prefab==null) return null;
        bursts++;
        float angle=direction.sqrMagnitude>.001f?Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg:0;
        var burst=FeedbackBurst.Spawn(r.prefab,position,Quaternion.Euler(0,0,angle));
        burst.Begin(r,position+(Vector3)direction,target,tier,sprite,tint);
        return burst;
    }
    public static int WeaponTier => Mathf.Clamp(PlayerSkillInventoryManager.Instance?.Weapon?.upgradeStage??0,0,3);
    public static int MinionTier(MainMinionDataSO data=null)
    {
        var inventory=InventoryManager.Instance;
        if(data==null) data=inventory!=null?inventory.MainSummon:null;
        return Mathf.Clamp((data!=null?data.feedbackTier:0)+(inventory!=null?inventory.MainSummonEnhanceLevel:0),0,3);
    }
    public static PlayerController Player => GameManager.Instance!=null?GameManager.Instance.PLAYERCONTROLLER:null;
    public static void AtPlayer(FeedbackCue cue, int tier=0, Sprite sprite=null)
    {
        if(Player!=null) Play(cue,Player.transform.position,Vector2.up,tier,sprite:sprite);
    }
    public static void Pickup(Vector3 position, Sprite sprite, Vector3? sourceWorldScale=null)
    {
        Play(FeedbackCue.Pickup,position,Vector2.up);
        FeedbackOverlay.Instance?.Pulse(FeedbackCue.Pickup,position,sprite,sourceWorldScale:sourceWorldScale);
    }
    public static void ResolvedHit(CharacterHealth health, CharacterStat attacker, DamageInfo info, bool critical)
    {
        if(health==null || !DamageRules.IsPlayerSourced(info.category) || info.category==DamageCategory.Parry) return;
        Vector3 pos=health.transform.position;
        Vector2 dir=info.attacker!=null ? pos-info.attacker.transform.position:Vector2.right;
        bool minion=info.playerBasicAttack==null;
        int tier=minion?MinionTier():WeaponTier;
        Play(FeedbackCue.DirectionHit,pos,dir,tier);
        if(critical) Play(FeedbackCue.CriticalEcho,pos,dir,tier);
        if(info.feedbackComboFinish) { Play(FeedbackCue.ComboFinish,pos,dir,tier); Play(FeedbackCue.HeavyDust,pos,dir,tier); }
        var entity=health.GetComponentInParent<BaseEntity>();
        Color material=entity!=null&&entity.MinionData!=null?entity.MinionData.impactColor:Color.white;
        Play(FeedbackCue.MaterialHit,pos,dir,tier,tint:material);
        if(minion) { var main=InventoryManager.Instance!=null?InventoryManager.Instance.MainSummon:null; Play(FeedbackCue.MinionFamily,pos,dir,tier,tint:main!=null?main.feedbackColor:(Color?)null); }
        if(info.feedbackHitIndex>0)
        {
            Play(FeedbackCue.MultiHit,pos,-dir,tier);
            Play(FeedbackCue.MarionetteHit,pos,-dir,tier);
        }
        Play(FeedbackCue.ImpactSound,pos,dir,tier);
        if(critical||info.feedbackComboFinish)
        {
            if(Enabled(FeedbackCue.CameraKick)) CameraManager.Instance?.DirectionalKick(dir,Catalog.cameraForce*Catalog.Find(FeedbackCue.CameraKick).intensity);
            if(tier>=2&&Enabled(FeedbackCue.HitStop)&&Time.timeScale>.9f) HitStopManager.Instance?.DoHitStop(Catalog.hitStopSeconds*Catalog.Find(FeedbackCue.HitStop).intensity);
            Play(FeedbackCue.Distortion,pos,dir,tier);
        }
    }
    public static void Killed(CharacterHealth health, DamageInfo info)
    {
        if(health==null) return;
        var entity=health.GetComponentInParent<BaseEntity>();
        if(entity==null) return;
        var pos=entity.transform.position;
        Play(FeedbackCue.KillHit,pos,Vector2.up,MinionTier());
        if(Player!=null) Play(FeedbackCue.SoulReturn,pos,Vector2.up,MinionTier(),Player.transform);
        if(entity.MinionData!=null&&entity.MinionData.tier==EnemyTier.Boss)
        {
            Play(FeedbackCue.BossDeath,pos,Vector2.up,3);
            if(Enabled(FeedbackCue.SlowMotion)) HitStopManager.Instance?.DoSlowMotion(Catalog.slowMotionSeconds,Catalog.slowMotionScale);
        }
    }
}
