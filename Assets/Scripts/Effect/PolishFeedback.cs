using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>연출 호출과 설정 조회만 담당한다. 게임 판정은 호출한 원래 시스템이 소유한다.</summary>
public static class PolishFeedback
{
    private static FeedbackCatalog catalog;
    private static bool loaded;
    private static int frame, bursts, sounds, guardBursts;
    // 연출을 꺼도 전투/보상 추첨의 Unity Random 상태가 바뀌지 않는다.
    private static readonly System.Random visualRandom = new System.Random();
    internal static float VisualRange(float min, float max) => min+(max-min)*(float)visualRandom.NextDouble();
    private static readonly float[] next = new float[(int)FeedbackCue.PerfectGuardRing + 1];
    public static FeedbackCatalog Catalog
    {
        get { if(!loaded) { catalog=Resources.Load<FeedbackCatalog>("FeedbackCatalog"); loaded=true; } return catalog; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { catalog=null; loaded=false; frame=-1; System.Array.Clear(next,0,next.Length); Shader.SetGlobalFloat("_FeedbackDitherDisabled", 0f); }
    public static bool ExistingEnabled(ExistingFeedbackCue cue) => Catalog == null || Catalog.ExistingEnabled(cue);
    public static void RefreshShaderSettings() => Shader.SetGlobalFloat("_FeedbackDitherDisabled", ExistingEnabled(ExistingFeedbackCue.Dither) ? 0f : 1f);
    public static bool Enabled(FeedbackCue cue)
    {
        var c=Catalog; var r=c!=null?c.Find(cue):null;
        return c!=null && c.enabled && c.intensity>0 && r!=null && r.enabled && r.intensity>0;
    }
    public static FeedbackBurst Play(FeedbackCue cue, Vector3 position, Vector2 direction=default, int tier=0,
        Transform target=null, Sprite sprite=null, Color? tint=null)
    {
        var c=Catalog;
        if(c==null || !c.enabled || c.intensity<=0) return null;
        var r=c.Find(cue);
        if(r==null || !r.enabled || r.intensity<=0) return null;
        if(tier<r.minimumTier) return null;
        if(frame!=Time.frameCount) { frame=Time.frameCount; bursts=0; sounds=0; guardBursts=0; }
        int id=(int)cue;
        if(Time.time<next[id]) return null;
        // 성공을 알려주는 두 표시는 전투 이펙트가 몰린 프레임에도 누락하지 않는다.
        bool guardCue=cue==FeedbackCue.PerfectGuardFlash || cue==FeedbackCue.PerfectGuardRing;
        if(guardCue && guardBursts>=2) return null;
        if(!guardCue && (bursts>=c.burstsPerFrame || FeedbackBurst.ActiveCount>=c.simultaneousBursts)) return null;
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
        if(guardCue) guardBursts++;
        float angle=direction.sqrMagnitude>.001f?Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg:0;
        var burst=FeedbackBurst.Spawn(r.prefab,position,Quaternion.Euler(0,0,angle));
        burst.Begin(r,position+(Vector3)direction,target,tier,sprite,tint);
        return burst;
    }

    /// <summary>공용 효과와 현재 미니언의 타격 효과를 로딩 화면에서 조금씩 준비한다.</summary>
    public static IEnumerator Prewarm()
    {
        var c = Catalog;
        if (c == null || !c.enabled) yield break;
        int perFrame = 0;
        var prefabs = new HashSet<FeedbackBurst>();
        if (c.effects != null) foreach (var recipe in c.effects)
            if (recipe != null && recipe.enabled && recipe.prefab != null) prefabs.Add(recipe.prefab);
        foreach (var prefab in prefabs)
        {
            for (int i = 0; i < c.prewarmBurstCount; i++)
            {
                FeedbackBurst.PrewarmOne(prefab, c.prewarmBurstCount);
                if (++perFrame >= 2) { perFrame = 0; yield return null; }
            }
        }
        // 교체나 진화 후 처음 때릴 때도 같은 비용이 몰리지 않도록 등록된 소환수의 연출을 중복 없이 준비한다.
        var minions = new Queue<MainMinionDataSO>();
        var seen = new HashSet<MainMinionDataSO>();
        var impacts = new HashSet<ImpactFeedback>();
        var current = InventoryManager.Instance != null ? InventoryManager.Instance.MainSummon : null;
        if (current != null) minions.Enqueue(current);
        var data = GameManager.Instance != null ? GameManager.Instance.dataManager : null;
        var registry = data != null ? data.GET_GROWTH_REGISTRY() : null;
        if (registry != null && registry.minionDatas != null)
            foreach (var entry in registry.minionDatas) if (entry is MainMinionDataSO main) minions.Enqueue(main);
        if (data != null && data.ALL_MINION_DATA != null)
            foreach (var entry in data.ALL_MINION_DATA) if (entry is MainMinionDataSO main) minions.Enqueue(main);
        while (minions.Count > 0)
        {
            var minion = minions.Dequeue();
            if (minion == null || !seen.Add(minion)) continue;
            if (minion.evolutions != null) foreach (var next in minion.evolutions) if (next != null) minions.Enqueue(next);
            var impact = minion.hitEffectPrefab != null ? minion.hitEffectPrefab.GetComponent<ImpactFeedback>() : null;
            if (impact != null) impacts.Add(impact);
        }
        foreach (var impact in impacts)
            for (int i = 0; i < c.prewarmImpactCount; i++)
            {
                ImpactFeedback.PrewarmOne(impact, c.prewarmImpactCount);
                if (++perFrame >= 2) { perFrame = 0; yield return null; }
            }
        if (c.prewarmHitPrefabs != null) foreach (var prefab in c.prewarmHitPrefabs)
        {
            for (int i = 0; prefab != null && i < c.prewarmHitCount; i++)
            {
                PooledHitEffect.PrewarmOne(prefab, c.prewarmHitCount);
                if (++perFrame >= 2) { perFrame = 0; yield return null; }
            }
        }
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
        if(Player!=null) Play(cue,Player.transform.position,Vector2.up,tier,sprite:sprite)?.FollowOrigin(Player.transform);
    }
    public static IEnumerator ChestOpening(Transform chest, Animator animator)
    {
        if (chest == null) yield break;
        var settings = Catalog;
        if (settings == null) yield break;
        if (animator != null && animator.isActiveAndEnabled)
        {
            // Animator.Play가 실제 상태에 반영된 뒤 재생 비율을 읽는다.
            yield return null;
            while (chest != null && animator != null && animator.isActiveAndEnabled &&
                   animator.GetCurrentAnimatorStateInfo(0).IsName("Chest_Open") &&
                   animator.GetCurrentAnimatorStateInfo(0).normalizedTime < Mathf.Clamp(settings.chestOpenAnimationTime, 0f, .95f))
                yield return null;
        }
        if (chest == null) yield break;
        var renderer = chest.GetComponent<SpriteRenderer>();
        // 원형 빛은 회전할 필요가 없다. 위쪽 drift까지 90도 회전해 옆으로 밀리지 않게 한다.
        Play(FeedbackCue.ChestOpen, chest.position + settings.chestOpenOffset, Vector2.right)?.FollowOrigin(chest, renderer);
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
        Play(FeedbackCue.DirectionHit,pos,dir,tier)?.FollowOrigin(health.transform);
        if(critical) Play(FeedbackCue.CriticalEcho,pos,dir,tier)?.FollowOrigin(health.transform);
        if(info.feedbackComboFinish) { Play(FeedbackCue.ComboFinish,pos,dir,tier)?.FollowOrigin(health.transform); Play(FeedbackCue.HeavyDust,pos,dir,tier); }
        var entity=health.GetComponentInParent<BaseEntity>();
        Color material=entity!=null&&entity.MinionData!=null?entity.MinionData.impactColor:Color.white;
        Play(FeedbackCue.MaterialHit,pos,dir,tier,tint:material)?.FollowOrigin(health.transform);
        if(minion) { var main=InventoryManager.Instance!=null?InventoryManager.Instance.MainSummon:null; Play(FeedbackCue.MinionFamily,pos,dir,tier,tint:main!=null?main.feedbackColor:(Color?)null)?.FollowOrigin(health.transform); }
        if(info.feedbackHitIndex>0)
        {
            Play(FeedbackCue.MultiHit,pos,-dir,tier)?.FollowOrigin(health.transform);
            Play(FeedbackCue.MarionetteHit,pos,-dir,tier)?.FollowOrigin(health.transform);
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
