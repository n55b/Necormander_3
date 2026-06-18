using UnityEngine;
using System.Collections.Generic;

// --- 공용 시너지 핸들러 (이벤트 버스 활용) ---
public class SynergyDamageAmplifier
{
    public static void Initialize()
    {
        DamageEventBus.OnBeforeDamageCalculated += HandleDamageAmplification;
    }

    private static void HandleDamageAmplification(CharacterHealth target, ref DamageInfo info)
    {
        var stat = target.GetComponent<CharacterStat>();
        var status = target.GetComponent<CharacterStatus>();
        if (stat == null || status == null) return;

        bool isEnemyTarget = stat.IsEnemy;
        float remainingDamage = info.amount;

        if (status.GetDebuffBool(DebuffBoolType.Corroded))
        {
            float corrosionAmp = GemRuleSystem.GetCorrosionDamageAmp(isEnemyTarget);
            remainingDamage *= (1.0f + corrosionAmp);
        }
        
        if (status.GetDebuffBool(DebuffBoolType.Corroded))
        {
            float senilityAmp = GemRuleSystem.GetSenilityDamageAmp(isEnemyTarget);
            remainingDamage *= (1.0f + senilityAmp);
        }

        info.amount = remainingDamage;
    }
}

// 사용 중지된(Deprecated) 보석 핸들러들은 Assets/Scripts/Systems/Growth/Deprecated/ 폴더에 백업되어 있습니다.
