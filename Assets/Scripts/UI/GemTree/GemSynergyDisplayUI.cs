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
                    string synergyName = GetUIString("UI Text Table", $"Synergy_{group}", group.ToString());
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
                    string uniqueLabel = GetUIString("UI Text Table", "UI_Unique", "유니크");
                    CreateSynergyItem(uniqueLabel, 0, uniqueCount, GetUniqueDescription(unique), Color.yellow);
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
        string fallback = "New power unlocked.";
        switch (group)
        {
            case GemSynergyGroup.Poison:
                if (level == 1) fallback = "Poison duration extended to +5s.";
                if (level == 2) fallback = "Basic attacks apply +1 extra Poison stack.";
                if (level == 3) fallback = "Poison tick interval reduced to 3s (0.6x).";
                break;
            case GemSynergyGroup.Priest_Chill:
                if (level == 1) fallback = "Slow effect per stack increased by 5%.";
                if (level == 2) fallback = "Refund 25 Chill stacks upon freezing.";
                if (level == 3) fallback = "Freezing deals true damage based on max HP.";
                break;
            case GemSynergyGroup.BloodPop:
                if (level == 1) fallback = "BloodPop damage multiplier increased to 0.5.";
                if (level == 2) fallback = "BloodPop explosion radius increased by 1.5x.";
                break;
            case GemSynergyGroup.Priest_Aging:
                if (level == 1) fallback = "Slow effect per stack increased by 5%.";
                if (level == 2) fallback = "Maximum Aging stacks increased to 120.";
                if (level == 3) fallback = "Senile enemies take 12% extra damage.";
                break;
            case GemSynergyGroup.Priest_Corrosion:
                if (level == 1) fallback = "Corrosion damage amplification increased to 25%.";
                if (level == 2) fallback = "Corrosion damage amplification increased to 40%.";
                break;
            case GemSynergyGroup.Execution:
                if (level == 1) fallback = "Basic attacks and throws apply 1 Execute stack.";
                break;
            case GemSynergyGroup.Warrior_Executioner:
                if (level == 1) fallback = "Warrior throw damage +20% to enemies below 50% HP.";
                if (level == 2) fallback = "Warrior throw HP cost reduced by 30%.";
                if (level == 3) fallback = "Throw damage amplified up to +50% based on enemy missing HP.";
                break;
            case GemSynergyGroup.Archer_ArcheryPrinciples:
                if (level == 1) fallback = "Fires a piercing arrow after every 5 missed attacks.";
                break;
            case GemSynergyGroup.Shield_Guardian:
                if (level == 1) fallback = "Throwing Shieldbearer deals 20% of shield as AoE damage.";
                if (level == 2) fallback = "Shield expiration deals true damage to nearby enemies.";
                if (level == 3) fallback = "Converts 15% of excess healing into Shield.";
                if (level == 4) fallback = "All stats +15% while shielded.";
                break;
            case GemSynergyGroup.Spearman_Vanguard:
                if (level == 1) fallback = "Dash distance +30%, dash speed +20%.";
                if (level == 2) fallback = "Dash deals 150% physical damage to collided enemies.";
                if (level == 3) fallback = "Allies touched by dash gain +15% move/evasion speed.";
                if (level == 4) fallback = "Player becomes invincible during dash.";
                break;
            case GemSynergyGroup.Stamina:
                if (level == 1) fallback = "Stamina regen +1 (Out of combat & Base).";
                if (level == 2) fallback = "Max stamina +20.";
                if (level == 3) fallback = "Stamina regen up to 2x at 0 stamina.";
                break;
            case GemSynergyGroup.Fastball:
                if (level == 1) fallback = "Direct throw efficiency +50%.";
                if (level == 2) fallback = "Direct throw efficiency +75%.";
                break;
        }
        return GetRewardString($"Synergy_{group}_Lv{level}", fallback);
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
            case GemUniqueType.CatchBreath:
            case GemUniqueType.HarvestOfDeath:
            case GemUniqueType.BasicFitness:
            case GemUniqueType.EndlessVitality:
            case GemUniqueType.OverflowingThrow:
            case GemUniqueType.OrderedBreath:
            case GemUniqueType.ThrowOverload:
            case GemUniqueType.MasterOfRapidFire:
            case GemUniqueType.LimitBreak:
            case GemUniqueType.EfficientThrow: return GemSynergyGroup.Stamina;
            case GemUniqueType.SetPosition:
            case GemUniqueType.Windup:
            case GemUniqueType.MagicPitchFireball:
            case GemUniqueType.MagicPitchArirangBall:
            case GemUniqueType.Closer: return GemSynergyGroup.Fastball;
            default: return GemSynergyGroup.Base;
        }
    }

    private string GetUniqueDescription(GemUniqueType type)
    {
        string fallback = "Unique ability active";
        switch (type)
        {
            // [스태미나 유니크 10종]
            case GemUniqueType.CatchBreath: fallback = "Out of combat: Stamina regen +1."; break;
            case GemUniqueType.HarvestOfDeath: fallback = "Dead minions grant +1 stamina regen until revived."; break;
            case GemUniqueType.BasicFitness: fallback = "Max stamina +20."; break;
            case GemUniqueType.EndlessVitality: fallback = "Stamina regen +0.5."; break;
            case GemUniqueType.OverflowingThrow: fallback = "Throw cost +5, Throw effect +25%."; break;
            case GemUniqueType.OrderedBreath: fallback = "Throw cost -3."; break;
            case GemUniqueType.ThrowOverload: fallback = "Throw effect increases 2% per 1 stamina cost."; break;
            case GemUniqueType.MasterOfRapidFire: fallback = "Throw cost -7, Throw effect -30%."; break;
            case GemUniqueType.LimitBreak: fallback = "Stamina limit drops to -50. Regen is halved if < 0."; break;
            case GemUniqueType.EfficientThrow: fallback = "Max stamina -40, Throw effect +60%."; break;

            // [강속구 유니크 5종]
            case GemUniqueType.SetPosition: fallback = "Charge time -0.1s, Direct throw efficiency -2%."; break;
            case GemUniqueType.Windup: fallback = "Charge time +0.5s, Direct throw efficiency +2%."; break;
            case GemUniqueType.MagicPitchFireball: fallback = "Direct throw efficiency +10% per 1s charge time."; break;
            case GemUniqueType.MagicPitchArirangBall: fallback = "Charge time -0.5s, Direct throw efficiency -40%."; break;
            case GemUniqueType.Closer: fallback = "Max charge time extended to 5s."; break;

            case GemUniqueType.LethalPoison: fallback = "Ally throw deals extra damage based on target's Poison stacks."; break;
            case GemUniqueType.LethalDose: fallback = "Poison tick interval -50%."; break;
            case GemUniqueType.PoisonContagion: fallback = "Spreads 50% of remaining Poison stacks to 1 enemy on death."; break;
            case GemUniqueType.WoundInfection: fallback = "Basic attacks reduce Poison tick timer by 0.1s."; break;
            case GemUniqueType.GreenFluid: fallback = "Poison tick has 30% chance to drop a Poison Potion."; break;
            case GemUniqueType.PoisonHost: fallback = "Spreads 10% of Poison stacks to nearby enemies every 3s."; break;
            case GemUniqueType.PoisonFootprint: fallback = "All allies' movement speed +15%."; break;

            case GemUniqueType.ColdBloodedHunter: fallback = "Chilled enemies' move speed reduced by extra 10%."; break;
            case GemUniqueType.AchingBones: fallback = "Chill stacks do not accumulate while frozen."; break;
            case GemUniqueType.SlowlyFreezingFlower: fallback = "Max Chill stacks +10."; break;
            case GemUniqueType.ShatterIcicle: fallback = "Throwing at frozen enemy causes AoE fixed damage."; break;
            case GemUniqueType.Frostbreaker: fallback = "Deals 5% extra damage to chilled enemies."; break;

            case GemUniqueType.NoCountryForOldMen: fallback = "Aging max stacks +100, triggers instant kill at max."; break;
            case GemUniqueType.Goryeojang: fallback = "Spawns slow/aging aura around highest Aging enemy."; break;
            case GemUniqueType.DimVision: fallback = "Enemies with 50+ Aging stacks gain 25% miss chance."; break;
            case GemUniqueType.AgingHunter: fallback = "Ally attack speed +10% per 100 total Aging stacks on field."; break;

            case GemUniqueType.PriestsCantAttack: fallback = "Healing received +20% when Corrosion synergy is lv 2."; break;
            case GemUniqueType.DoubleCorrosion: fallback = "Corrosion damage amplification +10%."; break;
            case GemUniqueType.WeaponCorrosion: fallback = "Corroded enemies' attack power -10%."; break;
            case GemUniqueType.RustedArmor: fallback = "Reflects 5% max HP damage per 5 ally basic attacks on corroded enemies."; break;

            case GemUniqueType.ExplodingFlesh: fallback = "Explosion spreads 25% of consumed BloodPop stacks."; break;
            case GemUniqueType.BloodArmor: fallback = "Grants shield to allies upon BloodPop explosion."; break;
            case GemUniqueType.MeltingCorpse: fallback = "Creates poison puddle upon BloodPop explosion."; break;
            case GemUniqueType.MutualDestruction: fallback = "Pulls nearby enemies into BloodPop explosion center."; break;
            case GemUniqueType.AmIExplodingToo: fallback = "Applies Weakness (20% extra damage) for 5s after explosion."; break;
            case GemUniqueType.ImprovisedExplosive: fallback = "BloodPop damage +10%."; break;

            case GemUniqueType.Guillotine: fallback = "Execution threshold increased by 10%."; break;
            case GemUniqueType.Fear: fallback = "Executing an enemy fears nearby enemies for 1s."; break;

            case GemUniqueType.WarriorBallistics1: fallback = "Parabolic throw damage +10%."; break;
            case GemUniqueType.WarriorBallistics2: fallback = "Straight throw damage +10%."; break;
            case GemUniqueType.WarriorBallistics3: fallback = "Single-target throw damage +15%."; break;
            case GemUniqueType.CrushingPower: fallback = "Overkill throw damage heals Warrior."; break;
            case GemUniqueType.WarriorsMedal: fallback = "Warrior basic stats +15%."; break;
            case GemUniqueType.TrackingEye: fallback = "Successive throws on same target deal +12% damage."; break;
            case GemUniqueType.FanaticRage: fallback = "Basic attacks heal Warrior for 3% of damage dealt."; break;

            case GemUniqueType.HuntersHerding: fallback = "AoE damage +5% per enemy hit."; break;
            case GemUniqueType.SpreadShot: fallback = "Throw blast radius +20%."; break;
            case GemUniqueType.AimedStrike: fallback = "+20% damage to enemies in dead center."; break;
            case GemUniqueType.WindDirection: fallback = "Throw damage increases based on flight time (up to 33%)."; break;
            case GemUniqueType.SupportFire: fallback = "Throw AoE damage +15%."; break;
            case GemUniqueType.TensionPower: fallback = "Attack speed -25%, Attack damage +25%."; break;
            case GemUniqueType.UnseenMiss: fallback = "Records when Archer's basic attack misses."; break;
            case GemUniqueType.ReflectingNature: fallback = "Upon miss, +15% ATK (HP < 50%) or +15% ASPD (HP >= 50%)."; break;

            case GemUniqueType.SturdyShield: fallback = "Grants shield equal to 50% max HP upon entering a room."; break;
            case GemUniqueType.ShieldsWillCourage: fallback = "Allies gain +12% ATK SPD for 10s upon entering a room."; break;
            case GemUniqueType.ShieldsWillWind: fallback = "Allies gain +14% Move SPD for 10s upon entering a room."; break;
            case GemUniqueType.ShieldsWillClash: fallback = "Allies gain +8% ATK for 10s upon entering a room."; break;
            case GemUniqueType.ThornArmor: fallback = "Reflects 2% of enemy's current HP as fixed damage upon hit."; break;
            case GemUniqueType.HeavyArmor: fallback = "Throw deals 14% of shield amount as physical damage."; break;
            case GemUniqueType.TwistedGround: fallback = "Throw deals 20% of shield amount as AoE damage."; break;
            case GemUniqueType.AuraOfPatience: fallback = "Grants shield (18% max HP) to nearby allies every 5s."; break;
            case GemUniqueType.AuraOfOverwhelming: fallback = "Slows nearby enemies by 35% every 5s."; break;

            case GemUniqueType.Vanguard: fallback = "Enables player dash when throwing Spearman."; break;
            case GemUniqueType.SpearSwiftness: fallback = "Spearman throw flight time reduced by 33%."; break;
            case GemUniqueType.IronMountain: fallback = "Basic attacks knockback enemies; +damage on wall collision."; break;
            case GemUniqueType.ThousandStabs: fallback = "Spearman basic attack damage +3%."; break;

            default: fallback = "Unique ability active"; break;
        }
        return GetRewardString($"UniqueEffect_{type}", fallback);
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
            case GemSynergyGroup.Stamina: return new Color(0.1f, 0.8f, 0.2f); // Greenish
            case GemSynergyGroup.Fastball: return new Color(1.0f, 0.5f, 0.0f); // Amber
            default: return Color.white;
        }
    }

    private void ClearItems()
    {
        foreach (var item in _spawnedItems) if (item != null) Destroy(item);
        _spawnedItems.Clear();
    }

    private string GetUIString(string table, string key, string fallback)
    {
        var op = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.GetLocalizedStringAsync(table, key);
        if (op.IsDone)
        {
            return (string.IsNullOrEmpty(op.Result) || op.Result.Contains("No translation")) ? fallback : op.Result;
        }
        
        var handle = op;
        handle.WaitForCompletion();
        return (string.IsNullOrEmpty(handle.Result) || handle.Result.Contains("No translation")) ? fallback : handle.Result;
    }
    
    private string GetRewardString(string key, string fallback)
    {
        return GetUIString("Reward Text Table", key, fallback);
    }
}
