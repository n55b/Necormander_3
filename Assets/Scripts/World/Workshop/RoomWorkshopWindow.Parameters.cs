using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public sealed partial class RoomWorkshopWindow
{
    private ProjectParameterBindings.Entry[] dataCatalog;
    private ProjectParameterBindings.Entry dataEntry;
    private List<ProjectParameters.Field> dataFields;
    private ProjectParameters.Field dataField;
    private DraftStore.Draft paramDraft;
    private string dataSearch="";
    private int dataPage;
    private TMP_InputField dataInput;
    private string parameterDraft;
    private bool creatingContent;
    private int referencePage;

    private TMP_InputField Input(string name,string initial,int x,int y,int width,int height,bool multiline=false)
    {
        var box=WorkshopUI.Frame(content,name,x,y,width,height);
        var field=box.gameObject.AddComponent<TMP_InputField>();
        field.textViewport=box.rectTransform;
        var label=WorkshopUI.Label(box.transform,"",4,0,width-8,height,12);
        label.GetComponent<PixelText>().UseFaceResolver(catalog.FontForInput);
        label.richText=false;
        if(multiline)
        {
            field.lineType=TMP_InputField.LineType.MultiLineNewline;
            label.alignment=TextAlignmentOptions.TopLeft;
            label.textWrappingMode=TextWrappingModes.Normal;
            label.overflowMode=TextOverflowModes.Masking;
            box.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        }
        field.textComponent=label;field.richText=false;field.SetTextWithoutNotify(initial);
        return field;
    }

    // filter가 있으면 그 영역의 에셋 목록부터 보인다(허브 타일). 없으면 이전 항목을 그대로 연다. 카탈로그는 들어갈 때마다 다시 만들어 새 변형을 잡는다.
    private void OpenParameters(string filter=null)
    {
        if(filter!=null){ dataEntry=null; paramDraft=null; dataField=null; creatingContent=false; dataSearch=filter; dataPage=0; }
        dataCatalog=null; EnsureCatalog();
        Navigate(Page.Parameters);
    }
    private void DrawParameters()
    {
        if(showPresets && dataEntry!=null){DrawPresets();return;}
        bool Matches(string text)=>text.IndexOf(dataSearch,StringComparison.OrdinalIgnoreCase)>=0;
        if(creatingContent)
        {
            WorkshopUI.Label(content,dataEntry.Title+" 기반 · 새 콘텐츠 이름",12,50,610,25,12);
            var title=Input("새 콘텐츠 이름","",12,91,390,30);
            Btn("만들기",12,155,100,25,()=>{
                // 새 정의는 상점에 자동 가입하지 않는다(autoJoin:false). 예전처럼 원본이 든 상점 목록에 함께 넣는다. 세이브 해석 목록은 자동이다.
                var join=ProjectParameterBindings.Registries().OfType<ShopRegistrySO>().SelectMany(shop=>ProjectContentVariants.Lists(shop)
                    .Where(l=>l.list?.Contains(dataEntry.source)==true).Select(l=>((ScriptableObject)shop,l.field))).ToArray();
                string id=ProjectContentVariants.Create(dataEntry,title.text,join:join);
                creatingContent=false;OpenParameters();
                SelectContent(dataCatalog.Single(e=>e.id==id));
                Say("DATA에 생성했습니다. 원본과 같은 획득 목록에 등록됩니다.");
            },true);
            Btn("취소",120,155,80,25,()=>{creatingContent=false;Draw(page);});return;
        }
        if(dataField!=null)
        {
            WorkshopUI.Label(content,dataEntry.Title+"   /   "+dataField.path,12,45,615,24,12);
            if(dataField.readOnly)
            {
                WorkshopUI.Label(content,"읽기 전용 · 세이브 이름·애니메이터·실행 상태와 묶인 값이라 원본 에셋에서만 바꿉니다.",12,86,615,24);
                WorkshopUI.Label(content,ValueLabel(dataField),12,120,610,46,12);
                Btn("닫기",12,196,75,23,()=>{dataField=null;Draw(page);});
                return;
            }
            if(dataField.IsReference || dataField.IsAsset)
            {   // 그림은 구운 카탈로그 표(WorkshopCatalog.assets)에서 고른다. 순서 = 키(텍스처 GUID·조각 이름) 순.
                // 조각 이름은 시트마다 겹치므로(Frame_0 등) 그림 버튼의 고정 이름은 "그림/<키>"다.
                var candidates=dataField.IsAsset ?
                    (WorkshopCatalog.Shared?.assets ?? Array.Empty<WorkshopCatalog.AssetRef>()).Where(a=>a.asset!=null && dataField.type.IsInstanceOfType(a.asset))
                        .Select(a=>(id:a.key,title:a.asset.name)).ToArray() :
                    dataCatalog.Where(e=>dataField.type.IsInstanceOfType(e.source)).OrderBy(e=>e.Title,StringComparer.Ordinal).Select(e=>(id:e.id,title:e.Title)).ToArray();
                int referencePages=Math.Max(1,(candidates.Length+5)/6);
                referencePage=Mathf.Clamp(referencePage,0,referencePages-1);
                for(int n=referencePage*6;n<Math.Min(candidates.Length,(referencePage+1)*6);n++)
                {
                    var option=candidates[n];
                    Btn(option.title,12,79+n%6*27,608,24,()=>{parameterDraft=option.id;AcceptParameter();Draw(page);},
                        option.id==parameterDraft,dataField.IsAsset ? "그림/"+option.id : null);
                }
                Btn("이전",12,250,66,23,()=>{referencePage--;Draw(page);});
                Btn("다음",90,250,66,23,()=>{referencePage++;Draw(page);});
                Btn("원본 연결",270,250,100,23,()=>{DraftStore.Set(paramDraft,dataField,null);dataField=null;Draw(page);});
                Btn("취소",520,250,100,23,()=>{dataField=null;Draw(page);});
                return;
            }
            WorkshopUI.Label(content,(string.IsNullOrEmpty(dataField.group) ? "" : dataField.group+" · ")+dataField.label+
                (dataField.unit==null ? "" : " ("+dataField.unit+")"),12,86,615,24);
            var field=dataField;   // 위젯 콜백은 dataField를 비운 뒤에도 이 필드를 읽는다
            string helpLine=(field.help ?? "").Split('\n')[0];
            // bool·enum은 입력칸·[적용] 없이 고르는 즉시 초안에 넣고 목록으로 돌아간다. 값 문자열은 Original과 같은 형식이라 원본을 고르면 키가 지워진다.
            void Pick(string text)=>Safe(()=>{DraftStore.Set(paramDraft,field,text);dataField=null;Draw(page);});
            if(field.type==typeof(bool))
            {
                WorkshopUI.Toggle(content,"설정값",12,120,120,()=>bool.TryParse(ParameterValue(field),out bool on) && on,on=>Pick(on.ToString()));
                WorkshopUI.Label(content,helpLine,12,170,615,22);
            }
            else if(field.type.IsEnum)
            {
                var names=Enum.GetNames(field.type);
                var labels=names.Select(n=>DesignerInspectorLabels.EnumLabel(field.type,n)).ToArray();
                if(names.Length<=8) WorkshopUI.Choice(content,"설정값",12,120,610,24,labels,()=>Array.IndexOf(names,ParameterValue(field)),i=>Pick(names[i]));
                else WorkshopUI.Picker(content,content,"설정값",12,120,300,labels,()=>Array.IndexOf(names,ParameterValue(field)),i=>Pick(names[i]));
                WorkshopUI.Label(content,helpLine,12,170,615,22);
            }
            else
            {
                dataInput=Input("설정값",parameterDraft ?? ParameterValue(field),12,120,610,46,field.type==typeof(string));
                dataInput.onValueChanged.AddListener(v=>parameterDraft=v);
                bool ranged=(field.type==typeof(int) || field.type==typeof(float)) && !float.IsInfinity(field.min) && !float.IsInfinity(field.max);
                if(ranged)   // 움직이면 입력칸과 parameterDraft를 함께 바꾼다. 반영은 [적용]·저장과 같다.
                    WorkshopUI.Slider(content,"설정값 슬라이더",12,174,300,field.min,field.max,()=>(float)Number(parameterDraft ?? ParameterValue(field)),
                        v=>{ string text=Format(field,Math.Round(v,4)); dataInput.SetTextWithoutNotify(text); parameterDraft=text; },field.type==typeof(int));
                WorkshopUI.Label(content,helpLine,ranged ? 320 : 12,170,ranged ? 300 : 615,22);
                Btn("적용",12,196,85,23,()=>{AcceptParameter();Draw(page);});
            }
            Btn("원본값으로",107,196,117,23,()=>{DraftStore.Set(paramDraft,dataField,null);dataField=null;Draw(page);});
            Btn("취소",236,196,75,23,()=>{dataField=null;Draw(page);});
            Btn("프로젝트에 저장",455,196,165,23,SaveParameters,true);
            return;
        }
        var search=Input("데이터 검색",dataSearch,12,40,261,23);
        search.onEndEdit.AddListener(value=>{if(value==dataSearch)return;dataSearch=value;dataPage=0;Draw(page);});
        string blocked=dataEntry==null ? null : paramDraft.Blocked;
        if(blocked!=null)
        {   // 이 판에 없는 키가 든 문서: 저장을 막고 정리 버튼을 같은 자리에 둔다.
            WorkshopUI.Label(content,blocked,282,40,220,23,8);
            Btn("판 차이 정리",505,40,118,23,()=>{DraftStore.DropUnknown(paramDraft);Draw(page);},false,"이 판에 없는 항목 지우기");
        }
        else WorkshopUI.Label(content,dataEntry==null ? "에셋·영역 검색" : dataEntry.Title+"  /  속성 검색",282,40,220,23);
        if(blocked==null && dataEntry!=null && ProjectContentVariants.CanCreate(dataEntry))
            Btn("복제해 만들기",505,40,118,23,()=>{
                if(paramDraft.Dirty) throw new InvalidOperationException("변경값을 저장한 뒤 복제하세요.");
                creatingContent=true;Draw(page);
            });
        var rows=new List<(string title,string id,Action action)>();
        if(dataEntry==null)
        {
            foreach(var item in dataCatalog.Where(e=>Matches(e.group+" "+e.Title)))
            {
                rows.Add((item.group+"   "+item.Title,null,()=>SelectContent(item)));
            }
        }
        else
        {
            foreach(var f in dataFields.Where(f=>Matches(f.path+" "+f.label)))
            {
                string lockNote=f.readOnly?"   (읽기 전용)":"";
                // 캡션은 한글 라벨, GameObject 이름은 검사가 누르는 예전 "<경로>   <값>" 그대로다.
                rows.Add((f.label+"   "+DisplayValue(f)+lockNote,f.path+"   "+ValueLabel(f)+lockNote,
                    ()=>{dataField=f;parameterDraft=ParameterValue(f);referencePage=0;Draw(page);}));
            }
            Btn("프로젝트에 저장",480,299,145,23,SaveParameters,true).interactable=blocked==null;
            Btn("프리셋",208,299,74,23,()=>{showPresets=true;presetPage=0;Draw(page);});
            // 초안은 DraftStore에 남으므로 저장하지 않고 목록으로 가도 된다. 헤더 '미저장 n'이 알린다.
            Btn("에셋 목록",380,299,92,23,()=>{dataEntry=null;paramDraft=null;dataPage=0;dataSearch="";Draw(page);});
            Btn("변경 취소",288,299,85,23,()=>{DraftStore.Revert(paramDraft);Draw(page);});
        }
        const int size=8;
        int pages=Math.Max(1,(rows.Count+size-1)/size);dataPage=Mathf.Clamp(dataPage,0,pages-1);
        for(int i=dataPage*size;i<Math.Min(rows.Count,(dataPage+1)*size);i++)
            Btn(rows[i].title,12,70+i%size*27,612,24,rows[i].action,false,rows[i].id);
        Btn("이전",12,299,54,23,()=>{dataPage--;Draw(page);});
        WorkshopUI.Label(content,(dataPage+1)+" / "+pages,77,299,73,23);
        Btn("다음",148,299,54,23,()=>{dataPage++;Draw(page);});
    }
    private string ParameterValue(ProjectParameters.Field field)=>paramDraft.Value(field);
    private string ValueLabel(ProjectParameters.Field field)
    {
        string value=ParameterValue(field);
        if(field.IsAsset) return string.IsNullOrEmpty(value) ? "비어 있음" :
            Array.Find(WorkshopCatalog.Shared?.assets ?? Array.Empty<WorkshopCatalog.AssetRef>(),a=>a.key==value)?.asset?.name ?? "기존 연결";
        if(!field.IsReference)return value;
        return dataCatalog.FirstOrDefault(e=>e.id==value)?.Title ?? (string.IsNullOrEmpty(value)?"비어 있음":"기존 연결");
    }
    private string DisplayValue(ProjectParameters.Field field)=>field.type.IsEnum ? DesignerInspectorLabels.EnumLabel(field.type,ParameterValue(field)) :
        field.type==typeof(bool) ? (bool.TryParse(ParameterValue(field),out bool on) && on ? "켜짐" : "꺼짐") : ValueLabel(field);
    private void SelectContent(ProjectParameterBindings.Entry item)
    {
        dataEntry=item;paramDraft=DraftStore.Open(item);dataFields=paramDraft.fields;
        PlayerPrefs.SetString("Workshop.Opened."+item.Document,DateTime.UtcNow.Ticks.ToString("D19"));
        RememberRecent(Area.Content,item.Document,ContentTitle(item));
        dataField=null;dataPage=0;dataSearch="";Draw(page);
        if(paramDraft.Blocked!=null) Say(paramDraft.Blocked+" · 최신 제작판이면 [판 차이 정리]로 지운 뒤 저장하세요.");
    }
    private void AcceptParameter()
    {
        DraftStore.Set(paramDraft,dataField,parameterDraft ?? paramDraft.Value(dataField));
        dataField=null;
    }
    private void SaveParameters()
    {
        if(dataEntry==null) return;
        if(dataField!=null) AcceptParameter();
        string when=DraftStore.Save(paramDraft);
        Draw(page); Say("DATA 저장됨 · "+dataEntry.Title+(when!=null ? " · "+when+" 적용" : ""));
    }
}
