#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 제작실 아이템 화면 검사. WorkshopPlayModeCheck(Editor 어셈블리)가 상태이상 검사 다음에 부른다.
// DraftStore·ProjectParameters·WorkshopUI 같은 internal 멤버를 쓰므로 런타임 어셈블리에 둔다(WorkshopStatusCheck와 같다).
// 1~8은 목록·상세, 9~14는 생성·이름·삭제·영구 삭제·복원·표 보기·크게 보기, 15는 시험 지급과 복귀, 16은 헤더 탭의 도메인 이동이다. 검사가 만든 새 아이템은 지우고 원본 삭제는 복원한다.
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
        // 9. 새 아이템 → 빈 아이템 → 만들기: 등장 꺼짐 초안(효과 없음 ⚠1), 세이브 해석 목록에만 가입
        var shop=ProjectParameterBindings.Registries().OfType<ShopRegistrySO>().First();
        Click("새 아이템"); yield return Frames();
        GameObject.Find("새 아이템 이름").GetComponent<TMP_InputField>().text="QA 빈 주머니";
        Click("시작/빈 아이템"); yield return Frames();
        Require(GameObject.Find("만들 때 등장").GetComponentInChildren<TMP_Text>().text=="꺼짐","new items start hidden from rewards and shops");
        WorkshopStatusCheck.VerifyText("item create");
        Click("만들기"); yield return Frames();
        var blank=ProjectContentVariants.Entries.Single(e=>e.title=="QA 빈 주머니");
        var made=(ItemSO)blank.source;
        var madeRow=Rows().SingleOrDefault(r=>r.name=="아이템/"+blank.id);
        Require(madeRow!=null && madeRow.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="⚠1"),"new blank row shows one warning (no effects)");
        Require(made.effects.Count==0 && Cards().Length==0 && !made.availableAsReward,"blank item has no effect cards and appearance off");
        Require(!shop.itemPool.Contains(made) && growth.items.Contains(made),"blank item joins only the save-resolving list");
        capture?.Invoke("9");
        // 10. 이름 변경 → 저장: definition.json title, 표시 이름, 최근 칩
        Child<TMP_InputField>("title","이름").text="QA 빈 주머니 2"; yield return Frames();
        Click("프로젝트에 저장"); yield return Frames();
        string folder=Path.Combine(ProjectContentStore.Root,blank.Document);
        Require((string)JObject.Parse(File.ReadAllText(Path.Combine(folder,"definition.json")))["title"]=="QA 빈 주머니 2" && made.DisplayName=="QA 빈 주머니 2","renaming saves the title");
        Require(PlayerPrefs.GetString("Workshop.Recent","").Contains("\t"+blank.Document+"\tQA 빈 주머니 2"),"recent chip uses the saved name");
        capture?.Invoke("10");
        Debug.Log("[Workshop items] PASS blank item creation and rename");
        // 11. 삭제 → 저장: 목록에서 빠지고 세이브 키는 풀린다. 파일까지 지우기는 Git에 올린 적 없을 때만(격리 복사본은 Git이 없어 막힘)
        Click("아이템 삭제"); yield return Frames();
        Click("프로젝트에 저장"); yield return Frames();
        Require(!Rows().Any(r=>r.name=="아이템/"+blank.id),"a deleted item leaves the list");
        Require(!growth.items.Contains(made) && growth.ResolveSavedItem("content_"+blank.id)==made,"a deleted new item keeps its save key");
        Click("아이템 필터/삭제됨"); yield return Frames();
        Require(Rows().Any(r=>r.name=="아이템/"+blank.id),"the deleted filter lists it");
        string blocker=ProjectContentVariants.PurgeBlocker(blank.id);
        if(blocker==null)
        {   // 편집 중인 Git 체크아웃: 두 번 눌러 지운다
            Click("아이템 영구 삭제"); yield return Frames();
            Click("아이템 영구 삭제"); yield return Frames();
        }
        else
        {   // 막힘과 이유를 보고 검사가 폴더를 지운다. 화면이 하는 정리(R21)를 같은 순서로 한다
            var purge=WorkshopStatusCheck.Find("아이템 영구 삭제");
            Require(purge!=null && purge.gameObject.activeInHierarchy && !purge.interactable && Texts().Any(t=>t.text==blocker),"purge is blocked with its reason: "+blocker);
            capture?.Invoke("11");
            Directory.Delete(folder,true);
            ProjectParameterBindings.ReloadCheckout(); DraftStore.Forget(blank.Document);
            Click("게임 콘텐츠"); yield return Frames();
            Click("아이템"); yield return Frames();
        }
        Require(!Directory.Exists(folder) && !ProjectContentVariants.Entries.Any(e=>e.id==blank.id),"the purged folder is gone");
        Click("아이템 필터/전체"); yield return Frames();
        Debug.Log("[Workshop items] PASS delete, deleted filter and purge ("+(blocker ?? "purged")+")");
        // 12. 원본 삭제 → 저장 → 목록에서 빠지고 세이브 이름은 풀린다 → 복원 → 저장 → 원래 순서
        var shield=Load("PortableShield");
        var itemsBefore=growth.items.ToArray(); var poolBefore=shop.itemPool.ToArray();
        yield return Select(Id(shield));
        string shieldFolder=Path.Combine(ProjectContentStore.Root,Draft(shield).entry.Document);
        bool existed=Directory.Exists(shieldFolder);
        Click("아이템 삭제"); yield return Frames();
        Click("프로젝트에 저장"); yield return Frames();
        Require(!growth.items.Contains(shield) && !shop.itemPool.Contains(shield) && growth.ResolveSavedItem("PortableShield")==shield,"a deleted original leaves the lists and keeps its save");
        capture?.Invoke("12");
        Click("아이템 복원"); yield return Frames();
        Click("프로젝트에 저장"); yield return Frames();
        Require(growth.items.SequenceEqual(itemsBefore) && shop.itemPool.SequenceEqual(poolBefore),"restoring returns it to the original order");
        if(!existed && Directory.Exists(shieldFolder) && !Directory.EnumerateFiles(shieldFolder,"*",SearchOption.AllDirectories).Any()) Directory.Delete(shieldFolder,true);
        Debug.Log("[Workshop items] PASS original delete and restore");
        // 13. 표 보기: 9행, 등급 정렬(같으면 이름 순, 다시 누르면 내림차순), 칸 편집은 그 항목의 초안
        Click("표 보기"); yield return Frames();   // 고른 항목의 쪽으로 열린다. 정렬은 첫 쪽으로 돌아간다
        Click("정렬/등급"); yield return Frames();
        Require(TableRows().Length==9,"table shows nine rows ("+TableRows().Length+")");
        var ascending=TableRows().Select(b=>Load(AssetDatabase.GUIDToAssetPath(b.name.Substring(2)),true)).ToArray();
        Require(ascending.Zip(ascending.Skip(1),(a,b)=>a.tier<b.tier || a.tier==b.tier && string.CompareOrdinal(a.DisplayName,b.DisplayName)<=0).All(x=>x),"tier sort is ascending, then by name");
        Click("정렬/등급"); yield return Frames();
        var descending=TableRows().Select(b=>Load(AssetDatabase.GUIDToAssetPath(b.name.Substring(2)),true)).ToArray();
        Require(descending.Zip(descending.Skip(1),(a,b)=>a.tier>=b.tier).All(x=>x),"the same header again sorts descending");
        WorkshopStatusCheck.VerifyText("item table");
        string first=TableRows()[0].name.Substring(2);
        var firstItem=Load(AssetDatabase.GUIDToAssetPath(first),true);
        Child<Button>("표/"+first+"/maxStack","+").onClick.Invoke(); yield return Frames();
        Require(TableRows()[0].transform.Find("●").gameObject.activeSelf && Draft(firstItem).Dirty,"a table edit marks that row unsaved");
        capture?.Invoke("13");
        Click("상세 보기"); yield return Frames();
        DraftStore.Revert(Draft(firstItem));
        Require(!DraftStore.Unsaved.Any(),"table check leaves nothing unsaved");
        Debug.Log("[Workshop items] PASS table rows, sort and row edit");
        // 14. 크게 보기: 주머니 실물·상점 카드 실물에 표시 이름, Esc로 닫힘
        var opened=(ItemSO)DraftStore.All.Single(d=>d.live).entry.source;
        Click("미리보기 크게"); yield return Frames();
        for(float until=Time.realtimeSinceStartup+1f;Time.realtimeSinceStartup<until;) yield return null;   // 주머니 실물의 열림 연출(UIOpenMotion)이 끝난 화면을 남긴다
        var big=GameObject.Find("아이템 미리보기");
        var pouch=big!=null ? big.GetComponentInChildren<PouchUI>(true) : null;
        Require(pouch!=null && pouch.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.text==opened.DisplayName),"large preview shows the pouch detail");
        capture?.Invoke("14");
        Click("크게 보기/상점"); yield return Frames();
        capture?.Invoke("14-shop");
        var card=big.GetComponentInChildren<ShopPopupSlot>(true);
        Require(card!=null && big.GetComponentInChildren<PouchUI>(true)==null && card.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.text==opened.DisplayName),"large preview shows the shop card");
        yield return Press(Key.Escape);
        Require(GameObject.Find("아이템 미리보기")==null && GameObject.Find("Item detail")!=null,"Esc closes the large preview");
        Require(!DraftStore.Unsaved.Any(),"item check leaves nothing unsaved");
        Debug.Log("[Workshop items] PASS blank item, rename, delete, purge, restore, table and large preview");
        // 15. 시험 지급: 주머니 탭의 보유 개수만큼 넣고, 도구막대 패널로 하나 더 넣고, 돌아오면 같은 항목·같은 스크롤
        yield return Select(clothId);
        Click("미리보기/주머니"); yield return Frames();
        for(int n=0;n<20 && Count()!="1";n++) GameObject.Find("보유 개수/−").GetComponent<Button>().onClick.Invoke();
        GameObject.Find("보유 개수/+").GetComponent<Button>().onClick.Invoke(); yield return Frames();
        Require(Count()=="2","pouch preview count is two ("+Count()+")");
        GameObject.Find("Item detail").GetComponent<ScrollRect>().verticalNormalizedPosition=.5f; yield return Frames();
        float scrolled=((RectTransform)Detail()).anchoredPosition.y;
        Require(scrolled>0,"item detail scrolls before the trial");
        Click("시험 F6");
        yield return WorkshopStatusCheck.Battle();
        var pouchNow=ItemPouch.Instance;
        Require(pouchNow!=null && pouchNow.CountOf(cloth)>=2,"trial gives the previewed count ("+(pouchNow!=null ? pouchNow.CountOf(cloth) : -1)+")");
        Require(Texts().Any(t=>t.text=="지급: "+cloth.DisplayName+" ×2"),"toolbar reports what the trial gave");
        int had=pouchNow.CountOf(cloth);
        Click("아이템 지급"); yield return Frames();
        Click("방식/넣기"); yield return Frames();
        for(int n=0;n<5 && WorkshopStatusCheck.Find("지급/"+clothId)?.gameObject.activeInHierarchy!=true;n++){ Click("지급 다음 쪽"); yield return Frames(); }
        Click("지급/"+clothId); yield return Frames();
        Require(pouchNow.CountOf(cloth)==had+1,"toolbar panel adds one more");
        capture?.Invoke("15");
        Click("편집으로  F6");
        yield return WorkshopStatusCheck.Workshop();
        Require(GameObject.Find("Item detail")!=null && Draft(cloth).live,"returning reopens the same item");
        Require(Mathf.Abs(((RectTransform)Detail()).anchoredPosition.y-scrolled)<1,"returning restores the detail scroll");
        Require(!DraftStore.Unsaved.Any(),"item check leaves nothing unsaved");
        Debug.Log("[Workshop items] PASS trial loadout, toolbar give and return");
        // 16. 게임 콘텐츠 헤더 탭: 전용 화면(상태이상·아이템)만 탭이고, 탭으로 도메인을 오가도 미저장 초안이 남는다
        var tabs=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>b.gameObject.activeInHierarchy && b.name.StartsWith("탭/",StringComparison.Ordinal)).Select(b=>b.name).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
        Require(tabs.SequenceEqual(new[]{"탭/상태이상","탭/아이템"}),"content header tabs are the dedicated screens: "+string.Join(",",tabs));
        int stack=cloth.MaxStack;
        GameObject.Find("maxStack/+").GetComponent<Button>().onClick.Invoke(); yield return Frames();
        Click("탭/상태이상"); yield return Frames();
        Require(WorkshopStatusCheck.Find("상태이상/poison")?.gameObject.activeInHierarchy==true && Draft(cloth).Dirty,"status tab opens the status screen and keeps the item draft");
        Click("상태이상/poison"); yield return Frames();
        GameObject.Find("poisonTickDamage/+").GetComponent<Button>().onClick.Invoke(); yield return Frames();
        var status=DraftStore.All.Single(d=>d.entry.source is StatusBalanceSO);
        Click("탭/아이템"); yield return Frames();
        Require(GameObject.Find("Item detail")!=null && Draft(cloth).live && Draft(cloth).Dirty && cloth.MaxStack==stack+1,"item tab reopens the same item with its unsaved edit");
        Require(status.Dirty,"the status draft survives the tab move");
        capture?.Invoke("16");
        Click("변경 취소"); yield return Frames();
        DraftStore.Revert(status);
        Require(!DraftStore.Unsaved.Any() && cloth.MaxStack==stack,"tab check leaves nothing unsaved");
        Debug.Log("[Workshop items] PASS content header tabs keep drafts across domains");
    }
    private static string[] Cards()=>Detail().Cast<Transform>().Where(t=>t.name.StartsWith("효과/",StringComparison.Ordinal) && t.GetComponent<Image>()!=null && !t.GetComponent<Button>())
        .OrderBy(t=>-((RectTransform)t).anchoredPosition.y).Select(t=>t.name.Substring(3)).ToArray();
    // 표 보기의 이름 칸 버튼("표/<GUID>"), 위에서 아래 순.
    private static Button[] TableRows()=>Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
        .Where(b=>b.gameObject.activeInHierarchy && b.name.StartsWith("표/",StringComparison.Ordinal) && b.name.Count(c=>c=='/')==1)
        .OrderBy(b=>-((RectTransform)b.transform).anchoredPosition.y).ToArray();

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
