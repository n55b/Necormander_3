using UnityEngine;

/// <summary>
/// 창병: 적군을 넉백시키거나 플레이어를 대시하게 합니다.
/// </summary>
public class SpearmanAction : ImpactAction
{
    public float force;
    public SpearmanAction(float val) => force = val;

    public override void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe)
    {
        CharacterStat stat = target.GetComponentInChildren<CharacterStat>();
        if (stat == null) return;

        float knockbackForce = recipe.GetScaledValue(force);
        Vector2 knockbackDir = Vector2.zero;

        if (target.CompareTag("Player"))
        {
            var pc = GameManager.Instance.PLAYERCONTROLLER;
            if (pc != null)
            {
                knockbackDir = pc.MoveInput;
                if (knockbackDir == Vector2.zero) knockbackDir = (recipe.targetingMode == TargetingMode.Self) ? travelDir : ((Vector2)target.transform.position - impactPos).normalized;
                Debug.Log($"<color=yellow>[Dash]</color> 플레이어 대시! 방향: {knockbackDir}, 힘: {knockbackForce:F1}");
            }
        }
        else
        {
            if (recipe.targetingMode == TargetingMode.Area)
            {
                knockbackDir = ((Vector2)target.transform.position - impactPos).normalized;
                if (knockbackDir == Vector2.zero) knockbackDir = Random.insideUnitCircle.normalized;
                knockbackForce *= 1.5f; 
            }
            else
            {
                knockbackDir = travelDir;
                if (knockbackDir == Vector2.zero) knockbackDir = ((Vector2)target.transform.position - impactPos).normalized;
            }
        }

        if (knockbackDir != Vector2.zero)
            stat.Status.ApplyKnockback(knockbackDir.normalized, knockbackForce);
    }
}
