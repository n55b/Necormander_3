using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 정적 데이터(미니언 정보, 조합 정보, 설정값)를 제공하는 순수 데이터 제공 클래스입니다.
/// </summary>
public class DataManager : MonoBehaviour
{
    [Header("Data Registries")]
    [SerializeField] private MinionRegistrySO minionRegistry;
    [SerializeField] private CombinationRegistrySO combinationRegistry;
    [SerializeField] private ThrowEffectRegistrySO throwEffectRegistry;
    [SerializeField] private AIPatternSO defaultAIPattern; 

    public ThrowEffectRegistrySO THROW_EFFECT_REGISTRY => throwEffectRegistry;
    public AIPatternSO DEFAULT_AI_PATTERN => defaultAIPattern;

    // 데이터 게터
    public List<MinionDataSO> ALL_MINION_DATA => minionRegistry != null ? minionRegistry.allyMinionData : null;
    public List<MinionDataSO> ENEMY_MINION_DATA => minionRegistry != null ? minionRegistry.enemyMinionData : null;

    /// <summary>
    /// 다른 매니저들이 준비되기 전에 가장 먼저 초기화되어야 합니다.
    /// </summary>
    public void Initialize()
    {
        Debug.Log("<color=green>[DataManager]</color> Initialized.");
    }

    public ThrowCombinationSO GetCombination(CommandData type1, CommandData type2)
    {
        if (combinationRegistry == null) return null;
        foreach (var combo in combinationRegistry.allCombinations)
        {
            if (combo.IsMatch(type1, type2)) return combo;
        }
        return null;
    }

    public MinionDataSO GetMinionData(CommandData type)
    {
        if (minionRegistry == null) return null;

        foreach (var data in minionRegistry.allyMinionData)
            if (data.minionType == type) return data;
        
        foreach (var data in minionRegistry.enemyMinionData)
            if (data.minionType == type) return data;
        
        return null;
    }

    public GameObject CreateUnit(MinionDataSO data, Vector3 position)
    {
        if (data == null || data.minionPrefab == null) return null;
        GameObject unitObj = Instantiate(data.minionPrefab, position, Quaternion.identity);
        if (unitObj.TryGetComponent<BaseEntity>(out var entity)) entity.Initialize(data);
        return unitObj;
    }
}
