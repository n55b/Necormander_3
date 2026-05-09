using System;
using System.Collections.Generic;
using UnityEngine;

// StatType enum is defined in GemSO.cs, so it's globally accessible

/// <summary>
/// 플레이어가 소유한 개별 보석의 인스턴스를 나타냅니다.
/// 획득 시 결정되는 고유 ID, 하위 슬롯 수, 무작위 보너스/패널티를 가집니다.
/// </summary>
public class GemInstance
{
    public GemSO BaseData { get; private set; }
    public string InstanceId { get; private set; }
    public int SubSlots { get; private set; }
    public List<StatModifier> RandomModifiers { get; private set; }
    public CommandData TargetJob { get; private set; } // [추가] 이 보석이 효과를 줄 대상 직업

    public GemInstance(GemSO baseData, CommandData targetJob)
    {
        if (baseData == null)
        {
            Debug.LogError("GemInstance: BaseData cannot be null.");
        }

        BaseData = baseData;
        TargetJob = targetJob; // [추가] 직업 할당
        InstanceId = Guid.NewGuid().ToString();

        SubSlots = UnityEngine.Random.Range(0, 3); 
        RandomModifiers = new List<StatModifier>(); 
    }

    /// <summary>
    /// 루트 노드와 같이 특정 슬롯 개수가 강제되어야 하는 경우에 사용되는 생성자입니다.
    /// </summary>
    public GemInstance(GemSO baseData, CommandData targetJob, int forcedSubSlots) : this(baseData, targetJob)
    {
        SubSlots = forcedSubSlots;
    }
}

/// <summary>
/// 스탯 변화를 나타내는 일반적인 구조체 또는 클래스입니다.
/// </summary>
[Serializable] // Unity Inspector에서 보이도록 Serializable 추가
public class StatModifier
{
    public StatType Type; // GemSO.cs에 정의된 StatType 사용
    public float Value;

    public StatModifier(StatType type, float value)
    {
        Type = type;
        Value = value;
    }
}
