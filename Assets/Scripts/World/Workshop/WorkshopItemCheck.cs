#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 제작실 아이템 화면 검사. WorkshopPlayModeCheck(Editor 어셈블리)가 상태이상 검사 다음에 부른다.
// DraftStore·ProjectParameters·WorkshopUI 같은 internal 멤버를 쓰므로 런타임 어셈블리에 둔다(WorkshopStatusCheck와 같다).
// 모든 변경을 되돌리고 아이템 상세를 연 채로 끝난다(이어지는 UI 해상도 검사가 "Item detail"을 본다).
public static class WorkshopItemCheck
{
    private const string Items="Assets/SOData/Items/";

    public static IEnumerator Run(Action<string> capture=null)
    {
        var cloth=Load("ClothArmor"); var whetstone=Load("Whetstone"); var charm=Load("HealingCharm");
        string clothId=Id(cloth);
        // 1. 처음 화면 → 게임 콘텐츠 → 아이템: 아이콘과 등급 색이 있는 행, 글리프·넘침
        Click("처음 화면"); yield return Frames();
        Click("게임 콘텐츠"); yield return Frames();
        Click("아이템"); yield return Frames();
        var rows=Rows();
        Require(rows.Length==Math.Min(10,AssetDatabase.FindAssets("t:ItemSO",new[]{"Assets/SOData/Items"}).Length) && rows.Length>=10,"first page lists ten item rows ("+rows.Length+")");
        foreach(var row in rows)
        {
            var item=Load(AssetDatabase.GUIDToAssetPath(row.name.Substring("아이템/".Length)),true);
            Require(row.transform.Find("Icon")?.GetComponent<Image>()?.sprite==item.icon && item.icon!=null,"row "+item.name+" shows its icon");
            Require(Close(row.transform.Find("Stripe").GetComponent<Image>().color,item.TierColor),"row "+item.name+" stripe is the tier color");
        }
        WorkshopStatusCheck.VerifyText("items");
        capture?.Invoke("1");
        // 2. 화면 필드 = SO 필드(세이브 옛 이름 제외)
        yield return Select(clothId);
        var expected=ProjectParameters.Fields(cloth).Select(f=>f.path).Where(p=>!p.StartsWith("formerAssetNames/",StringComparison.Ordinal)).ToHashSet();
        var shown=Detail().Cast<Transform>().Select(t=>t.name).Where(n=>expected.Contains(n) || n.Contains("/@")).ToHashSet();
        Require(shown.SetEquals(expected),"item detail covers every ItemSO field; hidden: "+string.Join(",",expected.Except(shown))+" extra: "+string.Join(",",shown.Except(expected)));
        Require(!Texts().Any(t=>t.text.StartsWith("필드 없음",StringComparison.Ordinal)),"no missing field rows");
        WorkshopStatusCheck.VerifyText("item detail");
        capture?.Invoke("2");
        // 3. 등급 → 상점 미리보기 가격·목록 색, Ctrl+Z
        Click("tier/2"); yield return Frames();
        Click("미리보기/상점"); yield return Frames();
        Require(cloth.tier==ItemTier.Tier2,"tier choice edits the live item");
        Require(Texts().Any(t=>t.text.Contains("400 G") && t.transform.IsChildOf(GameObject.Find("Aside").transform)),"shop preview shows the tier price");
        Require(Close(Rows().Single(r=>r.name=="아이템/"+clothId).transform.Find("Stripe").GetComponent<Image>().color,ItemTierRules.TierColor(ItemTier.Tier2)),"list stripe follows the tier");
        capture?.Invoke("3");
        yield return Press(Key.LeftCtrl,Key.Z);
        Require(cloth.tier==ItemTier.Tier4,"Ctrl+Z restores the tier");
        // 4. 최대 보유 → 주머니 보유 개수 상한
        Click("미리보기/주머니"); yield return Frames();
        string Count()=>GameObject.Find("보유 개수/값").GetComponentInChildren<TMP_Text>().text;
        for(int i=0;i<cloth.MaxStack+2;i++) GameObject.Find("보유 개수/+").GetComponent<Button>().onClick.Invoke();
        yield return Frames();
        int max=cloth.MaxStack;
        Require(Count()==max.ToString(),"pouch count stops at the max stack ("+Count()+")");
        GameObject.Find("maxStack/+").GetComponent<Button>().onClick.Invoke();
        GameObject.Find("보유 개수/+").GetComponent<Button>().onClick.Invoke();
        yield return Frames();
        Require(cloth.MaxStack==max+1 && Count()==(max+1).ToString(),"raising the max stack raises the pouch count limit");
        capture?.Invoke("4");
        Click("변경 취소"); yield return Frames();
        Debug.Log("[Workshop items] PASS rows, field coverage, tier preview and undo, pouch count limit");
        // 5. 효과 추가 · 순서 · 삭제 · 실행 취소 · 변경 취소
        string document=Draft(cloth).entry.Document;
        int before=cloth.effects.Count;
        Click("효과 추가"); yield return Frames();
        Click("효과 종류/방 입장 시 보호막"); yield return Frames();
        var added=Draft(cloth).files.Where(p=>p.Key.StartsWith("LISTS/effects/",StringComparison.Ordinal) && p.Value.Contains("\"type\"")).ToArray();
        Require(added.Length==1 && Draft(cloth).files.Keys.Count(k=>k.StartsWith("LISTS/",StringComparison.Ordinal))==1,"adding an effect writes one LISTS draft file");
        string id=added[0].Key.Substring("LISTS/effects/".Length).Replace(".json","");
        string[] Cards()=>Detail().Cast<Transform>().Where(t=>t.name.StartsWith("효과/",StringComparison.Ordinal) && t.GetComponent<Image>()!=null && !t.GetComponent<Button>())
            .OrderBy(t=>-((RectTransform)t).anchoredPosition.y).Select(t=>t.name.Substring(3)).ToArray();
        Require(cloth.effects.Count==before+1 && Cards().Last()==id,"new effect card is last");
        Click("효과/"+id+"/위"); yield return Frames();
        Require(Cards()[Cards().Length-2]==id && cloth.effects[before-1].id==id,"moving up swaps the card order");
        capture?.Invoke("5");
        Click("효과/"+id+"/삭제"); yield return Frames();
        Require(!Cards().Contains(id),"removing deletes the card");
        yield return Press(Key.LeftCtrl,Key.Z);
        Require(Cards().Contains(id),"Ctrl+Z restores the card");
        Click("변경 취소"); yield return Frames();
        Require(!Draft(cloth).files.Keys.Any(k=>k.StartsWith("LISTS/",StringComparison.Ordinal)) && !Draft(cloth).Dirty && cloth.effects.Count==before,"discarding drops the LISTS drafts");
        Debug.Log("[Workshop items] PASS effect add, move, delete, undo and discard");
        // 6. 비율 보정 켠 능력치 보정: 고정치만 읽는 능력치는 흐리고 고를 수 없다
        yield return Select(Id(whetstone));
        var stat=whetstone.effects.OfType<ItemStatEffect>().First();
        string prefix=ItemAuthoring.EffectPath(stat);
        var statBefore=stat.statType;
        if(!stat.isPercent){ Child<Button>(prefix+"/isPercent","값").onClick.Invoke(); yield return Frames(); }
        Child<Button>(prefix+"/statType","값").onClick.Invoke(); yield return Frames();
        var crit=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="값/"+DesignerInspectorLabels.EnumLabel(typeof(StatType),"CritChance"));
        Require(crit.GetComponentInChildren<TMP_Text>().alpha<.5f,"crit chance is dimmed for a percent bonus");
        crit.onClick.Invoke(); yield return Frames();
        string reason=ItemAuthoring.StatReason(StatType.CritChance,true);
        Require(Texts().Any(t=>t.text==reason),"picker explains why");
        Require(whetstone.effects.OfType<ItemStatEffect>().First().statType==statBefore,"a blocked stat is not written");
        capture?.Invoke("6");
        WorkshopUI.CloseOverlay();
        Click("변경 취소"); yield return Frames();
        // 7. 글꼴에 없는 글자 경고
        string missing="QA"+(char)0xBDC1;
        Child<TMP_InputField>("itemName","값").text=missing; yield return Frames();
        var warning=Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).SingleOrDefault(t=>t.name=="글꼴 경고" && t.transform.parent.name=="itemName");
        Require(warning!=null && warning.gameObject.activeInHierarchy && warning.text.Contains((char)0xBDC1),"name input warns about a glyph missing from the atlas");
        Require(whetstone.DisplayName==missing,"name input writes the draft without leaving the field");
        capture?.Invoke("7");
        Click("변경 취소"); yield return Frames();
        Debug.Log("[Workshop items] PASS blocked stat choice and missing-glyph warning");
        // 8. 시작 아이템은 지울 수 없고, 참조 버튼이 그 필드로 간다
        yield return Select(Id(charm));
        Click("아이템 삭제"); yield return Frames();
        var reference=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).SingleOrDefault(b=>b.name.StartsWith("참조/",StringComparison.Ordinal) && b.name.EndsWith("/startingItem",StringComparison.Ordinal));
        Require(reference!=null && !ProjectStructure.IsRemoved(Draft(charm).files),"deleting the starting item is blocked and lists the reference");
        capture?.Invoke("8");
        reference.onClick.Invoke(); yield return Frames();
        var growth=ProjectParameterBindings.Registries().OfType<GrowthRegistrySO>().First();
        Require(Texts().Any(t=>t.text.StartsWith(growth.name,StringComparison.Ordinal)) && GameObject.Find("Item detail")==null,"reference opens the registry in all values");
        Click("게임 콘텐츠"); yield return Frames();
        Click("아이템"); yield return Frames();
        Require(GameObject.Find("Item detail")!=null && Draft(charm).live,"returning reopens the item screen");
        Require(!DraftStore.Unsaved.Any(),"item check leaves nothing unsaved");
        Debug.Log("[Workshop items] PASS starting-item delete block and reference jump");
    }

    private static ItemSO Load(string name,bool path=false)
    {
        var item=AssetDatabase.LoadAssetAtPath<ItemSO>(path ? name : Items+name+".asset");
        Require(item!=null,"item asset "+name);
        return item;
    }
    private static Transform Detail()=>GameObject.Find("Item detail").transform.Find("Content");
    private static string Id(ItemSO item)=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item));
    private static DraftStore.Draft Draft(ItemSO item)=>DraftStore.All.Single(d=>d.entry.source==item);
    private static Button[] Rows()=>Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
        .Where(b=>b.gameObject.activeInHierarchy && b.transform.parent.name=="아이템 목록").ToArray();
    private static TMP_Text[] Texts()=>Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(t=>t.gameObject.activeInHierarchy).ToArray();
    private static bool Close(Color a,Color b)=>Mathf.Abs(a.r-b.r)<.01f && Mathf.Abs(a.g-b.g)<.01f && Mathf.Abs(a.b-b.b)<.01f;
    // 그 행이 보일 때까지 쪽을 넘긴 뒤 누른다.
    private static IEnumerator Select(string id)
    {
        for(int n=0;n<10 && !Rows().Any(r=>r.name=="아이템/"+id);n++){ Click("아이템 다음 쪽"); yield return Frames(); }
        Click("아이템/"+id); yield return Frames();
    }
    // 이름에 '/'가 든 행(효과 경로)의 자식은 GameObject.Find로 찾지 못하고, "값"·"+"는 행마다 겹친다(R4).
    private static T Child<T>(string parent,string child) where T:Component
    {
        var found=Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Where(t=>t.gameObject.activeInHierarchy && t.name==parent)
            .SelectMany(t=>t.Cast<Transform>()).Where(c=>c.name==child && c.gameObject.activeInHierarchy).Select(c=>c.GetComponent<T>()).Where(c=>c!=null).ToArray();
        Require(found.Length==1,found.Length+" '"+child+"' under '"+parent+"'");
        return found[0];
    }
    private static IEnumerator Press(params Key[] keys)
    {
        var previous=InputSystem.settings;
        var settings=Object.Instantiate(previous);
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings=settings;
        var keyboard=InputSystem.AddDevice<Keyboard>();
        try
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys)); yield return Frames();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return Frames();
        }
        finally { InputSystem.RemoveDevice(keyboard); InputSystem.settings=previous; Object.Destroy(settings); }
    }
    private static void Click(string name)=>WorkshopStatusCheck.Click(name);
    private static IEnumerator Frames(int count=2)=>WorkshopStatusCheck.Frames(count);
    private static void Require(bool condition,string message)
    {
        if(!condition) throw new InvalidOperationException("[Workshop items] "+message);
    }
}
#endif
