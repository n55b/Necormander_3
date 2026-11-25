using System.Linq;
using UnityEngine;

public sealed partial class RoomWorkshopWindow
{
    private ProjectPlayerTuning balance;
    private float[] balanceDefaults;

    private void DrawBalance()
    {
        if(balance==null)
        {
            balance=new ProjectPlayerTuning();
            var prefab=catalog.playerPrefab;
            balanceDefaults=prefab.GetComponent<PlayerController>().WorkshopTuningDefaults()
                .Concat(prefab.GetComponent<MeleeCombatController>().WorkshopTuningDefaults()).ToArray();
        }
        WorkshopUI.Label(content,"플레이어 이동과 공격",12,37,315,22,14);
        int y=74;
        for(int i=0;i<ProjectPlayerTuning.Fields.Length;i++)
        {
            int index=i;
            float value=balance.Value(ProjectPlayerTuning.Fields[i],balanceDefaults[i]);
            Btn("기본값",361,y+3,62,22,()=>{balance.ResetField(index);Draw(page);});
            Stepper(ProjectPlayerTuning.Titles[i],value.ToString("0.##"),ref y,delta=>
                balance.Set(index,value+delta*ProjectPlayerTuning.Steps[index]));
        }
        Btn("프로젝트에 저장",12,254,146,25,SaveBalance,true);
    }
    private void SaveBalance()
    {
        balance.Save();
        Draw(page);
        Say("DATA 저장됨 · 다음 플레이어 생성부터 적용");
    }
}
