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

    public void ProcessThrowImpact(ThrowRecipe recipe, Vector2 impactPos, Vector2 travelDir, ThrowCluster cluster = null)
    {
        StartCoroutine(ExecuteImpactRoutine(recipe, impactPos, travelDir, cluster));
    }

    private IEnumerator ExecuteImpactRoutine(ThrowRecipe recipe, Vector2 impactPos, Vector2 travelDir, ThrowCluster cluster)
    {
        // [추가] 빗나간 투척에 대한 전용 처리 (실패 경로)
        if (recipe.info.isMissed)
        {
            HandleMissedImpact(recipe, impactPos);
            yield break;
        }

        int totalExecutions = recipe.GetTotalExecutionCount();
        List<GameObject> targets = (recipe.info.targetingMode == TargetingMode.Area) ? ScanAreaTargets(recipe, impactPos) : null;

        // [추가] 던지기 능력 Hook (OnImpact)
        if (InventoryManager.Instance != null)
        {
            bool isDirect = recipe.info.chargeRatio >= 0.98f;
            foreach (var ability in InventoryManager.Instance.ActiveAbilities)
            {
                if (ability != null && ability.IsApplicable(isDirect, recipe.info.targetingMode))
                {
                    ability.OnImpact(recipe, impactPos, cluster);
                }
            }
        }

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
        switch (recipe.info.targetingMode)
        {
            case TargetingMode.Target:
                Vector2 vfxPos = (recipe.info.finalTarget != null) ? (Vector2)recipe.info.finalTarget.transform.position : pos;
                SpawnImpactVFX(recipe, vfxPos, false);
                if (recipe.info.finalTarget != null) ApplyActionsToTarget(recipe, recipe.info.finalTarget, pos, travelDir);
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
        
        var status = target.GetComponentInChildren<CharacterStatus>();
        if (status != null)
        {
            // 1. 디버프 보석 효과 적용 (레시피 기반 - 기존 로직)
            if (recipe.modifiers.debuffStacks.Count > 0)
            {
                foreach (var kvp in recipe.modifiers.debuffStacks)
                {
                    status.AddDebuffStack(kvp.Key, kvp.Value);
                }
            }

            // 2. [신규] 귀수 속성 부여 (전역 보석 효과)
            if (InventoryManager.Instance != null)
            {
                foreach (var kvp in InventoryManager.Instance.GlobalGemStats.HandAttributes)
                {
                    if (kvp.Value > 0)
                    {
                        float amount = kvp.Value;
                        status.AddDebuffStack(kvp.Key, amount);
                    }
                }

                // [특수] 치명적인 독: 현재 부여된 독 스택을 2배로 올려줌 (리마크 기준)
                if (InventoryManager.Instance.HasUniqueEffect(GemUniqueType.LethalPoison))
                {
                    int current = status.GetDebuffStack(DebuffStackType.Poison);
                    if (current > 0)
                    {
                        status.AddDebuffStack(DebuffStackType.Poison, (float)current);
                    }
                }
            }
        }

        // 3. 기존 액션들 실행
        foreach (var action in recipe.actions)
        {
            action.Execute(target, impactPos, travelDir, recipe);
        }
    }

    /// <summary>
    /// 타겟을 맞추지 못했을 때의 처리를 담당합니다. (추후 실패 VFX 등 추가 가능)
    /// </summary>
    private void HandleMissedImpact(ThrowRecipe recipe, Vector2 impactPos)
    {
        Debug.Log("<color=gray>[Impact]</color> Throw missed. No effective actions triggered.");
        // 예: Instantiate(missedVFX, impactPos, Quaternion.identity);
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
