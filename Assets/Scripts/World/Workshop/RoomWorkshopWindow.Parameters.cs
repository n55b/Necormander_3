using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

public sealed partial class RoomWorkshopWindow
{
    private ProjectParameterBindings.Entry[] dataCatalog;
    private ProjectParameterBindings.Entry dataEntry;
    private List<ProjectParameters.Field> dataFields;
    private ProjectParameters.Field dataField;
    private Dictionary<string,string> parameterBasis;
    private JObject parameters;
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

    private void OpenParameters()
    {
#if UNITY_EDITOR
        dataCatalog=ProjectParameterBindings.All();
#else
        dataCatalog=ProjectParameterBindings.CreatorCatalog ?? Array.Empty<ProjectParameterBindings.Entry>();
#endif
        ProjectParameterBindings.Bind(dataCatalog);
        dataCatalog=dataCatalog.Concat(ProjectContentVariants.Entries).ToArray();
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
                string id=ProjectContentVariants.Create(dataEntry,title.text);
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
            if(dataField.IsReference)
            {
                var candidates=dataCatalog.Where(e=>dataField.type.IsInstanceOfType(e.source)).OrderBy(e=>e.Title,StringComparer.Ordinal).ToArray();
                int referencePages=Math.Max(1,(candidates.Length+5)/6);
                referencePage=Mathf.Clamp(referencePage,0,referencePages-1);
                for(int n=referencePage*6;n<Math.Min(candidates.Length,(referencePage+1)*6);n++)
                {
                    var option=candidates[n];
                    Btn(option.Title,12,79+n%6*27,608,24,()=>{parameterDraft=option.id;AcceptParameter();Draw(page);},
                        option.id==parameterDraft);
                }
                Btn("이전",12,250,66,23,()=>{referencePage--;Draw(page);});
                Btn("다음",90,250,66,23,()=>{referencePage++;Draw(page);});
                Btn("기본 연결",270,250,100,23,()=>{parameters.Remove(dataField.path);dataField=null;Draw(page);});
                Btn("취소",520,250,100,23,()=>{dataField=null;Draw(page);});
                return;
            }
            WorkshopUI.Label(content,(string.IsNullOrEmpty(dataField.group) ? "" : dataField.group+" · ")+dataField.label+
                (dataField.unit==null ? "" : " ("+dataField.unit+")"),12,86,615,24);
            WorkshopUI.Label(content,dataField.type.IsEnum ? string.Join(" / ",Enum.GetNames(dataField.type)) :
                dataField.type==typeof(bool) ? "True / False" : (dataField.help ?? "").Split('\n')[0],12,170,615,22);
            dataInput=Input("설정값",parameterDraft ?? ParameterValue(dataField),12,120,610,46,dataField.type==typeof(string));
            dataInput.onValueChanged.AddListener(v=>parameterDraft=v);
            Btn("적용",12,196,85,23,()=>{AcceptParameter();Draw(page);});
            Btn("기본값 복원",107,196,117,23,()=>{parameters.Remove(dataField.path);dataField=null;Draw(page);});
            Btn("취소",236,196,75,23,()=>{dataField=null;Draw(page);});
            Btn("프로젝트에 저장",455,196,165,23,SaveParameters,true);
            return;
        }
        var search=Input("데이터 검색",dataSearch,12,40,261,23);
        search.onEndEdit.AddListener(value=>{if(value==dataSearch)return;dataSearch=value;dataPage=0;Draw(page);});
        WorkshopUI.Label(content,dataEntry==null ? "에셋·영역 검색" : dataEntry.Title+"  /  속성 검색",282,40,220,23);
        if(dataEntry!=null && ProjectContentVariants.CanCreate(dataEntry))
            Btn("복제해 만들기",505,40,118,23,()=>{
                if(!JToken.DeepEquals(parameters,ProjectParameters.Values(parameterBasis)))
                    throw new InvalidOperationException("변경값을 저장한 뒤 복제하세요.");
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
            Btn("프로젝트에 저장",480,299,145,23,SaveParameters,true);
            Btn("프리셋",208,299,74,23,()=>{showPresets=true;presetPage=0;Draw(page);});
            Btn("에셋 목록",380,299,92,23,()=>{
                if(!JToken.DeepEquals(parameters,ProjectParameters.Values(parameterBasis)))
                {Say("변경값을 저장하거나 되돌린 뒤 목록으로 이동하세요.");return;}
                dataEntry=null;dataPage=0;dataSearch="";Draw(page);
            });
            Btn("변경 취소",288,299,85,23,()=>{parameters=ProjectParameters.Values(parameterBasis);Draw(page);});
        }
        const int size=8;
        int pages=Math.Max(1,(rows.Count+size-1)/size);dataPage=Mathf.Clamp(dataPage,0,pages-1);
        for(int i=dataPage*size;i<Math.Min(rows.Count,(dataPage+1)*size);i++)
            Btn(rows[i].title,12,70+i%size*27,612,24,rows[i].action,false,rows[i].id);
        Btn("이전",12,299,54,23,()=>{dataPage--;Draw(page);});
        WorkshopUI.Label(content,(dataPage+1)+" / "+pages,77,299,73,23);
        Btn("다음",148,299,54,23,()=>{dataPage++;Draw(page);});
    }
    private string ParameterValue(ProjectParameters.Field field)=>
        parameters.TryGetValue(field.path,out var value)?(string)value:ProjectParameters.Original(dataEntry.source,field);
    private string ValueLabel(ProjectParameters.Field field)
    {
        string value=ParameterValue(field);
        if(!field.IsReference)return value;
        return dataCatalog.FirstOrDefault(e=>e.id==value)?.Title ?? (string.IsNullOrEmpty(value)?"비어 있음":"기존 연결");
    }
    private string DisplayValue(ProjectParameters.Field field)=>field.type.IsEnum ? DesignerInspectorLabels.EnumLabel(field.type,ParameterValue(field)) :
        field.type==typeof(bool) ? (bool.TryParse(ParameterValue(field),out bool on) && on ? "켜짐" : "꺼짐") : ValueLabel(field);
    private void SelectContent(ProjectParameterBindings.Entry item)
    {
        dataEntry=item;dataFields=ProjectParameters.Fields(item.source,true);
        parameterBasis=ProjectParameters.Snapshot(item.Document);parameters=ProjectParameters.Values(parameterBasis);
        dataField=null;dataPage=0;dataSearch="";Draw(page);
    }
    private void AcceptParameter()
    {
        string value=parameterDraft ?? ParameterValue(dataField);
        if(ProjectParameters.Original(dataEntry.source,dataField)==value) parameters.Remove(dataField.path);
        else {dataField.Parse(value);parameters[dataField.path]=value;}
        dataField=null;
    }
    private void SaveParameters()
    {
        if(dataEntry==null) return;
        if(dataField!=null) AcceptParameter();
        parameterBasis=ProjectParameters.Save(dataEntry.Document,dataEntry.source,parameterBasis,parameters);
        parameters=ProjectParameters.Values(parameterBasis); Draw(page); Say("DATA 저장됨 · "+dataEntry.source.name);
    }
}
