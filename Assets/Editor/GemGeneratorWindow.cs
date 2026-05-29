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

        // 중독 6종 (공용 - Poison)
        CreateGemSO(poisonPath, "PoisonHost", "숙주", "3초마다 독 스택 광역 전이", GemUniqueType.PoisonHost, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "PoisonFootprint", "부식석 발자취", "이동 장판 생성", GemUniqueType.PoisonFootprint, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "PoisonFlask", "중독 플라스크", "던지기 스택 기본값 +1", GemUniqueType.PoisonFlask, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "WoundInfection", "상처 감염", "평타 시 독 틱 단축", GemUniqueType.WoundInfection, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "PoisonContagion", "중독 전염", "사망 시 주변 적에게 독 전이", GemUniqueType.PoisonContagion, SynergyCategory.Common, GemSynergyGroup.Poison);
        CreateGemSO(poisonPath, "GreenFluid", "초록색 체액", "독 틱 시 포션 스폰", GemUniqueType.GreenFluid, SynergyCategory.Common, GemSynergyGroup.Poison);

        // 한기 5종 (사제 - Priest_Chill)
        CreateGemSO(chillPath, "ColdBloodedHunter", "냉혹한 사냥꾼", "한기 적 이속 추가 감소", GemUniqueType.ColdBloodedHunter, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);
        CreateGemSO(chillPath, "BitingWind", "칼바람", "주변 적 한기 1스택 부여", GemUniqueType.BitingWind, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);
        CreateGemSO(chillPath, "Frostbreaker", "동상 파괴자", "한기 적에게 5% 추가 피해", GemUniqueType.Frostbreaker, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);
        CreateGemSO(chillPath, "AbsoluteZero", "절대영도", "동결 시 반경 50스택 광역 (방당 1회)", GemUniqueType.AbsoluteZero, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);
        CreateGemSO(chillPath, "ShatterIcicle", "고드름 부시기", "투척 명중 시 동결 해제 및 50% 추가 피해", GemUniqueType.ShatterIcicle, SynergyCategory.Priest, GemSynergyGroup.Priest_Chill);

        // 처형 2종 (공용 - Execution)
        CreateGemSO(executionPath, "ExecutionFear", "공포", "처형 당한 적 주변 일반 적 1초 공포", GemUniqueType.Fear, SynergyCategory.Common, GemSynergyGroup.Execution);
        CreateGemSO(executionPath, "ExecutionGuillotine", "단두대", "처형 스택 기준치 10% 완화", GemUniqueType.Guillotine, SynergyCategory.Common, GemSynergyGroup.Execution);

        // 비폭 6종 (공용 - BloodPop)
        CreateGemSO(bloodPopPath, "ImprovisedExplosive", "급조 폭팔물", "비폭 폭발 피해 10% 증가", GemUniqueType.ImprovisedExplosive, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "GoreParty", "내장 파티", "폭발 범위 내 아군 비폭 스택만큼 체력 회복", GemUniqueType.GoreParty, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "BloodArmor", "피철갑", "폭발 범위 내 아군 비폭 스택 2배만큼 쉴드 획득", GemUniqueType.BloodArmor, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "MeltingCorpse", "녹아내리는 시체", "폭발 후 장판 생성, 5초간 지속 피해", GemUniqueType.MeltingCorpse, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "MutualDestruction", "동귀어진", "폭발 전 주변 1.5배 적 끌어당김", GemUniqueType.MutualDestruction, SynergyCategory.Common, GemSynergyGroup.BloodPop);
        CreateGemSO(bloodPopPath, "AmIExplodingToo", "나도 폭발하는걸까?", "비폭 피해 입은 적 20% 추가 피해 약점", GemUniqueType.AmIExplodingToo, SynergyCategory.Common, GemSynergyGroup.BloodPop);

        // 노화 3종 (사제 - Priest_Aging)
        CreateGemSO(agingPath, "Goryeojang", "고려장", "노화 최고스택 1명 주변 둔화 장판", GemUniqueType.Goryeojang, SynergyCategory.Priest, GemSynergyGroup.Priest_Aging);
        CreateGemSO(agingPath, "DimVision", "침침한 시야", "노화 50 이상 적 미스 확률 25% 증가", GemUniqueType.DimVision, SynergyCategory.Priest, GemSynergyGroup.Priest_Aging);
        CreateGemSO(agingPath, "AgingHunter", "노화 사냥꾼", "전체 노화 100스택 당 아군 공/이속 10% 증가", GemUniqueType.AgingHunter, SynergyCategory.Priest, GemSynergyGroup.Priest_Aging);

        // 부식 4종 (사제 - Priest_Corrosion)
        string corrosionPath = "Assets/SOData/Rewards/Gems/Corrosion";
        if (!AssetDatabase.IsValidFolder(corrosionPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Corrosion");

        CreateGemSO(corrosionPath, "PriestsCantAttack", "사제는 공격을 할 수 없어!", "부식 시너지 활성화 시 아군 치유량 20% 증가", GemUniqueType.PriestsCantAttack, SynergyCategory.Priest, GemSynergyGroup.Priest_Corrosion);
        CreateGemSO(corrosionPath, "DoubleCorrosion", "부식 2배.", "부식 시너지 효과 10% 증폭", GemUniqueType.DoubleCorrosion, SynergyCategory.Priest, GemSynergyGroup.Priest_Corrosion);
        CreateGemSO(corrosionPath, "WeaponCorrosion", "무기 부식", "부식된 적 공격력 10% 감소", GemUniqueType.WeaponCorrosion, SynergyCategory.Priest, GemSynergyGroup.Priest_Corrosion);
        CreateGemSO(corrosionPath, "RustedArmor", "녹슬어 버린 갑옷", "부식된 적 아군 평타 5회 피격 시 체력 5% 고정피해", GemUniqueType.RustedArmor, SynergyCategory.Priest, GemSynergyGroup.Priest_Corrosion);

        // 전사 7종 (처형자 - Warrior_Executioner)
        string warriorPath = "Assets/SOData/Rewards/Gems/Warrior";
        if (!AssetDatabase.IsValidFolder(warriorPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Warrior");

        CreateGemSO(warriorPath, "WarriorBallistics1", "전사 인형 탄도학 I", "전사의 포물선 던지기 데미지 10% 증가", GemUniqueType.WarriorBallistics1, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "WarriorBallistics2", "전사 인형 탄도학 II", "전사의 직구 던지기 데미지 10% 증가", GemUniqueType.WarriorBallistics2, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "WarriorBallistics3", "전사 인형 탄도학 III", "전사의 던지기 효과, 단일 적용 시 15% 증가", GemUniqueType.WarriorBallistics3, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "CrushingPower", "짓눌리는 힘", "전사 던지기 피해 초과 시, 초과량만큼 전사 체력 회복 (압살)", GemUniqueType.CrushingPower, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "WarriorMedal", "전사의 훈장", "전사의 모든 기본 스탯 15% 증가", GemUniqueType.WarriorMedal, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "TrackingEye", "추적하는 눈", "같은 대상을 던지기로 맞췄을 때 12% 추가 피해", GemUniqueType.WarriorPursuit, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);
        CreateGemSO(warriorPath, "FanaticRage", "광적인 분노", "기본 공격 피해의 3%만큼 흡혈", GemUniqueType.WarriorFrenzy, SynergyCategory.Warrior, GemSynergyGroup.Warrior_Executioner);

        // 궁수 8종 (집궁제원칙 - Archer_ArcheryPrinciples)
        string archerPath = "Assets/SOData/Rewards/Gems/Archer";
        if (!AssetDatabase.IsValidFolder(archerPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Archer");

        CreateGemSO(archerPath, "ArcherTerrain", "선관지형", "범위 내, 상대 적이 많을 수록 던지기 효과 증가", GemUniqueType.ArcherTerrain, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "ArcherWind", "후관풍세", "궁수의 던지기 범위 20% 증가", GemUniqueType.ArcherWind, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "ArcherStance", "비정비팔", "중앙에 있는 적에게 20% 추가 피해", GemUniqueType.ArcherStance, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "ArcherBreath", "흉어복실", "비행 거리에 비례하여 던지기 효과 1~33% 증가", GemUniqueType.ArcherBreath, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "ArcherPush", "전추태산", "궁수의 던지기 광역 피해가 15% 증가", GemUniqueType.ArcherPush, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "ArcherTension", "발여호미", "기본 공격 빈도 25% 느려짐, 공격력 25% 증가", GemUniqueType.ArcherTension, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "ArcherMiss", "발이부중", "공격 빗나갈 시 다음 공격 100% 증폭 (반구저기 필요)", GemUniqueType.ArcherMiss, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);
        CreateGemSO(archerPath, "ArcherReflect", "반구저기", "체력 50% 미만 공격력 15%, 50% 이상 공속 15% 증가 (발이부중 필요)", GemUniqueType.ArcherReflect, SynergyCategory.Archer, GemSynergyGroup.Archer_ArcheryPrinciples);

        // 방패병 9종 (수호신 - Shield_Guardian)
        string shieldPath = "Assets/SOData/Rewards/Gems/Shieldbearer";
        if (!AssetDatabase.IsValidFolder(shieldPath)) AssetDatabase.CreateFolder("Assets/SOData/Rewards/Gems", "Shieldbearer");

        CreateGemSO(shieldPath, "ShieldSturdy", "든든한 방패", "방에 진입할 때 방패병에게 체력의 50%만큼 보호막 부여", GemUniqueType.ShieldSturdy, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "ShieldWillCourage", "방패의 의지 - 용기", "방패병의 모든 스탯 10% 증가\n방에 진입할 때 아군의 공격 속도가 10초동안 12% 증가", GemUniqueType.ShieldWillCourage, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "ShieldWillWind", "방패의 의지 - 바람", "방패병의 모든 스탯 10% 증가\n방에 진입할 때 아군의 이동 속도가 10초동안 14% 증가", GemUniqueType.ShieldWillWind, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "ShieldWillClash", "방패의 의지 - 격돌", "방패병의 모든 스탯 10% 증가\n방에 진입할 때 아군의 공격력이 10초동안 8% 증가", GemUniqueType.ShieldWillClash, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "ShieldThornArmor", "가시 갑옷", "방패병이 기본 공격 피격 시 적 현재 체력 2% 고정 피해", GemUniqueType.ShieldThornArmor, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "ShieldHeavyArmor", "육중한 갑옷", "적군 단일 타겟 던지기 적중 시 14% 추가 피해", GemUniqueType.ShieldHeavyArmor, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "ShieldPatienceAura", "인내의 오오라", "5초마다 주변 소환수에게 방패병 체력 18% 보호막 부여", GemUniqueType.ShieldPatienceAura, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "ShieldOverwhelm", "압도", "5초마다 주변 적에게 방패병 체력 4% 피해 및 피해량 120% 흡혈", GemUniqueType.ShieldOverwhelm, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);
        CreateGemSO(shieldPath, "ShieldTwistedGround", "뒤틀리는 지반", "단일 타겟 적중 시 전방 부채꼴 범위에 30% 추가 피해", GemUniqueType.ShieldTwistedGround, SynergyCategory.Shieldbearer, GemSynergyGroup.Shield_Guardian);

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
