using System;
using System.Linq;
using UnityEngine;

public sealed partial class RoomWorkshopWindow
{
    private WorkshopFloor floorSettings;
    private bool legacyTools;
    private Vector2Int? floorCell;
    private WorkshopFloor roomFloor;

    private System.Collections.Generic.List<string> routeDraft;
    private int routeIndex;

    private void DrawRoute()
    {
        WorkshopUI.Label(content,"튜토리얼 방 순서",12,37,355,22,15);
        int first = routeIndex / 7 * 7;
        for (int i = first; i < Mathf.Min(first+7,routeDraft.Count); i++)
        {
            int index = i;
            var template = catalog.FindRoom(routeDraft[i]);
            Btn((i+1)+"   "+template.title,12,68+(i-first)*33,280,28,
                ()=>{routeIndex=index;Draw(page);},routeIndex==i);
        }
        if (routeDraft.Count > 7)
        {
            Btn("이전",12,303,66,22,()=>{routeIndex=first-1;Draw(page);}).interactable=first>0;
            Btn("다음",84,303,66,22,()=>{routeIndex=first+7;Draw(page);}).interactable=first+7<routeDraft.Count;
        }
        void Move(int step)
        {
            int next = routeIndex+step;
            (routeDraft[next],routeDraft[routeIndex])=(routeDraft[routeIndex],routeDraft[next]);
            routeIndex=next; Draw(page);
        }
        WorkshopUI.Label(content,"시작 방과 마지막 출구는 고정됩니다.",312,72,317,22);
        Btn("앞으로 이동",312,107,135,26,()=>Move(-1)).interactable=routeIndex>1 && routeIndex<routeDraft.Count-1;
        Btn("뒤로 이동",455,107,155,26,()=>Move(1)).interactable=routeIndex>0 && routeIndex<routeDraft.Count-2;
        Btn("기본 순서",312,155,135,24,()=>{
            routeDraft=new WorkshopFloor().TutorialOrder(catalog);routeIndex=0;Draw(page);
        });
        var selected=catalog.FindRoom(routeDraft[routeIndex]);
        var doors=selected.room.GetComponentsInChildren<RoomAnchor>(true)
            .Select(anchor=>Array.IndexOf(directions,anchor.direction)).Where(i=>i>=0)
            .Distinct().OrderBy(i=>i).Select(i=>compass[i]);
        WorkshopUI.Label(content,selected.title,312,196,298,24,12);
        WorkshopUI.Label(content,"문 방향  ·  "+string.Join(" / ",doors),312,220,298,20);
        Btn("취소",312,259,95,26,()=>{routeDraft=null;Draw(page);});
        Btn("순서 적용",416,259,194,26,()=>{
            floorSettings.ApplyTutorialOrder(routeDraft,catalog);
            floorSettings.Save();routeDraft=null;Draw(page);Say("출구 연결을 확인하고 순서를 저장했습니다.");
        },true);
    }

