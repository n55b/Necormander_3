using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow
{
    private readonly Dictionary<string,Texture2D> thumbnails = new();
    private int palettePage;
    private bool drawer;
    private string paletteQuery="";
    private string roomQuery="";

    private void DrawDesk(List<(string label,string meta,string key,WorkshopCatalog.Template template,Action run)> rooms,string previousControl)
    {
        bool project = ProjectContentStore.Authoring;
        WorkshopUI.Frame(content, "Room actions", 8, 38, 223, 197);
        WorkshopUI.Label(content, "방 만들기", 20, 47, 200, 26, 15);
        WorkshopUI.Label(content, "지형을 그리고 적과 출구를 배치합니다.", 20, 76, 204, 20);
        var create = Btn("새 방 만들기", 20, 103, 199, 31, () => New(catalog.DefaultRoom), true);
        PixelText.Configure(create.GetComponentInChildren<TMP_Text>(), 12);
        var focus = create;
        if (Doc != null)
            focus = Btn("편집하던 방 계속", 20, 141, 199, 24, () => OpenMap(false));
        int next = Doc == null ? 145 : 170;
        if (project)
            Btn("게임 방 편집", 20, next, 199, 22, () => Navigate(Page.Project));
        Btn("기존 방 복제", 20, next + (project ? 27 : 0), 199, 22, () => { legacyTools=false; Navigate(Page.Legacy); });

        WorkshopUI.Frame(content, "Saved rooms", 239, 38, 393, 197);
        WorkshopUI.Label(content, "내 방", 251, 46, 70, 26, 15);
        var search=Input("방 이름 검색",roomQuery,323,48,140,21);
        search.placeholder=WorkshopUI.Label(search.transform,"방 이름 검색",5,0,130,21);
        search.ForceLabelUpdate();
        void SearchRooms()
        {
            roomQuery=search.text.Trim();
            listPage=0;
            Draw(page);
        }
        search.onSubmit.AddListener(_=>SearchRooms());
        Btn("검색",468,48,56,21,SearchRooms);
        if (project) Btn("모두 저장", 531, 48, 89, 21, SaveAllDocuments);
        if (roomQuery.Length>0)
        {
            rooms=rooms.FindAll(r=>r.label?.IndexOf(roomQuery,StringComparison.OrdinalIgnoreCase)>=0);
            Btn("검색 해제",410,205,85,22,()=>{roomQuery="";listPage=0;Draw(page);});
        }
        const int perPage = 3;
        int pages = Mathf.Max(1, (rooms.Count + perPage - 1) / perPage);
        listPage = Mathf.Clamp(listPage, 0, pages - 1);
        if (rooms.Count == 0)
        {
            WorkshopUI.Label(content, roomQuery.Length>0 ? "검색한 이름의 방이 없습니다." : "아직 저장한 방이 없습니다.", 253, 101, 353, 24, 12);
            if (roomQuery.Length==0)
                WorkshopUI.Label(content, "왼쪽에서 새 방을 만든 뒤 저장하면 여기에 나타납니다.", 253, 131, 359, 22);
        }
        for (int i=listPage*perPage; i<Math.Min(rooms.Count,(listPage+1)*perPage); i++)
        {
            var row=rooms[i];
            int y=76+(i%perPage)*42;
            var open=Btn(row.label,251,y,312,39,row.run);
            open.name=row.meta+" "+row.key;
            var title=open.GetComponentInChildren<TMP_Text>();
            title.rectTransform.anchoredPosition=new Vector2(41,-1);
            title.rectTransform.sizeDelta=new Vector2(266,20);
            title.alignment=TextAlignmentOptions.MidlineLeft;
            PixelText.Configure(title,12);
            WorkshopUI.Label(open.transform,row.meta,41,20,266,17).color=WorkshopUI.Mint;
            RoomThumbnail(row.template,255,y+4,31);
            string pin="Workshop.Pinned."+row.key;
            bool pinned=PlayerPrefs.GetInt(pin)!=0;
            Btn(pinned ? "고정됨" : "고정",568,y+8,52,23,()=>{
                PlayerPrefs.SetInt(pin,pinned ? 0 : 1); Draw(page);
            },pinned).name=pin;
        }
        if (pages>1)
        {
            Btn("이전",251,205,55,22,()=>{listPage--;Draw(page);}).interactable=listPage>0;
            WorkshopUI.Label(content,(listPage+1)+" / "+pages,313,205,62,22);
            Btn("다음",565,205,55,22,()=>{listPage++;Draw(page);}).interactable=listPage+1<pages;
        }
        else if (rooms.Count>0) WorkshopUI.Label(content,"방 "+rooms.Count+"개",251,205,100,22);
        WorkshopUI.Frame(content,"Content tools",8,243,624,84);
        WorkshopUI.Label(content,project ? "게임 콘텐츠 편집" : "함께 작업하기",20,249,376,22,12);
        if (project)
        {
            Btn("플레이어 수치",20,279,111,30,()=>Navigate(Page.Balance));
            Btn("콘텐츠 수치",137,279,111,30,OpenParameters);
            Btn("대사 편집",254,279,111,30,()=>Navigate(Page.Dialogue));
            Btn("UI·보상 번역",371,279,127,30,()=>Navigate(Page.Translations));
            Btn("아트 원본",504,279,116,30,()=>Navigate(Page.Art));
        }
        else
        {
            Btn("동료의 방 가져오기",20,279,190,30,()=>Navigate(Page.Shared));
            Btn("시험 층 설정",218,279,190,30,()=>Navigate(Page.Game));
            Btn("기존 도구 보기",416,279,204,30,()=>{legacyTools=true;Navigate(Page.Legacy);});
        }
        if (previousControl=="방 이름 검색" || previousControl=="검색 해제") previousControl="검색";
        if (previousControl!=null)
        {
            var previous=content.Find(previousControl)?.GetComponent<Button>();
            if (previous!=null && previous.interactable) focus=previous;
        }
        focus.Select();
    }

    private void RoomThumbnail(WorkshopCatalog.Template room,int x,int y,int size=48)
    {
        if (room==null) return;
        if (!thumbnails.TryGetValue(room.id,out var texture))
        {
            var ground=WorkshopCatalog.Map(room.room.transform,0);
            if (ground==null) return;
            var wall=WorkshopCatalog.Map(room.room.transform,1);
            var b=ground.cellBounds;
            const int side=48;
            float scale=Mathf.Max(b.size.x,b.size.y,1)/(float)(side-4);
            var pixels=new Color32[side*side];
            for (int v=0;v<side;v++)
            for (int u=0;u<side;u++)
            {
                var cell=Vector3Int.FloorToInt(b.center+new Vector3((u-23.5f)*scale,(v-23.5f)*scale,0));
                cell.z=0;
                pixels[v*side+u]=wall!=null && wall.HasTile(cell) ? new Color32(153,128,154,255) :
                    ground.HasTile(cell) ? new Color32(99,160,146,255) : new Color32(37,34,47,255);
            }
            foreach(var anchor in room.room.GetComponentsInChildren<RoomAnchor>(true))
            {
                Vector3 p=ground.WorldToCell(anchor.transform.position)-b.center;
                int u=Mathf.Clamp(Mathf.RoundToInt(23.5f+p.x/scale),1,side-2);
                int v=Mathf.Clamp(Mathf.RoundToInt(23.5f+p.y/scale),1,side-2);
                for(int i=-1;i<=1;i++) for(int j=-1;j<=1;j++) pixels[(v+i)*side+u+j]=WorkshopUI.Paper;
            }
            texture=new Texture2D(side,side,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
            texture.SetPixels32(pixels); texture.Apply(false,true); thumbnails.Add(room.id,texture);
        }
        var image=WorkshopUI.Rect(content,"Room footprint",x,y,size,size).gameObject.AddComponent<RawImage>();
        image.texture=texture; image.raycastTarget=false;
    }

    private void DrawPalette()
    {
        int total=tool==Tool.Enemy ? enemies.Length : tool==Tool.Prop ? props.Length : palette.Length;
        string Title(int i) => tool==Tool.Enemy ? enemies[i].prefab.name : tool==Tool.Prop ? props[i].prefab.name : palette[i].Title;
        var matches=new List<int>();
        for(int i=0;i<total;i++)
            if(Title(i).IndexOf(paletteQuery,StringComparison.OrdinalIgnoreCase)>=0) matches.Add(i);
        int count=matches.Count;
        palettePage=Mathf.Clamp(palettePage,0,Mathf.Max(0,(count-1)/12));
        WorkshopUI.Frame(content,"Materials",7,54,WorkshopPreview.Width,WorkshopPreview.Height);
        WorkshopUI.Label(content,tool==Tool.Auto ? "자동 지형" : "재료 "+count+"개",16,60,100,22,12);
        var search=Input("재료 검색어",paletteQuery,116,60,235,22);
        search.GetComponent<Image>().color=WorkshopUI.ButtonColor;
        void Filter() {paletteQuery=search.text.Trim();palettePage=0;Draw(page);}
        search.onSubmit.AddListener(_=>Filter());
        Btn("검색",355,60,52,22,Filter);
        Btn("전체",412,60,61,22,()=>{paletteQuery="";palettePage=0;Draw(page);});
        if(count==0) WorkshopUI.Label(content,"일치하는 재료가 없습니다.",20,115,445,26,12);
        for(int slot=palettePage*12;slot<Math.Min(count,(palettePage+1)*12);slot++)
        {
            int index=matches[slot], x=15+slot%3*155, y=87+slot%12/3*47;
            var piece=tool==Tool.Enemy ? enemies[index] : tool==Tool.Prop ? props[index] : null;
            var sprite=piece!=null ? piece.prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite :
                palette[index].Sample;
            string title=Title(index);
            var button=Btn(title,x,y,151,43,()=>{
                if (tool==Tool.Enemy) enemyIndex=index;
                else if(tool==Tool.Prop) propIndex=index;
                else {SelectMaterial(index);if(tool!=Tool.Auto)tool=Tool.Paint;}
                drawer=false; Draw(page);
            }, index==(tool==Tool.Enemy ? enemyIndex : tool==Tool.Prop ? propIndex : materialIndex));
            ShowMaterial(button,sprite,33,37,109,43);
        }
        Btn("이전 재료",16,292,80,22,()=>{palettePage--;Draw(page);}).interactable=palettePage>0;
        Btn("다음 재료",100,292,80,22,()=>{palettePage++;Draw(page);}).interactable=(palettePage+1)*12<count;
        WorkshopUI.Label(content,(palettePage+1)+" / "+Mathf.Max(1,(count+11)/12),202,292,85,22);
        Btn("닫기",392,292,80,22,()=>{drawer=false;Draw(page);});
    }

    private static void ShowMaterial(Button button,Sprite sprite,int width,int height,int labelWidth,int rowHeight)
    {
        if (sprite==null) return;
        var label=button.GetComponentInChildren<TMP_Text>();
        label.rectTransform.anchoredPosition=new Vector2(width+6,0);
        label.rectTransform.sizeDelta=new Vector2(labelWidth,rowHeight);
        label.alignment=TextAlignmentOptions.MidlineLeft;
        var icon=WorkshopUI.Rect(button.transform,"Sample",3,(rowHeight-height)/2,width,height).gameObject.AddComponent<Image>();
        icon.sprite=sprite; icon.preserveAspect=true; icon.raycastTarget=false;
    }

    private TMP_InputField nameField;
    private void NameField()
    {
        WorkshopUI.Label(content,"방 이름",9,31,41,21);
        var field=WorkshopUI.Frame(content,"방 이름",51,31,183,21);
        nameField=field.gameObject.AddComponent<TMP_InputField>();
        var label=WorkshopUI.Label(field.transform,"",5,0,173,21,12);
        label.GetComponent<PixelText>().UseFaceResolver(catalog.FontForInput);
        nameField.textViewport=(RectTransform)field.transform;
        nameField.textComponent=label;
        nameField.characterLimit=64; nameField.richText=false;
        nameField.SetTextWithoutNotify(Doc.title);
        nameField.onEndEdit.AddListener(value=>Safe(()=>{
            string next=value.Trim();
            if(next.Length==0) {nameField.SetTextWithoutNotify(Doc.title);return;}
            if(next==Doc.title) return;
            before=Doc.Serialize(); Doc.title=next; EndStroke();
        }));
    }
}
