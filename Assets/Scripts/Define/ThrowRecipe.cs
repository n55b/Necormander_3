using System.Collections.Generic;
using UnityEngine;

public enum TargetingMode { Self, Target, Area }

/// <summary>
/// 투척 시 미니언들의 조합을 분석한 결과물입니다.
/// </summary>
public class ThrowRecipe
{
    public TargetingMode targetingMode = TargetingMode.Self;
    public Team targetTeam = Team.Enemy;
    
    // 효과 포함 여부 및 기본 수치
    public bool hasCC = false;
    public float ccBaseValue = 0f;

    public bool hasShield = false;
    public float shieldBaseValue = 0f;

    public bool hasFormation = false;
    public float formationBaseValue = 0f;

    // 투척 데미지 관련
    public float impactDamage = 0f;
    
    // 마법사에 의한 반복 횟수 (1명당 +1회)
    public int magicianCount = 0;

    // 타겟팅 정보
    public GameObject finalTarget; 
    public Vector2 impactPoint;    
    public float chargeRatio;
    public float baseAreaRadius = 3.0f; 

    // 조합의 주력 유닛(전사/궁수)에 의해 결정된 모드 배율
    public float modeMultiplier = 1.0f;

    // 차징 정도에 따른 위력 배율
    public float chargeMultiplier = 1.0f;

    // 즉시 발동 여부 (Self 모드 전용)
    public bool isImmediateApplied = false;

    /// <summary>
    /// 효과의 위력(Value)을 계산합니다.
    /// </summary>
    public float GetScaledValue(float baseValue)
    {
        if (baseValue <= 0) return 0;

        float treasurePowerBonus = 0f; 

        // [최종 계산] 합산된 수치 * 모드 배율(전사/궁수의 Mult) * 차징 배율
        return baseValue * modeMultiplier * chargeMultiplier * (1.0f + treasurePowerBonus);
    }

    /// <summary>
    /// 광역(Area) 모드일 때의 최종 효과 범위를 계산합니다.
    /// </summary>
    public float GetScaledRadius()
    {
        float rangeMultiplier = 1.0f;
        return baseAreaRadius * rangeMultiplier;
    }

    /// <summary>
    /// 효과를 총 몇 번 실행할지 루프 횟수를 반환합니다.
    /// </summary>
    public int GetTotalExecutionCount()
    {
        float treasureRepeatBonusPower = 0f; 
        return 1 + magicianCount + Mathf.FloorToInt(treasureRepeatBonusPower);
    }

    /// <summary>
    /// [리팩토링] 지정된 위치에서 효과를 1회 실행합니다.
    /// </summary>
    public void Execute(int index, Vector2 pos, Vector2 travelDir, List<GameObject> areaTargets = null)
    {
        switch (targetingMode)
        {
            case TargetingMode.Target:
                if (finalTarget != null)
                {
                    SpawnImpactVFX(finalTarget.transform.position, false);
                    ApplyLogicToTarget(finalTarget, pos, travelDir);
                }
                else if (index == 0) // 첫 번째 시도에서만 로그 출력
                {
                    Debug.Log($"<color=gray>[Impact]</color> Target mode executed, but <b>No Target</b> found at {pos}");
                }
                break;
            case TargetingMode.Area:
                SpawnImpactVFX(pos, true);
                if (areaTargets != null && areaTargets.Count > 0)
                {
                    foreach (var target in areaTargets)
                    {
                        if (target != null) ApplyLogicToTarget(target, pos, travelDir);
                    }
                }
                else if (index == 0)
                {
                    Debug.Log($"<color=gray>[Impact]</color> Area mode executed, but <b>0 targets</b> in range at {pos}");
                }
                break;
            case TargetingMode.Self:
                GameObject player = GameManager.Instance.PLAYERCONTROLLER.gameObject;
                SpawnImpactVFX(player.transform.position, false);
                ApplyLogicToTarget(player, pos, travelDir);
                break;
        }
    }

    /// <summary>
    /// Area 모드일 때 효과를 적용할 대상들을 미리 스캔합니다.
    /// </summary>
    public List<GameObject> ScanAreaTargets(Vector2 pos)
    {
        float radius = GetScaledRadius();
        Collider2D[] hitColls = Physics2D.OverlapCircleAll(pos, radius);
        List<GameObject> targets = new List<GameObject>();
        HashSet<GameObject> processed = new HashSet<GameObject>();

        foreach (var coll in hitColls)
        {
            GameObject obj = coll.gameObject;
            if (processed.Contains(obj)) continue;
            
            // 유효한 대상(엔티티 또는 플레이어)인지 확인
            if (obj.GetComponent<BaseEntity>() != null || obj.CompareTag("Player"))
            {
                targets.Add(obj);
                processed.Add(obj);
            }
        }
        return targets;
    }

    private void SpawnImpactVFX(Vector2 spawnPos, bool isArea)
    {
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry == null) return;

        float duration = 1.0f;

