using UnityEngine;

/// <summary>
/// 아이템 효과 한 조각. ItemSO.effects 에 [SerializeReference] 로 담기므로 아이템 하나가
/// 여러 개를 가질 수 있고, 새 종류를 추가할 때 ItemSO 의 필드를 늘리지 않아도 된다.
///
/// 지금 있는 종류:
///   · ItemStatEffect        — 주머니에 있는 동안 상시 플레이어 스탯 보정 (최대 체력 +10 등)
///   · ItemRoomClearHealEffect — 방 클리어 시 회복 (고정치 또는 최대 체력 비율)
///
/// 강화/성장 개념은 없다 — 아이템 효과는 고정이다(장비의 enhanceLevel 과 다른 점).
/// </summary>
[System.Serializable]
public abstract class ItemEffect
{
    [Tooltip("인스펙터 식별용 라벨(표기 전용).")]
    public string label;
}

[System.Serializable]
public class ItemRoomEntryShieldEffect : ItemEffect
{
    [Min(0f)] public float amount = 6f;
}

[System.Serializable]
public class ItemDashChargeEffect : ItemEffect
{
    [Min(1)] public int copiesPerCharge = 3;
}

/// <summary>소지 여부만 본다. 여러 개 소지해도 초과 확률을 중복 변환하지 않는다.</summary>
[System.Serializable]
public class ItemCritOverflowEffect : ItemEffect { }

/// <summary>
/// 주머니에 들어 있는 동안 플레이어 스탯을 보정한다.
/// 실제 적용/회수는 ItemPouch 가 자기 자신을 source 키로 써서 통째로 다시 계산한다.
/// </summary>
[System.Serializable]
public class ItemStatEffect : ItemEffect
{
    public StatType statType = StatType.Health;

    [Tooltip("보정치. isPercent 면 0.1 = +10%, 아니면 고정치.")]
    public float value = 10f;

    [Tooltip("true=비율(합연산), false=고정치.")]
    public bool isPercent = false;

    public void Apply(CharacterStat stat, object source)
    {
        if (stat == null) return;
        if (isPercent) stat.Mods.AddPercent(source, statType, value);
        else           stat.Mods.AddFlat(source, statType, value);
    }
}

/// <summary>
/// 방을 클리어할 때 플레이어를 회복시킨다.
///
/// [26/09/09] 예전엔 RoomInstance.MarkCleared 가 아이템과 무관하게 무조건 10 회복시켰다.
/// 그 로직을 이 효과로 옮겼다 — 이 효과를 가진 아이템이 주머니에 있어야만 회복한다.
/// 실제 발동은 ItemPouch 가 RoomInstance.OnAnyRoomCleared 를 구독해서 처리한다.
///
/// 같은 효과를 가진 아이템을 여러 개 들고 있으면 회복량은 그대로 합산된다(칸마다 1회).
/// </summary>
[System.Serializable]
public class ItemRoomClearHealEffect : ItemEffect
{
    [Tooltip("회복량. isPercent 면 0.1 = 최대 체력의 10%, 아니면 고정치.")]
    public float value = 10f;

    [Tooltip("true=최대 체력 비율, false=고정치.")]
    public bool isPercent = false;

    /// <summary>이번 방 클리어로 회복시킬 양. 음수는 0 으로 막는다(회복 효과가 딜을 넣으면 안 된다).</summary>
    public float ResolveAmount(CharacterHealth health)
    {
        if (health == null) return 0f;
        float amount = isPercent ? health.MaxHP * value : value;
        return Mathf.Max(0f, amount);
    }
}
