using UnityEngine;
using Necromancer.Player;
using Necromancer.Object;
using System.Collections.Generic;

namespace Necromancer.Player.Effects
{
    /// <summary>
    /// 방패병 + 사제 조합 효과 데이터입니다.
    /// 서포터 중 방패병이 많으면 성역 범위가, 사제가 많으면 회복량이 증가합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "ShieldbearerPriestCombinationImpact", menuName = "Necromancer/Throw/Combination Impact/ShieldbearerPriestImpact")]
    public class ShieldbearerPriestCombinationImpactSO : BaseCombinationEffectSO
    {
        [Header("성역 설정")]
        [SerializeField] private GameObject sanctuaryPrefab;
        [SerializeField] private LayerMask allyLayer; // Player 및 Ally 레이어 체크 필요

        [Header("기본 수치")]
        [SerializeField] private float baseRadius = 3f;
        [SerializeField] private float baseDuration = 4f;
        [SerializeField] private float baseHealPerSecond = 20f; // 초당 기본 회복량

        [Header("서포터 보너스 설정")]
        [SerializeField] private float radiusPerShieldbearer = 1.2f; // 방패병 1마리당 범위 증가
        [SerializeField] private float healPerPriest = 15f;          // 사제 1마리당 초당 회복량 증가

        public override void Execute(CombinationContext context)
        {
            Vector2 impactPos = context.impactPosition;

            // 1. 서포터 분석
            int extraShieldbearers = 0;
            int extraPriests = 0;

            if (context.supporters != null)
            {
                foreach (var supporter in context.supporters)
                {
                    if (supporter.MinionType == CommandData.SkeletonShieldbearer)
                        extraShieldbearers++;
                    else if (supporter.MinionType == CommandData.SkeletonPriest)
                        extraPriests++;
                }
            }

            // 2. 최종 수치 계산
            float chargeFactor = context.chargeRatio; 
            
            // 범위: 기본 + (차지 보너스) + (방패병 보너스)
            float finalRadius = (baseRadius * (1f + chargeFactor * 0.3f)) + (extraShieldbearers * radiusPerShieldbearer);
            
            // 초당 회복량: 기본 + (사제 보너스)
            float finalHealAmount = baseHealPerSecond + (extraPriests * healPerPriest);

            // 3. 성역 생성
            if (sanctuaryPrefab != null)
            {
                GameObject sanctuaryObj = Instantiate(sanctuaryPrefab, impactPos, Quaternion.identity);
                if (sanctuaryObj.TryGetComponent<SanctuaryThrowEffect>(out var effect))
                {
                    effect.Initialize(finalRadius, baseDuration, finalHealAmount, allyLayer);
                }
            }
            else
            {
                Debug.LogError("[ShieldbearerPriestCombinationImpactSO] Sanctuary Prefab is missing!");
            }

            Debug.Log($"<color=green>[Combination]</color> 신성한 성역 생성! (범위: {finalRadius:F1}, 초당 힐: {finalHealAmount:F1})");
        }
    }
}
