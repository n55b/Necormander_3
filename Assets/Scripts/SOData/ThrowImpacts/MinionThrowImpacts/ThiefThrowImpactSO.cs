using UnityEngine;

[CreateAssetMenu(fileName = "ThiefThrowImpact", menuName = "Necromancer/Throw/Basic Impact/Thief")]
public class ThiefThrowImpactSO : BaseThrowImpactSO
{
    public override void Apply(ImpactContext context)
    {
        if (context.attacker == null) return;

        // 1. 본체 스탯 반토막
        if (context.attacker.TryGetComponent<CharacterStat>(out var originalStat))
        {
            originalStat.ApplySplitStats();
        }

        // 2. 분신 생성을 위해 본체의 정보(AllyController) 가져오기
        if (context.attacker.TryGetComponent<AllyController>(out var originalAlly))
        {
            // AllyManager 찾기 (플레이어에게 붙어 있음)
            AllyManager allyManager = originalAlly.player.GetComponent<AllyManager>();
            
            if (allyManager != null && originalAlly.MinionData != null)
            {
                // 약간 옆 위치에 분신 소환
                Vector3 spawnPos = context.impactPosition + (Vector2)Random.insideUnitCircle * 0.5f;
                
                // 새로운 분신 소환
                AllyController clone = allyManager.SpawnAlly(originalAlly.MinionData, spawnPos);
                
                // 분신의 스탯도 반토막 냄
                if (clone != null && clone.TryGetComponent<CharacterStat>(out var cloneStat))
                {
                    cloneStat.ApplySplitStats();
                }
                
                Debug.Log("<color=orange>[Thief Impact]</color> 본체와 분신 모두 스탯 반토막 및 분산 완료!");
            }
        }
    }
}
