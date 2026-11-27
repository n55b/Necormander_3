using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Entry = ProjectParameterBindings.Entry;

// 아이템 화면. 옛 Unity 아이템 편집기(Tools > 기획 > 아이템 편집기)의 목록·상세·효과 칸·미리보기·확인 필요를 목록-상세 골격으로 옮겼다.
// 고른 항목의 초안은 살아 있는 ItemSO에 바로 적용된다(DraftStore.Live). 그래서 미리보기·경고·효과 카드가 모두 초안 기준이다.
// 효과 추가·순서·삭제는 LISTS, 상점 진열은 MEMBERS, 삭제는 removed.json이다(ProjectStructure). 규칙은 ItemAuthoring에 있다.
public sealed partial class RoomWorkshopWindow
{
    private DraftStore.Draft itemDraft;
    private string itemSearch="", itemFilter="전체";
    private int itemPage, itemPreview, itemCount=1;           // 미리보기 탭(0 툴팁, 1 상점, 2 주머니), 주머니 보유 개수
    private (string path,string text)? itemFocus;              // 경고가 고른 필드: 다음 Draw에서 그 줄로 스크롤한다
    private RectTransform itemList;
    private Entry[] itemRows;                                  // 검색·필터를 바꿀 때만 다시 계산한다(값을 고쳐도 행이 사라지지 않게)
    private (string owner,string label,string document,string path)[] itemBlockers;   // 삭제를 막은 참조
    private static readonly string[] ItemFilters={"전체","1","2","3","4","⚠","삭제됨"};
    private const int ItemsPerPage=ListH/RowH;

    private IEnumerable<Entry> Items()=>EnsureCatalog().Where(e=>e.source is ItemSO);
    private static GrowthRegistrySO ItemGrowth=>ProjectParameterBindings.Registries().OfType<GrowthRegistrySO>().FirstOrDefault();
    private static ShopRegistrySO ItemShop=>ProjectParameterBindings.Registries().OfType<ShopRegistrySO>().FirstOrDefault();
    private static bool IsItemVariant(Entry e)=>e.group==ProjectContentVariants.Folder;
    // 삭제 표시: 연 초안이 있으면 초안, 없으면 디스크.
    private static bool Removed(Entry e)=>DraftStore.Find(e.Document) is DraftStore.Draft d ? ProjectStructure.IsRemoved(d.files) :
        File.Exists(Path.Combine(ProjectContentStore.Root,e.Document,ProjectStructure.RemovedFile));
    // ItemAuthoring의 경고 + 화면 글꼴(정적 아틀라스)에 없는 글자.
    private static List<ItemAuthoring.Warning> ItemWarnings(Entry e)
    {
        var item=(ItemSO)e.source;
        var list=ItemAuthoring.Warnings(item,ItemGrowth,ItemShop);
        void Glyphs(string field,string text)
        {
            string missing=PixelText.MissingGlyphs(text);
            if(missing.Length>0) list.Add(new ItemAuthoring.Warning(field,"글꼴에 없는 글자 "+missing.Substring(0,Math.Min(3,missing.Length)),
                "화면 글꼴에 없는 글자: "+missing+" · 아틀라스를 다시 만들어야 게임에 보입니다(Tools > UI > Rebuild Pixel Font Atlases, Windows 제작판은 다음 빌드)."));
        }
        Glyphs(IsItemVariant(e) ? "title" : "itemName",item.DisplayName);
        Glyphs("description",item.description); Glyphs("shopDescription",item.shopDescription);
        foreach(var effect in item.effects ?? new List<ItemEffect>()) if(effect!=null) Glyphs(ItemAuthoring.EffectPath(effect)+"/displayText",effect.displayText);
        return list;
    }
    private Entry[] ItemRows()
    {
        string q=itemSearch.Trim();
        bool Match(ItemSO i)=>q.Length==0 || new[]{i.DisplayName,i.name,i.description,i.shopDescription,i.EffectSummary(1)}
            .Any(t=>t!=null && t.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0);
        bool Pass(Entry e)
        {
            var i=(ItemSO)e.source; bool removed=Removed(e);
            return itemFilter switch
            {
                "삭제됨"=>removed, "전체"=>!removed, "⚠"=>!removed && ItemWarnings(e).Count>0,
                _=>!removed && ((int)i.tier).ToString()==itemFilter,
            };
        }
        return Items().Where(e=>Match((ItemSO)e.source) && Pass(e)).OrderBy(e=>(int)((ItemSO)e.source).tier)
            .ThenBy(e=>((ItemSO)e.source).DisplayName,StringComparer.Ordinal).ToArray();
    }