    private void DrawFloor()
    {
        if (floorSettings == null)
        {
            try { floorSettings = WorkshopFloor.Load(); }
            catch (Exception e) { Say(e.Message); floorSettings = new WorkshopFloor(); }
        }
        if (routeDraft != null) { DrawRoute(); return; }
        WorkshopUI.Label(content,"게임 설정  /  시험할 무대",12,35,400,22,12);
        var presets = new[] {"던전","엘리트","튜토리얼","마을"};
        for (int i=0; i<presets.Length; i++)
        {
            var scenario=(WorkshopFloor.Scenario)i;
            Btn(presets[i],12+i*94,65,90,25,()=>{floorSettings.SelectStage(scenario,floorSettings.floor); Draw(page);},floorSettings.scenario==scenario);
        }
        bool dungeon=floorSettings.scenario==WorkshopFloor.Scenario.Dungeon;
        bool village=floorSettings.scenario==WorkshopFloor.Scenario.Village;
        var baseline=catalog.generation;
        int floor=floorSettings.FloorNumber;
        int y=103;
        if (!village)
        {
            if (floor>0)
                Stepper("층", floor.ToString(),ref y,delta=>floorSettings.SelectStage(floorSettings.scenario,Mathf.Clamp(floor+delta,1,99)));
            Stepper("배치 시드",floorSettings.seed.ToString(),ref y,delta=>floorSettings.seed=unchecked(floorSettings.seed+delta));
            if(dungeon)
            {
                int total = floorSettings.rooms<0 ? baseline.GetRoomCountsForFloor(floor).total : floorSettings.rooms;
                Stepper("목표 방 수",total.ToString(),ref y,delta=>floorSettings.rooms=Mathf.Clamp(total+delta,2,40));
            }
            int waves=floorSettings.waves<0 ? baseline.GetWavesForFloor(floor) : floorSettings.waves;
            if(dungeon || floorSettings.scenario==WorkshopFloor.Scenario.Tutorial)
                Stepper("일반 전투 웨이브",waves.ToString(),ref y,delta=>floorSettings.waves=Mathf.Clamp(waves+delta,1,8));
        }
        WorkshopUI.Frame(content,"Summary",359,103,268,151);
        WorkshopUI.Label(content,presets[(int)floorSettings.scenario],371,113,249,20,15);
        string summary=floorSettings.scenario switch {
            WorkshopFloor.Scenario.Village=>"마을 입구에서 시작",
            WorkshopFloor.Scenario.Tutorial=>floorSettings.TutorialOrder(catalog).Count+"개 방을 순서대로 플레이",
            WorkshopFloor.Scenario.Elite=>"스폰 방과 엘리트 아레나",
            _=>baseline.GetBossForFloor(floor)!=null ? "보스가 등장하는 층" : "시드로 생성하는 던전" };
        WorkshopUI.Label(content,summary,371,135,249,18);
        if(!village)
        {
            try { Graph(floorSettings.Preview(catalog,out _)); }
            catch(System.IO.InvalidDataException e) { Say(e.Message); }
        }
        else WorkshopUI.Label(content,"마을 편집에서 NPC와 조명을 수정합니다.",371,182,249,27);
        if (floorSettings.scenario==WorkshopFloor.Scenario.Tutorial)
            Btn("방 순서 편집",12,205,156,26,()=>{routeDraft=floorSettings.TutorialOrder(catalog);routeIndex=0;Draw(page);});
        if (!village) Btn("층 기본값 복원",12,265,132,24,()=>{floorSettings.rooms=-1;floorSettings.waves=-1;Draw(page);});
        Btn("설정 저장",385,265,100,24,()=>{floorSettings.Save();Say("게임 설정을 저장했습니다.");});
        Btn("무대 플레이",490,265,137,24,()=>{
            floorSettings.Save(false); WorkshopTrial.PlayFloor(floorSettings);
        },true);
        if(Doc!=null) Btn("방으로",345,5,60,20,()=>OpenMap(false));
    }
    private void Graph(DungeonLayout layout)
    {
        int left=layout.Rooms.Min(r=>r.Cell.x),right=layout.Rooms.Max(r=>r.Cell.x);
        int bottom=layout.Rooms.Min(r=>r.Cell.y),top=layout.Rooms.Max(r=>r.Cell.y);
        int spacing=Mathf.Min(24,Mathf.Min(248/(1+right-left),90/(1+top-bottom)));
        var center=new Vector2Int((988-(right+left)*spacing)/2,(403+(top+bottom)*spacing)/2);
        Vector2Int Point(Vector2Int cell)=>center+new Vector2Int(cell.x,-cell.y)*spacing;
        foreach(var link in layout.Doors)
        {
            var a=Point(layout.Rooms[link.From].Cell); var b=Point(layout.Rooms[link.To].Cell);
            WorkshopUI.Box(content,"Linked exits",Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),
                Mathf.Max(2,Mathf.Abs(a.x-b.x)),Mathf.Max(2,Mathf.Abs(a.y-b.y)),WorkshopUI.ButtonColor);
        }
        int size=Mathf.Clamp(spacing-4,5,19);
        foreach(var room in layout.Rooms)
        {
            var pt=Point(room.Cell);
            string mark=room.Type switch {RoomType.Spawn=>room.Cell==Vector2Int.zero ? "S" : "+",RoomType.Normal=>"·",RoomType.Boss=>"B",
                RoomType.Reward=>"★",RoomType.Shop or RoomType.EnhanceShop=>"$",RoomType.Elite=>"E",_=>"+"};
            var template=System.Array.Find(catalog.rooms,r=>r.room.gameObject==room.Prefab);
            Btn(mark,pt.x-size/2,pt.y-size/2,size,size,()=>{
                if(template==null) return;
                var slot=floorSettings.At(room.Cell,template.id);
                if(slot!=null) Begin(WorkshopFiles.Read(WorkshopFiles.FileFor(slot.document),catalog),false);
                else New(template);
                floorCell=room.Cell; roomFloor=floorSettings;
                Draw(page);
            },room.Cell==Vector2Int.zero).name="방 "+room.Cell.x+","+room.Cell.y;
        }
    }
    private void Stepper(string name,string value,ref int y,Action<int> change)
    {
        var row = WorkshopUI.Frame(content,name,12,y,334,28).rectTransform;
        WorkshopUI.Label(row,name,9,0,145,28);
        WorkshopUI.Label(row,value,161,0,73,28,12).name = "값";
        WorkshopUI.Button(row,"−",241,3,40,22,()=>Safe(()=>{change(-1);Draw(page);}));
        WorkshopUI.Button(row,"+",286,3,40,22,()=>Safe(()=>{change(1);Draw(page);}));
        y+=32;
    }
    private void DrawLegacyTools()
    {
        WorkshopUI.Label(content,"팀 공용 원본",17,76,602,22,12);
        string[] titles={"방 배치 씬","방 후보 목록","층 생성 설정","몬스터 편집기","아이템 편집기","장비 편집기","소환수 관리","사운드 테스트 툴"};
        string[] paths={"Assets/Scenes/MapDesign.unity","Assets/SOData/Map/RoomPrefabData.asset","Assets/SOData/Map/MapGenerationData.asset",
            "Tools/기획/몬스터 편집기","Tools/기획/아이템 편집기","Tools/기획/장비 편집기","Tools/기획/소환수 관리","Tools/기획/사운드 테스트 툴"};
        for(int i=0;i<titles.Length;i++)
        {
            int index=i; int x=17+(i%2)*308, y=105+(i/2)*40;
            Btn(titles[i],x,y,297,33,()=>{
#if UNITY_EDITOR
                if(index<3) {
                    var asset=UnityEditor.AssetDatabase.LoadMainAssetAtPath(paths[index]);
                    UnityEditor.Selection.activeObject=asset; UnityEditor.EditorGUIUtility.PingObject(asset);
                }
                else UnityEditor.EditorApplication.ExecuteMenuItem(paths[index]);
#endif
                Say(paths[index]);
            });
        }
        WorkshopUI.Label(content,"Unity에서 원본을 선택하거나 기존 편집기를 열 수 있습니다.",17,282,608,20);
    }
}
