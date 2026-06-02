using UnityEngine;

/// <summary>
/// 투척 충격 시 실행될 개별 액션의 추상 기반 클래스입니다.
/// </summary>
[System.Serializable]
public abstract class ImpactAction
{
    public abstract void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe);

    protected void ApplyCommonSynergyDebuffs(GameObject target, ThrowRecipe recipe)
    {
        // 이제 이 로직은 ThrowEventBus.OnThrowImpactAfterDamage 핸들러로 위임될 예정이므로 공란으로 비워둡니다.
    }
}
