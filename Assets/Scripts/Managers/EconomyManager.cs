using System;
using UnityEngine;

/// <summary>
/// 게임의 경제 시스템(BonePoint 재화 및 소환 비용)을 관리하는 매니저입니다.
/// </summary>
public class EconomyManager : MonoBehaviour
{
    [Header("Economy Settings")]
    [SerializeField] private int bonePoint;
    public int BonePoint => bonePoint;

    public void Initialize()
    {
        Debug.Log("<color=yellow>[EconomyManager]</color> Initialized.");
    }

    public void AddBonePoint(int amount)
    {
        bonePoint += amount;
    }

    /// <summary>
    /// 소환 가능 여부를 계산하고 재화를 차감합니다.
    /// </summary>
    public int CalculateAffordableSummonCount(int requestedNum, int cost)
    {
        if (cost <= 0) return 0;

        if (bonePoint < cost)
        {
            Debug.LogWarning($"<color=orange>[EconomyManager]</color> 자원 부족! 현재: {bonePoint}, 필요: {cost}");
            return 0;
        }

        int affordableCount = bonePoint / cost;
        if (affordableCount == 0) return 0;

        int finalCount = Math.Min(requestedNum, affordableCount);
        bonePoint -= cost * finalCount;

        return finalCount;
    }
}
