using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 궁수 + 마법사 조합 효과 데이터입니다 (레일건).
/// 서포터 중 궁수가 많으면 발사 횟수가, 마법사가 많으면 방어막 파괴 개수가 증가합니다.
/// </summary>
[CreateAssetMenu(fileName = "ArcherMagicianCombinationImpact", menuName = "Necromancer/Throw/Combination Impact/ArcherMagicianImpact")]
public class ArcherMagicianCombinationImpactSO : BaseCombinationEffectSO
{
    [Header("레일건 설정")]
    [SerializeField] private GameObject railgunPrefab;
    [SerializeField] private LayerMask enemyLayer;

    [Header("기본 수치")]
    [SerializeField] private float baseDamage = 25f;
    [SerializeField] private int baseShieldBreak = 1;
    [SerializeField] private float beamWidth = 0.5f;

    [Header("서포터 보너스 설정")]
    [SerializeField] private int extraStrikesPerArcher = 1;      // 궁수 1마리당 추가 발사 횟수
    [SerializeField] private int extraShieldBreakPerMagician = 1; // 마법사 1마리당 추가 방어막 파괴량

    public override void Execute(CombinationContext context)
    {
        Vector2 impactPos = context.impactPosition;

        // 1. 서포터 분석
        int extraArchers = 0;
        int extraMagicians = 0;

        if (context.supporters != null)
        {
            foreach (var supporter in context.supporters)
            {
                if (supporter.MinionType == CommandData.SkeletonArcher)
                    extraArchers++;
                else if (supporter.MinionType == CommandData.SkeletonMagician)
                    extraMagicians++;
            }
        }

        // 2. 최종 수치 계산
        int totalRepeatCount = extraArchers * extraStrikesPerArcher;
        int totalShieldBreak = baseShieldBreak + (extraMagicians * extraShieldBreakPerMagician);
        
        // 차지 수치는 데미지에 영향을 줌
        float chargeFactor = context.chargeRatio; 
        float finalDamage = baseDamage * (1f + chargeFactor * 0.8f);

        // 3. 레일건 효과 생성
        if (railgunPrefab != null)
        {
            GameObject railgunObj = Instantiate(railgunPrefab, impactPos, Quaternion.identity);
            if (railgunObj.TryGetComponent<RailgunThrowEffect>(out var effect))
            {
                effect.Initialize(impactPos, finalDamage, totalShieldBreak, enemyLayer, beamWidth, totalRepeatCount);
            }
        }
        else
        {
            Debug.LogError("[ArcherMagicianCombinationImpactSO] Railgun Prefab is missing!");
        }

        Debug.Log($"<color=blue>[Combination]</color> 레일건 발사! (발사 횟수: {totalRepeatCount + 1}, 방어막 파괴: {totalShieldBreak})");
    }
}
