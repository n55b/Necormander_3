using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미니언 조합을 분석하여 투척 모드와 효과(Recipe)를 결정하는 클래스입니다.
/// </summary>
public class ThrowStrategy : MonoBehaviour
{
    private ThrowController _controller;

    public void Init(ThrowController controller)
    {
        _controller = controller;
    }

    /// <summary>
    /// 특정 타입의 유닛을 현재 집을 수 있는지 확인합니다.
    /// </summary>
    public bool CanPickUpType(CommandData targetType, List<IThrowable> heldObjects, int maxHoldCount)
    {
        if (heldObjects.Count >= maxHoldCount) return false;

        // 상자(None)와 다른 미니언 섞기 방지
        bool hasBox = false;
        bool hasMinion = false;
        foreach (var held in heldObjects)
        {
            if (held.MinionType == CommandData.None) hasBox = true;
            else hasMinion = true;
        }

        if (targetType == CommandData.None && hasMinion) return false;
        if (targetType != CommandData.None && hasBox) return false;

        if (targetType == CommandData.SkeletonMagician && heldObjects.Count == 0) return false;

        bool hasWarrior = false;
        bool hasArcher = false;

        foreach (var held in heldObjects)
        {
            CommandData heldType = held.MinionType;
            if (heldType == CommandData.SkeletonWarrior) hasWarrior = true;
            else if (heldType == CommandData.SkeletonArcher) hasArcher = true;
            if (heldType == targetType) return false;
        }

        if (targetType == CommandData.SkeletonWarrior && hasArcher) return false;
        if (targetType == CommandData.SkeletonArcher && hasWarrior) return false;

        return true;
    }

    public TargetingMode GetCurrentTargetingMode(List<IThrowable> heldObjects)
    {
        if (heldObjects.Count == 0) return TargetingMode.Self;

        // 상자가 있으면 Warrior와 동일하게 단일 타겟팅(Target) 모드로 설정
        foreach (var obj in heldObjects) if (obj.MinionType == CommandData.None) return TargetingMode.Target;

        // 우선순위: Archer(Area) > Warrior(Target) > 기타(Spearman 포함 - Self)
        foreach (var obj in heldObjects) if (obj.MinionType == CommandData.SkeletonArcher) return TargetingMode.Area;
        foreach (var obj in heldObjects) if (obj.MinionType == CommandData.SkeletonWarrior) return TargetingMode.Target;

        return TargetingMode.Self;
    }

    public Team GetExpectedTargetTeam(List<IThrowable> heldObjects)
    {
        if (heldObjects.Count == 0) return Team.Enemy;

        bool hasWarrior = false;
        bool hasShield = false;
        bool hasOthers = false;

        foreach (var obj in heldObjects)
        {
            CommandData type = obj.MinionType;
            if (type == CommandData.None) return Team.Enemy; // 상자는 적에게 던짐
            if (type == CommandData.SkeletonWarrior) hasWarrior = true;
            else if (type == CommandData.SkeletonShieldbearer) hasShield = true;
            else if (type == CommandData.SkeletonMagician) { /* 팀 결정에 영향 없음 */ }
            else hasOthers = true;
        }

        return (hasWarrior && hasShield && !hasOthers) ? Team.Ally : Team.Enemy;
    }

