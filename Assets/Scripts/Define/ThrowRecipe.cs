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

    // 주력 유닛에 의해 결정되는 최종 배율
    public float masterMultiplier = 1.0f;

    /// <summary>
    /// 효과의 위력(Value)을 계산합니다.
    /// </summary>
    public float GetScaledValue(float baseValue)
    {
        if (baseValue <= 0) return 0;

        float modeMultiplier = 1.0f;
        switch (targetingMode)
        {
            case TargetingMode.Target: modeMultiplier = 2.0f; break; 
            case TargetingMode.Self:   modeMultiplier = 1.5f; break; 
            case TargetingMode.Area:   modeMultiplier = 1.0f; break; 
        }

        // 보물에 의한 위력 증가 보너스 (예: 0.3f는 30% 증가)
        float treasurePowerBonus = 0f; 

        // [최종 계산] 보조 유닛 수치 * 타겟팅 모드 배율 * 주력 유닛 배율 * (1 + 보물 보너스)
        return baseValue * modeMultiplier * masterMultiplier * (1.0f + treasurePowerBonus);
    }

    /// <summary>
    /// 광역(Area) 모드일 때의 최종 효과 범위를 계산합니다.
    /// </summary>
    public float GetScaledRadius()
    {
        float rangeMultiplier = 1.0f;
        // rangeMultiplier = GameManager.Instance.dataManager.GetAreaRangeMultiplier();
        return baseAreaRadius * rangeMultiplier;
    }

    /// <summary>
    /// 효과를 총 몇 번 실행할지 루프 횟수를 반환합니다.
    /// </summary>
    public int GetTotalExecutionCount()
    {
        // 보물에 의한 반복 파워 보너스 (100%당 +1회)
        float treasureRepeatBonusPower = 0f; 
        // treasureRepeatBonusPower = GameManager.Instance.dataManager.GetTreasureRepeatPower();

        // 1(기본 실행) + 마법사 수 + 보물 추가 횟수
        return 1 + magicianCount + Mathf.FloorToInt(treasureRepeatBonusPower);
    }
}
