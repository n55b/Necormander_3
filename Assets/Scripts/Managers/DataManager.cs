using System.Collections.Generic;
using UnityEngine;

public class DataManager : MonoBehaviour
{
    [SerializeField] List<MinionDataSO> allMinionData;

    public List<MinionDataSO> ALL_MINION_DATA => allMinionData;

    // CommandData를 바탕으로 데이터(SO)를 찾아주는 함수
    public MinionDataSO GetMinionData(CommandData type)
    {
        foreach (var data in allMinionData)
        {
            if (data.minionType == type)
            {
                return data;
            }
        }
        
        Debug.LogWarning($"DataManager: {type}에 해당하는 MinionDataSO를 찾을 수 없습니다!");
        return null;
    }
}
