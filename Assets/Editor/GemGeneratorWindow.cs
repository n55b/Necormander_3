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

        // --- 투포환 보석 ---
        string shotputPath = "Assets/SOData/Rewards/Gems/Shotput";
        if (!AssetDatabase.IsValidFolder(shotputPath)) CreateFolderRecursively(shotputPath);

        CreateGemSO(shotputPath, "Gem_Shotput_Protractor", "각도기", "포물선 던지기 시 투척 효율이 증가합니다.", GemUniqueType.None, SynergyCategory.Common, GemSynergyGroup.Shotput, 2, StatType.ParabolicEffectMultiplier, 0.2f);
        CreateGemSO(shotputPath, "Gem_Shotput_EfficientCurve", "효율적인 곡선", "포물선 던지기의 투척 속도가 빨라집니다. (20% 더 빨리 떨어집니다)", GemUniqueType.None, SynergyCategory.Common, GemSynergyGroup.Shotput, 2, StatType.ParabolicFlightTimeMultiplier, 0.2f);
        CreateGemSO(shotputPath, "Gem_Shotput_JustThrowIt", "일단 던지고 보자", "이번 방에서 포물선 던질 때 마다 8초동안 투척 속도가 8% 빨라지며 해당 효과는 5번까지 중첩됩니다.", GemUniqueType.JustThrowIt, SynergyCategory.Common, GemSynergyGroup.Shotput, 1);
        CreateGemSO(shotputPath, "Gem_Shotput_Ballistics", "탄도학", "거리에 비례하여 1칸마다 투척 효율 10% 증가", GemUniqueType.Ballistics, SynergyCategory.Common, GemSynergyGroup.Shotput, 1);
        CreateGemSO(shotputPath, "Gem_Shotput_SiegeMode", "시즈 모드", "플레이어가 해당 위치에 고정되며, 카메라 위치가 넓게 고정됩니다.\n보유한 소환수의 수 만큼 탄약으로 변경되어 고각도 포격을 실시합니다.", GemUniqueType.SiegeMode, SynergyCategory.Common, GemSynergyGroup.Shotput, 0);
        CreateGemSO(shotputPath, "Gem_Shotput_Monocle", "단안경", "5칸을 기준으로 역순. 즉 자기 발밑에 던지면 투척 효율이 최대(50%) 증가하며, 멀어질수록 감소합니다.", GemUniqueType.Monocle, SynergyCategory.Common, GemSynergyGroup.Shotput, 0);

        // --- 큰손 보석 ---
        string bigHandPath = "Assets/SOData/Rewards/Gems/BigHand";
        if (!AssetDatabase.IsValidFolder(bigHandPath)) CreateFolderRecursively(bigHandPath);

        CreateGemSO(bigHandPath, "Gem_BigHand_DemonHandPower", "귀수의 힘", "집을 수 있는 소환수 범위 0.5칸 증가", GemUniqueType.DemonHandPower, SynergyCategory.Common, GemSynergyGroup.BigHand, 2);
        CreateGemSO(bigHandPath, "Gem_BigHand_HumanWaveTactics", "인해전술", "3명 이상 투척 시 1명당 투척 효율 7% 증가", GemUniqueType.HumanWaveTactics, SynergyCategory.Common, GemSynergyGroup.BigHand, 2);
        CreateGemSO(bigHandPath, "Gem_BigHand_TwinFusion", "쌍둥이 연성", "조합 투척 시 앞 큐의 2명의 소환수가 10초동안 합체합니다. 합체한 소환수의 능력치는 2명의 소환수를 합한 것과 같습니다. 시간이 지나거나 파괴되면, 소환수들이 반피 비율로 튀어 나옵니다.", GemUniqueType.TwinFusion, SynergyCategory.Common, GemSynergyGroup.BigHand, 1);
        CreateGemSO(bigHandPath, "Gem_BigHand_MobMentality", "군중심리", "소환수 집기 범위 내 소환수가 많을 경우 1명당 플레이어 이동속도 0.1증가", GemUniqueType.MobMentality, SynergyCategory.Common, GemSynergyGroup.BigHand, 1);
        CreateGemSO(bigHandPath, "Gem_BigHand_SwiftRelocation", "신속한 재배치", "3명 이상 투척 시 사용된 소환수들의 이동 속도가 5초간 50% 증가", GemUniqueType.SwiftRelocation, SynergyCategory.Common, GemSynergyGroup.BigHand, 1);
        CreateGemSO(bigHandPath, "Gem_BigHand_Afterimage", "잔상", "바로 직전의 소환수 조합(타입, 개수, 순서가 동일)을 똑같이 연속해서 던질 시, 해당 조합의 투척 효율 150% 증폭", GemUniqueType.Afterimage, SynergyCategory.Common, GemSynergyGroup.BigHand, 1);
        CreateGemSO(bigHandPath, "Gem_BigHand_AllMine", "다 내꺼야", "소환수 1마리를 집을 때 마다 집을 수 있는 소환수 범위 증가", GemUniqueType.AllMine, SynergyCategory.Common, GemSynergyGroup.BigHand, 1);
        CreateGemSO(bigHandPath, "Gem_BigHand_Golemizing", "골레마이징", "조합 투척 시, 앞의 5명의 소환수가 일정 시간동안 골렘으로 합체합니다. 능력치는 5마리를 합한 것과 같고 매우 거대해집니다.", GemUniqueType.Golemizing, SynergyCategory.Common, GemSynergyGroup.BigHand, 0);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=green>신규 보석 SO 데이터들이 성공적으로 생성(또는 갱신)되었습니다!</color>");
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

    // statType의 디폴트값을 활용하여 기존 코드 호환성 유지 (StatType.Attack을 임의로 넘기되 value가 0이면 추가 안함)
    private static void CreateGemSO(string path, string fileName, string gemName, string desc, GemUniqueType uniqueType, SynergyCategory category, GemSynergyGroup synergyGroup, int subSlots, StatType statType = StatType.Attack, float statValue = 0f)
    {
        string fullPath = $"{path}/{fileName}.asset";
        GemSO gem = AssetDatabase.LoadAssetAtPath<GemSO>(fullPath);
        bool isNew = false;
        
        if (gem == null)
        {
            gem = ScriptableObject.CreateInstance<GemSO>();
            isNew = true;
        }

        // 새 보석일 때만 덮어씌워 기존 보석의 밸런스 패치 내용을 초기화하지 않게 방어
        if (isNew)
        {
            gem.itemName = gemName;
            gem.description = desc;
            gem.rarity = ItemRarity.Legendary; 
            gem.category = category;
            gem.synergyGroup = synergyGroup;
            gem.subSlots = subSlots;
            
            int baseCost = 40;
            int multiplier = (int)Mathf.Pow(2, (int)gem.rarity);
            gem.shopCost = baseCost * multiplier;
            
            gem.effects = new System.Collections.Generic.List<GemEffect>();

            if (uniqueType != GemUniqueType.None)
            {
                var effect = new GemUniqueEffect { uniqueType = uniqueType, displayDescription = desc };
                gem.effects.Add(effect);
            }

            if (statValue > 0f)
            {
                var statEffect = new GemStatEffect { statType = statType, value = statValue };
                gem.effects.Add(statEffect);
            }

            AssetDatabase.CreateAsset(gem, fullPath);
        }
        else
        {
            // 이미 존재하는 경우에도 기획 변경(설명, 노드 수 등 밸런스와 무관한 구조적 데이터)은 갱신해줍니다.
            gem.description = desc;
            gem.subSlots = subSlots;
            
            // UniqueEffect의 설명 텍스트도 같이 갱신
            if (gem.effects != null)
            {
                foreach(var effect in gem.effects)
                {
                    if (effect is GemUniqueEffect uniqueEffect && uniqueEffect.uniqueType == uniqueType)
                    {
                        uniqueEffect.displayDescription = desc;
                    }
                }
            }
            EditorUtility.SetDirty(gem);
        }
    }
}
#endif