    public ThrowRecipe CreateRecipe(Vector2 targetPos, float chargeRatio, List<IThrowable> heldObjects)
    {
        ThrowRecipe recipe = new ThrowRecipe();
        recipe.info.impactPoint = targetPos;
        recipe.info.chargeRatio = chargeRatio;
        recipe.state.heldUnits.AddRange(heldObjects); // [추가] 유닛 리스트 저장

        // 플레이어 컨트롤러로부터 계산된 배율을 가져옴
        recipe.modifiers.chargeMultiplier = GameManager.Instance.PLAYERCONTROLLER.GetThrowChargeMultiplier(chargeRatio);
        
        // [보물 시스템] 던지기 전역 강화 수치 적용
        recipe.modifiers.treasurePowerMultiplier = 1.0f + InventoryManager.Instance.GetTreasureBonus(TreasureEffectType.GlobalThrowEffect);

        // [수정] 타겟팅 모드와 팀을 능력 Hook 호출 전에 미리 결정 (필터링에 필요)
        recipe.info.targetingMode = GetCurrentTargetingMode(heldObjects);
        recipe.info.targetTeam = GetExpectedTargetTeam(heldObjects);
        bool isDirect = chargeRatio >= 0.98f;
        recipe.info.isDirect = isDirect; // [추가] 레시피에 직구 여부 저장

        // [추가] 인벤토리에 장착된 모든 던지기 능력들의 Hook 실행 (ModifyRecipe)
        if (InventoryManager.Instance != null)
        {
            foreach (var ability in InventoryManager.Instance.ActiveAbilities)
            {
                if (ability != null && ability.IsApplicable(isDirect, recipe.info.targetingMode))
                {
                    ability.ModifyRecipe(recipe, heldObjects);
                }
            }
        }

        // [추가] 이벤트 버스 / 핸들러 훅 호출 (보석 스탯, 투척 효율 등 동적 계산)
        var throwCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponentInChildren<ThrowController>();
        if (throwCtrl != null) throwCtrl.InvokeRecipeCreated(recipe);

        if (heldObjects.Count == 0) return recipe;

        // 주력 유닛(전사/궁수)의 데이터를 가져옴
        IThrowable leadUnit = null;
        if (recipe.info.targetingMode == TargetingMode.Area)
        {
            foreach(var obj in heldObjects) if(obj.MinionType == CommandData.SkeletonArcher) { leadUnit = obj; break; }
        }
        else if (recipe.info.targetingMode == TargetingMode.Target)
        {
            foreach(var obj in heldObjects) if(obj.MinionType == CommandData.SkeletonWarrior) { leadUnit = obj; break; }
        }

        // 주력 유닛(전사/궁수)이 없거나 Self 모드인 경우, 섞인 유닛 중 가장 첫 번째 유닛의 배율을 기저 배율로 사용
        if (leadUnit == null && heldObjects.Count > 0) leadUnit = heldObjects[0];

        // [수정] 주력 유닛의 배율을 기저 배율로 사용하되, 상자(None)와 같이 데이터가 없는 경우 1.0을 기본값으로 함
        recipe.modifiers.modeMultiplier = (leadUnit != null && leadUnit.MinionData != null) ? leadUnit.MinionData.effectMultiplier : 1.0f;

        // 최종 배율 계산 (나중에 보정치 적용을 위해 미리 계산)
        float totalMultiplier = recipe.modifiers.modeMultiplier * recipe.modifiers.chargeMultiplier * 
                                recipe.modifiers.treasurePowerMultiplier * recipe.modifiers.abilityMultiplier;
                                
        if (!isDirect && InventoryManager.Instance != null)
        {
            float parabolicDmgBonus = InventoryManager.Instance.GetAggregatedGemBonus(CommandData.None, StatType.ParabolicDamageMultiplier);
            Vector2 playerPos = GameManager.Instance.PLAYERCONTROLLER.transform.position;
            float dist = Vector2.Distance(playerPos, targetPos);
            
            // [탄도학] 거리 1마다 10% 증가 * 보유 개수
            int ballisticsCount = InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.Ballistics);
            if (ballisticsCount > 0)
            {
                parabolicDmgBonus += (dist * 0.10f * ballisticsCount);
            }

            // [단안경] (On/Off형 효과) 거리 5칸을 기준으로 가까울수록 10%씩 증가 (최대 50%)
            if (InventoryManager.Instance.HasUniqueEffect(GemUniqueType.Monocle))
            {
                float monocleBonus = Mathf.Max(0f, 5f - dist) * 0.10f;
                parabolicDmgBonus += monocleBonus;
            }

            // [시너지] 투포환 (2) / (4) 세트 효과 - 포물선 투척 효율 25% / 50% 증가
            int shotputGems = InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Shotput);
            if (shotputGems >= 4) parabolicDmgBonus += 0.50f;
            else if (shotputGems >= 2) parabolicDmgBonus += 0.25f;

