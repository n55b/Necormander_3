using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [저글링] 능력을 구현하는 클래스입니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowJugglingAbility", menuName = "Necromancer/Growth/Throw Ability/Juggling")]
public class ThrowJugglingAbilitySO : ThrowAbilitySO
{
    [Header("Juggling Visuals")]
    [SerializeField] private GameObject catchZonePrefab;
    [SerializeField] private float stackMultiplier = 0.5f; // 스택당 공격력 증가량

    public override void ModifyRecipe(ThrowRecipe recipe, List<IThrowable> heldObjects)
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        if (player != null)
        {
            var stateManager = player.GetComponent<ThrowAbilityStateManager>();
            if (stateManager != null)
            {
                float bonus = 1.0f + (stateManager.JugglingStacks * stackMultiplier);
                recipe.modifiers.abilityMultiplier *= bonus;
                
                if (stateManager.JugglingStacks > 0)
                {
                    Debug.Log($"<color=yellow>[Juggling]</color> Applying Stack Bonus: {bonus}x (Stacks: {stateManager.JugglingStacks})");
                }
            }
        }
    }

    public override void OnThrowLaunch(ThrowController controller, ThrowRecipe recipe, Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float ratio)
    {
        // 던지는 순간에는 아무것도 하지 않음 (충격 시점으로 로직 이전)
    }

    public override void OnImpact(ThrowRecipe recipe, Vector2 impactPoint, ThrowCluster cluster)
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        if (player == null) return;

        var stateManager = player.GetComponent<ThrowAbilityStateManager>();
        if (stateManager == null) stateManager = player.gameObject.AddComponent<ThrowAbilityStateManager>();

        var throwController = player.GetComponentInChildren<ThrowController>();
        GameObject clusterPrefab = throwController != null ? throwController.clusterPrefab.gameObject : null;

        if (stateManager != null)
        {
            stateManager.SpawnJugglingCatchZone(impactPoint, catchZonePrefab, clusterPrefab);
        }
    }
}
