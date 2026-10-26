using UnityEngine;
using UnityEngine.UI;

/// <summary>전용 프리팹의 고정 이미지 풀. 입력을 받지 않는 획득/준비/피격 표시.</summary>
public sealed class FeedbackOverlay : MonoBehaviour
{
    public static FeedbackOverlay Instance { get; private set; }
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private Image[] motes;
    [SerializeField] private Image[] edges;
    [SerializeField] private RectTransform bagAnchor, healthAnchor, skillAnchor, goldAnchor;
    [SerializeField, Min(1f), Tooltip("중첩 알림 등 HUD 아이콘의 최대 크기(UI 단위). 습득 중 날아가는 아이콘은 바닥에서 보이던 화면 크기를 유지합니다.")]
    private float iconSize = 24f;
    [SerializeField, Min(.01f), Tooltip("회복 구슬이 체력바에 도착한 뒤 작아지며 흡수되는 시간(초)입니다.")]
    private float healAbsorbDuration = .16f;
    private float[] ages;
    private Vector2[] starts, ends;
    private FeedbackRecipe[] recipes;
    private System.Func<Vector2?>[] destinations;
    private System.Action<bool>[] completions;
    private bool[] arrived;
    private int cursor;
    private float hurtAge=10;
    private int hurtEdge;
    private float healthFraction=1;
    private void Awake()
    {
        Instance=this;
        ages=new float[motes.Length]; starts=new Vector2[motes.Length]; ends=new Vector2[motes.Length]; recipes=new FeedbackRecipe[motes.Length];
        destinations=new System.Func<Vector2?>[motes.Length]; completions=new System.Action<bool>[motes.Length]; arrived=new bool[motes.Length];
        foreach(var image in motes) { image.raycastTarget=false; image.enabled=false; }
        foreach(var image in edges) { image.raycastTarget=false; image.enabled=false; }
    }
    public void SetHealth(float fraction) => healthFraction=fraction;
    public void Hurt(Vector2 direction)
    {
        hurtAge=0;
        hurtEdge=Mathf.Abs(direction.x)>Mathf.Abs(direction.y)?(direction.x>0?1:3):(direction.y>0?0:2);
    }
    // 동적 목적지는 화면 좌표를 반환한다. null이면 소유 HUD가 더 이상 이 연출을 기다리지 않는다.
    // 완료 콜백의 false는 풀 교체/비활성화: 기다리는 체력 표시를 즉시 실제 값에 합류시킨다.
    public bool Pulse(FeedbackCue cue, Vector3 world, Sprite sprite=null, RectTransform destination=null, Vector3? sourceWorldScale=null,
        System.Func<Vector2?> screenDestination=null, System.Action<bool> onFinished=null)
    {
        if(!isActiveAndEnabled||!PolishFeedback.Enabled(cue)||motes.Length==0) return false;
        Vector2? dynamicEnd=screenDestination?.Invoke();
        if(screenDestination!=null&&!dynamicEnd.HasValue) return false;
        var recipe=PolishFeedback.Catalog.Find(cue);
        int i=cursor++%motes.Length;
        Finish(i,false);
        var anchor=destination!=null?destination:cue==FeedbackCue.Pickup||cue==FeedbackCue.StackUp||cue==FeedbackCue.StackMax?bagAnchor:
            cue==FeedbackCue.Heal?healthAnchor:cue==FeedbackCue.Purchase?goldAnchor:skillAnchor;
        var cam=Camera.main;
        Vector2 screen=cam!=null?(Vector2)cam.WorldToScreenPoint(world):new Vector2(Screen.width*.5f,Screen.height*.5f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,null,out starts[i]);
        var endScreen=dynamicEnd??RectTransformUtility.WorldToScreenPoint(null,anchor.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,endScreen,null,out ends[i]);
        if(cue!=FeedbackCue.Pickup&&cue!=FeedbackCue.Heal) starts[i]=ends[i];
        ages[i]=0; recipes[i]=recipe;
        destinations[i]=screenDestination; completions[i]=onFinished; arrived[i]=false;
        var image=motes[i];
        image.sprite=sprite;
        image.preserveAspect=true;
        Vector2 size=sprite!=null?Vector2.one*Mathf.Max(1f,iconSize):new Vector2(10,10);
        if(cue==FeedbackCue.Pickup && sprite!=null && cam!=null)
        {
            // 텍스처 픽셀 수는 UI 크기가 아니다. 월드에서 보이던 크기를 화면 → 캔버스로 옮긴다.
            Vector3 scale=sourceWorldScale??Vector3.one;
            Vector2 spriteSize=sprite.rect.size/Mathf.Max(.001f,sprite.pixelsPerUnit);
            Vector3 half=new Vector3(spriteSize.x*Mathf.Abs(scale.x),spriteSize.y*Mathf.Abs(scale.y),0f)*.5f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,cam.WorldToScreenPoint(world-half),null,out var lower);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,cam.WorldToScreenPoint(world+half),null,out var upper);
            size=new Vector2(Mathf.Abs(upper.x-lower.x),Mathf.Abs(upper.y-lower.y));
        }
        image.rectTransform.localScale=Vector3.one;
        image.rectTransform.sizeDelta=size;
        image.rectTransform.anchoredPosition=starts[i];
        var color=recipe.color; color.a*=recipe.intensity*PolishFeedback.Catalog.intensity;
        image.color=color;
        image.enabled=true;
        return true;
    }
    private void Update()
    {
        PolishFeedback.RefreshShaderSettings();
        var catalog=PolishFeedback.Catalog; if(catalog==null) return;
        for(int i=0;i<motes.Length;i++)
        {
            var recipe=recipes[i]; if(recipe==null) continue;
            ages[i]+=Time.unscaledDeltaTime;
            float t=Mathf.Clamp01(ages[i]/Mathf.Max(.02f,recipe.duration));
            if(!PolishFeedback.Enabled(recipe.cue)) { Finish(i,false); continue; }
            if(destinations[i]!=null)
            {
                var target=destinations[i]();
                if(!target.HasValue) { Finish(i,false); continue; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,target.Value,null,out ends[i]);
            }
            bool heal=recipe.cue==FeedbackCue.Heal&&destinations[i]!=null;
            if(t>=1&&!arrived[i])
            {
                arrived[i]=true;
                var completed=completions[i]; completions[i]=null;
                completed?.Invoke(true);
                if(!heal) { Finish(i,true); continue; }
            }
            float absorption=heal&&arrived[i]?Mathf.Clamp01((ages[i]-Mathf.Max(.02f,recipe.duration))/Mathf.Max(.01f,healAbsorbDuration)):0f;
            if(absorption>=1) { Finish(i,true); continue; }
            var p=Vector2.Lerp(starts[i],ends[i],recipe.motion.Evaluate(t));
            if(arrived[i]) p=ends[i];
            if(starts[i]!=ends[i]) p.y+=Mathf.Sin(t*Mathf.PI)*recipe.radius*100;
            motes[i].rectTransform.anchoredPosition=p;
            motes[i].rectTransform.localScale=Vector3.one*(heal?Mathf.Lerp(1f,.1f,absorption):1f);
            var color=recipe.color; color.a*=recipe.intensity*catalog.intensity*(heal?1-absorption:1-t);
            motes[i].color=color;
        }
        hurtAge+=Time.unscaledDeltaTime;
        float low=healthFraction>0&&healthFraction<catalog.lowHealthRatio&&PolishFeedback.Enabled(FeedbackCue.LowHealth)
            ?(.5f+.5f*Mathf.Sin(Time.unscaledTime*Mathf.PI*2/Mathf.Max(.01f,catalog.lowHealthInterval)))*catalog.Find(FeedbackCue.LowHealth).color.a*catalog.Find(FeedbackCue.LowHealth).intensity:0;
        for(int i=0;i<edges.Length;i++)
        {
            var hurtRecipe=catalog.Find(FeedbackCue.HurtDirection);
            float hurt=i==hurtEdge&&PolishFeedback.Enabled(FeedbackCue.HurtDirection)?Mathf.Clamp01(1-hurtAge/Mathf.Max(.01f,hurtRecipe.duration))*hurtRecipe.color.a*hurtRecipe.intensity:0;
            var color=hurt>low?hurtRecipe.color:catalog.Find(FeedbackCue.LowHealth).color;
            color.a=Mathf.Max(low,hurt)*catalog.intensity;
            edges[i].color=color; edges[i].enabled=color.a>0;
        }
    }
    private void Finish(int i, bool reached)
    {
        var completed=completions[i]; completions[i]=null;
        recipes[i]=null; destinations[i]=null; arrived[i]=false;
        motes[i].enabled=false;
        completed?.Invoke(reached);
    }
    private void OnDisable()
    {
        if(recipes==null) return;
        for(int i=0;i<recipes.Length;i++) Finish(i,false);
    }
    private void OnDestroy() { if(Instance==this) Instance=null; }
}
