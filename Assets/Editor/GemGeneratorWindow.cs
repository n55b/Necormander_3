#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public class GemGeneratorWindow
{
    [MenuItem("Necromancer/Generate All Unique Gems")]
    public static void GenerateGems()
    {
        string poisonPath = "Assets/SOData/Rewards/Gems/Poison";
        string chillPath = "Assets/SOData/Rewards/Gems/Chill";
        string executionPath = "Assets/SOData/Rewards/Gems/Execution";
        string bloodPopPath = "Assets/SOData/Rewards/Gems/BloodPop";
        string agingPath = "Assets/SOData/Rewards/Gems/Aging";

        // 폴더가 없으면 생성
        if (!AssetDatabase.IsValidFolder("Assets/SOData")) AssetDatabase.CreateFolder("Assets", "SOData");
        if (!AssetDatabase.IsValidFolder("Assets/SOData/Rewards")) AssetDatabase.CreateFolder("Assets/SOData", "Rewards");
        if (!AssetDatabase.IsValidFolder("Assets/SOData/Rewards/Gems")) AssetDatabase.CreateFolder("Assets/SOData/Rewards", "Gems");
        
        if (!AssetDatabase.IsValidFolder(poisonPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Poison");
        if (!AssetDatabase.IsValidFolder(chillPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Chill");
        if (!AssetDatabase.IsValidFolder(executionPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Execution");
        if (!AssetDatabase.IsValidFolder(bloodPopPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "BloodPop");
        if (!AssetDatabase.IsValidFolder(agingPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Aging");

        // Poison 6
        CreateGemSO(poisonPath, "Gem_Unique_PoisonHost", "Poison Host", "Spreads poison stacks in an area every 3 seconds.", GemUniqueType.PoisonHost, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "Gem_Unique_PoisonFootprint", "Poison Footprint", "Creates a poison trail. Allies gain 15% movement speed.", GemUniqueType.PoisonFootprint, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "Gem_Unique_PoisonFlask", "Poison Flask", "Increases base throw poison stacks by 1.", GemUniqueType.PoisonFlask, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "Gem_Unique_WoundInfection", "Wound Infection", "Basic attacks reduce poison tick interval.", GemUniqueType.WoundInfection, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "Gem_Unique_PoisonContagion", "Poison Contagion", "Spreads poison to nearby enemies upon death.", GemUniqueType.PoisonContagion, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "Gem_Unique_GreenFluid", "Green Fluid", "Spawns a potion on poison tick.", GemUniqueType.GreenFluid, SynergyCategory.Common, GemSynergyGroup.Poison);

        // Chill 5
        CreateGemSO(chillPath, "Gem_Unique_ColdBloodedHunter", "Cold-Blooded Hunter", "Applies an additional 10% slow to chilled enemies.", GemUniqueType.ColdBloodedHunter, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);
        CreateGemSO(chillPath, "Gem_Unique_BitingWind", "Biting Wind", "Applies 1 chill stack to nearby enemies.", GemUniqueType.BitingWind, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);
        CreateGemSO(chillPath, "Gem_Unique_Frostbreaker", "Frostbreaker", "Deals 5% additional damage to chilled enemies.", GemUniqueType.Frostbreaker, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);
        CreateGemSO(chillPath, "Gem_Unique_AbsoluteZero", "Absolute Zero", "Applies 50 chill stacks in an area upon freezing (Once per room).", GemUniqueType.AbsoluteZero, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);
        CreateGemSO(chillPath, "Gem_Unique_ShatterIcicle", "Shatter Icicle", "Throws remove freeze and deal 50% additional damage.", GemUniqueType.ShatterIcicle, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);

        // Execution 2
        CreateGemSO(executionPath, "Gem_Unique_ExecutionFear", "Fear", "Fears nearby enemies for 1 second upon executing a target.", GemUniqueType.Fear, SynergyCategory.Common, GemSynergyGroup.Execution);
        CreateGemSO(executionPath, "Gem_Unique_ExecutionGuillotine", "Guillotine", "Relaxes the execute threshold by 10%.", GemUniqueType.Guillotine, SynergyCategory.Common, GemSynergyGroup.Execution);

        // BloodPop 6
        CreateGemSO(bloodPopPath, "Gem_Unique_ImprovisedExplosive", "Improvised Explosive", "Increases Blood Pop damage by 10%.", GemUniqueType.ImprovisedExplosive, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "Gem_Unique_GoreParty", "Gore Party", "Heals allies within the explosion radius by the number of Blood Pop stacks.", GemUniqueType.GoreParty, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "Gem_Unique_BloodArmor", "Blood Armor", "Grants allies shields equal to double the Blood Pop stacks upon explosion.", GemUniqueType.BloodArmor, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "Gem_Unique_MeltingCorpse", "Melting Corpse", "Creates a poison area after explosion lasting for 5 seconds.", GemUniqueType.MeltingCorpse, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "Gem_Unique_MutualDestruction", "Mutual Destruction", "Pulls enemies within 1.5x radius before explosion.", GemUniqueType.MutualDestruction, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "Gem_Unique_AmIExplodingToo", "Am I Exploding Too?", "Applies a weakness debuff causing victims to take 20% more damage.", GemUniqueType.AmIExplodingToo, SynergyCategory.Common, GemSynergyGroup.BloodPop);

        // Aging 3
        CreateGemSO(agingPath, "Gem_Unique_Goryeojang", "Goryeojang", "Creates a 20% slow and aging area around the enemy with the highest aging stacks.", GemUniqueType.Goryeojang, SynergyCategory.Priest, GemSynergyGroup.Priest_Aging);
        CreateGemSO(agingPath, "Gem_Unique_DimVision", "Dim Vision", "Increases miss chance by 25% for enemies with 50+ aging stacks.", GemUniqueType.DimVision, SynergyCategory.Priest, GemSynergyGroup.Priest_Aging);
        CreateGemSO(agingPath, "Gem_Unique_AgingHunter", "Aging Hunter", "Increases ally attack speed by 10% per 100 total aging stacks on the field.", GemUniqueType.AgingHunter, SynergyCategory.Priest, GemSynergyGroup.Priest_Aging);

        // Corrosion 4
        string corrosionPath = "Assets/SOData/Rewards/Gems/Corrosion";
        if (!AssetDatabase.IsValidFolder(corrosionPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Corrosion");

        CreateGemSO(corrosionPath, "Gem_Unique_PriestsCantAttack", "Priests Can't Attack!", "Increases ally healing received by 20% when Corrosion synergy is active.", GemUniqueType.PriestsCantAttack, SynergyCategory.Priest, GemSynergyGroup.Priest_Corrosion);
        CreateGemSO(corrosionPath, "Gem_Unique_DoubleCorrosion", "Double Corrosion", "Enhances Corrosion synergy damage amplification by 10%.", GemUniqueType.DoubleCorrosion, SynergyCategory.Priest, GemSynergyGroup.Priest_Corrosion);
        CreateGemSO(corrosionPath, "Gem_Unique_WeaponCorrosion", "Weapon Corrosion", "Reduces attack damage of corroded enemies by 10%.", GemUniqueType.WeaponCorrosion, SynergyCategory.Priest, GemSynergyGroup.Priest_Corrosion);
        CreateGemSO(corrosionPath, "Gem_Unique_RustedArmor", "Rusted Armor", "Reflects 5% max HP fixed damage to corroded enemies every 5 hits received.", GemUniqueType.RustedArmor, SynergyCategory.Priest, GemSynergyGroup.Priest_Corrosion);

        // Warrior 7
        string warriorPath = "Assets/SOData/Rewards/Gems/Warrior";
        if (!AssetDatabase.IsValidFolder(warriorPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Warrior");

        CreateGemSO(warriorPath, "Gem_Unique_WarriorBallistics1", "Warrior Ballistics I", "Increases Warrior's parabolic throw damage by 10%.", GemUniqueType.WarriorBallistics1, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "Gem_Unique_WarriorBallistics2", "Warrior Ballistics II", "Increases Warrior's direct throw damage by 10%.", GemUniqueType.WarriorBallistics2, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "Gem_Unique_WarriorBallistics3", "Warrior Ballistics III", "Increases Warrior's single-target throw damage by 15%.", GemUniqueType.WarriorBallistics3, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "Gem_Unique_CrushingPower", "Crushing Power", "Heals the Warrior for any excess throw damage dealt upon executing an enemy.", GemUniqueType.CrushingPower, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "Gem_Unique_WarriorMedal", "Warrior's Medal", "Increases all basic stats of the Warrior by 15%.", GemUniqueType.WarriorMedal, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "Gem_Unique_TrackingEye", "Tracking Eye", "Deals 12% additional damage when repeatedly hitting the same target with throws.", GemUniqueType.WarriorPursuit, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "Gem_Unique_FanaticRage", "Fanatic Rage", "Grants 3% lifesteal on basic attacks.", GemUniqueType.WarriorFrenzy, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);

        // Archer 8
        string archerPath = "Assets/SOData/Rewards/Gems/Archer";
        if (!AssetDatabase.IsValidFolder(archerPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Archer");

        CreateGemSO(archerPath, "Gem_Unique_ArcherTerrain", "Hunter's Herding", "Increases throw damage by 5% per enemy hit in the radius.", GemUniqueType.ArcherTerrain, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "Gem_Unique_ArcherWind", "Spread Shot", "Increases the Archer's throw radius by 20%.", GemUniqueType.ArcherWind, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "Gem_Unique_ArcherStance", "Aimed Strike", "Deals 20% additional damage to enemies in the center of the throw radius.", GemUniqueType.ArcherStance, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "Gem_Unique_ArcherBreath", "Wind Direction", "Increases throw damage by 1% to 33% based on flight distance.", GemUniqueType.ArcherBreath, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "Gem_Unique_ArcherPush", "Support Fire", "Increases the Archer's AoE throw damage by 15%.", GemUniqueType.ArcherPush, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "Gem_Unique_ArcherTension", "Tension Power", "Reduces basic attack speed by 25% but increases basic attack damage by 25%.", GemUniqueType.ArcherTension, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "Gem_Unique_ArcherMiss", "Unseen Miss", "Acts as a trigger: missing a basic attack primes the next buff. (Requires Reflecting Nature)", GemUniqueType.ArcherMiss, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "Gem_Unique_ArcherReflect", "Reflecting Nature", "Increases Attack by 15% if HP < 50%, or Attack Speed by 15% if HP >= 50% after a miss. (Requires Unseen Miss)", GemUniqueType.ArcherReflect, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);

        // Shieldbearer 9
        string shieldPath = "Assets/SOData/Rewards/Gems/Shieldbearer";
        if (!AssetDatabase.IsValidFolder(shieldPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Shieldbearer");

        CreateGemSO(shieldPath, "Gem_Unique_ShieldSturdy", "Sturdy Shield", "Grants the Shieldbearer a shield equal to 50% of Max HP upon entering a room.", GemUniqueType.ShieldSturdy, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "Gem_Unique_ShieldWillCourage", "Shield's Will - Courage", "Increases all Shieldbearer stats by 10%.\nIncreases ally attack speed by 12% for 10 seconds upon entering a room.", GemUniqueType.ShieldWillCourage, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "Gem_Unique_ShieldWillWind", "Shield's Will - Wind", "Increases all Shieldbearer stats by 10%.\nIncreases ally movement speed by 14% for 10 seconds upon entering a room.", GemUniqueType.ShieldWillWind, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "Gem_Unique_ShieldWillClash", "Shield's Will - Clash", "Increases all Shieldbearer stats by 10%.\nIncreases ally attack damage by 8% for 10 seconds upon entering a room.", GemUniqueType.ShieldWillClash, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "Gem_Unique_ShieldThornArmor", "Thorn Armor", "Reflects fixed damage equal to 2% of the attacker's current HP when taking basic attack damage.", GemUniqueType.ShieldThornArmor, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "Gem_Unique_ShieldHeavyArmor", "Heavy Armor", "Deals additional physical damage equal to 14% of the shield amount on single-target throw hit.", GemUniqueType.ShieldHeavyArmor, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "Gem_Unique_ShieldPatienceAura", "Aura of Patience", "Grants allies a shield equal to 18% of Shieldbearer's Max HP every 5 seconds.", GemUniqueType.ShieldPatienceAura, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "Gem_Unique_ShieldOverwhelm", "Aura of Overwhelming", "Deals 4% of Shieldbearer's Max HP to nearby enemies every 5 seconds and heals for 120% of damage dealt.", GemUniqueType.ShieldOverwhelm, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "Gem_Unique_ShieldTwistedGround", "Twisted Ground", "Deals 20% of the shield amount as AoE damage upon single-target throw hit.", GemUniqueType.ShieldTwistedGround, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=green>유니크 보석 SO 데이터들이 성공적으로 생성되었습니다!</color>");
    }

    private static void CreateGemSO(string path, string fileName, string gemName, string desc, GemUniqueType uniqueType, SynergyCategory category, GemSynergyGroup synergyGroup)
    {
        string fullPath = $"{path}/{fileName}.asset";
        if (AssetDatabase.LoadAssetAtPath<GemSO>(fullPath) != null)
        {
            Debug.Log($"이미 존재함: {fullPath}");
            return;
        }

        GemSO gem = ScriptableObject.CreateInstance<GemSO>();
        gem.itemName = gemName;
        gem.description = desc;
        gem.rarity = ItemRarity.Legendary; 
        gem.category = category;
        gem.synergyGroup = synergyGroup;
        
        var effect = new GemUniqueEffect { uniqueType = uniqueType, displayDescription = desc };
        gem.effects = new System.Collections.Generic.List<GemEffect> { effect };

        AssetDatabase.CreateAsset(gem, fullPath);
    }
}
#endif
