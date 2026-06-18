using UnityEngine;
using System.Collections.Generic;

public enum DebuffSynergyType
{
    DamageOnly,
    AddStack
}

[CreateAssetMenu(fileName = "MinionDebuffSynergy", menuName = "Necromancer/Skills/Minion/DebuffSynergy")]
public class MinionDebuffSynergySO : MinionSkillSO
{
    public DebuffSynergyType synergyType;
    
    [Header("Damage Settings (If DamageOnly)")]
    public float damageMultiplier = 1.6f; // 기본 공격력의 160%

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        var ally = user.GetComponent<AllyController>();
        if (ally == null || ally.Stats.Health.IsDead) return;

        // 타겟 찾기 (디버프가 걸려있는 가장 가까운 적)
        Transform closestTarget = null;
        float minDist = float.MaxValue;
        
        if (validTargets != null && validTargets.Count > 0)
        {
            foreach (var vt in validTargets)
            {
                if (vt == null) continue;
                var stat = vt.GetComponent<CharacterStat>();
                if (stat == null || stat.Status == null) continue;

                // 디버프가 최소 1스택이라도 있고, 타입이 None이 아닐 때만 반응
                if (stat.Status.DebuffStackCount > 0 && stat.Status.CurrentDebuffType != DebuffType.None)
                {
                    float dist = Vector2.Distance(user.position, vt.position);
                    if (dist < minDist) { minDist = dist; closestTarget = vt; }
                }
            }
        }

        if (closestTarget == null) return; // 디버프 걸린 적이 없으면 발동 취소

        // 타겟에게 이동
        Vector2 dirFromPlayer = ((Vector2)closestTarget.position - (Vector2)user.position).normalized;
        if (dirFromPlayer == Vector2.zero) dirFromPlayer = Vector2.right;
        
        user.position = (Vector2)closestTarget.position - dirFromPlayer * 0.5f;

        PlaySkillSound();
        ShakeCamera();

        var targetStat = closestTarget.GetComponent<CharacterStat>();
        if (targetStat == null || targetStat.Status == null) return;

        // 1. 데미지 입히기 옵션
        if (synergyType == DebuffSynergyType.DamageOnly)
        {
            float finalDamage = ally.Stats.ATK * damageMultiplier;
            var health = closestTarget.GetComponent<CharacterHealth>();
            if (health != null)
            {
                DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, user.gameObject, false, 1f, false, "Minion Debuff Damage Synergy");
                health.GetDamage(info);
            }
        }
        // 2. 디버프 스택 추가 옵션
        else if (synergyType == DebuffSynergyType.AddStack)
        {
            DebuffStackType stackType = ConvertDebuffTypeToStackType(targetStat.Status.CurrentDebuffType);
            
            if (stackType != DebuffStackType.BloodPop && targetStat.Status.CurrentDebuffType != DebuffType.None)
            {
                targetStat.Status.ApplyElementalDebuff(stackType, 1, user.gameObject);
                Debug.Log($"<color=cyan>[Minion Synergy]</color> {closestTarget.name}에게 {stackType} 1스택 추가 부여!");
            }
            else if (targetStat.Status.CurrentDebuffType == DebuffType.BloodPop)
            {
                targetStat.Status.ApplyElementalDebuff(DebuffStackType.BloodPop, 1, user.gameObject);
                Debug.Log($"<color=cyan>[Minion Synergy]</color> {closestTarget.name}에게 BloodPop 1스택 추가 부여!");
            }
        }
    }

    private DebuffStackType ConvertDebuffTypeToStackType(DebuffType type)
    {
        switch (type)
        {
            case DebuffType.BloodPop: return DebuffStackType.BloodPop;
            case DebuffType.Bleed: return DebuffStackType.Bleed;
            case DebuffType.Wound: return DebuffStackType.Wound;
            case DebuffType.Corrosion: return DebuffStackType.Corrosion;
            case DebuffType.Fracture: return DebuffStackType.Fracture;
            default: return DebuffStackType.BloodPop; // 안전장치
        }
    }
}
