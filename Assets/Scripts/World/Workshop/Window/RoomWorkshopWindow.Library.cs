#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.Linq;
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

    // The Windows creator edits DATA content only. Rooms are prefabs, so they are made inside the Unity editor.
    private const string RoomsNeedEditor = "방 편집은 Unity 에디터의 레벨 제작실에서만 할 수 있습니다.";

    private void DrawDesk(string previousControl)
    {
        bool project = ProjectContentStore.Authoring;
        WorkshopUI.Frame(content, "Rooms", 8, 38, 624, 197);
        WorkshopUI.Label(content, "방 편집", 20, 47, 300, 26, 15);
        WorkshopUI.Label(content, RoomsNeedEditor, 20, 80, 600, 22);
        WorkshopUI.Label(content, "이 제작 도구에서는 게임 설정, 수치, 대사, 번역, 아트를 편집합니다.", 20, 104, 600, 22).color = WorkshopUI.Mint;
        if (!project)
            WorkshopUI.Label(content, "먼저 위의 프로젝트 연결로 체크아웃 폴더를 연결해 주세요.", 20, 140, 600, 22);

        WorkshopUI.Frame(content, "Content tools", 8, 243, 624, 84);
        WorkshopUI.Label(content, "게임 콘텐츠 편집", 20, 249, 376, 22, 12);
        Button focus = null;
        if (project)
        {
            Btn("모두 저장", 531, 249, 89, 21, SaveAllDocuments);
            focus = Btn("플레이어 수치", 20, 279, 111, 30, () => Navigate(Page.Balance));
            Btn("콘텐츠 수치", 137, 279, 111, 30, OpenParameters);
            Btn("대사 편집", 254, 279, 111, 30, () => Navigate(Page.Dialogue));
            Btn("UI·보상 번역", 371, 279, 127, 30, () => Navigate(Page.Translations));
            Btn("아트 원본", 504, 279, 116, 30, () => Navigate(Page.Art));
        }
        if (previousControl != null)
        {
            var previous = content.Find(previousControl)?.GetComponent<Button>();
            if (previous != null && previous.interactable) focus = previous;
        }
        focus?.Select();
    }

    private void RoomThumbnail(WorkshopCatalog.Template room,int x,int y,int size=48)
    {
        if (room==null || room.room==null) return;
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

    // One list for every kind of material, with tabs. Picking an entry switches to the tool that places it, like the strip.
    private void DrawPalette()
    {
        bool choosingSet=paletteKind==3 && tool==Tool.Prop && scatter;
        var entries=PaletteEntries(paletteKind);
        var matches=new List<(QuickMaterial material,string title,Sprite sprite)>();
        foreach(var e in entries)
            if(e.title.IndexOf(paletteQuery,StringComparison.OrdinalIgnoreCase)>=0) matches.Add(e);
        int count=matches.Count;
        palettePage=Mathf.Clamp(palettePage,0,Mathf.Max(0,(count-1)/12));
        WorkshopUI.Frame(content,"Materials",StripX,54,MapAreaWidth,WorkshopPreview.Height);
        int[] tabX={16,52,88,114};
        int[] tabW={34,34,24,34};
        for(int k=0;k<PaletteKinds.Length;k++)
        {
            int kind=k;
            Btn(PaletteKinds[k],tabX[k],60,tabW[k],22,()=>{paletteKind=kind;palettePage=0;Draw(page);},paletteKind==k).name="재료 종류 "+PaletteKinds[k];
        }
        var search=Input("재료 검색어",paletteQuery,154,60,176,22);
        search.GetComponent<Image>().color=WorkshopUI.ButtonColor;
        void Filter() {paletteQuery=search.text.Trim();palettePage=0;Draw(page);}
        search.onSubmit.AddListener(_=>Filter());
        Btn("검색",334,60,46,22,Filter);
        Btn("전체",384,60,46,22,()=>{paletteQuery="";palettePage=0;Draw(page);});
        if(count==0) WorkshopUI.Label(content,"일치하는 재료가 없습니다.",20,115,445,26,12);
        var current=CurrentMaterial;
        for(int slot=palettePage*12;slot<Math.Min(count,(palettePage+1)*12);slot++)
        {
            var (material,title,sprite)=matches[slot];
            int x=15+slot%3*155, y=87+slot%12/3*47;
            int propIndex_=material.kind=='p' ? Array.FindIndex(props,p=>p.id==material.id) : -1;
            bool chosen=choosingSet ? scatterSet.Contains(propIndex_) : current.HasValue && current.Value.Same(material);
            var button=Btn(title,x,y,151,43,()=>{
                // While scattering, each click adds or removes a prop from the set and the list stays open.
                if (choosingSet) {ToggleScatterMember(propIndex_);Draw(page);return;}
                UseQuick(material);
            },chosen);
            ShowMaterial(button,sprite,33,37,109,43);
            if (!choosingSet) FavoriteToggle(material,x+133,y+2);
        }
        Btn("이전 재료",16,292,80,22,()=>{palettePage--;Draw(page);}).interactable=palettePage>0;
        Btn("다음 재료",100,292,80,22,()=>{palettePage++;Draw(page);}).interactable=(palettePage+1)*12<count;
        WorkshopUI.Label(content,(palettePage+1)+" / "+Mathf.Max(1,(count+11)/12),190,292,60,22);
        WorkshopUI.Label(content,choosingSet ? "흩뿌릴 소품 "+scatterSet.Count+"종" : PaletteKinds[paletteKind]+" "+count+"개",254,292,130,22);
        Btn("닫기",392,292,80,22,()=>{drawer=false;Draw(page);});
    }

    // Tiles follow the palette order when the list matches the tool (recent first, tiles of this room next).
    private List<(QuickMaterial material,string title,Sprite sprite)> PaletteEntries(int kind)
    {
        var list=new List<(QuickMaterial,string,Sprite)>();
        if(kind<=1)
        {
            bool terrain=kind==0;
            IEnumerable<WorkshopCatalog.TileEntry> tiles=KindOfTool==kind ? palette :
                catalog.tiles.Where(t=>t.tile!=null && (t.tile is WorkshopTerrainTile)==terrain)
                    .OrderBy(t=>RecentRank(t.id)).ThenBy(t=>t.tile.name,StringComparer.Ordinal).ThenBy(t=>t.id,StringComparer.Ordinal);
            foreach(var t in tiles) list.Add((new QuickMaterial('t',t.id,terrain ? -1 : layer),t.Title,t.Sample));
        }
        else
            foreach(var p in kind==2 ? enemies : props)
                list.Add((new QuickMaterial(kind==2 ? 'e' : 'p',p.id),p.prefab.name,p.prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite));
        return list;
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
        WorkshopUI.StyleInput(nameField);
        nameField.onEndEdit.AddListener(value=>Safe(()=>{
            string next=value.Trim();
            if(next.Length==0) {nameField.SetTextWithoutNotify(Doc.title);return;}
            if(next==Doc.title) return;
            before=Doc.Serialize(); Doc.title=next; EndStroke();
        }));
    }
}
#endif
