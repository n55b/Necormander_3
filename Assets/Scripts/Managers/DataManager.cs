using System;
using System.Collections.Generic;
using UnityEngine;
using Necromancer.Player;

public class DataManager : MonoBehaviour
{
    [SerializeField] List<MinionDataSO> allMinionData;
    [SerializeField] List<ThrowCombinationSO> allCombinations;
    [SerializeField] int bonePoint;

    public int BONEPOINT {get {return bonePoint;}}
    public List<MinionDataSO> ALL_MINION_DATA => allMinionData;

    // 두 유닛 타입에 맞는 조합 데이터를 찾아주는 함수
    public ThrowCombinationSO GetCombination(CommandData type1, CommandData type2)
    {
        foreach (var combo in allCombinations)
        {
            if (combo.IsMatch(type1, type2)) return combo;
        }
        return null;
    }

    // 재화 얻었을 때 실행될 함수
    public void AddBonePoint(int _addNum)
    {
        bonePoint += _addNum;
    }

    // 소환시 필요 재화 계산식
    public int CalculateBonepoint(int summonNum, int cost)
    {
        int affordableCount = bonePoint / cost;
        
        // 재화 부족시 소환 x
        if(affordableCount == 0)
            return 0;

        int finalSummonCount = Math.Min(summonNum, affordableCount);
        bonePoint -= cost * finalSummonCount;

        return finalSummonCount;
    }

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