    // document가 없으면 직전 항목, 그것도 없으면 첫 행. listPage는 시험 복귀가 넘긴다.
    private void OpenItems(string document,int? listPage=null)
    {
        dataCatalog=null; EnsureCatalog(); itemRows=null;
        document ??= itemDraft?.entry.Document;
        var entry=Items().FirstOrDefault(e=>e.Document==document);
        if(entry!=null && Removed(entry)!=(itemFilter=="삭제됨")) itemFilter=Removed(entry) ? "삭제됨" : "전체";
        itemRows=ItemRows();
        entry ??= itemRows.FirstOrDefault() ?? Items().FirstOrDefault() ?? throw new InvalidOperationException("아이템 원본 에셋을 찾을 수 없습니다.");
        SelectItem(entry);
        int index=Array.FindIndex(itemRows,e=>e.Document==entry.Document);
        itemPage=listPage ?? Math.Max(0,index)/ItemsPerPage;
        Navigate(Page.Items);   // Navigate가 상태 줄을 비우므로 안내는 그 뒤에 띄운다
        if(itemDraft.Blocked is string reason) Say(reason+" · 최신 제작판이면 [판 차이 정리]로 지운 뒤 저장하세요.");
    }
    private void SelectItem(Entry e)
    {
        itemDraft=DraftStore.Open(e); DraftStore.Live(itemDraft);
        PlayerPrefs.SetString("Workshop.Opened."+e.Document,DateTime.UtcNow.Ticks.ToString("D19"));
        RememberRecent(Area.Content,e.Document,ContentTitle(e));
        itemFocus=null; itemBlockers=null;
    }
    private void SaveItem()
    {
        if(itemDraft==null) return;
        CommitPendingInputs();
        string when=DraftStore.Save(itemDraft);
        itemRows=null; Draw(page);
        Say("DATA 저장됨 · "+((ItemSO)itemDraft.entry.source).DisplayName+(when!=null ? " · "+when : ""));
    }
    private void ResetItems(){ itemDraft=null; itemRows=null; itemFocus=null; itemBlockers=null; }
    // 상태이상과 같은 조건의 ↑↓(행 이동, 쪽 경계면 쪽 넘김)와 PgUp/PgDn(쪽).
    private void ItemKeys()
    {
        var keys=Keyboard.current; if(keys==null || itemRows==null || itemRows.Length==0 || InputFocused()) return;
        int move=keys.upArrowKey.wasPressedThisFrame ? -1 : keys.downArrowKey.wasPressedThisFrame ? 1 : 0;
        int flip=keys.pageUpKey.wasPressedThisFrame ? -1 : keys.pageDownKey.wasPressedThisFrame ? 1 : 0;
        if(move==0 && flip==0) return;
        var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        if(selected!=null && (itemList==null || !selected.transform.IsChildOf(itemList))) return;
        if(flip!=0){ itemPage=Mathf.Clamp(itemPage+flip,0,(itemRows.Length-1)/ItemsPerPage); Draw(page); return; }
        int i=Array.FindIndex(itemRows,e=>e.Document==itemDraft?.entry.Document);
        int next=Mathf.Clamp(i<0 ? itemPage*ItemsPerPage : i+move,0,itemRows.Length-1);
        SelectItem(itemRows[next]); itemPage=next/ItemsPerPage; Draw(page);
    }

