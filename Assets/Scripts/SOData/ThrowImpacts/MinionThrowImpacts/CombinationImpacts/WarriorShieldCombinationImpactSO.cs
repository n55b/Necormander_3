using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 전사 + 방패병 조합 효과 데이터입니다.
/// 에디터에서 적군 레이어를 직접 지정할 수 있도록 수정되었습니다.
/// </summary>
[CreateAssetMenu(fileName = "WarriorShieldCombinationImpact", menuName = "Necromancer/Throw/Combination Impact/WarriorShieldImpact")]
public class WarriorShieldCombinationImpactSO : BaseCombinationEffectSO
{
    [Header("방패 벽 설정")]
    [SerializeField] private GameObject shieldWallRootPrefab; 
    [SerializeField] private GameObject shieldUnitPrefab;     
    [SerializeField] private LayerMask enemyLayer;             // 에디터에서 지정할 적군 레이어
    
    [Header("기본 수치")]
    [SerializeField] private int baseWallWidth = 3;            
    [SerializeField] private float baseSpeed = 8f;
    [SerializeField] private float baseDistance = 4f;
    [SerializeField] private float baseDamage = 15f;
    [SerializeField] private float knockbackForce = 12f;

    [Header("서포터 보너스 설정")]
    [SerializeField] private float damagePerWarrior = 10f;     
    [SerializeField] private int widthPerShieldbearer = 2;    

    public override void Execute(CombinationContext context)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        Vector2 playerPos = player.transform.position;
        Vector2 impactPos = context.impactPosition;
        Vector2 pushDir = (impactPos - playerPos).normalized;

        int extraWarriors = 0;
        int extraShieldbearers = 0;

        if (context.supporters != null)
        {
            foreach (var supporter in context.supporters)
            {
                if (supporter.MinionType == CommandData.SkeletonWarrior)
                    extraWarriors++;
                else if (supporter.MinionType == CommandData.SkeletonShieldbearer)
                    extraShieldbearers++;
            }
        }

        float chargeFactor = context.chargeRatio; 
        float finalDamage = (baseDamage * (1f + chargeFactor * 0.5f)) + (extraWarriors * damagePerWarrior);
        int finalWidth = baseWallWidth + (extraShieldbearers * widthPerShieldbearer);
        float finalDistance = baseDistance * (1f + chargeFactor);

        if (shieldWallRootPrefab != null && shieldUnitPrefab != null)
        {
            GameObject wallObj = Instantiate(shieldWallRootPrefab, impactPos, Quaternion.identity);
            if (wallObj.TryGetComponent<ShieldWallThrowEffect>(out var effect))
            {
                // 마지막 인자로 enemyLayer를 추가로 전달합니다.
                effect.Initialize(pushDir, baseSpeed, finalDistance, finalDamage, knockbackForce, finalWidth, shieldUnitPrefab, enemyLayer);
            }
        }
    }
}
