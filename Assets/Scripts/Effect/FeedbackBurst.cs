using UnityEngine;
using System.Collections.Generic;

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
    private int[] defaultLayers, defaultOrders;
    private int iconLayer, iconOrder;
    private float age, power, reach;
    private int count;
    private bool registered;
    private FeedbackBurst source;
    private static readonly Dictionary<FeedbackBurst,Queue<FeedbackBurst>> pool = new Dictionary<FeedbackBurst,Queue<FeedbackBurst>>();
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
    private static void ResetCount() { ActiveCount=0; pool.Clear(); }

    public static FeedbackBurst Spawn(FeedbackBurst prefab, Vector3 position, Quaternion rotation)
    {
        FeedbackBurst result=null;
        if(pool.TryGetValue(prefab,out var available)) while(available.Count>0&&result==null) result=available.Dequeue();
        if(result==null) { result=Instantiate(prefab,position,rotation); result.source=prefab; }
        else { result.transform.SetPositionAndRotation(position,rotation); result.gameObject.SetActive(true); }
        return result;
    }
    /// <summary>로딩 중 한 개씩 준비한다. 풀에 남은 유효한 객체가 목표 수 이상이면 생성하지 않는다.</summary>
    public static void PrewarmOne(FeedbackBurst prefab, int desired)
    {
        if (prefab == null || desired <= 0) return;
        if (!pool.TryGetValue(prefab, out var available)) pool[prefab] = available = new Queue<FeedbackBurst>();
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
        if(!pool.TryGetValue(source,out var available)) pool[source]=available=new Queue<FeedbackBurst>();
        if(available.Count<PolishFeedback.Catalog.simultaneousBursts) available.Enqueue(this);
        else Destroy(gameObject);
    }

    public void Begin(FeedbackRecipe settings, Vector3 destination, Transform follow, int tier, Sprite sprite, Color? tint)
    {
        originAnchor = null; sortingAnchor = null;
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
        if(icon!=null) { icon.sprite=sprite; icon.enabled=sprite!=null; icon.transform.localScale=Vector3.one; }
        registered=true; ActiveCount++;
        Render(0,false);
    }
    // 부모로 묶지 않아 대상이 사라져도 연출과 풀의 수명은 유지한다.
    public void FollowOrigin(Transform anchor, SpriteRenderer sorting = null)
    {
        if (anchor == null) return;
        originAnchor = anchor; originOffset = transform.position - anchor.position; sortingAnchor = sorting;
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
        foreach (var line in lines) if (line != null)
        { line.sortingLayerID = sortingAnchor.sortingLayerID; line.sortingOrder = sortingAnchor.sortingOrder + 1; }
        if (icon != null) { icon.sortingLayerID = sortingAnchor.sortingLayerID; icon.sortingOrder = sortingAnchor.sortingOrder + 2; }
    }
    private Color color;
    private void Update()
    {
        if(recipe==null) return;
        if(!PolishFeedback.Enabled(recipe.cue)) { Release(); return; }
        age+=Time.deltaTime;
        if(age<0) return;
        Render(Mathf.Clamp01(age/Mathf.Max(.02f,recipe.duration)),true);
        if(age>=recipe.duration) Release();
    }
    private void Render(float t,bool visible)
    {
        float p=recipe.motion.Evaluate(t);
        float fade=(1-t)*(1-t);
        Vector3 destination=target!=null?target.position:end;
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
            line.widthMultiplier=recipe.width;
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
                        line.SetPosition(j,new Vector3((dir.x*point.x-dir.y*point.y)*length,(dir.y*point.x+dir.x*point.y)*length*.65f)+drift);
                    }
                    break;
                case FeedbackShape.Link:
                    Vector3 local=transform.InverseTransformPoint(destination);
                    if(i==0)
                    {
                        line.positionCount=12;
                        for(int j=0;j<12;j++) { float f=j/11f; line.SetPosition(j,Vector3.Lerp(Vector3.zero,local,f)+Vector3.up*Mathf.Sin(f*Mathf.PI)*reach*(1-t)); }
                        var path=c; path.a*=.35f; line.startColor=line.endColor=path;
                    }
                    else
                    {
                        line.positionCount=2;
                        Vector3 orb=Vector3.Lerp(Vector3.zero,local,p)+Vector3.up*Mathf.Sin(p*Mathf.PI)*reach*(1-t);
                        line.widthMultiplier=recipe.width*2;
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
            icon.transform.position=Vector3.Lerp(origin,destination,p)+Vector3.up*Mathf.Sin(t*Mathf.PI)*reach;
            var c=Color.white; c.a=1-t; icon.color=c;
        }
    }
    private void OnDisable()
    {
        originAnchor = null; sortingAnchor = null; target = null;
        if(registered) ActiveCount=Mathf.Max(0,ActiveCount-1);
        registered=false;
        if(lines!=null) foreach(var line in lines) if(line!=null) line.enabled=false;
        if(icon!=null) icon.enabled=false;
    }
}