    private void DrawItems()
    {
        WorkshopUI.Label(content,"아이템",12,33,60,20,15);
        if(!ProjectContentStore.Authoring || itemDraft==null || itemDraft.entry.source==null)
        {   // 허브 타일이 막혀 있어 보통은 들어올 수 없다. 작업 공간을 바꾼 직후에만 여기에 온다.
            WorkshopUI.Label(content,"프로젝트 연결 필요 · 저장은 체크아웃에 연결한 기본 작업 공간에서만 됩니다.",12,62,612,22,12);
            return;
        }
        var d=itemDraft; var item=(ItemSO)d.entry.source;
        var search=Input("아이템 검색",itemSearch,76,33,88,18);
        search.onEndEdit.AddListener(v=>{ if(v==itemSearch) return; itemSearch=v; itemPage=0; itemRows=null; Draw(page); });
        WorkshopUI.Choice(content,"아이템 필터",168,33,300,18,ItemFilters,()=>Array.IndexOf(ItemFilters,itemFilter),
            i=>Safe(()=>{ itemFilter=ItemFilters[i]; itemPage=0; itemRows=null; Draw(page); }));
        string pin="Workshop.Pinned."+d.entry.Document; bool pinned=PlayerPrefs.GetInt(pin)!=0;
        Btn(pinned ? "고정됨" : "고정",566,33,66,18,()=>{PlayerPrefs.SetInt(pin,pinned ? 0 : 1);Draw(page);},pinned,"아이템/고정");

        itemRows ??= ItemRows();
        int pages=Math.Max(1,(itemRows.Length+ItemsPerPage-1)/ItemsPerPage);
        itemPage=Mathf.Clamp(itemPage,0,pages-1);
        itemList=WorkshopUI.Rect(content,"아이템 목록",ListX,BodyY,ListW,ListH);
        for(int n=itemPage*ItemsPerPage;n<Math.Min(itemRows.Length,(itemPage+1)*ItemsPerPage);n++)
        {
            var e=itemRows[n]; var i=(ItemSO)e.source;
            ListRow(itemList,n%ItemsPerPage,"아이템/"+e.id,i.DisplayName,()=>ItemSummary(e),i.TierColor,()=>ItemBadge(e),e.Document==d.entry.Document,()=>{
                SelectItem(e); Draw(page);
                if(itemDraft.Blocked is string reason) Say(reason+" · 최신 제작판이면 [판 차이 정리]로 지운 뒤 저장하세요.");
            },i.icon);
        }
        if(itemRows.Length==0) WorkshopUI.Label(content,"맞는 아이템이 없습니다",ListX+4,BodyY+4,ListW-8,16,8);
        Btn("◀",112,280,16,16,()=>{itemPage--;Draw(page);},false,"아이템 이전 쪽").interactable=itemPage>0;
        WorkshopUI.Label(content,(itemPage+1)+"/"+pages,128,280,20,16,8).alignment=TextAlignmentOptions.Center;
        Btn("▶",148,280,16,16,()=>{itemPage++;Draw(page);},false,"아이템 다음 쪽").interactable=itemPage<pages-1;

        var detail=Detail("item/"+d.entry.Document,"Item detail");
        EndDetail(detail,DrawItemDetail(detail,d,item));
        DrawItemAside(d);
        if(itemBlockers!=null) DrawItemBlockers();
        else { HelpBox(); ShowHelp("필드에 마우스를 올리면 설명이 보입니다.",""); }
        if(itemFocus.HasValue)
        {
            var (path,text)=itemFocus.Value;
            if(ChildNamed(detail,path) is RectTransform target)
            {   // 그 줄이 상세 맨 위에 오게 스크롤한다(정수 y)
                detailScrollY=Mathf.Clamp(Mathf.Round(-target.anchoredPosition.y),0,Mathf.Max(0,detail.rect.height-DetailH));
                detail.anchoredPosition=new Vector2(0,detailScrollY);
            }
            if(itemBlockers==null) ShowHelp(text ?? (d.byPath.TryGetValue(path,out var f) ? HelpText(f) : ""),path);
        }
        itemFocus=null;

        bool removed=ProjectStructure.IsRemoved(d.files);
        Btn("변경 취소",8,ActionsY,72,22,()=>{DraftStore.Revert(d);itemBlockers=null;Draw(page);});
        if(removed) Btn("복원",84,ActionsY,60,22,()=>{DraftStore.Edit(d,(f,_)=>f.Remove(ProjectStructure.RemovedFile));Draw(page);},false,"아이템 복원");
        else Btn("삭제",84,ActionsY,60,22,()=>DeleteItem(d),false,"아이템 삭제");
        Btn("모든 필드",212,ActionsY,64,22,()=>{OpenParameters();SelectContent(d.entry);});
        if(d.Blocked!=null) Btn("판 차이 정리",348,ActionsY,68,22,()=>{DraftStore.DropUnknown(d);Draw(page);},false,"이 판에 없는 항목 지우기");
        Btn("시험 F6",420,ActionsY,80,22,StartContentTrial);
        Btn("프로젝트에 저장",504,ActionsY,128,22,SaveItem,true).interactable=d.Blocked==null;
    }
    // 목록 요약 칸(22px)은 한 가지만: ⚠n > 시작 > 꺼짐(보상·상점 모두 없음) > 새(새 콘텐츠).
    private static string ItemSummary(Entry e)
    {
        var i=(ItemSO)e.source; var shop=ItemShop;
        int warnings=ItemWarnings(e).Count;
        if(warnings>0) return "⚠"+warnings;
        if(ItemGrowth?.startingItem==i) return "시작";
        if(!i.availableAsReward && (shop==null || !shop.itemPool.Contains(i))) return "꺼짐";
        return IsItemVariant(e) ? "새" : "";
    }
    private static (bool,bool) ItemBadge(Entry e)
    {
        var d=DraftStore.Find(e.Document);
        return (d?.Dirty==true,d!=null && (d.values.Count>0 || d.StructureChanged));
    }
    private static RectTransform ChildNamed(Transform parent,string name)
    {
        foreach(Transform t in parent) if(t.name==name) return (RectTransform)t;
        return null;
    }
    // 참조가 있으면 막고 설명 칸 자리에 그 목록을 보인다. 없으면 삭제 표시(저장하면 목록·보상·상점에서 빠지고 세이브 해석은 남는다).
    private void DeleteItem(DraftStore.Draft d)
    {
        itemBlockers=ProjectStructure.References(d.entry.source,EnsureCatalog()).ToArray();
        if(itemBlockers.Length>0){ Draw(page); Say("삭제할 수 없습니다 · 이 아이템을 쓰는 곳을 먼저 바꾸세요."); return; }
        itemBlockers=null;
        DraftStore.Edit(d,(f,_)=>f[ProjectStructure.RemovedFile]=ProjectContentStore.Json(new JObject{["removed"]=true}));
        Draw(page);
        Say("삭제 표시 · 저장하면 목록·보상·상점에서 빠집니다. 기존 세이브는 유지됩니다(Ctrl+Z로 되돌림).");
    }
    private void DrawItemBlockers()
    {
        var box=WorkshopUI.Frame(content,"Help",AsideX,HelpY,AsideW,88).rectTransform;
        var head=WorkshopUI.Label(box,"삭제할 수 없습니다 · 이 아이템을 쓰는 곳",4,3,AsideW-8,22,8);
        head.textWrappingMode=TextWrappingModes.Normal; head.alignment=TextAlignmentOptions.TopLeft;
        for(int k=0;k<Math.Min(3,itemBlockers.Length);k++)
        {
            var (owner,label,document,path)=itemBlockers[k];
            var button=WorkshopUI.Button(box,owner+" · "+label,4,28+k*16,AsideW-8,14,()=>Safe(()=>{
                OpenContent(document); dataSearch=path; dataPage=0; Draw(page);
            }),false,"참조/"+document+"/"+path);
            var caption=button.GetComponentInChildren<TMP_Text>(); PixelText.Configure(caption,8); caption.overflowMode=TextOverflowModes.Truncate;
        }
        if(itemBlockers.Length>3) WorkshopUI.Label(box,"외 "+(itemBlockers.Length-3)+"곳",4,76,AsideW-8,12,8);
    }

