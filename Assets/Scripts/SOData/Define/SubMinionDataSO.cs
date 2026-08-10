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

    [Header("우클릭")]
    [Tooltip("이 소환수가 플레이어의 우클릭에 부여하는 행동(패링/카운터/가드). " +
             "None 이면 우클릭이 아무것도 하지 않는다 — 서브를 안 낀 것과 같다.")]
    public MinionRightClick rightClick = new MinionRightClick();

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
        Add(subPassive.atkSpeedBonus * 100f, "공격 속도 +{0}%");
        Add(subPassive.basicAttackDamageBonus, "평타 피해 +{0}");
        Add(subPassive.skillCooldownReduction * 100f, "스킬 쿨타임 감소 +{0}%");
        Add(subPassive.healOnSkillHit, "스킬 적중 시 체력 {0} 회복");
        Add(subPassive.healOnAcquire, "획득 시 체력 {0} 회복");
        Add(subPassive.healOnRoomClear, "방 클리어마다 체력 {0} 회복");

        string rc = rightClick != null ? rightClick.Describe() : null;
        if (!string.IsNullOrEmpty(rc)) lines.Add(rc);

        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }
}
