using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 투척 충격이 발생했을 때의 실제 게임플레이 효과와 연출을 실행하는 매니저입니다.
/// </summary>
public class ThrowImpactManager : MonoBehaviour
{
    public void Initialize()
    {
        Debug.Log("<color=cyan>[ThrowImpactManager]</color> Initialized.");
    }

    /// <summary>
    /// 투척 충격 효과 시퀀스를 시작합니다.
    /// </summary>
    public void ProcessThrowImpact(ThrowRecipe recipe, Vector2 impactPos, Vector2 travelDir)
    {
        StartCoroutine(ExecuteImpactRoutine(recipe, impactPos, travelDir));
    }

    private IEnumerator ExecuteImpactRoutine(ThrowRecipe recipe, Vector2 impactPos, Vector2 travelDir)
    {
        int totalExecutions = recipe.GetTotalExecutionCount();
        
        // Area 모드의 경우 첫 프레임에 대상 캐싱
        List<GameObject> targets = null;
        if (recipe.targetingMode == TargetingMode.Area)
        {
            targets = ScanAreaTargets(recipe, impactPos);
        }

        // [핵심 수정] 첫 번째 발동 전 아주 짧은 대기 (1프레임)
        // 투척을 실행한 프레임(Sync)에서 발생하는 다른 물리/상태 로직과의 충돌을 방지합니다.
        yield return null;

        for (int i = 0; i < totalExecutions; i++)
        {
            ApplyRecipe(recipe, i, impactPos, travelDir, targets);
            
            if (i < totalExecutions - 1)
                yield return new WaitForSeconds(0.1f);
        }
    }

