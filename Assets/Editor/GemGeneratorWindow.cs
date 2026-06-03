#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public class GemGeneratorWindow
{
    [MenuItem("Necromancer/Generate All Unique Gems")]
    public static void GenerateGems()
    {
        string staminaPath = "Assets/SOData/Rewards/Gems/Stamina";
        string fastballPath = "Assets/SOData/Rewards/Gems/Fastball";
        
        if (!AssetDatabase.IsValidFolder(staminaPath)) CreateFolderRecursively(staminaPath);
        if (!AssetDatabase.IsValidFolder(fastballPath)) CreateFolderRecursively(fastballPath);

        // --- 스태미너 보석 ---
        CreateGemSO(staminaPath, "Gem_Stamina_CatchBreath", "Catch Breath", "Increases natural stamina regeneration when out of combat.", GemUniqueType.CatchBreath, SynergyCategory.Common, GemSynergyGroup.Stamina, 2);
        CreateGemSO(staminaPath, "Gem_Stamina_HarvestOfDeath", "Harvest of Death", "Increases stamina regeneration based on the number of dead minions.", GemUniqueType.HarvestOfDeath, SynergyCategory.Common, GemSynergyGroup.Stamina, 2);
        CreateGemSO(staminaPath, "Gem_Stamina_BasicFitness", "Basic Fitness", "Max stamina +20.", GemUniqueType.BasicFitness, SynergyCategory.Common, GemSynergyGroup.Stamina, 1);
        CreateGemSO(staminaPath, "Gem_Stamina_EndlessVitality", "Endless Vitality", "Increases natural stamina regeneration (+0.5).", GemUniqueType.EndlessVitality, SynergyCategory.Common, GemSynergyGroup.Stamina, 1);
        CreateGemSO(staminaPath, "Gem_Stamina_OverflowingThrow", "Overflowing Throw", "Stamina cost +5, Throw effect +25%.", GemUniqueType.OverflowingThrow, SynergyCategory.Common, GemSynergyGroup.Stamina, 1);
        CreateGemSO(staminaPath, "Gem_Stamina_OrderedBreath", "Ordered Breath", "Stamina cost -3.", GemUniqueType.OrderedBreath, SynergyCategory.Common, GemSynergyGroup.Stamina, 1);
        CreateGemSO(staminaPath, "Gem_Stamina_ThrowOverload", "Throw Overload", "Throw effect +2% per 1 stamina cost.", GemUniqueType.ThrowOverload, SynergyCategory.Common, GemSynergyGroup.Stamina, 1);
        CreateGemSO(staminaPath, "Gem_Stamina_MasterOfRapidFire", "Master of Rapid Fire", "Stamina cost -7, Throw effect -30%.", GemUniqueType.MasterOfRapidFire, SynergyCategory.Common, GemSynergyGroup.Stamina, 1);
        CreateGemSO(staminaPath, "Gem_Stamina_LimitBreak", "Limit Break", "Stamina can drop below zero (up to -50). Halves regeneration when negative.", GemUniqueType.LimitBreak, SynergyCategory.Common, GemSynergyGroup.Stamina, 0);
        CreateGemSO(staminaPath, "Gem_Stamina_EfficientThrow", "Efficient Throw", "Max stamina -40, Throw effect +60%.", GemUniqueType.EfficientThrow, SynergyCategory.Common, GemSynergyGroup.Stamina, 0);

        // --- 강속구 보석 ---
        CreateGemSO(fastballPath, "Gem_Fastball_SetPosition", "Set Position", "Charge time -0.1s, Fastball effect -2%.", GemUniqueType.SetPosition, SynergyCategory.Common, GemSynergyGroup.Fastball, 2);
        CreateGemSO(fastballPath, "Gem_Fastball_Windup", "Windup", "Charge time +0.5s, Fastball effect +2%.", GemUniqueType.Windup, SynergyCategory.Common, GemSynergyGroup.Fastball, 2);
        CreateGemSO(fastballPath, "Gem_Fastball_MagicPitchFireball", "Magic Pitch: Fireball", "Fastball effect +10% per 1s of required charge time.", GemUniqueType.MagicPitchFireball, SynergyCategory.Common, GemSynergyGroup.Fastball, 1);
        CreateGemSO(fastballPath, "Gem_Fastball_MagicPitchArirangBall", "Magic Pitch: Arirang Ball", "Charge time -0.5s, Fastball effect -40%.", GemUniqueType.MagicPitchArirangBall, SynergyCategory.Common, GemSynergyGroup.Fastball, 1);
        CreateGemSO(fastballPath, "Gem_Fastball_Closer", "Closer", "Grants 4s of overcharge after fastball. Throw effect increases linearly up to +50% during overcharge.", GemUniqueType.Closer, SynergyCategory.Common, GemSynergyGroup.Fastball, 0);
        CreateGemSO(fastballPath, "Gem_Fastball_ExperiencedPitcher", "Experienced Pitcher", "Movement speed reduction while charging is reduced to 25%.", GemUniqueType.ExperiencedPitcher, SynergyCategory.Common, GemSynergyGroup.Fastball, 1);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=green>신규 15종 보석 SO 데이터들이 성공적으로 생성(또는 갱신)되었습니다!</color>");
    }

    private static void CreateFolderRecursively(string path)
    {
        string[] folders = path.Split('/');
        string currentPath = folders[0];
        for (int i = 1; i < folders.Length; i++)
        {
            if (!AssetDatabase.IsValidFolder(currentPath + "/" + folders[i]))
            {
                AssetDatabase.CreateFolder(currentPath, folders[i]);
            }
            currentPath += "/" + folders[i];
        }
    }

    private static void CreateGemSO(string path, string fileName, string gemName, string desc, GemUniqueType uniqueType, SynergyCategory category, GemSynergyGroup synergyGroup, int subSlots)
    {
        string fullPath = $"{path}/{fileName}.asset";
        GemSO gem = AssetDatabase.LoadAssetAtPath<GemSO>(fullPath);
        bool isNew = false;
        
        if (gem == null)
        {
            gem = ScriptableObject.CreateInstance<GemSO>();
            isNew = true;
        }

        // 유니크 타입 이름 대신, 전달받은 멋진 이름(gemName)을 사용합니다.
        gem.itemName = gemName;
        gem.description = desc;
        gem.rarity = ItemRarity.Legendary; 
        gem.category = category;
        gem.synergyGroup = synergyGroup;
        gem.subSlots = subSlots; // [추가] 기획된 노드 수 반영
        
        // 등급(rarity)에 따른 가격 설정: Common(0)=40, Rare(1)=80, Epic(2)=160, Legendary(3)=320
        int baseCost = 40;
        int multiplier = (int)Mathf.Pow(2, (int)gem.rarity);
        gem.shopCost = baseCost * multiplier;
        
        var effect = new GemUniqueEffect { uniqueType = uniqueType, displayDescription = desc };
        gem.effects = new System.Collections.Generic.List<GemEffect> { effect };

        if (isNew)
        {
            AssetDatabase.CreateAsset(gem, fullPath);
        }
        else
        {
            EditorUtility.SetDirty(gem);
        }
    }
}
#endif
