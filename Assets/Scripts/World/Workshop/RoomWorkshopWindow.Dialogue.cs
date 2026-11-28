using System;
using System.Linq;
using TMPro;
using UnityEngine;

public sealed partial class RoomWorkshopWindow
{
    private ProjectDialogue conversation;
    private string conversationLanguage="ko";
    private int conversationPage, lineIndex;
    private bool dialogueDirty;

    private void DrawDialogue()
    {
        if (conversation==null)
        {
            var ids=catalog.dialogueTable.AllIds.Union(ProjectDialogue.Ids()).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
            int pages=Math.Max(1,(ids.Length+7)/8);
            conversationPage=Mathf.Clamp(conversationPage,0,pages-1);
            var idInput=Input("새 대화 ID","",12,40,230,23);
            Btn("새 대화",250,40,85,23,()=>{
                string id=idInput.text.Trim();
                if(ids.Contains(id)) throw new InvalidOperationException("이미 등록된 대화 ID입니다.");
                OpenDialogue(id);
            });
            Btn("한국어",430,40,90,23,()=>{conversationLanguage="ko";Draw(page);},conversationLanguage=="ko");
            Btn("English",526,40,94,23,()=>{conversationLanguage="en";Draw(page);},conversationLanguage=="en");
            for(int n=conversationPage*8;n<Math.Min(ids.Length,(conversationPage+1)*8);n++)
            {
                string id=ids[n];
                Btn(id,12,72+n%8*27,608,24,()=>OpenDialogue(id));
            }
            Btn("이전",12,299,70,23,()=>{conversationPage--;Draw(page);});
            WorkshopUI.Label(content,(conversationPage+1)+" / "+pages,89,299,82,23);
            Btn("다음",175,299,70,23,()=>{conversationPage++;Draw(page);});
            return;
        }
        WorkshopUI.Label(content,conversation.id+"   /   "+conversation.locale,12,38,609,22,12);
        int count=conversation.order.Count;
        lineIndex=Mathf.Clamp(lineIndex,0,Math.Max(0,count-1));
        if(count>0)
        {
            var line=conversation.lines[conversation.order[lineIndex]];
            void Field(string title,string value,int x,int width,Action<string> assign)
            {
                WorkshopUI.Label(content,title,x,88,width,21);
                var field=Input(title,value,x,112,width,24);
                field.onValueChanged.AddListener(v=>{assign(v);dialogueDirty=true;});
            }
            Field("화자",line.speaker,12,152,v=>line.speaker=v);
            Field("표시 이름",line.displayName,170,145,v=>line.displayName=v);
            Field("등장 인물 ( | 구분)",line.cast==null?"":string.Join("|",line.cast),321,299,v=>
                line.cast=string.IsNullOrWhiteSpace(v)?null:v.Split('|').Select(part=>part.Trim()).ToArray());
            var body=Input("대사 본문",line.text,12,145,608,48,true);
            WorkshopUI.TextPreview(body,content,197,40);
            body.onValueChanged.AddListener(v=>{line.text=v;dialogueDirty=true;});
            Btn("앞줄",12,64,65,21,()=>{lineIndex--;Draw(page);});
            WorkshopUI.Label(content,(lineIndex+1)+" / "+count,82,64,85,21);
            Btn("뒷줄",170,64,65,21,()=>{lineIndex++;Draw(page);});
            Btn("위로",300,244,68,22,()=>MoveLine(-1));
            Btn("아래로",374,244,68,22,()=>MoveLine(1));
            Btn("줄 삭제",459,244,100,22,()=>{
                string key=conversation.order[lineIndex];
                conversation.order.RemoveAt(lineIndex);conversation.lines.Remove(key);dialogueDirty=true;Draw(page);
            });
        }
        Btn("줄 추가",12,244,92,22,()=>{
            string key=Guid.NewGuid().ToString("N");
            lineIndex=Math.Min(lineIndex+1,count);
            conversation.order.Insert(lineIndex,key);
            conversation.lines.Add(key,new DialogueLine{id=conversation.id,text=""});dialogueDirty=true;Draw(page);
        });
        Btn("변경 취소",89,299,99,23,()=>{conversation=null;dialogueDirty=false;Draw(page);});
        Btn("프로젝트에 저장",460,299,160,23,SaveDialogue,true);
    }

    private void OpenDialogue(string id)
    {
        // A new translation keeps the published Korean sequence as its starting point.
        var korean=new ProjectDialogue(id,"ko",catalog.dialogueTable.Get(id));
        conversation=conversationLanguage=="ko"?korean:new ProjectDialogue(id,conversationLanguage,korean.order.Select(k=>korean.lines[k]));
        lineIndex=0;dialogueDirty=false;Draw(page);
        RememberRecent(Area.Text,id,"대사 "+id);
    }
    private void MoveLine(int step)
    {
        int target=lineIndex+step;
        if(target<0||target>=conversation.order.Count)return;
        string key=conversation.order[lineIndex];
        conversation.order.RemoveAt(lineIndex);conversation.order.Insert(target,key);
        lineIndex=target;dialogueDirty=true;Draw(page);
    }
    private void SaveDialogue()
    {
        if(conversation==null)return;
        conversation.Save();catalog.dialogueTable.Invalidate();dialogueDirty=false;
        Draw(page);Say("DATA 저장됨 · 다음 대화부터 적용됩니다.");
    }
}
