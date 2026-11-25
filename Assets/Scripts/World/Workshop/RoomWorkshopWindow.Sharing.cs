using System;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class RoomWorkshopWindow
{
    private enum ShareView { Rooms, Packages, Workspaces }
    private ShareView shareView;
    private string packageTitle="";
    private readonly System.Collections.Generic.Dictionary<string,WorkshopFloor> openFloors=new();
    private WorkshopPackageReview review;
    private int sharePage, partIndex, differenceIndex;
    private bool differencesOnly;

    private void ShareTab(string label,int x,ShareView view)
    {
        Btn(label,x,38,88,23,()=>{
            notice=""; shareView=view; review=null; sharePage=0; listPage=0; Draw(page);
        },shareView==view);
    }

    private void DrawShareHeader()
    {
        ShareTab("방 파일",12,ShareView.Rooms);
        ShareTab("제작 묶음",104,ShareView.Packages);
        ShareTab("작업 공간",196,ShareView.Workspaces);
    }

    private void DrawPackages()
    {
        if (review!=null) { DrawReview(); return; }
        Directory.CreateDirectory(ShareFolder);
        var paths=Directory.GetFiles(ShareFolder,"*"+WorkshopPackage.Extension)
            .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        WorkshopUI.Label(content,"묶음 이름",12,71,77,24,12);
        var field=WorkshopUI.Frame(content,"묶음 이름 입력",94,71,357,24);
        var input=field.gameObject.AddComponent<TMPro.TMP_InputField>();
        input.textViewport=field.rectTransform;
        input.textComponent=WorkshopUI.Label(field.transform,"",5,0,346,24);
        input.characterLimit=64;input.richText=false;
        if (string.IsNullOrEmpty(packageTitle)) packageTitle="제작 묶음 "+DateTime.Now.ToString("yyyy-MM-dd");
        input.SetTextWithoutNotify(packageTitle);
        input.onValueChanged.AddListener(value=>packageTitle=value);
        Btn("묶음 내보내기",460,71,166,24,()=>{
            string path=WorkshopPackage.Export(catalog,packageTitle);
            sharePage=0; Draw(page); Say("공유 폴더에 저장했습니다. "+Path.GetFileName(path));
        });
        const int count=5;
        sharePage=Mathf.Clamp(sharePage,0,Math.Max(0,(paths.Length-1)/count));
        for(int i=sharePage*count;i<Math.Min(paths.Length,(sharePage+1)*count);i++)
        {
            string path=paths[i]; int y=104+i%count*37;
            WorkshopUI.Frame(content,"Package",12,y,614,32);
            WorkshopUI.Label(content,Path.GetFileNameWithoutExtension(path),21,y+4,385,23);
            WorkshopUI.Label(content,$"{new FileInfo(path).Length/1024f:0.#} KB",408,y+4,95,23);
            Btn("비교",521,y+4,96,24,()=>{
                var incoming=WorkshopPackage.Read(path,catalog);
                review=new WorkshopPackageReview(incoming,catalog);
                notice="사용할 개정을 고르면 새 작업 공간에 저장됩니다."; differencesOnly=false; differenceIndex=partIndex=0; Draw(page);
            }).name="비교 "+Path.GetFileName(path);
        }
        if (paths.Length==0) WorkshopUI.Label(content,"동료의 .levelpack 파일을 공유 폴더에 넣어 주세요.",14,119,600,28);
        WorkshopUI.Label(content,"마지막으로 저장한 방과 층·마을 설정이 함께 들어갑니다.",244,300,381,25);
        SharePager(paths.Length,count);
    }

    private void SharePager(int total,int count)
    {
        int pages=Math.Max(1,(total+count-1)/count);
        if (pages<=1) return;
        Btn("이전",12,302,65,22,()=>{sharePage--;Draw(page);}).interactable=sharePage>0;
        WorkshopUI.Label(content,(sharePage+1)+" / "+pages,88,302,65,22);
        Btn("다음",161,302,65,22,()=>{sharePage++;Draw(page);}).interactable=sharePage+1<pages;
    }

    private void DrawReview()
    {
        var different=review.parts.Where(p=>p.canChoose).ToList();
        var visible=differencesOnly && different.Count>0 ? different : review.parts;
        var part=visible[partIndex];
        int first=partIndex/5*5;
        WorkshopUI.Label(content,review.package.title+"  /  방 "+review.package.rooms.Count+"개  ·  다른 개정 "+different.Count+"개",12,68,467,22,12);
        Btn(differencesOnly ? "모두 보기" : "차이만 보기",493,68,134,22,()=>{
            differencesOnly=!differencesOnly;partIndex=differenceIndex=0;Draw(page);
        },differencesOnly).interactable=different.Count>0;
        for (int i=first;i<Math.Min(first+5,visible.Count);i++)
        {
            int target=i; var row=visible[i];
            Btn(row.name,12,98+(i-first)*34,221,29,()=>{partIndex=target;differenceIndex=0;Draw(page);},i==partIndex).name="개정 "+row.key;
        }
        if (visible.Count>5)
        {
            Btn("이전 항목",12,271,104,20,()=>{partIndex=first-1;Draw(page);}).interactable=first>0;
            Btn("다음 항목",126,271,107,20,()=>{partIndex=first+5;Draw(page);}).interactable=first+5<visible.Count;
        }
        WorkshopUI.Frame(content,"Comparison",242,98,385,193);
        WorkshopUI.Label(content,part.name,252,105,361,22,12);
        var summary=WorkshopUI.Label(content,part.summary,252,130,361,41);
        summary.textWrappingMode=TMPro.TextWrappingModes.Normal;
        string ours=part.localLabel, team=part.teamLabel;
        if (part.differences.Count>0)
        {
            differenceIndex=Mathf.Clamp(differenceIndex,0,part.differences.Count-1);
            var value=part.differences[differenceIndex];
            WorkshopUI.Label(content,value.label,252,170,218,19);
            if (part.differences.Count>1)
                Btn("다음 차이",538,169,77,18,()=>{differenceIndex=(differenceIndex+1)%part.differences.Count;Draw(page);});
            ours=value.local; team=value.team;
        }
        WorkshopUI.Label(content,"내 저장본   "+ours,252,192,365,21);
        WorkshopUI.Label(content,"팀 공유본   "+team,252,215,365,21);
        if (part.canChoose)
        {
            Btn("내 버전 사용",252,242,165,25,()=>{part.useLocal=true;Draw(page);},part.useLocal);
            Btn("팀 버전 사용",434,242,182,25,()=>{part.useLocal=false;Draw(page);},!part.useLocal);
        }
        Btn("목록으로",12,302,107,23,()=>{review=null;Draw(page);});
        if(ProjectContentStore.Authoring)
            Btn("선택한 내용 DATA에 적용",145,302,217,23,()=>{
                review.Publish();review=null;Draw(page);Say("선택한 방·층·마을을 프로젝트에 저장했습니다.");
            });
        Btn("새 작업 공간으로 열기",410,302,217,23,()=>{
            var entry=review.Import();
            ActivateWorkspace(entry);
        },true);
    }

    private void DrawWorkspaces()
    {
        var entries=WorkshopWorkspace.List();
        WorkshopUI.Label(content,"열어서 편집할 작업 공간",12,70,612,24,12);
        const int count=5;
        sharePage=Mathf.Clamp(sharePage,0,(entries.Count-1)/count);
        for (int i=sharePage*count;i<Math.Min(entries.Count,(sharePage+1)*count);i++)
        {
            var entry=entries[i]; int y=103+i%count*37;
            bool open=entry.id==WorkshopWorkspace.ActiveId;
            WorkshopUI.Frame(content,"Workspace row",12,y,614,32);
            WorkshopUI.Label(content,entry.title+(entry.id=="" ? "" : " · "+entry.id.Substring(0,8)),21,y+4,448,23);
            var button=Btn(open ? "열려 있음" : "열기",475,y+4,141,24,()=>ActivateWorkspace(entry),open);
            button.name="작업 공간 "+(entry.id=="" ? "기본" : entry.id); button.interactable=!open;
        }
        SharePager(entries.Count,count);
    }

    private void ActivateWorkspace(WorkshopWorkspace.Entry entry)
    {
        if (floorSettings!=null) openFloors[WorkshopWorkspace.ActiveId]=floorSettings;
        WorkshopWorkspace.Switch(entry.id);
        WorkshopTrial.Draft=null; WorkshopTrial.Undo.Clear(); WorkshopTrial.Redo.Clear();
        rememberedFloor=roomFloor=floorSettings=null; rememberedCell=floorCell=null;
        openFloors.TryGetValue(entry.id,out floorSettings);
        rememberedSelection=Array.Empty<string>(); rememberedStart=start=null;
        selection.Clear(); stamp.Clear(); dirty=false; persisted=null; review=null;
        shareView=ShareView.Workspaces; sharePage=0;
        Draw(page); Say(entry.title+" · 작업 공간을 열었습니다.");
    }
}
