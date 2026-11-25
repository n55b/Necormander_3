using UnityEngine;
using UnityEngine.Rendering;

/// <summary>프리팹에 미리 배치한 선/아이콘만 재생한다. 충돌체·피해·캐릭터 크기는 변경하지 않는다.</summary>
[DefaultExecutionOrder(10000)]
public sealed class FeedbackBurst : MonoBehaviour
{
    [SerializeField] private LineRenderer[] lines;
    [SerializeField] private SpriteRenderer icon;
    private FeedbackRecipe recipe;
    private Vector3 origin, end;
    private Transform target;
    private Transform originAnchor;
    private Vector3 originOffset;
    private SpriteRenderer sortingAnchor;
    private SpriteRenderer landingSprite;
    private FeedbackSurface landingSurface;
    private SortingGroup sortingGroup;
    private int[] defaultLayers, defaultOrders;
    private int iconLayer, iconOrder;
    private float age, power, reach;
    private int renderedTick, finishBeat;
    private bool decisive, boss;
    private Vector3 groundRight, groundUp;
    private int previousLayer, previousOrder;
    private bool hasOrder;
    private readonly Vector3[] arcVertices = new Vector3[17];
    private readonly Vector3[] linkVertices = new Vector3[12];
    private int count;
    private bool registered;
    private FeedbackBurst source;
    private static readonly Vector2[] ringPoints = MakeArc(Mathf.PI * 2f);
    private static readonly Vector2[] arcPoints = MakeArc(Mathf.PI * .8f);
    private static Vector2[] MakeArc(float span)
    {
        var points = new Vector2[17];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = i * span / 16f;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        return points;
    }
    public static int ActiveCount { get; private set; }
    private void Awake()
    {
        defaultLayers = new int[lines.Length]; defaultOrders = new int[lines.Length];
        for (int i = 0; i < lines.Length; i++) if (lines[i] != null)
        { defaultLayers[i] = lines[i].sortingLayerID; defaultOrders[i] = lines[i].sortingOrder; }
        if (icon != null) { iconLayer = icon.sortingLayerID; iconOrder = icon.sortingOrder; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCount() { ActiveCount=0; SceneEffectPool<FeedbackBurst>.Reset(); }

    public static FeedbackBurst Spawn(FeedbackBurst prefab, Vector3 position, Quaternion rotation)
    {
        var result=SceneEffectPool<FeedbackBurst>.Take(prefab);
        if(result==null) { result=Instantiate(prefab,position,rotation); result.source=prefab; }
        else { result.transform.SetPositionAndRotation(position,rotation); result.gameObject.SetActive(true); }
        return result;
    }
    /// <summary>로딩 중 한 개씩 준비한다. 풀에 남은 유효한 객체가 목표 수 이상이면 생성하지 않는다.</summary>
    public static void PrewarmOne(FeedbackBurst prefab, int desired)
    {
        if (prefab == null || desired <= 0) return;
        var available=SceneEffectPool<FeedbackBurst>.For(prefab);
        int valid = 0;
        foreach (var entry in available) if (entry != null) valid++;
        if (valid >= desired) return;
        var instance = Instantiate(prefab);
        instance.source = prefab;
        instance.gameObject.SetActive(false);
        available.Enqueue(instance);
    }
    private void Release()
    {
        gameObject.SetActive(false);
        if(source==null) { Destroy(gameObject); return; }
        var available=SceneEffectPool<FeedbackBurst>.For(source);
        if(available.Count<PolishFeedback.Catalog.simultaneousBursts) available.Enqueue(this);
        else Destroy(gameObject);
    }

    public void Begin(FeedbackRecipe settings, Vector3 destination, Transform follow, int tier, Sprite sprite, Color? tint)
    {
        transform.localScale = Vector3.one;
        originAnchor = null; sortingAnchor = null; hasOrder=false;
        for (int i = 0; i < lines.Length; i++) if (lines[i] != null)
        { lines[i].sortingLayerID = defaultLayers[i]; lines[i].sortingOrder = defaultOrders[i]; }
        if (icon != null) { icon.sortingLayerID = iconLayer; icon.sortingOrder = iconOrder; }
        recipe=settings; origin=transform.position; end=destination; target=follow;
        var catalog=PolishFeedback.Catalog;
        var growth=settings.scaleWithTier ? catalog.Tier(tier) : null;
        power=settings.intensity*catalog.intensity*(growth!=null?growth.intensity:1);
        reach=settings.radius*(growth!=null?growth.reach:1);
        count=Mathf.Clamp(Mathf.RoundToInt(settings.count*(growth!=null?growth.density:1)),1,lines.Length);
        if(settings.shape==FeedbackShape.Cross) count=Mathf.Max(2,count);
        if(settings.shape==FeedbackShape.Link) count=Mathf.Max(2,count);
        color=tint??settings.color;
        age=-settings.delay;
        renderedTick=-1;
        boss = settings.cue == FeedbackCue.BossDeath;
        decisive = boss || settings.cue == FeedbackCue.LastEnemyImpact;
        finishBeat = 0;
        if (decisive)
        {
            groundRight = transform.InverseTransformDirection(Vector3.right);
            groundUp = transform.InverseTransformDirection(Vector3.up);
        }
        if(icon!=null) { icon.sprite=sprite; icon.enabled=sprite!=null; icon.transform.localScale=Vector3.one; }
        registered=true; ActiveCount++;
        Render(0,false);
    }
    internal bool AnimateDrop(FeedbackSurface surface, SpriteRenderer picture)
    {
        if (icon == null || picture.sprite == null) return false;
        landingSurface = surface;
        landingSprite = picture;
        icon.flipX = picture.flipX;
        icon.flipY = picture.flipY;
        icon.transform.localScale = picture.transform.lossyScale;
        picture.enabled = false;
        return true;
    }

    public void FitLane(float length)
    {
        transform.localScale = new Vector3(length / Mathf.Max(.01f, reach * 2f), 1f, 1f);
    }
    // 부모로 묶지 않아 대상이 사라져도 연출과 풀의 수명은 유지한다.
    public void FollowOrigin(Transform anchor, SpriteRenderer sorting = null)
    {
        if (anchor == null) return;
        originAnchor = anchor; originOffset = transform.position - anchor.position;
        var actor = sorting == null ? WorldDepthOrder.FindParticipant(anchor) : null;
        sortingAnchor = actor != null ? actor.Renderer : sorting;
        sortingGroup = actor != null ? actor.Group : sorting != null ? WorldDepthOrder.FindEffectiveGroup(sorting) : null;
        Follow();
    }
    private void LateUpdate() => Follow();
    private void Follow()
    {
        if (originAnchor != null && originAnchor.gameObject.activeInHierarchy)
        {
            Vector3 position = originAnchor.position + originOffset;
            if (target == null) end += position - origin;
            transform.position = position; origin = position;
        }
        else originAnchor = null;
        if (sortingAnchor == null) return;
        WorldDepthOrder.ReadOrder(sortingAnchor, sortingGroup, out int layer, out int order);
        if (hasOrder && previousLayer==layer && previousOrder==order) return;
        hasOrder=true; previousLayer=layer; previousOrder=order;
        foreach (var line in lines) if (line != null)
        { line.sortingLayerID = layer; line.sortingOrder = WorldDepthOrder.AttachmentOrder(layer, order, 1); }
        if (icon != null) { icon.sortingLayerID = layer; icon.sortingOrder = WorldDepthOrder.AttachmentOrder(layer, order, 2); }
    }
    private Color color;
    private void Update()
    {
        if(recipe==null) return;
        if (!ReferenceEquals(landingSurface, null) && (landingSurface == null || !landingSurface.isActiveAndEnabled))
        {
            Release();
            return;
        }
        if(!PolishFeedback.Enabled(recipe.cue)) { Release(); return; }
        if (decisive && Time.timeScale <= 0) return;
        age += decisive ? Time.unscaledDeltaTime : Time.deltaTime;
        if(age<0) return;
        if (decisive) AdvanceFinishingBlow();
        int tick=Mathf.FloorToInt(age*60f);
        if (tick!=renderedTick) { renderedTick=tick; Render(Mathf.Clamp01(age/Mathf.Max(.02f,recipe.duration)),true); }
        if (age >= recipe.duration)
        {
            var landed = landingSurface;
            Release();
            if (landed != null && landed.isActiveAndEnabled) landed.DropLanded();
        }
    }
    // Run accents on the pooled marker's real-time clock so the shockwave crosses a frozen pose.
    private void AdvanceFinishingBlow()
    {
        Vector2 heading = (end - origin).normalized;
        if (finishBeat == 0)
        {
            finishBeat++;
            WorldAtmosphere.Current?.Accent(origin);
            WorldSparkField.Emit(WorldSparkField.Kind.EnemyHit, origin, heading, boss ? 32 : 22);
            PolishFeedback.Play(FeedbackCue.Distortion, origin, heading, 3, playSound: false);
        }
        if (finishBeat == 1 && age >= (boss ? .17f : .12f))
        {
            finishBeat++;
            WorldSparkField.Emit(WorldSparkField.Kind.Reflect, origin, -heading, boss ? 24 : 12);
            PolishFeedback.Play(boss ? FeedbackCue.BossLand : FeedbackCue.CriticalEcho, origin, Vector2.right, 3, playSound: false);
        }
        if (boss && finishBeat == 2 && age >= .39f)
        {
            finishBeat++;
            WorldAtmosphere.Current?.Accent(origin);
            WorldSparkField.Emit(WorldSparkField.Kind.Ember, origin, Vector2.up, 20);
            WorldSparkField.Emit(WorldSparkField.Kind.Wall, origin, heading, 14);
        }
    }

    private void RenderFinisher(float t, bool visible)
    {
        // Two traveling pressure fronts, a contact star, then a fan of fragments.
        float pixel = PixelViewport.WorldUnitsPerPixel;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == null) continue;
            bool ring = i < 6;
            bool echo = i >= 3 && i < 6;
            bool star = i == 6 || i == 7;
            float start = echo ? .18f + .025f * (i % 3) : i > 7 ? .035f * (i % 4) : 0;
            float endTime = star ? .46f : ring && !echo ? .78f : 1;
            float age = Mathf.InverseLerp(start, endTime, t);
            line.enabled = visible && i < count && t >= start && t < endTime;
            if (!line.enabled) continue;
            float spread = 1 - (1-age)*(1-age)*(1-age);
            Color tint = star ? Color.white : Color.Lerp(color, Color.white, echo ? .54f : .12f);
            tint *= Mathf.Max(1, power);
            tint.a = Mathf.Clamp01(power) * (1 - age * age);
            line.startColor = tint;
            line.endColor = tint;
            line.loop = false;
            line.widthMultiplier = PixelViewport.QuantizeStrokeWidth(recipe.width *
                (star ? 2.2f : ring ? 1.25f : 1f) * (1.15f - .65f * age), pixel);
            if (ring)
            {
                float radius = reach * (.08f + spread * (echo ? .78f : 1f));
                float segment = Mathf.PI * 2 / 3;
                float gap = (echo ? .22f : .02f) + age * age * .82f;
                float begin = segment * (i % 3) + (echo ? .62f - age * .26f : age * .19f);
                line.positionCount = arcVertices.Length;
                for (int j = 0; j < arcVertices.Length; j++)
                {
                    float u = j / (float)(arcVertices.Length - 1);
                    float angle = begin + segment * Mathf.Lerp(gap, 1, u);
                    arcVertices[j] = (groundRight * Mathf.Cos(angle) + groundUp * (.62f * Mathf.Sin(angle))) * radius;
                }
                var tail = tint; tail.a *= .24f;
                line.startColor = tail;
                line.SetPositions(arcVertices);
            }
            else if (star)
            {
                Vector3 axis = i == 6 ? new Vector3(1, .12f) : new Vector3(-.12f, 1);
                float span = reach * (i == 6 ? .82f : .44f) * (1 - age);
                line.positionCount = 2;
                line.SetPosition(0, -axis * span);
                line.SetPosition(1, axis * span);
            }
            else
            {
                float angle = (i-8) * 2.39996f;
                var ray = new Vector3(Mathf.Cos(angle), .84f * Mathf.Sin(angle));
                float tip = reach * (.27f + spread * (.68f + .16f * (i % 3)));
                line.positionCount = 2;
                line.SetPosition(0, ray * (tip - reach * .33f * (1 - age)));
                line.SetPosition(1, ray * tip);
            }
        }
    }

