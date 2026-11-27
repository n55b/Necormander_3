using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow : MonoBehaviour
{
    public WorkshopCatalog catalog;
    private enum Page { Desk, Legacy, Map, Shared, Game, Project, Balance, Git, Parameters, Dialogue, Translations, Art, Compare, Passage, Content, Status, Home, Items }
    private enum Tool { Select, Paint, Erase, Sample, Door, Enemy, Prop, Start, Stamp, Auto }
    private Page page;
    private Tool tool;
    private Canvas canvas;
    private RectTransform content, viewport;
    private TMP_Text feedback, detail, cursorLabel;
    private Button undoButton, redoButton, removeButton;
    private WorkshopPreview preview;
    private WorkshopPreview comparison;
    private WorkshopRoom Doc => WorkshopTrial.Draft;
    private string before, notice="";
    private int layer, materialIndex, enemyIndex, propIndex, dir, listPage;
    private Vector2Int? lastCell;
    private Vector3? start;
    private bool moving;
    private Vector2 lastPointer;
    private Vector3Int? cursorCell;
    private Button zoomIn, zoomOut;
    private readonly string[] layerNames={"바닥","벽","그림자","통행 불가"};
    private readonly Vector2Int[] directions={Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down};
    private readonly string[] compass={"동","북","서","남"};
    private WorkshopCatalog.Piece[] enemies, props;
    private WorkshopCatalog.TileEntry[] palette;
    private static string ShareFolder => WorkshopWorkspace.Shared;
    private static string Recovery(string id) => WorkshopFiles.FileFor(id)+".draft";
    private static bool dirty;
    private static string persisted;
    private static Dictionary<string,string> projectBasis;
    private readonly Dictionary<string,ProjectContentStore.Conflict> roomChoices=new();
    private ProjectContentStore.Conflict pendingConflict;
    private static string[] rememberedSelection=Array.Empty<string>();
    private static Vector3? rememberedStart;
    private static (Tool,int,int,int,int) rememberedTools;
    private static string rememberedTile;
    private static Vector2Int? rememberedCell;
    private static WorkshopFloor rememberedFloor;

    private void Awake()=>RegisterPages();
    private void Start()
    {
        WorkshopTrial.Library=catalog;
        enemies=catalog.pieces.Where(p=>p.enemy).ToArray();
        props=catalog.pieces.Where(p=>!p.enemy).ToArray();
        int archer=Array.FindIndex(enemies,e=>e.prefab.name.Contains("Range"));
        enemyIndex=Mathf.Max(0,archer);
        canvas=WorkshopUI.Canvas("Workshop canvas",25000);
        canvas.transform.SetParent(transform,false);
        // Root canvases get their logical transform from the frame camera.
        // 시험에서 돌아온 순서: 층 복귀 → 콘텐츠 화면 → 방 편집 → 첫 화면(작업 종류 카드).
        if (WorkshopTrial.ReturnToFloor)
        {
            WorkshopTrial.ReturnToFloor=false; WorkshopSession.FromContent=false; Draw(Page.Game);
        }
        else if (WorkshopSession.FromContent)
        {
            WorkshopSession.FromContent=false; if(Doc!=null) RestoreMap(); RestoreContent();
        }
        else if (Doc!=null) { RestoreMap(); OpenMap(false,rememberedTile); }
        else Draw(Page.Home);
        if (WorkshopSession.Lost!=null) { Say("시험 전에 저장하지 않은 "+WorkshopSession.Lost+" 변경은 사라졌습니다."); WorkshopSession.Lost=null; }
#if LEVEL_WORKSHOP && !UNITY_EDITOR
        Application.wantsToQuit+=WantsToQuit;
#endif
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged+=WarnUnsavedOnExit;
#endif
    }
    private void OnDestroy()
    {
        // 시험이 시작돼 씬이 바뀌는 중이면(Begin이 Active를 켠 뒤 로드한다) 창과 함께 사라지는 Title 페이지를 기록한다.
        if(WorkshopTrial.Active && pages!=null)
        {
            var lost=pages.Where(p=>p.Title!=null && p.Dirty()).Select(p=>p.Title).ToArray();
            WorkshopSession.Lost=lost.Length>0 ? string.Join("·",lost) : null;
        }
#if LEVEL_WORKSHOP && !UNITY_EDITOR
        Application.wantsToQuit-=WantsToQuit;
#endif
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged-=WarnUnsavedOnExit;
#endif
        DisposeArt();
        comparison?.Dispose();
        preview?.Dispose();
        foreach(var texture in thumbnails.Values) Destroy(texture);
        if(canvas!=null) Destroy(canvas.gameObject);
    }
    private void Draw(Page next)
    {
        var previousControl=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;   // 이미 파괴된 버튼일 수 있다: != null로만 확인한다
        bool inside=content!=null && previousControl!=null && previousControl.transform.IsChildOf(content);
        deskControl=next==Page.Desk && page==Page.Desk && inside ? previousControl.name : null;
        string kept=next==page && inside ? FocusKey(previousControl.transform) : null;   // 같은 화면을 다시 그리면 선택을 되살린다
        applyPiece=null; refreshers.Clear();
        page=next;
        if (content!=null) { content.gameObject.SetActive(false); Destroy(content.gameObject); }
        content=WorkshopUI.Box(canvas.transform,"Workspace",0,0,640,360,WorkshopUI.Ink).rectTransform;
        WorkshopUI.Frame(content,"Header",0,0,640,30);
        DrawHeader();
        Btn("메뉴",586,5,46,20,Leave);
        DrawUnsavedButton();
        pages[(int)page].Draw();
        if(page!=Page.Desk) Refocus(kept);          // 작업대는 DrawLibrary가 기존 규칙(검색→'검색' 치환, 기본 포커스)으로 고른다
        if(areaMenuOpen) DrawAreaMenu();            // 그리는 순서: 헤더 → 페이지 → 영역·미저장 목록 → 모달 → 상태 줄
        if(unsavedOpen) DrawUnsavedList();
        if(pendingConfirm!=null) DrawConfirm();     // Draw가 몇 번 다시 돌아도 모달이 남는다
        string hint = page == Page.Passage ? "이동 범위는 착지점에서 상하좌우로 이어진 바닥 타일 기준입니다." :
            !string.IsNullOrEmpty(notice) ? notice : page == Page.Map ? MapHint() :
            page is Page.Status or Page.Items ? "Ctrl+S 저장 · Ctrl+Z 되돌리기 · F6 시험" :
            page is Page.Desk or Page.Home ? (ProjectContentStore.Authoring ? "저장 위치  ·  프로젝트 DATA" : "저장 위치  ·  로컬 작업 공간") : "";
        feedback=WorkshopUI.Label(content,hint,9,page==Page.Map ? 328 : 335,page==Page.Map ? 382 : 623,page==Page.Map ? 15 : 19);
        if (page==Page.Map)
            WorkshopUI.Label(content,"휠 확대 · 가운데 버튼으로 이동 · Ctrl+S 저장 · F6 시험",9,344,385,15);
    }
    private Button Btn(string title,int x,int y,int w,int h,Action action,bool selectedTab=false,string id=null) =>
        WorkshopUI.Button(content,title,x,y,w,h,()=>Safe(action),selectedTab,id);
    private void Safe(Action action)
    {
        try { action(); }
        // 방 문서의 충돌만 방 충돌 화면으로 보낸다. 나머지는 아래에서 "서로 다르게 바뀐 항목: <문서>/<키>"로 알린다.
        catch(ProjectContentStore.Conflict clash) when(page==Page.Map && clash.field.StartsWith(ProjectRooms.DirectoryName+"/",StringComparison.Ordinal))
        { pendingConflict=clash; Draw(page); Say("두 사람의 변경을 확인해 주세요."); }
        catch(Exception ex) { Say(ex.Message); Debug.LogWarning("[Workshop] "+ex.Message); }
    }
    private void Say(string message) { notice=message; if(feedback!=null) feedback.text=message; }
    private void CloseMap()
    {
        EndStroke();
        comparison?.Dispose();comparison=null;
        if (preview!=null)
        {
            RememberView();
            preview.Dispose(); preview=null;
        }
    }
    private void Navigate(Page next)
    {
        CloseMap(); review=null; listPage=0; drawer=false; encounterOpen=false; areaMenuOpen=false; notice=""; Draw(next);
    }
    private void Leave()=>ConfirmUnsaved(()=>
    {
        CloseMap(); WorkshopTrial.Draft=null; WorkshopTrial.Undo.Clear(); WorkshopTrial.Redo.Clear();
        UnityNote.SceneLoader.Load("StartScene");
    });
    private void DrawLibrary(string deskControl)
    {
        bool legacy=page==Page.Legacy, shared=page==Page.Shared;
        if (shared) DrawShareHeader();
        else if (page!=Page.Desk) WorkshopUI.Label(content,legacy ? "Legacy  /  기존 방 원본" : "게임에 등장하는 방",12,36,305,24,12);
        if (legacy)
        {
            Btn(legacyTools ? "방 원본" : "기존 도구",488,39,137,20,()=>{legacyTools=!legacyTools;listPage=0;Draw(page);});
            if(legacyTools) { DrawLegacyTools(); return; }
        }
        if (!legacy && page!=Page.Desk)
        {
            Btn(shared ? "공유 폴더 열기" : "새 방 만들기",449,39,177,20,()=>{
                if (shared) { Directory.CreateDirectory(ShareFolder); Application.OpenURL(new Uri(ShareFolder).AbsoluteUri); }
                else New(catalog.DefaultRoom);
            });
        }
        if (shared && shareView==ShareView.Packages) { DrawPackages(); return; }
        if (shared && shareView==ShareView.Workspaces) { DrawWorkspaces(); return; }
        var rows=new List<(string label,string meta,string key,WorkshopCatalog.Template template,Action run)>();
        if(page==Page.Shared && ProjectContentStore.Authoring)
            Btn("Git 충돌 확인",288,38,123,23,()=>{gitFiles=ProjectGitReview.Files();Navigate(Page.Git);});
        if(page==Page.Project)
        {
            foreach(var template in catalog.rooms.Where(r=>r.inPool || r.tutorialOrder>0))
                rows.Add((template.title,"게임 원본 편집",template.id,template,()=>Begin(ProjectRooms.Template(template),false)));
        }
        else if (legacy)
        {
            foreach(var r in catalog.rooms)
            {
                string use=r.tutorialOrder>0 ? "튜토리얼 "+r.tutorialOrder : r.inPool ? "던전 후보" : "보관 원본";
                rows.Add((r.title,use+"  /  복제",r.id,r,()=>New(r)));
            }
        }
        else rows=RoomRows(shared);
        bool personal=page==Page.Desk||page==Page.Project;
        if(personal)rows=rows.OrderByDescending(r=>PlayerPrefs.GetInt("Workshop.Pinned."+r.key))
            .ThenByDescending(r=>PlayerPrefs.GetString("Workshop.Opened."+r.key,""),StringComparer.Ordinal).ToList();
        if (page==Page.Desk)
        {
            DrawDesk(rows,deskControl);
            return;
        }
        if(rows.Count==0)
        {
            WorkshopUI.Label(content,shared ? "동료의 방 파일을 공유 폴더에 넣어 주세요." : "기존 방을 복제해 첫 방을 만들어 보세요.",24,88,580,26,12);
            if(!shared) Btn("Legacy에서 방 고르기",24,125,181,25,()=>Navigate(Page.Legacy));
            else Btn("목록 새로 고침",24,125,128,25,()=>Draw(page));
        }
        const int pageSize=6;
        int pages=Mathf.Max(1,(rows.Count+pageSize-1)/pageSize); listPage=Mathf.Clamp(listPage,0,pages-1);
        for(int i=listPage*pageSize;i<Math.Min(rows.Count,(listPage+1)*pageSize);i++)
        {
            var row=rows[i]; int x=12+i%2*311,y=70+i%pageSize/2*73;
            WorkshopUI.Frame(content,"Room",x,y,303,66);
            RoomThumbnail(row.template,x+7,y+9);
            WorkshopUI.Label(content,row.label,x+65,y+4,personal ? 170 : 234,23,12);
            if(personal)
            {
                string pin="Workshop.Pinned."+row.key;
                bool pinned=PlayerPrefs.GetInt(pin)!=0;
                Btn(pinned ? "고정됨" : "고정",x+244,y+5,46,18,()=>{PlayerPrefs.SetInt(pin,pinned?0:1);Draw(page);},pinned);
            }
            Btn(row.meta,x+65,y+33,226,22,row.run).name=row.meta+" "+row.key;
        }
        if(rows.Count>pageSize)
        {
            Btn("이전",12,299,55,21,()=>{listPage=Mathf.Max(0,listPage-1);Draw(page);});
            WorkshopUI.Label(content,(listPage+1)+" / "+pages,80,299,60,21);
            Btn("다음",145,299,55,21,()=>{listPage=Mathf.Min(pages-1,listPage+1);Draw(page);});
        }
        if(Doc!=null) Btn("작업으로",345,5,72,20,()=>OpenMap(false));
    }
    // 방 목록(내 방)·공유 폴더의 행. 첫 화면의 최근 작업과 찾기도 같은 행을 연다.
    private List<(string label,string meta,string key,WorkshopCatalog.Template template,Action run)> RoomRows(bool shared)
    {
        var rows=new List<(string label,string meta,string key,WorkshopCatalog.Template template,Action run)>();
        string folder=shared ? ShareFolder : WorkshopFiles.Folder;
        Directory.CreateDirectory(folder);
        var ids=!shared && ProjectContentStore.Authoring ? ProjectRooms.Ids() : Array.Empty<string>();
        foreach(string id in ids)
        {
            try
            {
                var doc=ProjectRooms.Description(id);
                // 삭제 표시한 방은 방 목록의 [삭제됨] 보기에서만 보이고, 누르면 복원한다.
                if(ProjectRooms.IsRemoved(id))
                {
                    if(showRemovedRooms && page==Page.Desk)
                        rows.Add((doc.title,"삭제됨",id,catalog.FindRoom(doc.template),()=>{ProjectRooms.Restore(id);Draw(page);Say("방을 복원했습니다.");}));
                    continue;
                }
                rows.Add((doc.title,"프로젝트 편집",id,catalog.FindRoom(doc.template),()=>Begin(doc,false)));
            }
            catch(Exception error) { rows.Add((id,"데이터 확인",id,null,()=>Say(error.Message))); }
        }
        var paths=Directory.GetFiles(folder,shared ? "*.json" : "*.json*")
            .Where(p=>!ids.Contains(Path.GetFileNameWithoutExtension(p)))
            .Where(p=>p.EndsWith(".json") || p.EndsWith(".json.draft") || p.EndsWith(".json.bak"))
            .OrderByDescending(File.GetLastWriteTimeUtc);
        foreach(var path in paths)
        {
            try
            {
                var doc=WorkshopFiles.Read(path,catalog);
                bool recovery=path.EndsWith(".draft"), backup=path.EndsWith(".bak");
                rows.Add((doc.title,shared ? "가져오기" : recovery ? "자동 복구" : backup ? "이전 버전" : "편집",doc.id,catalog.FindRoom(doc.template),()=>{
                    if (shared || backup || recovery && ids.Contains(doc.id)) { doc.id=Guid.NewGuid().ToString("N"); doc.title+=" · 복사"; }
                    Begin(doc,shared || recovery || backup);
                }));
            }
            catch(Exception) { rows.Add((Path.GetFileName(path),"파일 확인",path,null,()=>WorkshopFiles.Read(path,catalog))); }
        }
        return rows;
    }
    private void New(WorkshopCatalog.Template r) => Begin(WorkshopRoom.Create(r),true);
    private void Begin(WorkshopRoom room,bool changed)
    {
        notice="";
        var basis = ProjectContentStore.Authoring ? ProjectRooms.Snapshot(room.id) : null;
        if (!changed && basis?.Count>0) room=ProjectRooms.Decode(room.id,basis,catalog);
        room.Validate(catalog);
        projectBasis=basis;pendingConflict=null;roomChoices.Clear();
        PlayerPrefs.SetString("Workshop.Opened."+room.id,DateTime.UtcNow.Ticks.ToString("D19"));
        RememberRecent(Area.Level,room.id,room.title);
        floorCell=null; roomFloor=null;
        WorkshopTrial.Draft=room; selection.Clear(); stamp.Clear(); sampling=false; encounterOpen=false; start=null; tool=Tool.Select;
        WorkshopTrial.Undo.Clear(); WorkshopTrial.Redo.Clear();
        var file=WorkshopFiles.FileFor(room.id);
        persisted=File.Exists(file) ? File.ReadAllText(file) : null; dirty=changed;
        if(changed) Recover();
        OpenMap(true);
    }
    private void OpenMap(bool home,string tileId=null)
    {
        comparison?.Dispose(); comparison=null;
        preview?.Dispose();
        preview=new WorkshopPreview(catalog.FindRoom(Doc.template),Doc,catalog);
        if (!home) { preview.Center=WorkshopTrial.View; preview.Zoom=WorkshopTrial.Zoom; preview.SetView(); }
        RefreshSelection();
        preview.MarkStart(start);
        SyncPalette();
        int saved=Array.FindIndex(palette,t=>t.id==tileId);
        if(saved>=0) SelectMaterial(saved);
        Draw(Page.Map);
    }
    private void SyncPalette()
    {
        var map=preview.Map(layer);
        var local=map!=null ? new UnityEngine.Tilemaps.TileBase[map.GetUsedTilesCount()] : Array.Empty<UnityEngine.Tilemaps.TileBase>();
        if(local.Length>0) map.GetUsedTilesNonAlloc(local);
        palette=catalog.tiles.Where(t => tool==Tool.Auto ? t.tile is WorkshopTerrainTile : t.tile is not WorkshopTerrainTile)
            .OrderByDescending(t => local.Contains(t.tile)).ThenBy(t => t.tile.name,StringComparer.Ordinal)
            .ThenBy(t => t.id,StringComparer.Ordinal).ToArray();
        int preferred=tool==Tool.Auto ? Array.FindIndex(palette, t=>((WorkshopTerrainTile)t.tile).PaintLayer==layer) : 0;
        SelectMaterial(Mathf.Max(0,preferred));
    }
    private void SelectMaterial(int index)
    {
        materialIndex=index;
        if(tool==Tool.Auto && palette.Length>0)
            layer=((WorkshopTerrainTile)palette[index].tile).PaintLayer;
    }
    private void DrawToolChoices()
    {
        WorkshopUI.Label(content,"지형 그리기",497,36,130,16,12).color=WorkshopUI.Mint;
        Choice("자동 타일",Tool.Auto,497,53); Choice("칠하기",Tool.Paint,563,53);
        Choice("지우개",Tool.Erase,497,71); Choice("스포이드",Tool.Sample,563,71);
        WorkshopUI.Label(content,"배치와 선택",497,90,130,16,12).color=WorkshopUI.Mint;
        Choice("선택",Tool.Select,497,107); Choice("소품",Tool.Prop,563,107);
        Choice("적",Tool.Enemy,497,125); Choice("출구",Tool.Door,563,125);
        Choice("시작점",Tool.Start,497,143); Choice("도장",Tool.Stamp,563,143);

        void Choice(string label, Tool choice, int x, int y)
        {
            Btn(label,x,y,62,17,()=>{
                var previous=tool;
                tool=choice; drawer=false; encounterOpen=false; notice="";
                if (tool==Tool.Auto) { stamp.Clear(); SyncPalette(); }
                else if(previous==Tool.Auto) SyncPalette();
                Draw(page);
            },tool==choice);
        }
    }
    private string MapHint() => tool switch
    {
        Tool.Select => "선택  ·  물체를 눌러 이동  /  빈 곳을 끌어 여러 개 선택",
        Tool.Auto => "자동 타일  ·  재료를 고르고 지도 위를 끌어 경계 잇기",
        Tool.Paint => "칠하기  ·  레이어와 타일을 고르고 지도 위를 끌어 칠하기",
        Tool.Erase => "지우개  ·  선택한 레이어에서 지울 곳을 끌기",
        Tool.Sample => "스포이드  ·  지도에서 타일을 눌러 같은 재료로 칠하기",
        Tool.Door => "출구  ·  방향을 고른 뒤 지도에서 연결할 위치 누르기",
        Tool.Enemy => "적 배치  ·  적을 고른 뒤 걸을 수 있는 바닥 누르기",
        Tool.Prop => "소품 배치  ·  소품을 고른 뒤 배치할 위치 누르기",
        Tool.Start => "시작점  ·  바닥을 누른 뒤 F6으로 그 위치에서 시험하기",
        Tool.Stamp when stamp.Count==0 => "도장  ·  복사할 타일 범위를 끌어 선택하기",
        _ => "도장  ·  지도에서 선택한 모양을 찍을 위치 누르기"
    };
    private void DrawMap()
    {
        viewport=WorkshopUI.Rect(content,"Map",7,54,WorkshopPreview.Width,WorkshopPreview.Height);
        var image=viewport.gameObject.AddComponent<RawImage>(); image.texture=preview.Frame;
        NameField();
        cursorCell=null;
        cursorLabel=WorkshopUI.Label(content,"",237,31,85,20);
        if(catalog.FindRoom(Doc.template).room.TryGetComponent<NormalRoomEvent>(out _))
            Btn("전투 설정",413,31,72,20,()=>{encounterOpen=!encounterOpen; drawer=false; Draw(page);},encounterOpen);
        if(floorCell.HasValue)
            Btn("층에 연결",324,31,87,20,()=>{
                Save(); roomFloor.Attach(floorCell.Value,Doc); roomFloor.Save();
                Say("이 방을 시험 층에 연결했습니다.");
            });
        Btn("저장",381,5,44,20,Save);
        Btn("비교",345,5,33,20,OpenComparison);
        if(ProjectContentStore.Authoring && !floorCell.HasValue && Doc.id!=Doc.template && catalog.FindRoom(Doc.template).inPool)
            Btn(ProjectRooms.IsRegistered(Doc) ? "등록 해제" : "던전 등록",325,31,82,20,()=>{
                if(ProjectRooms.IsRegistered(Doc))
                { ProjectRooms.Unregister(Doc); Draw(page); Say("일반 던전 후보에서 뺐습니다. 다음 방 생성부터 적용"); }
                else { Save(); ProjectRooms.Register(Doc); Draw(page); Say("일반 던전의 방 후보에 등록했습니다."); }
            });
        Btn("공유본",430,5,58,20,Export);
        Btn("플레이",493,5,83,20,Play,true);
        WorkshopUI.Frame(content,"Toolbox",491,36,143,291);
        DrawToolChoices();
        if(tool==Tool.Auto)
            WorkshopUI.Label(content,layerNames[layer]+" · 자동 연결",497,164,131,19);
        else if(tool is Tool.Paint or Tool.Erase or Tool.Sample or Tool.Stamp)
            Btn("레이어 · "+layerNames[layer],497,164,128,19,()=>{layer=(layer+1)%4;SyncPalette();stamp.Clear();drawer=false;Draw(page);}).name=layerNames[layer];
        else if(tool is Tool.Enemy or Tool.Prop)
            WorkshopUI.Label(content,"배치할 재료",497,164,128,19);
        switch(tool)
        {
            case Tool.Auto:
            case Tool.Paint:
            case Tool.Enemy:
            case Tool.Prop:
                string current=tool==Tool.Enemy ? enemies[enemyIndex].prefab.name :
                    tool==Tool.Prop ? props[propIndex].prefab.name : palette.Length>0 ? palette[materialIndex].Title : "재료 없음";
                var materialButton=Btn(current,497,185,128,20,()=>{paletteQuery="";palettePage=0;drawer=!drawer;Draw(page);});
                materialButton.interactable=tool!=Tool.Auto || palette.Length>0;
                var piece=tool==Tool.Enemy ? enemies[enemyIndex] : tool==Tool.Prop ? props[propIndex] : null;
                var sample=piece!=null ? piece.prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite :
                    palette.Length>0 ? palette[materialIndex].Sample : null;
                ShowMaterial(materialButton,sample,16,16,104,20);
                Btn("이전",497,207,62,18,()=>Cycle(-1)); Btn("다음",563,207,62,18,()=>Cycle(1));
                break;
            case Tool.Stamp:
                WorkshopUI.Label(content,stamp.Count==0 ? "끌어서 범위 선택" : stamp.Count+"칸 · 눌러 찍기",497,185,128,20);
                Btn("다시 고르기",497,207,62,18,()=>{stamp.Clear();drawer=false;Draw(page);});
                Btn("보관함",563,207,62,18,()=>{drawer=!drawer;palettePage=0;Draw(page);});
                break;
            case Tool.Door:
                WorkshopUI.Label(content,"출구 방향",497,164,128,19);
                for(int i=0;i<compass.Length;i++)
                {
                    int direction=i;
                    Btn(compass[i],497+i*33,184,30,24,()=>{dir=direction;Draw(page);},dir==i);
                }
                Btn("연결 미리보기",497,210,128,17,OpenPassage).interactable=preview.HasDoor(directions[dir]);
                break;
            default:
                string hint=tool switch {
                    Tool.Select=>"빈 곳을 끌어 묶음 선택",
                    Tool.Start=>"시작할 바닥을 선택",
                    Tool.Sample=>"지도에서 타일을 선택",
                    _=>"끌어서 타일 지우기" };
                WorkshopUI.Label(content,hint,497,183,129,19);
                if(tool==Tool.Select) Btn("선택한 배치물 수치",497,207,129,19,SelectedObject);
                break;
        }
        undoButton=Btn("실행 취소",497,231,62,20,()=>History(false));
        redoButton=Btn("다시 실행",563,231,62,20,()=>History(true));
        removeButton=Btn("선택 삭제",497,254,128,20,Remove);
        Btn("입구부터 플레이",497,277,128,18,()=>{start=null;preview.MarkStart(null);Play();});
        detail=WorkshopUI.Label(content,"",497,298,130,28);
        zoomOut=Btn("−",398,333,24,22,()=>ZoomMap(preview.Zoom-1));
        zoomOut.interactable=preview.Zoom>0;
        zoomIn=Btn("+",426,333,24,22,()=>ZoomMap(preview.Zoom+1));
        zoomIn.interactable=preview.Zoom<8;
        Btn("원위치",455,333,65,22,()=>{preview.Home();Draw(page);});
        Btn("전체 보기",525,333,105,22,()=>{preview.Overview();Draw(page);},preview.Zoom==0);
        UpdateDetail();
        if(drawer)
        {
            if(tool==Tool.Stamp) DrawStampLibrary(); else DrawPalette();
        }
        if(encounterOpen) DrawEncounter();
    }
    private void ZoomMap(int zoom)
    {
        if(zoom<=0) preview.Overview(); else preview.ZoomAt(Vector2.one * .5f,zoom);
        Draw(page);
    }
    private void Cycle(int delta)
    {
        int Wrap(int n,int length)=>(n+delta+length)%length;
        if(tool==Tool.Enemy) enemyIndex=Wrap(enemyIndex,enemies.Length);
        else if(tool==Tool.Prop) propIndex=Wrap(propIndex,props.Length);
        else if(palette.Length>0) SelectMaterial(Wrap(materialIndex,palette.Length));
        Draw(page);
    }
    private void UpdateDetail()
    {
        if(undoButton!=null) undoButton.interactable=WorkshopTrial.Undo.Count>0;
        if(redoButton!=null) redoButton.interactable=WorkshopTrial.Redo.Count>0;
        if(removeButton!=null) removeButton.interactable=selection.Count>0;
        if(detail!=null && preview!=null)
        {
            string target=selection.Count==1 ? preview.PieceName(selection.First()) :
                selection.Count>1 ? selection.Count+"개 선택" : "선택 없음";
            detail.text=(dirty ? "저장 필요" : "저장됨")+"  ·  "+(preview.Zoom==0 ? "전체" : preview.Zoom+"×")+"\n"+target;
        }
    }
    private void Recover(string snapshot = null) => WorkshopFiles.WriteAtomic(Recovery(Doc.id),snapshot ?? Doc.Serialize());
    private void FinishMapEdits()
    {
        applyPiece?.Invoke();
        if(nameField!=null && nameField.isFocused) nameField.DeactivateInputField();
        EndStroke();
    }
    private void Save()
    {
        FinishMapEdits();
        Doc.Validate(catalog);
        if(ProjectContentStore.Authoring)
        {
            string mine=Doc.Serialize();
            var baseline=projectBasis ?? throw new IOException("방을 다시 열어 저장 기준을 확인해 주세요.");
            projectBasis=ProjectRooms.Save(Doc,baseline,catalog,out var merged,roomChoices);
            pendingConflict=null; roomChoices.Clear();
            if(merged.Serialize()!=mine)
            {
                WorkshopTrial.Draft=merged;
                WorkshopTrial.Undo.Clear(); WorkshopTrial.Redo.Clear(); if(preview!=null) Rebuild();
            }
            if(File.Exists(Recovery(Doc.id))) File.Delete(Recovery(Doc.id));
            dirty=false; UpdateDetail();
            Say(Doc.id==Doc.template || ProjectRooms.IsRegistered(Doc) ? "DATA 저장됨 · 다음 방 생성부터 적용" : "DATA 저장됨 · 시험용 방");
            return;
        }
        var path=WorkshopFiles.FileFor(Doc.id);
        // The lock covers comparison and replacement when two creator processes share a workspace.
        Directory.CreateDirectory(WorkshopFiles.Folder);
        using(var guard=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            if ((File.Exists(path) ? File.ReadAllText(path) : null)!=persisted)
                throw new IOException("다른 편집본이 먼저 저장됐습니다. 공유본으로 내보내 두 버전을 보관하세요.");
            var json=Doc.Serialize(); WorkshopFiles.WriteAtomic(path,json); persisted=json;
            if(File.Exists(Recovery(Doc.id))) File.Delete(Recovery(Doc.id));
        }
        dirty=false; UpdateDetail(); Say("저장됨  ·  "+Doc.title);
    }
    private void Export()
    {
        EndStroke(); Doc.Validate(catalog);
        string path=Path.Combine(ShareFolder,Doc.id+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json");
        WorkshopFiles.WriteAtomic(path,Doc.Serialize()); Say("공유 폴더에 방 파일을 저장했습니다.");
    }
    private void Play()
    {
        EndStroke(); RememberMap();
        WorkshopTrial.View=preview.Center; WorkshopTrial.Zoom=preview.Zoom;
        WorkshopTrial.Play(start);
    }
    private void History(bool redo)
    {
        EndStroke();
        var from=redo ? WorkshopTrial.Redo : WorkshopTrial.Undo;
        var to=redo ? WorkshopTrial.Undo : WorkshopTrial.Redo;
        if(from.Count==0) return;
        string restored=from.Peek();
        var room=JsonUtility.FromJson<WorkshopRoom>(restored);
        var current=Doc.Serialize();
        WorkshopFiles.WriteAtomic(Recovery(room.id),restored);
        from.Pop(); to.Push(current); WorkshopTrial.Draft=room;
        dirty=true; Rebuild();
    }
    private void RememberView()
    {
        WorkshopTrial.View=page==Page.Passage ? passagePreviousCenter : preview.Center;
        WorkshopTrial.Zoom=page==Page.Passage ? passagePreviousZoom : preview.Zoom;
    }
    private void Rebuild()
    {
        RememberView();
        OpenMap(false,palette.Length>0 ? palette[materialIndex].id : null);
    }
    private void EndStroke()
    {
        if(before==null) return;
        string old=before; before=null; lastCell=null; moving=false;
        sampling=false; selectingArea=false; preview?.HideArea();
        var updated=Doc.Serialize();
        if(old==updated) return;
        WorkshopTrial.Undo.Push(old); WorkshopTrial.Redo.Clear(); dirty=true;
        Recover(updated); UpdateDetail();
    }
    private void Remove()
    {
        if(selection.Count==0) return;
        before=Doc.Serialize();
        foreach(string key in selection) EditPiece(key).removed=true;
        selection.Clear(); EndStroke(); Rebuild();
    }
    private WorkshopRoom.Piece EditPiece(string key)
    {
        var p=Doc.pieces.Find(e=>e.id==key);
        if(p==null)
        {
            p=new WorkshopRoom.Piece {id=key,source=key};
            Doc.pieces.Add(p);
        }
        return p;
    }
    private void SaveAllDocuments()
    {
        if(!ProjectContentStore.Authoring)return;
        CommitPendingInputs();
        if(page==Page.Map && Doc!=null) FinishMapEdits();
        if(Doc!=null && dirty)
        {
            try{Save();}
            catch(ProjectContentStore.Conflict conflict)
            {
                OpenMap(false);pendingConflict=conflict;Draw(page);Say("방 변경을 합친 뒤 모두 저장을 다시 눌러 주세요.");return;
            }
        }
        if(floorSettings!=null)
        {
            if(routeDraft!=null) floorSettings.ApplyTutorialOrder(routeDraft,catalog);
            floorSettings.Save(); routeDraft=null;
        }
        // 입력만 하고 '적용'을 안 누른 값(ProjectContent seed가 이 경로를 쓴다). 방·층을 저장한 뒤에 두어 잘못된 숫자 하나가 앞 단계를 막지 않게 한다.
        if(dataField!=null) AcceptParameter();
        var skipped=DraftStore.SaveAll();
        foreach(var entry in pages) if(entry.Title!=null && entry.Dirty()) entry.Save();
        Draw(page);
        Say(skipped.Count==0 ? "열린 제작 문서를 모두 저장했습니다." : "이 판에 없는 항목 때문에 저장하지 못한 문서: "+string.Join(", ",skipped));
    }
    private void Update()
    {
        var keys=Keyboard.current;
        if(pendingConfirm!=null){ if(keys!=null && keys.escapeKey.wasPressedThisFrame){ pendingConfirm=null; Draw(page); } return; }   // 미저장 확인 모달
        if(areaMenuOpen){ if(keys!=null && keys.escapeKey.wasPressedThisFrame){ areaMenuOpen=false; Draw(page); } return; }        // 영역 전환 목록: 지도 입력도 멈춘다
        var entry=pages[(int)page];
        // 키보드가 없어도(검사가 가상 키보드를 뺀 뒤) 지도 포인터 입력은 돌아야 한다.
        if(keys==null){ entry.Keys?.Invoke(); return; }
        bool ctrl=keys.ctrlKey.isPressed, shift=keys.shiftKey.isPressed;
        if(ctrl && shift && keys.sKey.wasPressedThisFrame && ProjectContentStore.Authoring){Safe(SaveAllDocuments);return;}
        if(page!=Page.Map)
        {
            if(ctrl && !shift && keys.sKey.wasPressedThisFrame && entry.Save!=null){CommitPendingInputs();Safe(entry.Save);return;}
            if(keys.escapeKey.wasPressedThisFrame)                 // 맨 위 팝업부터 닫는다
            {
                if(WorkshopUI.CloseOverlay()) return;
                if(unsavedOpen){ unsavedOpen=false; Draw(page); return; }
            }
            if(!InputFocused())
            {
                var draft=entry.Draft?.Invoke();
                bool redo=keys.yKey.wasPressedThisFrame || shift && keys.zKey.wasPressedThisFrame;
                if(draft!=null && ctrl && (keys.zKey.wasPressedThisFrame || keys.yKey.wasPressedThisFrame))
                { if(redo ? DraftStore.Redo(draft) : DraftStore.Undo(draft)) Draw(page); return; }
                if(ctrl && keys.fKey.wasPressedThisFrame){OpenFind();return;}
                if(keys.f6Key.wasPressedThisFrame && entry.Trial!=null){Safe(entry.Trial);return;}
            }
        }
        entry.Keys?.Invoke();
    }
    // 선택 객체는 Draw가 방금 파괴한 버튼일 수 있다. is 패턴과 ?.는 Unity의 가짜 null을 거르지 못하므로 != null로 확인한다.
    private static TMP_InputField FocusedInput()
    {
        var go=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        return go!=null && go.TryGetComponent<TMP_InputField>(out var field) && field.isFocused ? field : null;
    }
    private static bool InputFocused()=>FocusedInput()!=null;
    private static void CommitPendingInputs()=>FocusedInput()?.DeactivateInputField();   // onEndEdit로 인라인 입력을 확정한다
    // 지도 화면의 키·포인터 입력. 지도 페이지의 Keys 칸이다.
    private void UpdateMap()
    {
        if(preview==null || page!=Page.Map || pendingConflict!=null || UnityNote.SceneLoader.IsLoading) return;
        var kb=Keyboard.current;
        if(kb!=null && kb.ctrlKey.isPressed && kb.sKey.wasPressedThisFrame) {Safe(Save);return;}
        var focused=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        if(focused!=null && focused.TryGetComponent<TMP_InputField>(out var editing) && editing.isFocused) return;
        if(kb!=null && kb.escapeKey.wasPressedThisFrame && (drawer || encounterOpen || applyPiece!=null))
        {
            drawer=false; encounterOpen=false; Draw(page); return;
        }
        if(drawer || encounterOpen || applyPiece!=null) return;
        if(kb!=null)
        {
            bool ctrl=kb.ctrlKey.isPressed;
            if(ctrl && kb.zKey.wasPressedThisFrame) {Safe(()=>History(kb.shiftKey.isPressed));return;}
            if(ctrl && kb.yKey.wasPressedThisFrame) {Safe(()=>History(true));return;}
            if(tool==Tool.Select && kb.deleteKey.wasPressedThisFrame && before==null) {Safe(Remove);return;}
            if(kb.f6Key.wasPressedThisFrame) {Safe(Play);return;}
        }
        var mouse=Mouse.current; if(mouse==null) return;
        var screen=PixelViewport.PointerForCanvas(canvas,mouse.position.ReadValue());
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,screen,PixelViewport.CanvasCamera(canvas),out var pt);
        var uv=Rect.PointToNormalized(viewport.rect,pt);
        bool over=viewport.rect.Contains(pt);

        // A captured box or object drag can end on the edge, even after the pointer leaves the canvas.
        bool captured=selectingArea || sampling || moving;
        if(before!=null && mouse.leftButton.wasReleasedThisFrame)
        {
            if(captured) Safe(()=>ApplyPointer(preview.Point(uv),false));
            Safe(()=>FinishArea(over || captured,preview.Point(uv)));
            Safe(EndStroke);
        }
        else if(!over && !captured) Safe(EndStroke);
        if(over)
        {
            if(mouse.middleButton.isPressed && !mouse.middleButton.wasPressedThisFrame)
                preview.Pan(Rect.PointToNormalized(viewport.rect,lastPointer),uv);
            float scroll=mouse.scroll.ReadValue().y;
            if(scroll!=0)
            {
                if(scroll<0 && preview.Zoom<=1) preview.Overview();
                else preview.ZoomAt(uv,preview.Zoom+(scroll>0 ? 1 : -1));
                zoomOut.interactable=preview.Zoom>0; zoomIn.interactable=preview.Zoom<8;
                UpdateDetail();
            }
        }
        Vector3 world=preview.Point(uv);
        var tilemap=preview.Map(layer);
        preview.Hover(tilemap,world,over && tool!=Tool.Select,
            tool==Tool.Erase ? new Color32(255,104,111,144) : new Color32(163,244,195,136));
        if(over && tilemap!=null)
        {
            var cell=tilemap.WorldToCell(world);
            if(!cursorCell.HasValue || cursorCell.Value!=cell)
            {
                cursorLabel.text="X "+cell.x+"  Y "+cell.y; cursorCell=cell;
            }
        }
        else if(cursorCell.HasValue){cursorCell=null;cursorLabel.text="";}
        if((over || captured) && !mouse.middleButton.isPressed)
        {
            if(mouse.leftButton.wasPressedThisFrame) {before=Doc.Serialize();lastCell=null;}
            if(before!=null && mouse.leftButton.isPressed)
                Safe(()=>ApplyPointer(world,mouse.leftButton.wasPressedThisFrame));
        }
        lastPointer=pt;
    }
    private void ApplyPointer(Vector3 world,bool first)
    {
        var map=preview.Map(layer);

        Vector3 local=preview.Local(world); local.z=0;
        local.x=Mathf.Round(local.x*32)/32; local.y=Mathf.Round(local.y*32)/32;
        if(tool==Tool.Select) { SelectPointer(world,local,first); return; }
        if(map==null) {Say("이 원본에는 해당 레이어가 없습니다.");return;}
        var cell=(Vector2Int)map.WorldToCell(world);
        if(tool is Tool.Paint or Tool.Erase or Tool.Auto)
        {
            if(tool!=Tool.Erase && palette.Length==0) return;
            if(lastCell==cell || Mathf.Abs(cell.x)>1024 || Mathf.Abs(cell.y)>1024) return;
            var from=lastCell ?? cell;
            int steps=Mathf.Max(1,Mathf.Abs(cell.x-from.x),Mathf.Abs(cell.y-from.y));
            var id=tool==Tool.Erase ? "" : palette[materialIndex].id;
            var tile=tool==Tool.Erase ? null : palette[materialIndex].tile;
            for(int i=1;i<=steps;i++)
            {
                var at=Vector2Int.RoundToInt(Vector2.Lerp(from,cell,i/(float)steps));
                if(map.GetTile((Vector3Int)at)==tile) continue;
                Doc.Paint(layer,at,id); map.SetTile((Vector3Int)at,tile);
                if (layer<=1) WorkshopTerrainTile.RefreshNeighbors(map,(Vector3Int)at);
            }
            lastCell=cell; return;
        }
        if(tool==Tool.Stamp) { StampPointer(map,cell,first); return; }
        if(!first) return;
        if(tool==Tool.Sample)
        {
            var tile=map.GetTile((Vector3Int)cell);
            if(tile!=null)
            {
                tool=tile is WorkshopTerrainTile ? Tool.Auto : Tool.Paint;
                SyncPalette();
                SelectMaterial(Mathf.Max(0,Array.FindIndex(palette,t=>t.tile==tile)));
                Draw(page);
            }
        }
        else if(tool==Tool.Door) {Doc.SetDoor(directions[dir],local);preview.MarkDoor(directions[dir],local);}
        else if(tool==Tool.Start)
        {
            if(!preview.Walkable(world))
            {Say("시작점은 걸을 수 있는 바닥에 놓으세요.");return;}
            start=local; preview.MarkStart(start); Say("시험 시작점 지정  ·  F6 플레이");
        }
        else
        {
            var entry=tool==Tool.Enemy ? enemies[enemyIndex] : props[propIndex];
            if(entry.enemy && catalog.FindRoom(Doc.template).room.GetComponent<NormalRoomEvent>()==null)
            {Say("고정 적은 일반 전투가 있는 방에 배치할 수 있습니다.");return;}
            if(entry.enemy && !preview.Walkable(world))
            {Say("적을 배치할 바닥이 필요합니다.");return;}
            string selected=Guid.NewGuid().ToString("N");
            selection.Clear();selection.Add(selected);
            Doc.pieces.Add(new WorkshopRoom.Piece {id=selected,catalog=entry.id,local=local});
            EndStroke(); Rebuild();
        }
    }
}
