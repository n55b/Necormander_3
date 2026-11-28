using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow
{
    // 화면 하나의 자리. 시험 복귀와 뒤로가기가 같은 값으로 되살린다.
    private struct Spot
    {
        public Page page;
        public string item;     // 상태이상 행 key, 아이템 문서 경로, 모든 수치 entry.id(null은 에셋 목록), 대화 id, 아트 원본 경로, 번역 표, 공유 묶음 비교, 방 id
        public int listPage;    // 모든 수치 dataPage, 아이템 itemPage, 방 목록·공유 listPage
        public float scroll;    // 상세 content y(논리 px, 정수)
        public string search;   // 모든 수치 dataSearch
    }
    // 시험·게임 오버로 LevelWorkshop 씬이 다시 열려도 남는 화면 상태. remembered*(지도)와 같은 방식이다.
    private static class WorkshopSession
    {
        internal static bool FromContent;      // 콘텐츠 화면에서 시험을 시작했다. 시험이 실제로 시작된 뒤에만 켜고 Start()가 바로 소비한다
        internal static Spot Return;           // 그때 보던 화면
        internal static string Focus;          // FocusKey(부모 이름 + 이름)
        internal static string Lost;           // 시험으로 창과 함께 사라진 Title 페이지 이름들. 돌아와서 알린다
        internal static readonly List<Spot> History=new();   // 뒤로가기 기록. 끝이 직전 화면이다
    }
    private const int HistoryLimit=32;
    private float detailScrollY;           // 상세 content y. Scroll의 moved 콜백이 갱신한다
    private bool restoringScroll;          // Restore 중: Detail()이 restoreScroll을 쓴다
    private float restoreScroll;
    private Spot settled;                  // 직전 프레임 끝의 화면. 화면이 바뀐 프레임에 기록으로 넣는다
    private bool hasSettled, typing;       // typing: 직전 프레임에 입력칸이 편집 중이었다(입력칸이 Esc를 먼저 받은 프레임에도 뒤로 가지 않게)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        WorkshopSession.FromContent=false; WorkshopSession.Return=default; WorkshopSession.Focus=WorkshopSession.Lost=null;
        WorkshopSession.History.Clear(); QuitApproved=false;
    }

    // 등록표 Trial 칸(F6). 허수아비 없음·Validate 실패로 예외가 나거나 false면 기록하지 않으므로 FromContent가 남는 경로가 없다.
    private void StartContentTrial()
    {
        CommitPendingInputs();
        if(page==Page.Parameters && dataField!=null) AcceptParameter();
        // 아이템 화면은 고른 항목을 미리보기 주머니 탭의 보유 개수만큼 지급한다. 상태이상·모든 수치는 지급하지 않는다.
        WorkshopTrial.Loadout.Clear();
        if(page==Page.Items && itemDraft?.entry.source is ItemSO given) WorkshopTrial.Loadout.Add((itemDraft.entry.id,Mathf.Clamp(itemCount,1,given.MaxStack)));
        if(WorkshopTrial.PlayArena()) RememberContent(); else { WorkshopTrial.Loadout.Clear(); Say("시험을 시작할 수 없습니다."); }
    }
    // PlayArena는 씬 로드를 요청만 하므로 같은 프레임의 창·선택·스크롤을 그 뒤에 기록해도 된다.
    private void RememberContent()
    {
        var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        WorkshopSession.FromContent=true; WorkshopSession.Return=Here();
        WorkshopSession.Focus=selected!=null && selected.transform.IsChildOf(content) ? FocusKey(selected.transform) : null;
        if(Doc!=null) RememberMap();          // 방을 열어 둔 채 콘텐츠를 시험해도 지도 상태가 남는다
    }
    private void RestoreContent()
    {
        Restore(WorkshopSession.Return);
        Refocus(WorkshopSession.Focus);
    }

    private Spot Here()=>new Spot
    {
        page=page,
        item=page switch
        {
            Page.Status=>statusRow, Page.Items=>itemDraft?.entry.Document, Page.Parameters=>dataEntry?.id,
            Page.Dialogue=>conversation?.id, Page.Art=>art?.source, Page.Shared=>review!=null ? "묶음 비교" : null,
            Page.Translations=>translationTable!=null ? translationTable.TableCollectionName+" · "+translationTable.LocaleIdentifier.Code : null,
            Page.Map or Page.Compare or Page.Passage=>Doc?.id,
            _=>null,
        },
        listPage=page switch { Page.Items=>itemPage, Page.Parameters=>dataPage, _=>listPage },
        scroll=detailScrollY, search=dataSearch,
    };
    // 같은 화면인가: 목록과 그 항목 화면이 갈리는 페이지(모든 수치·대사·아트·번역·공유)는 항목까지 본다.
    // 목록-상세가 한 화면인 상태이상·아이템은 행을 바꿔도 기록하지 않는다(돌아가면 떠날 때의 행이 열린다).
    // 방 비교·연결 미리보기는 방 편집의 한 상태다: [편집으로]로 돌아와도 기록이 쌓이지 않고, 뒤로가기는 Back이 닫기로 처리한다.
    private static Page Screen(Page p)=>p is Page.Compare or Page.Passage ? Page.Map : p;
    private static bool SameScreen(Spot a,Spot b)=>Screen(a.page)==Screen(b.page) &&
        (a.page is not (Page.Parameters or Page.Dialogue or Page.Art or Page.Translations or Page.Shared) || a.item==b.item);

    // 화면 이동 기록: 한 번의 클릭이 Draw를 여러 번 불러도(Navigate 뒤 SelectContent 등) 프레임 끝의 화면만 비교한다.
    private void LateUpdate()
    {
        typing=InputFocused();
        if(content==null) return;
        var here=Here();
        if(hasSettled && !SameScreen(settled,here))
        {
            WorkshopSession.History.Add(settled);
            if(WorkshopSession.History.Count>HistoryLimit) WorkshopSession.History.RemoveAt(0);
        }
        settled=here; hasSettled=true;
    }
    private bool CanGoBack()=>page is Page.Passage or Page.Compare || WorkshopSession.History.Count>0 || hasSettled && !SameScreen(settled,Here());
    private void ForgetHistory(){ WorkshopSession.History.Clear(); hasSettled=false; }

    // [← 뒤로]·Esc·마우스 뒤로. 방 연결 미리보기·비교는 [편집으로]와 같다. 되살릴 수 없는 자리(편집하던 방이 사라짐)는 건너뛴다.
    // 화면이 막으면(미저장 대사 등) 기록을 그대로 두고 그 안내를 띄운다.
    private void Back()
    {
        var history=WorkshopSession.History;
        if(page is Page.Passage or Page.Compare)
        {
            if(page==Page.Passage) ClosePassage(); else CloseComparison();
            settled=Here(); return;
        }
        hasSettled=false;                      // 되살리는 동안 그리는 [뒤로]는 남은 기록만 센다
        try
        {
            while(history.Count>0)
            {
                var spot=history[^1]; history.RemoveAt(history.Count-1);
                if(SameScreen(spot,Here())) continue;   // 건너뛴 자리 뒤에 남은 지금 화면: 누른 뒤로가 제자리에서 끝나지 않게
                bool done;
                try { done=Restore(spot); }
                catch { history.Add(spot); throw; }
                if(done) return;
            }
        }
        finally { settled=Here(); hasSettled=true; }
    }
    // Esc·마우스 뒤로가 비켜 서는 인라인 편집. [← 뒤로] 버튼은 언제나 누를 수 있다.
    private bool EditingInline()=>typing || InputFocused() || page switch
    {
        Page.Parameters=>dataField!=null || showPresets || creatingContent,
        Page.Items=>creatingItem || pendingPurge!=null,
        Page.Translations=>creatingTranslation || translation!=null,
        Page.Game=>routeDraft!=null,
        Page.Map=>drawer || encounterOpen || applyPiece!=null || before!=null || pendingConflict!=null,
        _=>false,
    };

    // 그 자리를 다시 연다. false면 되살릴 수 없는 자리다.
    private bool Restore(Spot s)
    {
        restoreScroll=s.scroll; restoringScroll=true;   // Detail()이 한 번 적용한다
        try
        {
            switch(s.page)
            {
                case Page.Status: OpenStatus(s.item); break;
                case Page.Items: OpenItems(s.item,s.listPage); break;
                case Page.Parameters:
                    showPresets=false;
                    OpenParameters(s.search ?? "");   // 카탈로그를 다시 만든 뒤 그 안의 항목을 고른다(없어졌으면 목록)
                    var entry=s.item==null ? null : Array.Find(dataCatalog,e=>e.id==s.item);
                    if(entry!=null) SelectContent(entry);
                    dataSearch=s.search ?? ""; dataPage=s.listPage; Draw(page);
                    break;
                case Page.Map: case Page.Compare: case Page.Passage:
                    if(Doc==null || Doc.id!=s.item) return false;   // 그 뒤 다른 방을 열었다
                    if(page!=Page.Map) OpenMap(false);
                    break;
                case Page.Dialogue:
                    if(s.item!=null && conversation?.id!=s.item){ OpenRecent(Area.Text,s.item); break; }
                    if(s.item==null && conversation!=null)
                    {
                        if(dialogueDirty) throw new InvalidOperationException("대사를 저장하거나 변경 취소를 눌러 주세요.");
                        conversation=null;
                    }
                    Navigate(Page.Dialogue); break;
                case Page.Art:
                    if(s.item!=null && art?.source!=s.item)
                    {
                        artEntries??=WorkshopArtSource.Entries();
                        if(Array.Find(artEntries,e=>e.source==s.item)==null) return false;   // 원본 파일이 사라졌다
                        OpenRecent(Area.Art,s.item); break;
                    }
                    if(s.item==null && art!=null)
                    {
                        if(nextArt!=null) throw new InvalidOperationException("교체본을 저장하거나 교체 취소를 눌러 주세요.");
                        DisposeArt(); art=null; currentArt=null;
                    }
                    Navigate(Page.Art); break;
                case Page.Translations:
                    if(s.item==null && translationTable!=null)
                    {
                        if(translation!=null || creatingTranslation) throw new InvalidOperationException("번역을 저장하거나 취소해 주세요.");
                        translationTable=null;
                    }
                    Navigate(Page.Translations); break;
                case Page.Git: gitFiles=ProjectGitReview.Files(); Navigate(Page.Git); break;
                case Page.Desk: case Page.Legacy: case Page.Shared: case Page.Project:
                    Navigate(s.page); listPage=s.listPage; Draw(page); break;
                default: Navigate(s.page); break;
            }
            return true;
        }
        finally { restoringScroll=false; }   // Detail()을 쓰지 않는 화면에서 남지 않게
    }

    // 헤더 위치 표시(예: 게임 콘텐츠 › 소환수 › Champion Minion). 첫 화면은 없다.
    private IEnumerable<string> Location()
    {
        var area=pages[(int)page].Area;
        if(area==null) yield break;
        yield return areas[(int)area.Value].caption;
        string Tile(string id)=>contentTiles.FirstOrDefault(t=>t.id==id).caption;
        switch(page)
        {
            case Page.Parameters:
                yield return dataEntry!=null ? Tile(TileOf(dataEntry)) : contentTiles.FirstOrDefault(t=>t.group!=null && t.group==dataSearch).caption ?? "모든 수치";
                if(dataEntry!=null) yield return ContentTitle(dataEntry);
                break;
            case Page.Status: yield return "상태이상"; yield return StatusRows.FirstOrDefault(r=>r.key==statusRow).title; break;
            case Page.Items: yield return "아이템"; if(itemDraft!=null) yield return ContentTitle(itemDraft.entry); break;
            case Page.Balance: yield return "플레이어"; break;
            case Page.Project: yield return "게임 방 편집"; break;
            case Page.Git: yield return "Git 충돌"; break;
            case Page.Map: case Page.Compare: case Page.Passage:
                if(Doc!=null) yield return Doc.title;
                if(page!=Page.Map) yield return page==Page.Compare ? "비교" : "연결 미리보기";
                break;
            default:
                if(pages[(int)page].Tab.caption is string tab) yield return tab;
                var item=Here().item;
                if(item!=null) yield return page==Page.Art ? System.IO.Path.GetFileNameWithoutExtension(item) : item;
                break;
        }
    }
    // 탭 끝(left)부터 오른쪽 첫 헤더 버튼(미저장·뒤로·화면의 헤더 버튼)까지. 숨긴 미저장 버튼도 자리를 차지한다(제자리 편집으로 켜진다).
    // 넘치면 앞 단계부터 "…"로 줄이고, 자리가 좁으면(방 편집 등) 그리지 않는다.
    private void DrawLocation(int left)
    {
        var path=Location().Where(s=>!string.IsNullOrEmpty(s)).ToList();
        for(int i=path.Count-1;i>0;i--) if(path[i]==path[i-1]) path.RemoveAt(i);   // 게임 콘텐츠 › 게임 콘텐츠
        int right=BackX-4;
        foreach(RectTransform child in content)
            if(child.GetComponent<Button>()!=null && child.anchoredPosition.y>-30 && child.anchoredPosition.x>=left)
                right=Mathf.Min(right,(int)child.anchoredPosition.x-4);
        if(path.Count==0 || right-left<40) return;
        var label=WorkshopUI.Label(content,"",left,5,right-left,20,8);
        label.name="위치";
        var pixel=label.GetComponent<PixelText>();
        for(int skip=0;skip<path.Count;skip++)
        {
            label.text=(skip>0 ? "… › " : "")+string.Join(" › ",path.Skip(skip));
            pixel.PrepareForRender();   // 비트맵 글꼴 크기는 보통 그리기 직전에 정해진다. 재기 전에 적용한다
            if(label.GetPreferredValues(label.text).x<=right-left) break;
        }
    }

    private void RememberMap()
    {
        rememberedSelection=selection.ToArray(); rememberedStart=start; rememberedTools=(tool,layer,enemyIndex,propIndex,dir);
        rememberedTile=palette?.Length>0 ? palette[materialIndex].id : null; rememberedCell=floorCell; rememberedFloor=roomFloor;
    }
    private void RestoreMap()
    {
        selection.UnionWith(rememberedSelection); start=rememberedStart; floorCell=rememberedCell; roomFloor=rememberedFloor;
        (tool,layer,enemyIndex,propIndex,dir)=rememberedTools;
    }
    // 이름에 '/'가 들어가므로(상태이상/poison) Transform 경로 대신 부모 이름 + 이름으로 찾는다.
    private static string FocusKey(Transform t)=>t.parent.name+"\n"+t.name;
    // 입력칸은 되살리지 않는다: Select가 편집을 다시 시작하고, 입력칸 onEndEdit(선택 해제 중)에서 부른 Draw라면 EventSystem이 중첩 선택을 오류로 막는다.
    private void Refocus(string key)
    {
        var events=UnityEngine.EventSystems.EventSystem.current;
        if(key==null || events==null || events.alreadySelecting) return;
        foreach(var s in content.GetComponentsInChildren<Selectable>())
            if(s.interactable && s is not TMPro.TMP_InputField && FocusKey(s.transform)==key){ s.Select(); return; }
    }
}
