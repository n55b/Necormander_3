using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

public sealed partial class RoomWorkshopWindow
{
    private StringTable translationTable;
    private StringTableEntry translation;
    private string translationSearch="";
    private int translationPage;
    private Dictionary<string,string> translationBasis;
    private TMP_InputField translationInput;
    private string translationDraft;
    private bool creatingTranslation;
    private string newTranslationKey,newTranslationText;
    private void DrawTranslations()
    {
        if(translationTable==null)
        {
            var locales=LocalizationSettings.AvailableLocales.Locales;
            int y=48;
            foreach(string tableName in new[]{"UI Text Table","Reward Text Table"})
            {
                int x=12;
                foreach(var locale in locales)
                {
                    Btn(tableName+" · "+locale.Identifier.Code,x,y,240,28,()=>{
                        translationTable=LocalizationSettings.StringDatabase.GetTable(tableName,locale);
                        if(translationTable==null)throw new InvalidOperationException("언어 테이블을 읽을 수 없습니다.");
                        translationPage=0;translationSearch="";Draw(page);
                    });
                    x+=246;
                }
                y+=35;
            }
            return;
        }
        if(creatingTranslation)
        {
            WorkshopUI.Label(content,"새 번역 키",12,45,310,22);
            var key=Input("새 번역 키",newTranslationKey,12,72,608,24);
            key.onValueChanged.AddListener(v=>newTranslationKey=v);
            var body=Input("새 번역 본문",newTranslationText,12,108,608,70,true);
            WorkshopUI.TextPreview(body,content,183,52);
            body.onValueChanged.AddListener(v=>newTranslationText=v);
            Btn("취소",12,246,100,25,()=>{creatingTranslation=false;Draw(page);});
            Btn("프로젝트에 저장",460,246,160,25,SaveTranslation,true);return;
        }
        if(translation!=null)
        {
            WorkshopUI.Label(content,translationTable.TableCollectionName+"   /   "+translation.Key,12,44,608,26);
            translationInput=Input("번역 본문",translationDraft,12,88,608,85,true);
            WorkshopUI.TextPreview(translationInput,content,179,56);
            translationInput.onValueChanged.AddListener(value=>translationDraft=value);
            Btn("변경 취소",12,246,100,25,()=>{translation=null;Draw(page);});
            Btn("프로젝트에 저장",460,246,160,25,SaveTranslation,true);
            return;
        }
        var search=Input("번역 검색",translationSearch,12,40,285,24);
        search.onEndEdit.AddListener(query=>{if(query==translationSearch)return;translationSearch=query;translationPage=0;Draw(page);});
        Btn("새 번역",328,40,128,24,()=>{creatingTranslation=true;newTranslationKey="";newTranslationText="";Draw(page);});
        bool Matches(string text)=>text?.IndexOf(translationSearch,StringComparison.OrdinalIgnoreCase)>=0;
        var entries=translationTable.Values.Where(e=>Matches(e.Key)||Matches(e.Value)).OrderBy(e=>e.Key,StringComparer.Ordinal).ToArray();
        int pages=Math.Max(1,(entries.Length+7)/8);
        translationPage=Mathf.Clamp(translationPage,0,pages-1);
        for(int i=translationPage*8;i<Math.Min(entries.Length,(translationPage+1)*8);i++)
        {
            var e=entries[i];
            Btn(e.Key+"   "+e.Value,12,74+i%8*27,608,24,()=>{
                translation=e;translationDraft=e.Value;translationBasis=ProjectTranslations.Snapshot(translationTable);Draw(page);
            });
        }
        Btn("이전",12,299,66,23,()=>{translationPage--;Draw(page);});
        WorkshopUI.Label(content,(translationPage+1)+" / "+pages,86,299,73,22);
        Btn("다음",165,299,66,23,()=>{translationPage++;Draw(page);});
    }
    private void SaveTranslation()
    {
        if(creatingTranslation)
        {
            ProjectTranslations.Create(translationTable,newTranslationKey,newTranslationText);
            creatingTranslation=false;translationSearch=newTranslationKey;translationPage=0;
            Draw(page);Say("DATA에 번역 키와 본문을 저장했습니다.");return;
        }
        if(translation==null)return;
        ProjectTranslations.Save(translationTable,translation,translationBasis,translationDraft);
        translation=null;Draw(page);Say("DATA 저장됨 · 다음 번역 조회부터 적용됩니다.");
    }
}
