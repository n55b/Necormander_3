using System;
using UnityEngine;

/// <summary>
/// 모든 보석 효과의 최상위 추상 클래스입니다.
/// </summary>
[Serializable]
public abstract class GemEffect
{
    public abstract string GetDescription();
    public abstract void Apply(InventoryManager.GemAggregatedStats targetStats);
}
