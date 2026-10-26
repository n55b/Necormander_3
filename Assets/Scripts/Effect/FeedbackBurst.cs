using UnityEngine;
using System.Collections.Generic;

/// <summary>프리팹에 미리 배치한 선/아이콘만 재생한다. 충돌체·피해·캐릭터 크기는 변경하지 않는다.</summary>
public sealed class FeedbackBurst : MonoBehaviour
{
    [SerializeField] private LineRenderer[] lines;
    [SerializeField] private SpriteRenderer icon;
    private FeedbackRecipe recipe;
    private Vector3 origin, end;
    private Transform target;
    private float age, power, reach;
    private int count;
    private bool registered;
    private FeedbackBurst source;
    private static readonly Dictionary<FeedbackBurst,Queue<FeedbackBurst>> pool = new Dictionary<FeedbackBurst,Queue<FeedbackBurst>>();
    public static int ActiveCount { get; private set; }
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
            line.enabled=visible&&i<count;
            if(!line.enabled) continue;
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
                    float span=recipe.shape==FeedbackShape.Ring?Mathf.PI*2:Mathf.PI*.8f;
                    for(int j=0;j<17;j++) { float angle=a+j*span/16; line.SetPosition(j,new Vector3(Mathf.Cos(angle)*length,Mathf.Sin(angle)*length*.65f)+drift); }
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
        if(registered) ActiveCount=Mathf.Max(0,ActiveCount-1);
        registered=false;
        if(lines!=null) foreach(var line in lines) if(line!=null) line.enabled=false;
        if(icon!=null) icon.enabled=false;
    }
}
