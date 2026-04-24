using System;
using System.Collections.Generic;
using UnityEngine;
using Necromancer.Player;

public class DataManager : MonoBehaviour
{
    [Header("Data Registries")]
    [SerializeField] private MinionRegistrySO minionRegistry;
    [SerializeField] private CombinationRegistrySO combinationRegistry;

    [Header("Economy")]
    [SerializeField] int bonePoint;

    public int BONEPOINT => bonePoint;

    // 아군 미니언 리스트 반환 (소환 UI 등에서 사용)
    public List<MinionDataSO> ALL_MINION_DATA => minionRegistry != null ? minionRegistry.allyMinionData : null;

    // 적군 미니언 리스트 반환 (스포너에서 사용)
    public List<MinionDataSO> ENEMY_MINION_DATA => minionRegistry != null ? minionRegistry.enemyMinionData : null;

    // 두 유닛 타입에 맞는 조합 데이터를 찾아주는 함수
    public ThrowCombinationSO GetCombination(CommandData type1, CommandData type2)
    {
        if (combinationRegistry == null) return null;

        foreach (var combo in combinationRegistry.allCombinations)
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

    // CommandData를 바탕으로 데이터(SO)를 찾아주는 함수 (아군/적군 모두 검색)
    public MinionDataSO GetMinionData(CommandData type)
    {
        if (minionRegistry == null) return null;

        // 아군 리스트 먼저 검색
        foreach (var data in minionRegistry.allyMinionData)
        {
            if (data.minionType == type) return data;
        }
        
        // 적군 리스트 검색
        foreach (var data in minionRegistry.enemyMinionData)
        {
            if (data.minionType == type) return data;
        }
        
        Debug.LogWarning($"DataManager: {type}에 해당하는 MinionDataSO를 찾을 수 없습니다!");
        return null;
    }
}
