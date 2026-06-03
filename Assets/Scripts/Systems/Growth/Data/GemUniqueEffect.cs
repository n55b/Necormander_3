using System;
using UnityEngine.Localization.Settings;

/// <summary>
/// 특수 규칙 변조 로직(Enum 기반)을 활성화하는 유니크 보석 효과입니다.
/// </summary>
[Serializable]
public class GemUniqueEffect : GemEffect
{
    public GemUniqueType uniqueType;
    public string displayDescription;

    public override string GetDescription() 
    {
        string prefix = "Unique: ";
        var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync("UI Text Table", "UI_Unique");
        if (op.IsDone) prefix = op.Result;
        else 
        {
            var handle = op;
            handle.WaitForCompletion();
            prefix = handle.Result;
        }
        return $"{prefix}{displayDescription}";
    }
    public override void Apply(InventoryManager.GemAggregatedStats targetStats)
    {
        if (uniqueType != GemUniqueType.None)
        {
            if (!targetStats.UniqueEffectCounts.ContainsKey(uniqueType))
            {
                targetStats.UniqueEffectCounts[uniqueType] = 0;
            }
            targetStats.UniqueEffectCounts[uniqueType]++;
        }
    }
}
