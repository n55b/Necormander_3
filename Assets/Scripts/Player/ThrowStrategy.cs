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

        if (targetType == CommandData.SkeletonMagician && heldObjects.Count == 0) return false;

        bool hasWarrior = false;
        bool hasArcher = false;

        foreach (var held in heldObjects)
        {
            if (held is AllyController ally)
            {
                if (ally.MinionType == CommandData.SkeletonWarrior) hasWarrior = true;
                else if (ally.MinionType == CommandData.SkeletonArcher) hasArcher = true;
                if (ally.MinionType == targetType) return false;
            }
        }

        if (targetType == CommandData.SkeletonWarrior && hasArcher) return false;
        if (targetType == CommandData.SkeletonArcher && hasWarrior) return false;

        return true;
    }

    public TargetingMode GetCurrentTargetingMode(List<IThrowable> heldObjects)
    {
        if (heldObjects.Count == 0) return TargetingMode.Self;
        foreach (var obj in heldObjects) if (obj is AllyController ally && ally.MinionType == CommandData.SkeletonArcher) return TargetingMode.Area;
        foreach (var obj in heldObjects) if (obj is AllyController ally && ally.MinionType == CommandData.SkeletonWarrior) return TargetingMode.Target;
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
            if (obj is AllyController ally)
            {
                CommandData type = ally.MinionType;
                if (type == CommandData.SkeletonWarrior) hasWarrior = true;
                else if (type == CommandData.SkeletonShieldbearer) hasShield = true;
                else if (type == CommandData.SkeletonMagician) { /* 법사는 팀 결정에 영향 없음 */ }
                else hasOthers = true;
            }
        }

        return (hasWarrior && hasShield && !hasOthers) ? Team.Ally : Team.Enemy;
    }

    public ThrowRecipe CreateRecipe(Vector2 targetPos, float chargeRatio, List<IThrowable> heldObjects)
    {
        ThrowRecipe recipe = new ThrowRecipe();
        recipe.impactPoint = targetPos;
        recipe.chargeRatio = chargeRatio;

        float minMult = GameManager.Instance.dataManager.MIN_THROW_CHARGE_MULTIPLIER;
        float maxMult = GameManager.Instance.dataManager.MAX_THROW_CHARGE_MULTIPLIER;
        recipe.chargeMultiplier = Mathf.Lerp(minMult, maxMult, chargeRatio);

        if (heldObjects.Count == 0) return recipe;

        recipe.targetingMode = GetCurrentTargetingMode(heldObjects);
        recipe.targetTeam = GetExpectedTargetTeam(heldObjects);

        AllyController leadUnit = null;
        if (recipe.targetingMode == TargetingMode.Area)
        {
            foreach(var obj in heldObjects) if(obj is AllyController a && a.MinionType == CommandData.SkeletonArcher) { leadUnit = a; break; }
        }
        else if (recipe.targetingMode == TargetingMode.Target)
        {
            foreach(var obj in heldObjects) if(obj is AllyController a && a.MinionType == CommandData.SkeletonWarrior) { leadUnit = a; break; }
        }

        recipe.modeMultiplier = (leadUnit != null) ? leadUnit.MinionData.effectMultiplier : 1.0f;

        foreach (var obj in heldObjects)
        {
            if (obj is AllyController ally)
            {
                CommandData type = ally.MinionType;
                float baseVal = ally.MinionData.baseEffectValue;

                switch (type)
                {
                    case CommandData.SkeletonWarrior:
                        if (recipe.targetingMode != TargetingMode.Area) recipe.impactDamage += baseVal;
                        break;
                    case CommandData.SkeletonArcher: 
                        recipe.baseAreaRadius = ally.MinionData.baseAreaRadius;
                        if (recipe.targetingMode == TargetingMode.Area) recipe.impactDamage += baseVal;
                        break;
                    case CommandData.SkeletonPriest: recipe.hasCC = true; recipe.ccBaseValue += baseVal; break;
                    case CommandData.SkeletonShieldbearer: recipe.hasShield = true; recipe.shieldBaseValue += baseVal; break;
                    case CommandData.SkeletonSpearman: recipe.hasFormation = true; recipe.formationBaseValue += baseVal; break;
                    case CommandData.SkeletonMagician: recipe.magicianCount += Mathf.FloorToInt(baseVal); break;
                }
            }
        }

        if (recipe.targetingMode == TargetingMode.Target && chargeRatio < 0.98f)
        {
            recipe.finalTarget = FindSmartTarget(targetPos, recipe.targetTeam);
            if (recipe.finalTarget != null) recipe.impactPoint = recipe.finalTarget.transform.position;
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