    // 상세 열: 기본 · 등장 · 효과 · 코드 경로. 다음 y(= 내용 높이)를 돌려준다.
    private int DrawItemDetail(RectTransform detail,DraftStore.Draft d,ItemSO item)
    {
        var e=d.entry; var growth=ItemGrowth; var shop=ItemShop;
        bool variant=IsItemVariant(e), removed=ProjectStructure.IsRemoved(d.files);
        int y=2;
        Section(detail,ref y,"기본");
        y=variant ? ItemTitleRow(detail,y,d) : FieldRow(detail,y,d,"itemName");
        y=FieldRow(detail,y,d,"icon");
        y=FieldRow(detail,y,d,"tier",choices:new[]{"1","2","3","4"},redraw:true);
        if(ChildNamed(detail,"tier") is RectTransform tierRow)
        {
            var price=WorkshopUI.Label(tierRow,item.Price+" G",212,0,26,22,8);
            price.color=item.TierColor; price.overflowMode=TextOverflowModes.Overflow;
        }
        y=FieldRow(detail,y,d,"maxStack");
        y=FieldRow(detail,y,d,"description",lines:3);
        y=FieldRow(detail,y,d,"shopDescription",lines:2);

        Section(detail,ref y,"등장");
        if(removed) y=Note(detail,y,"삭제됨 · 목록·보상·상점에서 빠졌습니다. 기존 세이브는 이 아이템을 계속 불러옵니다.");
        else
        {
            bool InShop()=>shop!=null && shop.itemPool.Contains(item);
            string result=!item.availableAsReward ? "보상·상점에 나오지 않습니다(등장 꺼짐)" : InShop() ? "방 보상과 상점에 나옵니다" : "방 보상에 나옵니다";
            y=FieldRow(detail,y,d,"availableAsReward",note:result,redraw:true);
            if(shop!=null)
            {
                var row=WorkshopUI.Box(detail,"상점 진열",0,y,DetailW-6,24,Color.clear);
                var label=WorkshopUI.Label(row.transform,"상점 진열",0,0,118,22,10);
                if(d.files.Keys.Any(k=>k.StartsWith("MEMBERS/",StringComparison.Ordinal))) label.color=WorkshopUI.Mint;
                WorkshopUI.Toggle(row.transform,"값",120,1,90,InShop,on=>Safe(()=>{
                    DraftStore.Edit(d,(files,_)=>ProjectStructure.SetMember(files,item,shop,"itemPool",on)); Draw(page);
                }));
                const string help="상점 진열 · 던전 상점에 진열될 수 있는 후보입니다. 보상·상점 등장이 꺼져 있으면 진열되지 않습니다.";
                WorkshopUI.Hover(row,()=>ShowHelp(help,"MEMBERS/"+shop.name+"/itemPool"));
                foreach(var s in row.GetComponentsInChildren<Selectable>()) WorkshopUI.Hover(s,()=>ShowHelp(help,"MEMBERS/"+shop.name+"/itemPool"));
                y+=24;
                if(!item.availableAsReward) y=Note(detail,y,"보상·상점 등장이 꺼져 있어 진열되지 않습니다");
            }
            if(growth!=null)
            {
                var row=WorkshopUI.Box(detail,"시작 아이템",0,y,DetailW-6,24,Color.clear);
                WorkshopUI.Label(row.transform,"시작 아이템",0,0,118,22,10);
                if(growth.startingItem==item) WorkshopUI.Label(row.transform,"새 게임에 1개 지급",120,0,176,22,8);
                else WorkshopUI.Button(row.transform,"시작 아이템으로",120,1,110,20,()=>Safe(()=>MakeStartingItem(e)),false,"시작 아이템");
                y+=24;
            }
        }

        Section(detail,ref y,"효과");
        var effects=item.effects ?? new List<ItemEffect>();
        if(effects.Count==0) y=Note(detail,y,"효과가 없습니다. 주머니에 넣어도 아무 일도 일어나지 않습니다.");
        for(int n=0;n<effects.Count;n++) y=EffectCard(detail,y,d,item,n);
        WorkshopUI.Button(detail,"+ 효과 추가",2,y,100,18,()=>Safe(()=>OpenEffectTypes(d,item)),false,"효과 추가");
        y+=24;
        string code="문서 "+e.Document+" · "+(variant ? "세이브 키 content_"+e.id : "에셋 이름 "+item.name);
        if(item.formerAssetNames?.Length>0) code+="\n옛 세이브 이름: "+string.Join(", ",item.formerAssetNames);
        return Note(detail,y,code)+4;
    }
    // 새 콘텐츠의 이름(definition.json title). 입력하는 동안 초안에 넣고, 연속 입력은 실행 취소 한 번이다.
    private int ItemTitleRow(RectTransform detail,int y,DraftStore.Draft d)
    {
        var row=WorkshopUI.Box(detail,"title",0,y,DetailW-6,36,Color.clear);
        WorkshopUI.Label(row.transform,"이름",0,0,118,22,10);
        var input=Input(row.transform,"이름",d.Title ?? d.entry.Title,120,1,176,20);
        input.onValueChanged.AddListener(v=>Safe(()=>{ DraftStore.SetTitle(d,v); Refresh(); }));
        var glyphs=WorkshopUI.Label(row.transform,"",120,22,176,12,8);
        glyphs.name="글꼴 경고"; glyphs.color=Warn; glyphs.overflowMode=TextOverflowModes.Truncate;
        glyphs.GetComponent<PixelText>().UseFaceResolver(catalog.FontForInput);
        void Show()
        {
            string title=d.Title ?? d.entry.Title;
            if(!input.isFocused && input.text!=title) input.SetTextWithoutNotify(title);
            string missing=PixelText.MissingGlyphs(input.text);
            glyphs.gameObject.SetActive(missing.Length>0); glyphs.text="⚠ 글꼴에 없는 글자: "+missing;
        }
        Show(); refreshers.Add(Show);
        WorkshopUI.Hover(row,()=>ShowHelp("이름 · 화면에 보이는 이름입니다. 세이브는 content_"+d.entry.id+"로 저장하므로 바꿔도 세이브가 깨지지 않습니다.","definition.json/title"));
        return Note(detail,y+36,"새 콘텐츠 · 세이브 키 content_"+d.entry.id);
    }
    // 효과 카드: 배경 틀을 먼저 두고 행은 상세 열에 그린 뒤 틀 높이를 맞춘다(행 폭이 상세 열 폭이라 틀 안에 넣으면 넘친다).
    private int EffectCard(RectTransform detail,int y,DraftStore.Draft d,ItemSO item,int n)
    {
        var effect=item.effects[n];
        if(effect==null) return Note(detail,y,(n+1)+". 빈 효과 칸 · 게임에서 무시됩니다(Unity에서 정리)");
        string id=effect.id, prefix=ItemAuthoring.EffectPath(effect);
        ItemEffect Live()=>item.effects?.FirstOrDefault(x=>x!=null && x.id==id);   // 값을 적용할 때마다 새 원소는 다시 만들어진다
        string V(string leaf)=>d.byPath.TryGetValue(prefix+"/"+leaf,out var f) ? d.Value(f) : null;
        var info=ItemAuthoring.EffectTypes.FirstOrDefault(t=>t.type==effect.GetType());
        int top=y;
        var card=WorkshopUI.Frame(detail,"효과/"+id,0,y,DetailW-6,20).rectTransform;
        WorkshopUI.Label(card,(n+1)+". "+(info.name ?? effect.GetType().Name),4,2,234,16,10);
        void Structure(Action<Dictionary<string,string>,JObject> change)=>Safe(()=>{ DraftStore.Edit(d,change); Draw(page); });
        WorkshopUI.Button(card,"▲",244,2,16,16,()=>Structure((f,_)=>ProjectStructure.MoveElement(f,item,"effects",id,-1)),false,"효과/"+id+"/위").interactable=n>0;
        WorkshopUI.Button(card,"▼",262,2,16,16,()=>Structure((f,_)=>ProjectStructure.MoveElement(f,item,"effects",id,1)),false,"효과/"+id+"/아래").interactable=n<item.effects.Count-1;
        WorkshopUI.Button(card,"×",280,2,16,16,()=>Structure((f,v)=>ProjectStructure.RemoveElement(f,v,item,"effects",id)),false,"효과/"+id+"/삭제");
        y+=22;
        if(!string.IsNullOrEmpty(info.help)) y=Note(detail,y,info.help.Split(new[]{". "},StringSplitOptions.None)[0].TrimEnd('.')+".");
        static string Short(string label)=>label.Contains(" · ") ? label.Substring(label.LastIndexOf(" · ",StringComparison.Ordinal)+3) : label;
        foreach(var f in d.fields.Where(f=>f.path.StartsWith(prefix+"/",StringComparison.Ordinal)).ToArray())
        {
            string leaf=f.path.Substring(prefix.Length+1);
            if(leaf is "label" or "displayText") continue;
            Func<(float,string)> unit=null; Func<int,string> blocked=null;
            if(effect is ItemStatEffect && leaf=="value") unit=()=>Live() is ItemStatEffect s ? ItemAuthoring.StatUnit(s) : (1f,null);
            if(effect is ItemRoomClearHealEffect && leaf=="value") unit=()=>On(V("isPercent")) ? (100f,"%") : (1f,null);
            if(effect is ItemStatEffect && leaf=="statType")
            {
                var names=Enum.GetNames(typeof(StatType));
                blocked=i=>ItemAuthoring.StatReason((StatType)Enum.Parse(typeof(StatType),names[i]),On(V("isPercent")));
            }
            y=FieldRow(detail,y,d,f.path,label:Short(f.label),unitOf:unit,redraw:leaf is "statType" or "isPercent",disabledReason:blocked);
        }
        y=FieldRow(detail,y,d,prefix+"/label",label:"메모");
        y=FieldRow(detail,y,d,prefix+"/displayText",label:"표시 문구",lines:2);
        // 치환어 칩: 누르면 표시 문구 입력칸의 커서 자리에 넣는다.
        var display=ChildNamed(detail,prefix+"/displayText")?.GetComponentInChildren<TMP_InputField>();
        int x=2;
        foreach(string token in Regex.Matches(effect.TokenHelp,@"\{[^{}]+\}").Cast<Match>().Select(m=>m.Value).Distinct())
        {
            int w=6+token.Sum(c=>c<128 ? 5 : 9);
            if(x+w>DetailW-6){ x=2; y+=16; }
            var chip=WorkshopUI.Button(detail,token,x,y,w,14,()=>Safe(()=>{
                if(display==null) return;
                int at=Mathf.Clamp(display.stringPosition,0,display.text.Length);
                display.text=display.text.Insert(at,token); display.stringPosition=at+token.Length;
            }),false,"효과/"+id+"/치환어/"+token);
            var caption=chip.GetComponentInChildren<TMP_Text>(); PixelText.Configure(caption,8); caption.overflowMode=TextOverflowModes.Overflow;
            x+=w+2;
        }
        y+=18;
        var shown=WorkshopUI.Label(detail,"",2,y,DetailW-8,12,8);
        shown.overflowMode=TextOverflowModes.Truncate;
        void Show()=>shown.text="게임 표시(1개): "+(Live()?.Describe(1) ?? "");
        Show(); refreshers.Add(Show);
        y+=14;
        card.sizeDelta=new Vector2(DetailW-6,y-top+2);
        return y+6;
    }
    private void OpenEffectTypes(DraftStore.Draft d,ItemSO item)
    {
        var types=ItemAuthoring.EffectTypes;
        var box=WorkshopUI.Frame(WorkshopUI.Overlay(content,"효과 종류 선택"),"효과 종류",160,40,320,12+types.Count*34).rectTransform;
        for(int i=0;i<types.Count;i++)
        {
            var t=types[i];
            WorkshopUI.Button(box,t.name,8,8+i*34,304,18,()=>Safe(()=>{
                WorkshopUI.CloseOverlay();
                DraftStore.Edit(d,(f,_)=>ProjectStructure.AddElement(f,item,"effects",t.type));
                Draw(page);
            }),false,"효과 종류/"+t.name);
            WorkshopUI.Label(box,t.help,10,27+i*34,300,12,8).overflowMode=TextOverflowModes.Truncate;
        }
    }
    // 성장 레지스트리의 시작 아이템을 바꾸고 바로 저장한다. 그 문서에 다른 미저장 변경이 있으면 함께 저장되지 않게 막는다.
    private void MakeStartingItem(Entry e)
    {
        var growth=ItemGrowth;
        var entry=EnsureCatalog().FirstOrDefault(x=>x.source==growth) ?? throw new InvalidOperationException("성장 레지스트리를 찾을 수 없습니다.");
        var g=DraftStore.Open(entry);
        if(g.Dirty) throw new InvalidOperationException("성장 레지스트리에 저장하지 않은 변경이 있습니다 · 모든 수치에서 정리하세요");
        DraftStore.Set(g,g.byPath["startingItem"],e.id);
        DraftStore.Save(g);
        itemRows=null; Draw(page);
        Say("시작 아이템을 바꿨습니다 · 다음 새 게임부터");
    }

