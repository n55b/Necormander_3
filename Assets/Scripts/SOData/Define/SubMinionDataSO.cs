using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 서브 소환수. 실체화하지 않고 장착만으로 상시 스탯/패시브를 준다.
/// 슬롯은 1칸(InventoryManager.SLOT_SUB). 액티브도 대쉬/평타 변화도 없다.
/// </summary>
[CreateAssetMenu(fileName = "NewSubMinion", menuName = "Necromancer/Data/Sub Minion")]
public class SubMinionDataSO : MinionDataSO
{
    [Header("상시 패시브")]
    [Tooltip("장착만으로 항상 켜지는 스탯/패시브.")]
    public MinionSubPassive subPassive = new MinionSubPassive();

    // ── 카드/툴팁 ────────────────────────────────────────────────────
    // 서브는 액티브가 없어서 내세울 스킬 설명이 없다. 그래서 패시브 수치에서 직접 만들어낸다.
    // 손으로 쓴 문구를 쓰면 밸런스를 만질 때마다 카드가 거짓말을 하게 된다.

    public override string ResolveDescription() => base.ResolveDescription() ?? DescribeSubPassive();

    private string DescribeSubPassive()
    {
        if (subPassive == null) return null;

        var lines = new List<string>();
        void Add(float v, string fmt) { if (v > 0f) lines.Add(string.Format(fmt, v.ToString("0.##"))); }

        Add(subPassive.maxHpBonus, "최대 체력 +{0}");
        Add(subPassive.atkIntervalReduction, "공격 간격 -{0}초");   // 간격이라 낮을수록 빠름
        Add(subPassive.basicAttackDamageBonus, "평타 피해 +{0}");
        Add(subPassive.healOnAcquire, "획득 시 체력 {0} 회복");
        Add(subPassive.healOnRoomClear, "방 클리어마다 체력 {0} 회복");

        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }
}
