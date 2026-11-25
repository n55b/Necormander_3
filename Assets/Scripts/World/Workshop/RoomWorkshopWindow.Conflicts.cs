using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed partial class RoomWorkshopWindow
{
    private System.Collections.Generic.List<ProjectGitReview.FileConflict> gitFiles;

    private void DrawGit()
    {
        if(gitFiles==null || gitFiles.Count==0)
        {
            WorkshopUI.Label(content,"DATA에 해결할 Git 충돌이 없습니다.",15,72,600,26,12);
            return;
        }
        var file=gitFiles[0];
        WorkshopUI.Label(content,file.path,15,50,612,25);
        try
        {
            var result=ProjectGitReview.Prepare(file);
            WorkshopUI.Label(content,result==null ? "파일 삭제" : "선택 결과를 저장하고 이 파일을 스테이징합니다.",15,100,610,25);
            Btn("해결 저장 · 스테이징",15,154,175,26,()=>{ProjectGitReview.Stage(file);gitFiles=ProjectGitReview.Files();Draw(page);});
        }
        catch(ProjectContentStore.Conflict conflict)
        {
            WorkshopUI.Label(content,conflict.field,15,88,610,23);
            WorkshopUI.Label(content,"기준   "+(conflict.basis?.ToString(Formatting.None) ?? "삭제"),15,124,610,25);
            WorkshopUI.Label(content,"현재   "+(conflict.mine?.ToString(Formatting.None) ?? "삭제"),15,154,610,25);
            WorkshopUI.Label(content,"병합   "+(conflict.disk?.ToString(Formatting.None) ?? "삭제"),15,184,610,25);
            void Choose(bool mine){conflict.useMine=mine;file.decisions[conflict.field]=conflict;Draw(page);}
            Btn("현재 쪽 사용",15,228,145,25,()=>Choose(true));
            Btn("병합 쪽 사용",177,228,145,25,()=>Choose(false));
        }
    }
    private void DrawSaveConflict()
    {
        var conflict=pendingConflict;
        WorkshopUI.Label(content,"같은 위치에 두 가지 변경이 있습니다",15,48,610,25,14);
        WorkshopUI.Label(content,conflict.field,15,82,610,22);
        void Value(string title,JToken value,int y)
        {
            WorkshopUI.Label(content,title,15,y,100,23);
            WorkshopUI.Label(content,value?.ToString(Formatting.None) ?? "삭제",118,y,510,23);
        }
        Value("공통 원본",conflict.basis,118); Value("내 편집",conflict.mine,153); Value("디스크",conflict.disk,188);
        void Choose(bool mine)
        {
            conflict.useMine=mine;roomChoices[conflict.field]=conflict;
            pendingConflict=null; Draw(page);Save();
        }
        Btn("내 값 적용",15,238,130,25,()=>Choose(true));
        Btn("디스크 값 적용",155,238,146,25,()=>Choose(false));
        Btn("편집으로",320,238,110,25,()=>{pendingConflict=null;roomChoices.Clear();Draw(page);});
    }
}
