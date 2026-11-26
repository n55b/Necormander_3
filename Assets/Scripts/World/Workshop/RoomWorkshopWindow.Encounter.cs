using System.Linq;
using UnityEngine;

public sealed partial class RoomWorkshopWindow
{
    private bool encounterOpen;
    private int waveIndex;

    private void DrawEncounter()
    {
        WorkshopUI.Frame(content,"Encounter",7,54,WorkshopPreview.Width,WorkshopPreview.Height);
        WorkshopUI.Label(content,"전투 웨이브",18,64,225,24,15);
        Btn("닫기",414,65,60,21,()=>{encounterOpen=false;Draw(page);});
        WorkshopUI.Label(content,"고정 배치한 적이 있으면 먼저 등장합니다.",18,91,450,18);
        if(Doc.waves.Count==0)
            WorkshopUI.Label(content,"기존 방의 전투 규칙 사용",18,120,449,25,12);
        else
        {
            waveIndex=Mathf.Clamp(waveIndex,0,Doc.waves.Count-1);
            for(int i=0;i<Doc.waves.Count;i++)
            {
                int index=i;
                Btn((i+1).ToString(),18+i*39,116,35,22,()=>{waveIndex=index;Draw(page);},waveIndex==i);
            }
            var entry=catalog.FindCluster(Doc.waves[waveIndex]);
            WorkshopUI.Label(content,string.IsNullOrWhiteSpace(entry.asset.clusterName) ? entry.asset.name : entry.asset.clusterName,20,146,306,22,12);
            string members=string.Join("  ·  ",entry.asset.enemies.Where(e=>e.enemyData!=null && e.count>0)
                .Select(e=>e.enemyData.minionName+" ×"+e.count));
            WorkshopUI.Label(content,members,20,170,450,20);
            Say("군집은 순서대로 등장합니다. 마릿수에는 층과 증강 효과가 적용됩니다.");
            Btn("이전 군집",322,147,69,20,()=>CycleCluster(-1));
            Btn("다음 군집",395,147,76,20,()=>CycleCluster(1));
        }
        Btn("웨이브 추가",18,202,99,23,()=>{
            if(Doc.waves.Count==WorkshopRoom.MaxWaves || catalog.clusters.Length==0) return;
            before=Doc.Serialize();
            Doc.waves.Add(catalog.clusters[0].id); waveIndex=Doc.waves.Count-1;
            FinishEncounterEdit();
        }).interactable=Doc.waves.Count<WorkshopRoom.MaxWaves && catalog.clusters.Length>0;
        if(Doc.waves.Count>0)
        {
            Btn("앞으로",125,202,65,23,()=>MoveWave(-1)).interactable=waveIndex>0;
            Btn("뒤로",195,202,65,23,()=>MoveWave(1)).interactable=waveIndex+1<Doc.waves.Count;
            Btn("웨이브 삭제",269,202,98,23,()=>{
                before=Doc.Serialize(); Doc.waves.RemoveAt(waveIndex); FinishEncounterEdit();
            });
        }
    }
    private void CycleCluster(int direction)
    {
        int index=System.Array.FindIndex(catalog.clusters,c=>c.id==Doc.waves[waveIndex]);
        before=Doc.Serialize();
        Doc.waves[waveIndex]=catalog.clusters[(index+direction+catalog.clusters.Length)%catalog.clusters.Length].id;
        FinishEncounterEdit();
    }
    private void MoveWave(int direction)
    {
        int to=waveIndex+direction;
        before=Doc.Serialize();
        (Doc.waves[to],Doc.waves[waveIndex])=(Doc.waves[waveIndex],Doc.waves[to]);
        waveIndex=to; FinishEncounterEdit();
    }
    private void FinishEncounterEdit() { EndStroke(); Draw(page); }
}
