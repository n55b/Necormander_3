using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

/// <summary>
/// GemTreeUI에 의해 관리되며, 현재 활성화된 시너지 목록과 단계를 표시하는 클래스입니다.
/// </summary>
public class GemSynergyDisplayUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject synergyItemPrefab; // 시너지 항목 하나를 나타내는 프리팹
    [SerializeField] private Transform contentParent;      // 항목들이 생성될 부모

    private List<GameObject> _spawnedItems = new List<GameObject>();

    /// <summary>
    /// GemTreeUI에서 호출하여 UI를 갱신합니다.
    /// </summary>
    public void RefreshSynergyList()
    {
        ClearItems();

        if (InventoryManager.Instance == null) return;

        var globalStats = InventoryManager.Instance.GlobalGemStats;
        
        foreach (GemSynergyGroup group in System.Enum.GetValues(typeof(GemSynergyGroup)))
        {
            if (group == GemSynergyGroup.Base) continue;

            // 1. 해당 그룹의 시너지 단계들 표시 (누적된 모든 단계 노출)
            int count = globalStats.SynergyCounts.TryGetValue(group, out int c) ? c : 0;
            int maxLevel = GemSynergyLogic.GetLevel(count);

            if (maxLevel >= 1)
            {
                // 달성한 모든 레벨을 개별 항목으로 생성
                for (int lv = 1; lv <= maxLevel; lv++)
                {
                    string synergyName = group.ToString();
                    string description = GetSingleLevelDescription(group, lv);
                    CreateSynergyItem(synergyName, lv, count, description, GetSynergyColor(group));
                }
            }

            // 2. 해당 그룹에 속한 유니크 효과들 표시
            foreach (var uniqueKvp in globalStats.UniqueEffectCounts)
            {
                var unique = uniqueKvp.Key;
                var uniqueCount = uniqueKvp.Value;
                if (GetSynergyGroupOfUnique(unique) == group && uniqueCount > 0)
                {
                    CreateSynergyItem("Unique", 0, uniqueCount, GetUniqueDescription(unique), Color.yellow);
                }
            }
        }

        // [보강] 텍스트 크기 계산 및 레이아웃 갱신을 위해 코루틴 실행
        StopAllCoroutines();
        StartCoroutine(RefreshLayoutRoutine());
    }

    private System.Collections.IEnumerator RefreshLayoutRoutine()
    {
        // 1. UI 시스템이 텍스트 변경을 인지할 시간 부여
        yield return new WaitForEndOfFrame();

        if (contentParent is RectTransform rect)
        {
            // 2. 캔버스 강제 업데이트 및 레이아웃 재계산
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }
    }

    private void CreateSynergyItem(string name, int level, int count, string desc, Color color)
    {
        if (synergyItemPrefab == null) return;

        GameObject item = Instantiate(synergyItemPrefab, contentParent);
        _spawnedItems.Add(item);

        var texts = item.GetComponentsInChildren<TextMeshProUGUI>();
        
        var nameText = texts.FirstOrDefault(t => t.name.Contains("Name"));
        if (nameText != null)
        {
            string lvStr = level > 0 ? $"LV.{level}" : "UNIQUE";
            nameText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>[{name}]</color> {lvStr} <size=70%>({count})</size>";
            nameText.ForceMeshUpdate(); // 즉시 크기 계산 강제
        }

        var descText = texts.FirstOrDefault(t => t.name.Contains("Desc"));
        if (descText != null)
        {
            descText.text = desc;
            descText.ForceMeshUpdate(); // 즉시 크기 계산 강제
        }
    }

    private string GetSingleLevelDescription(GemSynergyGroup group, int level)
    {
        switch (group)
        {
            case GemSynergyGroup.Poison:
                if (level == 1) return "Poison duration extended to +5s.";
                if (level == 2) return "Basic attacks apply +1 extra Poison stack.";
                if (level == 3) return "Poison tick interval reduced to 3s (0.6x).";
                break;
            case GemSynergyGroup.Priest_Chill:
                if (level == 1) return "Slow effect per stack increased by 5%.";
                if (level == 2) return "Refund 25 Chill stacks upon freezing.";
                if (level == 3) return "Freezing deals true damage based on max HP.";
                break;
            case GemSynergyGroup.BloodPop:
                if (level == 1) return "BloodPop damage multiplier increased to 0.5.";
                if (level == 2) return "BloodPop explosion radius increased by 1.5x.";
                break;
            case GemSynergyGroup.Priest_Aging:
                if (level == 1) return "Slow effect per stack increased by 5%.";
                if (level == 2) return "Maximum Aging stacks increased to 120.";
                if (level == 3) return "Senile enemies take 12% extra damage.";
                break;
            case GemSynergyGroup.Priest_Corrosion:
                if (level == 1) return "Corrosion damage amplification increased to 25%.";
                if (level == 2) return "Corrosion damage amplification increased to 40%.";
                break;
            case GemSynergyGroup.Execution:
                if (level == 1) return "Basic attacks and throws apply 1 Execute stack.";
                break;
            case GemSynergyGroup.Warrior_Executioner:
                if (level == 1) return "Warrior throw damage +20% to enemies below 50% HP.";
                if (level == 2) return "Warrior throw HP cost reduced by 30%.";
                if (level == 3) return "Throw damage amplified up to +50% based on enemy missing HP.";
                break;
            case GemSynergyGroup.Archer_ArcheryPrinciples:
                if (level == 1) return "Fires a piercing arrow after every 5 missed attacks.";
                break;
            case GemSynergyGroup.Shield_Guardian:
                if (level == 1) return "Throwing Shieldbearer deals 20% of shield as AoE damage.";
                if (level == 2) return "Shield expiration deals true damage to nearby enemies.";
                if (level == 3) return "Converts 15% of excess healing into Shield.";
                if (level == 4) return "All stats +15% while shielded.";
                break;
            case GemSynergyGroup.Spearman_Vanguard:
                if (level == 1) return "Dash distance +30%, dash speed +20%.";
                if (level == 2) return "Dash deals 150% physical damage to collided enemies.";
                if (level == 3) return "Allies touched by dash gain +15% move/evasion speed.";
                if (level == 4) return "Player becomes invincible during dash.";
                break;
        }
        return "New power unlocked.";
    }

    private GemSynergyGroup GetSynergyGroupOfUnique(GemUniqueType type)
    {
        switch (type)
        {
            case GemUniqueType.LethalPoison:
            case GemUniqueType.LethalDose: return GemSynergyGroup.Poison;
            case GemUniqueType.AchingBones:
            case GemUniqueType.SlowlyFreezingFlower: return GemSynergyGroup.Priest_Chill;
            case GemUniqueType.ExplodingFlesh: return GemSynergyGroup.BloodPop;
            case GemUniqueType.NoCountryForOldMen: return GemSynergyGroup.Priest_Aging;
            default: return GemSynergyGroup.Base;
        }
    }

    private string GetUniqueDescription(GemUniqueType type)
    {
        switch (type)
        {
            case GemUniqueType.LethalPoison: return "Ally throw deals extra damage based on target's Poison stacks.";
            case GemUniqueType.LethalDose: return "Poison tick interval -50%.";
            case GemUniqueType.PoisonContagion: return "Spreads 50% of remaining Poison stacks to 1 enemy on death.";
            case GemUniqueType.WoundInfection: return "Basic attacks reduce Poison tick timer by 0.1s.";
            case GemUniqueType.GreenFluid: return "Poison tick has 30% chance to drop a Poison Potion.";
            case GemUniqueType.PoisonHost: return "Spreads 10% of Poison stacks to nearby enemies every 3s.";
            case GemUniqueType.PoisonFootprint: return "All allies' movement speed +15%.";

            case GemUniqueType.ColdBloodedHunter: return "Chilled enemies' move speed reduced by extra 10%.";
            case GemUniqueType.AchingBones: return "Chill stacks do not accumulate while frozen.";
            case GemUniqueType.SlowlyFreezingFlower: return "Max Chill stacks +10.";
            case GemUniqueType.ShatterIcicle: return "Throwing at frozen enemy causes AoE fixed damage.";
            case GemUniqueType.Frostbreaker: return "Deals 5% extra damage to chilled enemies.";

            case GemUniqueType.NoCountryForOldMen: return "Aging max stacks +100, triggers instant kill at max.";
            case GemUniqueType.Goryeojang: return "Spawns slow/aging aura around highest Aging enemy.";
            case GemUniqueType.DimVision: return "Enemies with 50+ Aging stacks gain 25% miss chance.";
            case GemUniqueType.AgingHunter: return "Ally attack speed +10% per 100 total Aging stacks on field.";

            case GemUniqueType.PriestsCantAttack: return "Healing received +20% when Corrosion synergy is lv 2.";
            case GemUniqueType.DoubleCorrosion: return "Corrosion damage amplification +10%.";
            case GemUniqueType.WeaponCorrosion: return "Corroded enemies' attack power -10%.";
            case GemUniqueType.RustedArmor: return "Reflects 5% max HP damage per 5 ally basic attacks on corroded enemies.";

            case GemUniqueType.ExplodingFlesh: return "Explosion spreads 25% of consumed BloodPop stacks.";
            case GemUniqueType.BloodArmor: return "Grants shield to allies upon BloodPop explosion.";
            case GemUniqueType.MeltingCorpse: return "Creates poison puddle upon BloodPop explosion.";
            case GemUniqueType.MutualDestruction: return "Pulls nearby enemies into BloodPop explosion center.";
            case GemUniqueType.AmIExplodingToo: return "Applies Weakness (20% extra damage) for 5s after explosion.";
            case GemUniqueType.ImprovisedExplosive: return "BloodPop damage +10%.";

            case GemUniqueType.Guillotine: return "Execution threshold increased by 10%.";
            case GemUniqueType.Fear: return "Executing an enemy fears nearby enemies for 1s.";

            case GemUniqueType.WarriorBallistics1: return "Parabolic throw damage +10%.";
            case GemUniqueType.WarriorBallistics2: return "Straight throw damage +10%.";
            case GemUniqueType.WarriorBallistics3: return "Single-target throw damage +15%.";
            case GemUniqueType.CrushingPower: return "Overkill throw damage heals Warrior.";
            case GemUniqueType.WarriorsMedal: return "Warrior basic stats +15%.";
            case GemUniqueType.TrackingEye: return "Successive throws on same target deal +12% damage.";
            case GemUniqueType.FanaticRage: return "Basic attacks heal Warrior for 3% of damage dealt.";

            case GemUniqueType.HuntersHerding: return "AoE damage +5% per enemy hit.";
            case GemUniqueType.SpreadShot: return "Throw blast radius +20%.";
            case GemUniqueType.AimedStrike: return "+20% damage to enemies in dead center.";
            case GemUniqueType.WindDirection: return "Throw damage increases based on flight time (up to 33%).";
            case GemUniqueType.SupportFire: return "Throw AoE damage +15%.";
            case GemUniqueType.TensionPower: return "Attack speed -25%, Attack damage +25%.";
            case GemUniqueType.UnseenMiss: return "Records when Archer's basic attack misses.";
            case GemUniqueType.ReflectingNature: return "Upon miss, +15% ATK (HP < 50%) or +15% ASPD (HP >= 50%).";

            case GemUniqueType.SturdyShield: return "Grants shield equal to 50% max HP upon entering a room.";
            case GemUniqueType.ShieldsWillCourage: return "Allies gain +12% ATK SPD for 10s upon entering a room.";
            case GemUniqueType.ShieldsWillWind: return "Allies gain +14% Move SPD for 10s upon entering a room.";
            case GemUniqueType.ShieldsWillClash: return "Allies gain +8% ATK for 10s upon entering a room.";
            case GemUniqueType.ThornArmor: return "Reflects 2% of enemy's current HP as fixed damage upon hit.";
            case GemUniqueType.HeavyArmor: return "Throw deals 14% of shield amount as physical damage.";
            case GemUniqueType.TwistedGround: return "Throw deals 20% of shield amount as AoE damage.";
            case GemUniqueType.AuraOfPatience: return "Grants shield (18% max HP) to nearby allies every 5s.";
            case GemUniqueType.AuraOfOverwhelming: return "Slows nearby enemies by 35% every 5s.";

            case GemUniqueType.Vanguard: return "Enables player dash when throwing Spearman.";
            case GemUniqueType.SpearSwiftness: return "Spearman throw flight time reduced by 33%.";
            case GemUniqueType.IronMountain: return "Basic attacks knockback enemies; +damage on wall collision.";
            case GemUniqueType.ThousandStabs: return "Spearman basic attack damage +3%.";

            default: return "Unique ability active";
        }
    }

    private Color GetSynergyColor(GemSynergyGroup group)
    {
        switch (group)
        {
            case GemSynergyGroup.Poison: return new Color(0.2f, 0.8f, 0.2f);
            case GemSynergyGroup.Priest_Chill: return new Color(0.3f, 0.6f, 1.0f);
            case GemSynergyGroup.Execution: return new Color(1.0f, 0.3f, 0.1f);
            case GemSynergyGroup.BloodPop: return new Color(1.0f, 0.0f, 1.0f);
            case GemSynergyGroup.Priest_Aging: return new Color(0.7f, 0.5f, 0.5f);
            case GemSynergyGroup.Priest_Corrosion: return new Color(1.0f, 0.8f, 0.0f);
            default: return Color.white;
        }
    }

    private void ClearItems()
    {
        foreach (var item in _spawnedItems) if (item != null) Destroy(item);
        _spawnedItems.Clear();
    }
}