    // 미리보기 열(158×148): 탭 y2 · 본문 y22(82) · 주머니 보유 개수 y106 · 경고 줄 y130.
    private void DrawItemAside(DraftStore.Draft d)
    {
        var item=(ItemSO)d.entry.source;
        var aside=Aside();
        WorkshopUI.Choice(aside,"미리보기",4,2,150,16,new[]{"툴팁","상점","주머니"},()=>itemPreview,i=>{ itemPreview=i; Draw(page); });
        var body=WorkshopUI.Label(aside,"",4,22,150,82,8);
        body.textWrappingMode=TextWrappingModes.Normal; body.overflowMode=TextOverflowModes.Truncate; body.alignment=TextAlignmentOptions.TopLeft;
        int Count()=>Mathf.Clamp(itemCount,1,item.MaxStack);
        string Text()
        {
            if(itemPreview==1) return item.DisplayName+"\n"+item.ShopBody+"\n"+item.Price+" G";
            if(itemPreview!=2) return item.TooltipBody;
            int n=Count();   // PouchUI.Select와 같은 문구
            return "개당 효과\n"+item.EffectSummary(1)+"\n현재 총 효과(×"+n+")\n"+item.EffectSummary(n)+
                "\n"+(n>=item.MaxStack ? "최대 중첩 도달" : "1개 더 획득 시\n"+item.EffectSummary(n+1));
        }
        void Show()=>body.text=Text();
        Show(); refreshers.Add(Show);
        if(itemPreview==2)
            refreshers.Add(WorkshopUI.Stepper(aside,"보유 개수",4,106,96,()=>Count(),v=>{ itemCount=(int)v; Refresh(); },1,1,999));
        var line=WorkshopUI.Box(aside,"경고 줄",4,130,150,14,Color.clear);
        void Warn()
        {
            foreach(Transform child in line.transform){ child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var list=ItemWarnings(d.entry);
            if(list.Count==0){ WorkshopUI.Label(line.transform,"확인할 것 없음",0,0,150,14,8); return; }
            WorkshopUI.Label(line.transform,"⚠ "+list.Count,0,0,26,14,8);
            var first=list[0];
            var button=WorkshopUI.Button(line.transform,first.title,28,0,122,14,()=>Safe(()=>{ itemFocus=(first.field,first.text); Draw(page); }),false,"경고/0");
            var caption=button.GetComponentInChildren<TMP_Text>(); PixelText.Configure(caption,8);
            caption.alignment=TextAlignmentOptions.MidlineLeft; caption.overflowMode=TextOverflowModes.Truncate;
            WorkshopUI.Hover(button,()=>ShowHelp(string.Join("\n",list.Select(w=>"⚠ "+w.title)),"확인할 것"));
        }
        Warn(); refreshers.Add(Warn);
        WorkshopUI.Hover(line,()=>ShowHelp(string.Join("\n",ItemWarnings(d.entry).Select(w=>"⚠ "+w.title)),"확인할 것"));
    }
}
