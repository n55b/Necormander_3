using UnityEngine;

/// <summary>
/// 투척 충격 시 실행될 개별 액션의 추상 기반 클래스입니다.
/// </summary>
[System.Serializable]
public abstract class ImpactAction
{
    public abstract void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe);
}