    private void ApplyRecipe(ThrowRecipe recipe, int index, Vector2 pos, Vector2 travelDir, List<GameObject> areaTargets)
    {
        switch (recipe.targetingMode)
        {
            case TargetingMode.Target:
                // [수정] 타겟이 없더라도 착지 지점(pos)에 이펙트는 보여줌
                Vector2 vfxPos = (recipe.finalTarget != null) ? (Vector2)recipe.finalTarget.transform.position : pos;
                SpawnImpactVFX(recipe, vfxPos, false);

                if (recipe.finalTarget != null)
                {
                    ApplyLogicToTarget(recipe, recipe.finalTarget, pos, travelDir);
                }
                else if (index == 0)
                {
                    Debug.Log($"<color=gray>[Impact]</color> Target mode executed, but <b>No Target</b> found at {pos}. Only VFX spawned.");
                }
                break;
            case TargetingMode.Area:
                SpawnImpactVFX(recipe, pos, true);
                if (areaTargets != null && areaTargets.Count > 0)
                {
                    foreach (var target in areaTargets)
                    {
                        if (target != null) ApplyLogicToTarget(recipe, target, pos, travelDir);
                    }
                }
                else if (index == 0)
                {
                    Debug.Log($"<color=gray>[Impact]</color> Area mode executed, but <b>0 targets</b> in range at {pos}");
                }
                break;
            case TargetingMode.Self:
                GameObject player = GameManager.Instance.PLAYERCONTROLLER.gameObject;
                SpawnImpactVFX(recipe, player.transform.position, false);
                ApplyLogicToTarget(recipe, player, pos, travelDir);
                break;
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

            if (recipe.HasAction(ImpactActionType.CC) && registry.ccAreaPrefab != null)
            {
                GameObject vfx = Instantiate(registry.ccAreaPrefab, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Destroy(vfx, duration);
                spawnedAnySpecific = true;
            }

            if (recipe.HasAction(ImpactActionType.Shield) && registry.shieldAreaPrefab != null)
            {
                GameObject vfx = Instantiate(registry.shieldAreaPrefab, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Destroy(vfx, duration);
                spawnedAnySpecific = true;
            }

            if (!spawnedAnySpecific && recipe.HasAction(ImpactActionType.Damage) && registry.basicAreaVFX != null)
            {
                GameObject vfx = Instantiate(registry.basicAreaVFX, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Destroy(vfx, duration);
            }
        }

        if (recipe.HasAction(ImpactActionType.Knockback) && registry.formationAreaVFX != null)
        {
            GameObject vfx = Instantiate(registry.formationAreaVFX, spawnPos, Quaternion.identity);
            float scale = isArea ? recipe.GetScaledRadius() : 1.0f;
            vfx.transform.localScale = Vector3.one * (scale * 2f);
            Destroy(vfx, 0.5f);
        }
    }

    private void ApplyLogicToTarget(ThrowRecipe recipe, GameObject target, Vector2 impactPos, Vector2 travelDir)
    {
        if (target == null) return;
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        
        if (target.TryGetComponent<BaseEntity>(out var entity))
        {
            if (entity.team == Team.Enemy)
            {
                // 1. 데미지 적용
                float dmgVal = recipe.GetActionValue(ImpactActionType.Damage);
                if (dmgVal > 0)
                {
                    float finalDamage = recipe.GetScaledValue(dmgVal);
                    entity.Stats.GetDamage(new DamageInfo(finalDamage, DamageType.Physical, null));
                }

                // 2. CC(슬로우) 적용
                float ccVal = recipe.GetActionValue(ImpactActionType.CC);
                if (ccVal > 0)
                {
                    float slowAmount = recipe.GetScaledValue(ccVal);
                    float duration = 5.0f;
                    entity.Stats.ApplySlow("ThrowCC", slowAmount, duration);
                    if (registry != null && registry.ccAttachVFX != null)
                    {
                        GameObject vfx = Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                        Destroy(vfx, duration);
                    }
                }

                // 3. 넉백 적용
                if (recipe.HasAction(ImpactActionType.Knockback)) 
                    ApplyKnockback(recipe, target, impactPos, travelDir);
            }
            else // 아군 미니언
            {
                bool allowShield = (recipe.targetingMode == TargetingMode.Area) || (recipe.targetTeam == Team.Ally);
                float shieldVal = recipe.GetActionValue(ImpactActionType.Shield);
                if (shieldVal > 0 && allowShield)
                {
                    float finalShield = recipe.GetScaledValue(shieldVal);
                    float duration = 3.0f;
                    entity.Stats.AddShield(finalShield, duration);
                    if (registry != null && registry.shieldAttachVFX != null)
                    {
                        GameObject vfx = Instantiate(registry.shieldAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                        entity.Stats.SetShieldVFX(vfx);
                    }
                }
            }
        }
        else if (target.CompareTag("Player"))
        {
            CharacterStat pStat = target.GetComponentInChildren<CharacterStat>();
            if (pStat == null) return;

            // 1. 보호막 적용
            bool allowShield = (recipe.targetingMode == TargetingMode.Self) || (recipe.targetingMode == TargetingMode.Area) || (recipe.targetTeam == Team.Ally);
            float shieldVal = recipe.GetActionValue(ImpactActionType.Shield);
            if (shieldVal > 0 && allowShield)
            {
                float finalShield = recipe.GetScaledValue(shieldVal);
                pStat.AddShield(finalShield, 3.0f);
                if (registry != null && registry.shieldAttachVFX != null)
                {
                    GameObject vfx = Instantiate(registry.shieldAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                    pStat.SetShieldVFX(vfx);
                }
            }

            // 2. 사제 효과 (정화 VFX)
            if (recipe.HasAction(ImpactActionType.CC) && (recipe.targetingMode == TargetingMode.Self || recipe.targetingMode == TargetingMode.Area))
            {
                if (registry != null && registry.ccAttachVFX != null)
                {
                    GameObject vfx = Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                    pStat.SetCCVFX(vfx);
                    Destroy(vfx, 1.0f);
                }
            }

            // 3. 진형 파괴 (대시)
            if (recipe.HasAction(ImpactActionType.Knockback) && (recipe.targetingMode == TargetingMode.Self || recipe.targetingMode == TargetingMode.Area))
            {
                ApplyKnockback(recipe, target, impactPos, travelDir);
            }
        }
    }

    private void ApplyKnockback(ThrowRecipe recipe, GameObject target, Vector2 impactPos, Vector2 travelDir)
    {
        CharacterStat stat = target.GetComponentInChildren<CharacterStat>();
        if (stat == null) return;

        float baseForce = recipe.GetActionValue(ImpactActionType.Knockback);
        float knockbackForce = recipe.GetScaledValue(baseForce);
        Vector2 knockbackDir = Vector2.zero;

        if (target.CompareTag("Player"))
        {
            var pc = GameManager.Instance.PLAYERCONTROLLER;
            if (pc != null)
            {
                // [수정] 플레이어가 대시할 때는 현재 이동 입력(MoveInput)을 최우선으로 함 (지그재그 무빙 지원)
                knockbackDir = pc.MoveInput;
                
                // 이동 입력이 없다면 마우스 방향(travelDir) 혹은 충격 지점으로부터의 반대 방향 사용
                if (knockbackDir == Vector2.zero)
                {
                    knockbackDir = (recipe.targetingMode == TargetingMode.Self) ? travelDir : ((Vector2)target.transform.position - impactPos).normalized;
                }
                
                Debug.Log($"<color=yellow>[Dash]</color> 플레이어 대시 실행! 방향: {knockbackDir}, 힘: {knockbackForce:F1}");
            }
        }
        else
        {
            // 적군/미니언 넉백 로직 (기존 유지)
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
            stat.ApplyKnockback(knockbackDir.normalized, knockbackForce);
    }
}