    private void Render(float t,bool visible)
    {
        if (decisive) { RenderFinisher(t, visible); return; }
        float p=recipe.motion.Evaluate(t);
        float fade=(1-t)*(1-t);
        Vector3 destination=target!=null?target.position:end;
        float unitsPerPixel = PixelViewport.WorldUnitsPerPixel;
        float stroke = PixelViewport.QuantizeStrokeWidth(recipe.width, unitsPerPixel);
        for(int i=0;i<lines.Length;i++)
        {
            var line=lines[i]; if(line==null) continue;
            bool enabled=visible&&i<count;
            if(line.enabled!=enabled) line.enabled=enabled;
            if(!enabled) continue;
            float a=i*Mathf.PI*2/count;
            if(recipe.shape==FeedbackShape.Cross) a=i*Mathf.PI/count;
            if(recipe.cue==FeedbackCue.DirectionHit||recipe.cue==FeedbackCue.WallHit||recipe.cue==FeedbackCue.Foliage)
                a=Mathf.Lerp(-.65f,.65f,count>1?i/(float)(count-1):.5f);
            Vector3 dir=new Vector3(Mathf.Cos(a),Mathf.Sin(a));
            float length=reach*(.3f+.7f*p);
            line.widthMultiplier=stroke;
            var c=color; c.r*=Mathf.Max(1,power); c.g*=Mathf.Max(1,power); c.b*=Mathf.Max(1,power); c.a*=Mathf.Clamp01(power)*fade;
            line.startColor=c; line.endColor=c;
            line.loop=false;
            Vector3 drift=(Vector3)recipe.drift*p;
            switch(recipe.shape)
            {
                case FeedbackShape.Ring:
                case FeedbackShape.Arc:
                    line.positionCount=17;
                    var points=recipe.shape==FeedbackShape.Ring?ringPoints:arcPoints;
                    for(int j=0;j<points.Length;j++)
                    {
                        var point=points[j];
                        arcVertices[j]=new Vector3((dir.x*point.x-dir.y*point.y)*length,(dir.y*point.x+dir.x*point.y)*length*.65f)+drift;
                    }
                    line.SetPositions(arcVertices);
                    break;
                case FeedbackShape.Link:
                    Vector3 local=transform.InverseTransformPoint(destination);
                    if(i==0)
                    {
                        line.positionCount=12;
                        for(int j=0;j<12;j++) { float f=j/11f; linkVertices[j]=Vector3.Lerp(Vector3.zero,local,f)+Vector3.up*Mathf.Sin(f*Mathf.PI)*reach*(1-t); }
                        line.SetPositions(linkVertices);
                        var path=c; path.a*=.35f; line.startColor=line.endColor=path;
                    }
                    else
                    {
                        line.positionCount=2;
                        Vector3 orb=Vector3.Lerp(Vector3.zero,local,p)+Vector3.up*Mathf.Sin(p*Mathf.PI)*reach*(1-t);
                        line.widthMultiplier=PixelViewport.QuantizeStrokeWidth(recipe.width*2, unitsPerPixel);
                        line.SetPosition(0,orb); line.SetPosition(1,orb+Vector3.up*recipe.width);
                    }
                    break;
                case FeedbackShape.Inward:
                    line.positionCount=2;
                    var inward=dir*reach*(1-p)+drift;
                    line.SetPosition(0,inward); line.SetPosition(1,inward+dir*recipe.width); break;
                case FeedbackShape.Dots:
                    line.positionCount=2;
                    var pos=dir*length+drift+Vector3.down*t*t*reach*.3f;
                    line.SetPosition(0,pos); line.SetPosition(1,pos+Vector3.right*recipe.width); break;
                case FeedbackShape.Cross:
                    line.positionCount=2; line.SetPosition(0,-dir*length*.5f); line.SetPosition(1,dir*length*.5f); break;
                case FeedbackShape.Slash:
                    line.positionCount=3; line.SetPosition(0,new Vector3(-length,-.08f*i)); line.SetPosition(1,new Vector3(0,.1f)); line.SetPosition(2,new Vector3(length,.08f*i)); break;
                default:
                    line.positionCount=2; line.SetPosition(0,dir*length*.35f+drift); line.SetPosition(1,dir*length+drift); break;
            }
        }
        if(icon!=null&&icon.sprite!=null)
        {
            icon.enabled=visible;
            icon.transform.rotation=Quaternion.identity;
            if (recipe.cue == FeedbackCue.DropLand)
            {
                float bounce = t < .72f ? Mathf.Sin(Mathf.PI * t / .72f) : .18f * Mathf.Sin(Mathf.PI * (t-.72f)/.28f);
                var ground = landingSprite != null ? landingSprite.transform.position : destination;
                var point = Vector3.Lerp(origin, ground, p) + Vector3.up * bounce * Mathf.Min(.48f,reach);
                if (unitsPerPixel > 0) { point.x=Mathf.Round(point.x/unitsPerPixel)*unitsPerPixel; point.y=Mathf.Round(point.y/unitsPerPixel)*unitsPerPixel; }
                icon.transform.position=point;
                Color tint = landingSprite != null ? landingSprite.color : Color.white;
                tint.a *= Mathf.SmoothStep(0f, 1f, t / .18f);
                icon.color = tint;
                if (landingSprite != null) icon.transform.rotation = landingSprite.transform.rotation;
            }
            else
            {
                icon.transform.position=Vector3.Lerp(origin,destination,p)+Vector3.up*Mathf.Sin(t*Mathf.PI)*reach;
                var c=Color.white; c.a=1-t; icon.color=c;
            }
        }
    }
    private void OnDisable()
    {
        if (landingSprite != null) landingSprite.enabled = true;
        landingSprite = null;
        landingSurface = null;
        if (icon != null) icon.flipX = icon.flipY = false;
        originAnchor = null; sortingAnchor = null; target = null;
        if(registered) ActiveCount=Mathf.Max(0,ActiveCount-1);
        registered=false;
        if(lines!=null) foreach(var line in lines) if(line!=null) line.enabled=false;
        if(icon!=null) icon.enabled=false;
    }
}
