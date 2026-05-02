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

        // 우선순위: Archer(Area) > Warrior(Target) > 기타(Spearman 포함 - Self)
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

        // 플레이어 컨트롤러로부터 계산된 배율을 가져옴
        recipe.chargeMultiplier = GameManager.Instance.PLAYERCONTROLLER.GetThrowChargeMultiplier(chargeRatio);
        recipe.treasurePowerMultiplier = 1.0f; // 기본값 (추후 보물 시스템에서 가산 가능)

        if (heldObjects.Count == 0) return recipe;

        recipe.targetingMode = GetCurrentTargetingMode(heldObjects);
        recipe.targetTeam = GetExpectedTargetTeam(heldObjects);

        // [복구] 하드코딩된 배율을 지우고, 데이터(SO)에 설정된 주력 유닛의 배율을 사용합니다.
        AllyController leadUnit = null;
        if (recipe.targetingMode == TargetingMode.Area)
        {
            foreach(var obj in heldObjects) if(obj is AllyController a && a.MinionType == CommandData.SkeletonArcher) { leadUnit = a; break; }
        }
        else if (recipe.targetingMode == TargetingMode.Target)
        {
            foreach(var obj in heldObjects) if(obj is AllyController a && a.MinionType == CommandData.SkeletonWarrior) { leadUnit = a; break; }
        }

        // 주력 유닛(전사/궁수)이 없거나 Self 모드인 경우, 섞인 유닛 중 가장 첫 번째 유닛의 배율을 기저 배율로 사용
        if (leadUnit == null && heldObjects.Count > 0 && heldObjects[0] is AllyController first) leadUnit = first;

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
                        // [조건 유지] 전사는 타겟/셀프 모드일 때만 데미지 기여
                        if (recipe.targetingMode != TargetingMode.Area) recipe.AddAction(ImpactActionType.Damage, baseVal);
                        break;
                    case CommandData.SkeletonArcher: 
                        // [조건 유지] 궁수는 범위(Area) 모드일 때 반지름 설정 및 데미지 기여
                        recipe.AddAction(ImpactActionType.Area, ally.MinionData.baseAreaRadius);
                        if (recipe.targetingMode == TargetingMode.Area) recipe.AddAction(ImpactActionType.Damage, baseVal);
                        break;
                    case CommandData.SkeletonPriest: 
                        // 사제는 CC(슬로우) 부여
                        recipe.AddAction(ImpactActionType.CC, baseVal); 
                        break;
                    case CommandData.SkeletonShieldbearer: 
                        // 방패병은 보호막 부여
                        recipe.AddAction(ImpactActionType.Shield, baseVal); 
                        break;
                    case CommandData.SkeletonSpearman: 
                        // 창병은 넉백/진형파괴 부여
                        recipe.AddAction(ImpactActionType.Knockback, baseVal); 
                        break;
                    case CommandData.SkeletonMagician: 
                        // 마법사는 반복 횟수 추가
                        recipe.AddAction(ImpactActionType.Repeat, baseVal); 
                        break;
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
