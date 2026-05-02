using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 투척 충격이 발생했을 때 효과 실행 스케줄을 관리하는 가벼운 매니저입니다.
/// </summary>
public class ThrowImpactManager : MonoBehaviour
{
    public void Initialize()
    {
        Debug.Log("<color=cyan>[ThrowImpactManager]</color> Initialized.");
    }

    public void ProcessThrowImpact(ThrowRecipe recipe, Vector2 impactPos, Vector2 travelDir)
    {
        StartCoroutine(ExecuteImpactRoutine(recipe, impactPos, travelDir));
    }

    private IEnumerator ExecuteImpactRoutine(ThrowRecipe recipe, Vector2 impactPos, Vector2 travelDir)
    {
        int totalExecutions = recipe.GetTotalExecutionCount();
        List<GameObject> targets = (recipe.targetingMode == TargetingMode.Area) ? ScanAreaTargets(recipe, impactPos) : null;

        // 투척 프레임의 다른 물리 로직과의 간섭 방지를 위해 1프레임 대기
        yield return null;

        for (int i = 0; i < totalExecutions; i++)
        {
            ApplyRecipe(recipe, i, impactPos, travelDir, targets);
            if (i < totalExecutions - 1) yield return new WaitForSeconds(0.1f);
        }
    }

    private void ApplyRecipe(ThrowRecipe recipe, int index, Vector2 pos, Vector2 travelDir, List<GameObject> areaTargets)
    {
        switch (recipe.targetingMode)
        {
            case TargetingMode.Target:
                Vector2 vfxPos = (recipe.finalTarget != null) ? (Vector2)recipe.finalTarget.transform.position : pos;
                SpawnImpactVFX(recipe, vfxPos, false);
                if (recipe.finalTarget != null) ApplyActionsToTarget(recipe, recipe.finalTarget, pos, travelDir);
                break;

            case TargetingMode.Area:
                SpawnImpactVFX(recipe, pos, true);
                if (areaTargets != null) foreach (var t in areaTargets) ApplyActionsToTarget(recipe, t, pos, travelDir);
                break;

            case TargetingMode.Self:
                GameObject player = GameManager.Instance.PLAYERCONTROLLER.gameObject;
                SpawnImpactVFX(recipe, player.transform.position, false);
                ApplyActionsToTarget(recipe, player, pos, travelDir);
                break;
        }
    }

    private void ApplyActionsToTarget(ThrowRecipe recipe, GameObject target, Vector2 impactPos, Vector2 travelDir)
    {
        if (target == null) return;
        
        // [핵심] 매니저는 더 이상 로직을 직접 수행하지 않고, 레시피에 담긴 액션들에게 실행을 위임합니다.
        foreach (var action in recipe.actions)
        {
            action.Execute(target, impactPos, travelDir, recipe);
        }
    }

    private List<GameObject> ScanAreaTargets(ThrowRecipe recipe, Vector2 pos)
    {
        float radius = recipe.GetScaledRadius();
        int targetMask = LayerMask.GetMask("Player", "Army", "Enemy");
        Collider2D[] hitColls = Physics2D.OverlapCircleAll(pos, radius, targetMask);
        
        List<GameObject> targets = new List<GameObject>();
        HashSet<GameObject> processed = new HashSet<GameObject>();

        foreach (var coll in hitColls)
        {
            GameObject obj = coll.gameObject;
            Transform root = obj.transform.root;
            GameObject rootObj = root.gameObject;

            if (processed.Contains(rootObj)) continue;
            if (rootObj.GetComponentInChildren<BaseEntity>() != null || rootObj.CompareTag("Player"))
            {
                targets.Add(rootObj);
                processed.Add(rootObj);
            }
        }
        return targets;
    }

    private void SpawnImpactVFX(ThrowRecipe recipe, Vector2 spawnPos, bool isArea)
    {
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry == null) return;

        float duration = 1.0f;
        if (isArea)
        {
            bool spawnedAnySpecific = false;
            float radius = recipe.GetScaledRadius();

            if (recipe.HasAction<PriestAction>() && registry.ccAreaPrefab != null)
            {
                GameObject vfx = Instantiate(registry.ccAreaPrefab, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Destroy(vfx, duration);
                spawnedAnySpecific = true;
            }

            if (recipe.HasAction<ShieldBearerAction>() && registry.shieldAreaPrefab != null)
            {
                GameObject vfx = Instantiate(registry.shieldAreaPrefab, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Destroy(vfx, duration);
                spawnedAnySpecific = true;
            }

            if (!spawnedAnySpecific && (recipe.HasAction<WarriorAction>() || recipe.HasAction<ArcherAction>()) && registry.basicAreaVFX != null)
            {
                GameObject vfx = Instantiate(registry.basicAreaVFX, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Destroy(vfx, duration);
            }
        }

        if (recipe.HasAction<SpearmanAction>() && registry.formationAreaVFX != null)
        {
            GameObject vfx = Instantiate(registry.formationAreaVFX, spawnPos, Quaternion.identity);
            float scale = isArea ? recipe.GetScaledRadius() : 1.0f;
            vfx.transform.localScale = Vector3.one * (scale * 2f);
            Destroy(vfx, 0.5f);
        }
    }
}
