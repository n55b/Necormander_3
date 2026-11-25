using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

public sealed partial class RoomWorkshopWindow
{
    private bool showPresets;
    private int presetPage;
    private string PresetFolder=>"PRESETS/"+dataEntry.id;
    private void DrawPresets()
    {
        string folder=Path.Combine(ProjectContentStore.Root,PresetFolder);
        var files=Directory.Exists(folder)?Directory.GetFiles(folder,"*.json").OrderBy(f=>f,StringComparer.Ordinal).ToArray():Array.Empty<string>();
        var title=Input("프리셋 이름","",12,46,300,25);
        Btn("현재 값으로 만들기",321,46,168,25,()=>{
            string name=title.text.Trim();
            if(string.IsNullOrEmpty(name)||name.Length>64)throw new InvalidDataException("프리셋 이름을 1~64자로 입력하세요.");
            var basis=ProjectContentStore.WithDocument(PresetFolder,(path,_)=>ProjectContentStore.Read(path));
            var next=new Dictionary<string,string>(basis);
            string file=Guid.NewGuid().ToString("N")+".json";
            next[file]=ProjectContentStore.Json(new JObject{["title"]=name,["values"]=parameters.DeepClone()});
            ProjectContentStore.Save(PresetFolder,basis,next,_=>{});
            Draw(page);Say("프리셋을 저장했습니다.");
        });
        Btn("수치로",512,46,110,25,()=>{showPresets=false;Draw(page);});
        int pages=Math.Max(1,(files.Length+6)/7);
        presetPage=Math.Clamp(presetPage,0,pages-1);
        for(int n=presetPage*7;n<Math.Min(files.Length,(presetPage+1)*7);n++)
        {
            var preset=JObject.Parse(File.ReadAllText(files[n]));
            Btn((string)preset["title"],12,86+n%7*27,606,24,()=>{
                var values=(JObject)preset["values"]??throw new InvalidDataException("프리셋에 값이 없습니다.");
                var fields=dataFields.ToDictionary(f=>f.path);
                foreach(var value in values)
                {
                    if(!fields.TryGetValue(value.Key,out var field))throw new InvalidDataException("현재 원본에 없는 항목입니다. "+value.Key);
                    field.Parse((string)value.Value);
                }
                parameters=(JObject)values.DeepClone();
                showPresets=false;dataField=null;Draw(page);Say("프리셋을 불러왔습니다. 프로젝트에 저장하면 적용됩니다.");
            });
        }
        Btn("이전",12,299,65,23,()=>{presetPage--;Draw(page);});
        WorkshopUI.Label(content,(presetPage+1)+" / "+pages,85,299,70,23);
        Btn("다음",165,299,65,23,()=>{presetPage++;Draw(page);});
    }
}
