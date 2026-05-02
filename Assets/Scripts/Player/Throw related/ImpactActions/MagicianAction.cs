using UnityEngine;

/// <summary>
/// 마법사: 효과의 반복 횟수를 추가합니다. (메타 데이터 전용)
/// </summary>
public class MagicianAction : ImpactAction
{
    public int repeatCount;
    public MagicianAction(float val) => repeatCount = Mathf.FloorToInt(val);

    public override void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe)
    {
        // 마법사 자체는 즉각적인 충격 효과가 없으며, 
        // ThrowImpactManager에서 루프 횟수를 결정하는 용도로 사용됩니다.
    }
}
