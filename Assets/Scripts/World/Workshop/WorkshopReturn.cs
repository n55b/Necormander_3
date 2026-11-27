using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class WorkshopReturn : MonoBehaviour
{
    private static readonly List<RectTransform> blockers=new();
    private static bool dragging;
    private RectTransform statusPanel; private TMP_Text statusResult; private int target;   // 0 가까운 적, 1 모든 적, 2 플레이어
    private RectTransform itemPanel; private TMP_Text giveResult; private int itemMode, itemPage;   // 방식 0 넣기, 1 빼기
    private const int ItemsPerPage=15;

    internal static void Block(RectTransform rect){ if(rect!=null && !blockers.Contains(rect)) blockers.Add(rect); }
    // 도구막대·열린 패널 사각형 안이거나, 그 안에서 시작한 드래그 중이면 전투 클릭을 막는다(LightingDebugPanel 방식).
    internal static bool CapturesPointer=>WorkshopTrial.Active && (dragging || Inside());
    private static bool Inside()
    {
        var mouse=Mouse.current; if(mouse==null) return false;
        blockers.RemoveAll(r=>r==null);
        foreach(var rect in blockers)
        {
            if(!rect.gameObject.activeInHierarchy) continue;
            var canvas=rect.GetComponentInParent<Canvas>().rootCanvas;
            if(RectTransformUtility.RectangleContainsScreenPoint(rect,PixelViewport.PointerForCanvas(canvas,mouse.position.ReadValue()),PixelViewport.CanvasCamera(canvas))) return true;
        }
        return false;
    }
    private void Start()
    {
        var canvas=WorkshopUI.Canvas("Workshop trial toolbar",32000);
        canvas.transform.SetParent(transform,false);
        if (WorkshopTrial.Village!=null && gameObject.scene.name=="VillageScene")
            gameObject.AddComponent<WorkshopVillageEditor>().Initialize(canvas,WorkshopTrial.Village);   // Initialize가 '마을 편집' 토글을 Block한다
        Block((RectTransform)WorkshopUI.Button(canvas.transform,"편집으로  F6",275,4,90,19,()=>WorkshopTrial.Open()).transform);
        Block((RectTransform)WorkshopUI.Button(canvas.transform,"상태이상",369,4,64,19,()=>ToggleStatus(canvas),false,"상태이상 부여").transform);
        Block((RectTransform)WorkshopUI.Button(canvas.transform,"아이템",437,4,52,19,()=>ToggleItems(canvas),false,"아이템 지급").transform);
        if(DraftStore.Previewing) WorkshopUI.Label(canvas.transform,"미저장 초안으로 시험 중",493,6,140,15,8).color=WorkshopUI.Mint;
        giveResult=WorkshopUI.Label(canvas.transform,"",493,22,140,12,8);   // 패널과 겹치므로 패널이 열려 있는 동안 숨긴다
        StartCoroutine(Give());
    }
    // 아이템 화면의 시험 지급. 새 게임 초기화(시작 아이템 등)가 끝난 주머니에 넣는다.
    private IEnumerator Give()
    {
        if(WorkshopTrial.Loadout.Count==0) yield break;
        while(GameManager.Instance==null || !GameManager.Instance.IsPlayerReady || ItemPouch.Instance==null) yield return null;
        var lines=new List<string>();
        foreach(var (id,count) in WorkshopTrial.Loadout)
        {
            ItemSO item;
            try { item=(ItemSO)ProjectParameterBindings.Resolve(id,typeof(ItemSO)); }
            catch(InvalidDataException e){ lines.Add(e.Message); continue; }
            int given=0;
            while(given<count && ItemPouch.Instance.TryAdd(item)) given++;
            lines.Add("지급: "+item.DisplayName+" ×"+(given==count ? count.ToString() : given+"/"+count+" · 최대 보유"));
        }
        giveResult.text=string.Join(" · ",lines);
    }
    private void CloseItems(){ if(itemPanel!=null) Destroy(itemPanel.gameObject); itemPanel=null; giveResult.gameObject.SetActive(statusPanel==null); }
    private void CloseStatus(){ if(statusPanel!=null) Destroy(statusPanel.gameObject); statusPanel=null; giveResult.gameObject.SetActive(itemPanel==null); }
    // 아이템 패널(196×150): 방식 · 쪽 넘김 y4 / 3열×5행 아이템 y26(아이콘+이름, 이름 "지급/<문서 id>") / 결과 줄 y127 / 능력치 줄 y138.
    // 상태이상 패널과 하나만 열린다.
    private void ToggleItems(Canvas canvas)
    {
        if(itemPanel!=null){ CloseItems(); return; }
        CloseStatus();
        itemPanel=WorkshopUI.Frame(canvas.transform,"Trial items",437,25,196,150).rectTransform; Block(itemPanel);
        giveResult.gameObject.SetActive(false);
        DrawItems("");
    }
    private void DrawItems(string result)
    {
        foreach(Transform child in itemPanel){ child.gameObject.SetActive(false); Destroy(child.gameObject); }
        // 바인딩 뒤의 세이브 해석 목록이다. DATA 변형이 들어 있고 삭제 표시 항목은 빠져 있다.
        var registry=GameManager.Instance!=null && GameManager.Instance.dataManager!=null ? GameManager.Instance.dataManager.GET_GROWTH_REGISTRY() : null;
        var items=registry!=null ? registry.items.Where(i=>i!=null).ToArray() : new ItemSO[0];
        int pages=Mathf.Max(1,(items.Length+ItemsPerPage-1)/ItemsPerPage);
        itemPage=Mathf.Clamp(itemPage,0,pages-1);
        WorkshopUI.Choice(itemPanel,"방식",4,4,120,18,new[]{"넣기","빼기"},()=>itemMode,i=>itemMode=i);
        WorkshopUI.Button(itemPanel,"◀",150,4,20,18,()=>{ itemPage--; DrawItems(""); },false,"지급 이전 쪽").interactable=itemPage>0;
        WorkshopUI.Button(itemPanel,"▶",172,4,20,18,()=>{ itemPage++; DrawItems(""); },false,"지급 다음 쪽").interactable=itemPage<pages-1;
        for(int n=0;n<ItemsPerPage && itemPage*ItemsPerPage+n<items.Length;n++)
        {
            var item=items[itemPage*ItemsPerPage+n];
            var button=WorkshopUI.Button(itemPanel,item.DisplayName,4+n%3*63,26+n/3*20,60,18,()=>Change(item),false,"지급/"+ProjectParameterBindings.Reference(item));
            var caption=button.GetComponentInChildren<TMP_Text>();
            PixelText.Configure(caption,8); caption.alignment=TextAlignmentOptions.MidlineLeft;
            caption.rectTransform.anchoredPosition=new Vector2(20,0); caption.rectTransform.sizeDelta=new Vector2(38,18);
            WorkshopUI.Picture(button.transform,"Icon",2,1,16,16,item.icon);
        }
        WorkshopUI.Label(itemPanel,result,4,127,188,10,8);
        WorkshopUI.Label(itemPanel,Stats(),4,138,188,10,8);
    }
    // 넣기는 최대 보유까지, 빼기는 그 아이템의 첫 칸을 뺀다.
    private void Change(ItemSO item)
    {
        var pouch=ItemPouch.Instance;
        string result;
        if(pouch==null) result="주머니가 아직 준비되지 않았습니다";
        else if(itemMode==0) result=pouch.TryAdd(item) ? "넣음: "+item.DisplayName+" ×"+pouch.CountOf(item) : "최대 보유입니다";
        else
        {
            int slot=-1;
            for(int i=0;i<pouch.SlotCount && slot<0;i++) if(pouch.Get(i)==item) slot=i;
            result=slot>=0 && pouch.RemoveAt(slot)!=null ? "뺌: "+item.DisplayName+" ×"+pouch.CountOf(item) : "가지고 있지 않습니다";
        }
        DrawItems(result);
    }
    // 주머니 합계와 아이템 효과가 반영된 플레이어 능력치.
    private static string Stats()
    {
        var pouch=ItemPouch.Instance;
        int held=0;
        if(pouch!=null) for(int i=0;i<pouch.SlotCount;i++) if(pouch.Get(i)!=null) held++;
        var stat=GameManager.Instance!=null && GameManager.Instance.PLAYERCONTROLLER!=null ? GameManager.Instance.PLAYERCONTROLLER.Stat : null;
        if(stat==null) return "보유 "+held;
        return "보유 "+held+" · 체력 "+Mathf.RoundToInt(stat.MAXHP)+" · 치명 "+Mathf.RoundToInt(stat.CRIT_CHANCE)+"% · 대시 "+Mathf.RoundToInt(stat.DASH_CDR*100)+"%";
    }
    private void ToggleStatus(Canvas canvas)
    {
        if(statusPanel!=null){ CloseStatus(); return; }
        CloseItems(); giveResult.gameObject.SetActive(false);
        statusPanel=WorkshopUI.Frame(canvas.transform,"Trial status",369,25,196,82).rectTransform; Block(statusPanel);
        WorkshopUI.Choice(statusPanel,"대상",4,4,188,18,new[]{"가까운 적","모든 적","플레이어"},()=>target,i=>target=i);   // 버튼 이름 "대상/<라벨>"
        var kinds=new[]{(StatusType.Stun,"기절"),(StatusType.Freeze,"빙결"),(StatusType.Bleed,"출혈"),(StatusType.Poison,"중독"),(StatusType.BloodPop,"비폭"),(StatusType.Vulnerable,"취약")};
        for(int i=0;i<kinds.Length;i++)
        {
            var (type,label)=kinds[i];
            WorkshopUI.Button(statusPanel,label,4+i%3*64,26+i/3*20,60,18,()=>{
                var (hit,total)=ApplyStatus(type,target); statusResult.text=label+" → "+hit+"/"+total+" 걸림"; },false,"부여/"+label);
        }
        // ApplyStatus는 내성(기절 뒤 3초)·슈퍼아머·사망이면 조용히 돌아간다. 연달아 눌러 안 걸리는 것이 고장처럼 보이지 않게 숫자로 보인다.
        statusResult=WorkshopUI.Label(statusPanel,"",4,68,188,11,8);
    }
    // StatusDebugKeys(UNITY_EDITOR·DEVELOPMENT_BUILD 전용)와 달리 제작판 릴리스에서도 컴파일된다.
    // 출혈은 실제 공격처럼 플레이어 공격력을 기억시킨다(CharacterHealth의 power 계산과 같다). 반환: 부여 뒤 실제로 그 상태인 대상 수 / 대상 수.
    internal static (int hit,int total) ApplyStatus(StatusType type,int target)
    {
        var player=GameManager.Instance!=null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if(player==null) return (0,0);
        float power=player.Stat!=null ? player.Stat.ATK*(1f+player.Stat.PHYS_AMP) : 0f;
        var targets=target==2 ? new[]{player.GetComponentInChildren<CharacterStatus>()} :          // 플레이어 상태는 자식 CharacterStatStuff에 있다
            target==1 ? CharacterStatus.ActiveEnemies.ToArray() :
            CharacterStatus.ActiveEnemies.Where(s=>s!=null).OrderBy(s=>(s.transform.position-player.transform.position).sqrMagnitude).Take(1).ToArray();
        int hit=0;
        foreach(var status in targets)   // 순회 중 비폭 폭발이 목록을 바꿀 수 있어 복사본을 돈다
        {
            if(status==null) continue;
            status.ApplyStatus(type,power:power);
            if(status!=null && status.HasStatus(type)) hit++;
        }
        return (hit,targets.Length);
    }
    private void Update()
    {
        var mouse=Mouse.current;
        if(mouse!=null){ if(mouse.leftButton.wasPressedThisFrame) dragging=Inside(); if(!mouse.leftButton.isPressed) dragging=false; }
        if (Keyboard.current?.f6Key.wasPressedThisFrame==true) WorkshopTrial.Open();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBlockers(){ blockers.Clear(); dragging=false; }
}
