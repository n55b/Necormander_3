using UnityEngine;
using Necromancer.Player;
using Necromancer.Object;
using System.Collections.Generic;

namespace Necromancer.Player.Effects
{
    /// <summary>
    /// 전사 + 마법사 조합 효과 데이터입니다.
    /// 서포터 중 방패병이 많으면 범위가, 마법사가 많으면 유지 시간이 증가합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "WarriorMagicianCombinationImpact", menuName = "Necromancer/Throw/Combination Impact/WarriorMagicianImpact")]
    public class WarriorMagicianCombinationImpactSO : BaseCombinationEffectSO
    {
        [Header("중력장 설정")]
        [SerializeField] private GameObject gravityFieldPrefab;
        [SerializeField] private LayerMask enemyLayer;

        [Header("기본 수치")]
        [SerializeField] private float baseRadius = 3.5f;
        [SerializeField] private float baseDuration = 3f;
        [SerializeField] private float pullStrength = 5f;

        [Header("서포터 보너스 설정")]
        [SerializeField] private float radiusPerShieldbearer = 1.5f; // 방패병 1마리당 범위 증가
        [SerializeField] private float durationPerMagician = 1f;     // 마법사 1마리당 지속 시간 증가

        public override void Execute(CombinationContext context)
        {
            Vector2 impactPos = context.impactPosition;

            // 1. 서포터 분석
            int extraShieldbearers = 0;
            int extraMagicians = 0;

            if (context.supporters != null)
            {
                foreach (var supporter in context.supporters)
                {
                    if (supporter.MinionType == CommandData.SkeletonShieldbearer)
                        extraShieldbearers++;
                    else if (supporter.MinionType == CommandData.SkeletonMagician)
                        extraMagicians++;
                }
            }

            // 2. 최종 수치 계산
            float chargeFactor = context.chargeRatio; 
            
            // 범위: 기본 + (차지 보너스) + (방패병 보너스)
            float finalRadius = (baseRadius * (1f + chargeFactor * 0.4f)) + (extraShieldbearers * radiusPerShieldbearer);
                                
            // 지속 시간: 기본 + (마법사 보너스)
            float finalDuration = baseDuration + (extraMagicians * durationPerMagician);

            // 3. 중력장 생성
            if (gravityFieldPrefab != null)
            {
                GameObject fieldObj = Instantiate(gravityFieldPrefab, impactPos, Quaternion.identity);
                if (fieldObj.TryGetComponent<GravityFieldThrowEffect>(out var effect))
                {
                    effect.Initialize(finalRadius, finalDuration, pullStrength, enemyLayer);
                }
            }
            else
            {
                Debug.LogError("[WarriorMagicianCombinationImpactSO] Gravity Field Prefab is missing!");
            }

            Debug.Log($"<color=purple>[Combination]</color> 중력장 생성! (범위: {finalRadius:F1}, 시간: {finalDuration:F1}s)");
        }
    }
}
