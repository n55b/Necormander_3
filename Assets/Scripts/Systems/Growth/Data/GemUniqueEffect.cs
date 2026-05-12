using System;

/// <summary>
/// 특수 규칙 변조 로직(Enum 기반)을 활성화하는 유니크 보석 효과입니다.
/// </summary>
[Serializable]
public class GemUniqueEffect : GemEffect
{
    public GemUniqueType uniqueType;
    public string displayDescription;

    public override string GetDescription() => $"Unique: {displayDescription}";
    public override void Apply(InventoryManager.GemAggregatedStats targetStats)
    {
        if (uniqueType != GemUniqueType.None) targetStats.UniqueEffects.Add(uniqueType);
    }
}