        if (isArea)
        {
            bool spawnedAnySpecific = false;
            float radius = GetScaledRadius();

            if (hasCC && registry.ccAreaPrefab != null)
            {
                GameObject vfx = Object.Instantiate(registry.ccAreaPrefab, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Object.Destroy(vfx, duration);
                spawnedAnySpecific = true;
            }

            if (hasShield && registry.shieldAreaPrefab != null)
            {
                GameObject vfx = Object.Instantiate(registry.shieldAreaPrefab, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Object.Destroy(vfx, duration);
                spawnedAnySpecific = true;
            }

            if (!spawnedAnySpecific && impactDamage > 0 && registry.basicAreaVFX != null)
            {
                GameObject vfx = Object.Instantiate(registry.basicAreaVFX, spawnPos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * (radius * 2f);
                Object.Destroy(vfx, duration);
            }
        }

        if (hasFormation && registry.formationAreaVFX != null)
        {
            GameObject vfx = Object.Instantiate(registry.formationAreaVFX, spawnPos, Quaternion.identity);
            float scale = isArea ? GetScaledRadius() : 1.0f;
            vfx.transform.localScale = Vector3.one * (scale * 2f);
            Object.Destroy(vfx, 0.5f);
        }
    }

    private void ApplyLogicToTarget(GameObject target, Vector2 impactPos, Vector2 travelDir)
    {
        if (target == null) return;
        if (GameManager.Instance == null || GameManager.Instance.dataManager == null) return;

        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        
        if (target.TryGetComponent<BaseEntity>(out var entity))
        {
            if (entity.team == Team.Enemy)
            {
                if (impactDamage > 0)
                {
                    // [수정] 데미지도 차징 및 모드 배율의 영향을 받도록 스케일링 적용
                    float finalDamage = GetScaledValue(impactDamage);
                    DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, null);
                    entity.Stats.GetDamage(info);
                }

                if (hasCC)
                {
                    float slowAmount = GetScaledValue(ccBaseValue);
                    float duration = 5.0f;
                    entity.Stats.ApplySlow("ThrowCC", slowAmount, duration);

                    if (registry != null && registry.ccAttachVFX != null)
                    {
                        GameObject vfx = Object.Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                        Object.Destroy(vfx, duration);
                    }
                }

                if (hasFormation) ApplyKnockback(target, impactPos, travelDir);
            }
            else // 아군
            {
                bool allowShield = (targetingMode == TargetingMode.Area) || (targetTeam == Team.Ally);
                if (hasShield && allowShield)
                {
                    float shieldAmount = GetScaledValue(shieldBaseValue);
                    float duration = 3.0f;
                    entity.Stats.AddShield(shieldAmount, duration);

                    if (registry != null && registry.shieldAttachVFX != null)
                    {
                        GameObject vfx = Object.Instantiate(registry.shieldAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                        Object.Destroy(vfx, duration);
                    }
                }
            }
        }
        else if (target.CompareTag("Player"))
        {
            CharacterStat pStat = target.GetComponent<CharacterStat>();

            // 1. 보호막 적용 (Self 모드거나 Area 모드일 때)
            bool allowShield = (targetingMode == TargetingMode.Self) || (targetingMode == TargetingMode.Area) || (targetTeam == Team.Ally);
            if (hasShield && allowShield && pStat != null)
            {
                float shieldAmount = GetScaledValue(shieldBaseValue);
                float duration = 3.0f;
                pStat.AddShield(shieldAmount, duration);

                if (registry != null && registry.shieldAttachVFX != null)
                {
                    GameObject vfx = Object.Instantiate(registry.shieldAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                    pStat.SetShieldVFX(vfx);
                }
            }

            // 2. 사제(CC) 효과: 플레이어에게는 정화 VFX만
            if (hasCC && (targetingMode == TargetingMode.Self || targetingMode == TargetingMode.Area))
            {
                if (registry != null && registry.ccAttachVFX != null)
                {
                    GameObject vfx = Object.Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                    if (pStat != null) pStat.SetCCVFX(vfx);
                    Object.Destroy(vfx, 1.0f);
                }
            }

            // 3. 진형 파괴 (Self 모드 대시 또는 Area 모드 넉백)
            if (hasFormation && (targetingMode == TargetingMode.Self || targetingMode == TargetingMode.Area))
            {
                ApplyKnockback(target, impactPos, travelDir);
            }
        }
    }

    private void ApplyKnockback(GameObject target, Vector2 impactPos, Vector2 travelDir)
    {
        if (target.TryGetComponent<CharacterStat>(out var stat))
        {
            float knockbackForce = GetScaledValue(formationBaseValue);
            Vector2 knockbackDir = Vector2.zero;

            // [특수 로직] 플레이어 대시 처리
            if (target.CompareTag("Player"))
            {
                var pc = GameManager.Instance.PLAYERCONTROLLER;
                if (pc != null)
                {
                    // 이동 입력이 있으면 입력 방향으로, 없으면 날아온 방향(또는 마우스 방향)으로 대시
                    knockbackDir = pc.MoveInput;
                    if (knockbackDir == Vector2.zero)
                    {
                        knockbackDir = (targetingMode == TargetingMode.Self) ? travelDir : ((Vector2)target.transform.position - impactPos).normalized;
                    }
                }
            }
            else
            {
                if (targetingMode == TargetingMode.Area)
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
}
