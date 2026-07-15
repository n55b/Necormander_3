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

    public override void Execute(Transform user, MinionDataSO data, List<Transform> validTargets)
    {
        var caster = user.GetComponent<MinionSkillCaster>();
        if (caster == null) return;
        if (data == null) data = caster.Data;
        if (data == null) return;

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
        
        // 대시와 동일 판정: 미니언이 벽/낭떠러지를 뚫고 타겟으로 순간이동하지 않도록 목적지 제동.
        Vector2 rawTelePos = (Vector2)closestTarget.position - dirFromPlayer * 0.5f;
        Vector2 teleTo = rawTelePos - (Vector2)user.position;
        float teleDist = teleTo.magnitude;
        user.position = teleDist > 0.001f
            ? SkillCombatUtil.GetSafeDestination(user.position, teleTo / teleDist, teleDist)
            : rawTelePos;

        PlaySkillSound();
        ShakeCamera();

        var targetStat = closestTarget.GetComponent<CharacterStat>();
        if (targetStat == null || targetStat.Status == null) return;

        // 1. 데미지 입히기 옵션
        if (synergyType == DebuffSynergyType.DamageOnly)
        {
            float finalDamage = data.attack * damageMultiplier;
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