            totalMultiplier *= (1f + parabolicDmgBonus);
        }

        foreach (var obj in heldObjects)
        {
            CommandData type = obj.MinionType;
            
            // [수정] 상자(None)인 경우 ThrowableBox의 데미지 설정을 가져와 전사 액션 추가
            if (type == CommandData.None)
            {
                float boxDmg = 1.0f;
                if (obj is ThrowableBox box) boxDmg = box.DamageAmount;
                recipe.actions.Add(new WarriorAction(boxDmg));
                continue;
            }

            // [보석 시스템 1: 투척 강화]
            float gemBonus = InventoryManager.Instance.GetAggregatedGemBonus(type, StatType.ThrowEffect);

            // [보석 시스템 2: 디버프 부여]
            // 해당 직업(type)에게 할당된 보석들의 디버프 스택(귀수 속성)만 가져와 적용합니다.
            var jobStats = InventoryManager.Instance.GetJobGemStats(type);
            if (jobStats != null)
            {
                foreach (var kvp in jobStats.HandAttributes)
                {
                    DebuffStackType debuffType = kvp.Key;
                    float aggregatedStacks = kvp.Value;

                    // [사용자 요청 공식] (보석 스택 합산) * (전체 배율)
                    float scaledStack = aggregatedStacks * totalMultiplier;
                    if (!recipe.modifiers.debuffStacks.ContainsKey(debuffType))
                        recipe.modifiers.debuffStacks[debuffType] = 0f;
                    
                    recipe.modifiers.debuffStacks[debuffType] += scaledStack;
                }
            }

            float baseVal = obj.MinionData.baseEffectValue;

            switch (type)
            {
                case CommandData.SkeletonWarrior:
                    // 전사: 보석 보너스를 데미지 고정치로 가산 (baseVal + 보너스)
                    float finalWarriorDmg = baseVal + gemBonus;
                    if (recipe.info.targetingMode != TargetingMode.Area) recipe.actions.Add(new WarriorAction(finalWarriorDmg));
                    break;

                case CommandData.SkeletonArcher: 
                    // 궁수: 보석 보너스는 범위(Radius) 고정 가산치로 적용
                    float finalRadius = obj.MinionData.baseAreaRadius + gemBonus;
                    
                    if (InventoryManager.Instance != null)
                    {
                        float radiusMult = 1.0f;
                        float damageMult = 1.0f;
                        float durationMult = 1.0f;

                        ThrowEventBus.TriggerThrowStart(CommandData.SkeletonArcher, ref damageMult, ref radiusMult, ref durationMult);

                        finalRadius *= radiusMult;
                    }

                    recipe.actions.Add(new ArcherAction(baseVal, finalRadius));
                    break;

                case CommandData.SkeletonPriest: 
                case CommandData.SkeletonShieldbearer: 
                case CommandData.SkeletonSpearman: 
                    // 사제/방패병/창병: 보석 보너스를 효과 배율(Multiplier)로 적용 (기본값 * (1 + 보너스))
                    float multiplierBonus = baseVal * (1.0f + gemBonus);
                    
                    if (type == CommandData.SkeletonPriest) recipe.actions.Add(new PriestAction(multiplierBonus)); 
                    else if (type == CommandData.SkeletonShieldbearer) recipe.actions.Add(new ShieldBearerAction(multiplierBonus)); 
                    else recipe.actions.Add(new SpearmanAction(multiplierBonus));
                    break;

                case CommandData.SkeletonMagician: 
                    // 마법사: 보석 보너스 1.0당 반복 횟수 1회 추가
                    int extraRepeats = Mathf.FloorToInt(gemBonus);
                    float finalMagiVal = baseVal + extraRepeats;
                    recipe.actions.Add(new MagicianAction(finalMagiVal)); 
                    break;
            }
        }

        if (recipe.info.targetingMode == TargetingMode.Target && chargeRatio < 0.98f)
        {
            recipe.info.finalTarget = FindSmartTarget(targetPos, recipe.info.targetTeam);
            if (recipe.info.finalTarget != null) recipe.info.impactPoint = recipe.info.finalTarget.transform.position;
        }

        return recipe;
    }

    public GameObject FindSmartTarget(Vector2 searchPos, Team targetTeam)
    {
        float searchRadius = 5.0f;
        LayerMask mask = (targetTeam == Team.Enemy) ? LayerMask.GetMask("Enemy") : LayerMask.GetMask("Army", "Player");
        Collider2D[] colls = Physics2D.OverlapCircleAll(searchPos, searchRadius, mask);
        GameObject bestTarget = null;
        float minTargetDist = float.MaxValue;

        foreach (var col in colls)
        {
            float dist = Vector2.Distance(searchPos, col.transform.position);
            if (dist < minTargetDist) { minTargetDist = dist; bestTarget = col.gameObject; }
        }
        return bestTarget;
    }
}
