using System.Collections.Generic;
using UnityEngine;

public enum TargetingMode { Self, Target, Area }

/// <summary>
/// 투척 시 미니언들의 조합을 분석한 결과물(순수 데이터 및 액션 묶음)입니다.
/// </summary>
public class ThrowRecipe
{
    public class BasicInfo
    {
        public TargetingMode targetingMode = TargetingMode.Self;
        public Team targetTeam = Team.Enemy;
        public GameObject finalTarget; 
        public Vector2 impactPoint;    
        public float chargeRatio;
        public bool isImmediateApplied = false;
        public bool isMissed = false;
        public bool isDirect = false; // [추가] 직구 여부

        public void CopyFrom(BasicInfo other)
        {
            targetingMode = other.targetingMode;
            targetTeam = other.targetTeam;
            finalTarget = other.finalTarget;
            impactPoint = other.impactPoint;
            chargeRatio = other.chargeRatio;
            isImmediateApplied = other.isImmediateApplied;
            isMissed = other.isMissed;
            isDirect = other.isDirect;
        }
    }

    public class Modifiers
    {
        public float chargeMultiplier = 1.0f;
        public float treasurePowerMultiplier = 1.0f;
        public float abilityMultiplier = 1.0f;
        public float gemPowerMultiplier = 1.0f; // [추가] 보석/시너지로 인한 투척 효율 곱연산
        public float gemDamageMultiplier = 1.0f; // [추가] 데미지에만 적용되는 추가 배율 (거리 패널티 등)
        public int treasureRepeatBonus = 0;
        
        // [신규] 데미지 계산을 위한 베이스와 보너스 분리
        public float baseDamage = 0f;
        public float bonusDamage = 0f;

        // [추가] 보석 등으로 인한 디버프 부여 데이터 (스택형)

        public void CopyFrom(Modifiers other)
        {
            chargeMultiplier = other.chargeMultiplier;
            treasurePowerMultiplier = other.treasurePowerMultiplier;
            abilityMultiplier = other.abilityMultiplier;
            gemPowerMultiplier = other.gemPowerMultiplier;
            gemDamageMultiplier = other.gemDamageMultiplier;
            treasureRepeatBonus = other.treasureRepeatBonus;
            baseDamage = other.baseDamage;
            bonusDamage = other.bonusDamage;
        }
    }

    public class TrajectoryState
    {
        public int bounceCount = 0;
        public bool isBouncing = false;
        public int pierceCount = 0;
        public int maxPierce = 0;
        public bool isMaster = true;
        
        public List<GameObject> hitTargets = new List<GameObject>();
        public List<IThrowable> heldUnits = new List<IThrowable>();
        public List<GameObject> areaTargets = new List<GameObject>(); // [추가] 범위 내 타겟 리스트 (궁수 시너지/유니크용)

        public void CopyFrom(TrajectoryState other)
        {
            bounceCount = other.bounceCount;
            isBouncing = other.isBouncing;
            pierceCount = other.pierceCount;
            maxPierce = other.maxPierce;
            isMaster = other.isMaster;
            hitTargets = new List<GameObject>(other.hitTargets);
            heldUnits = new List<IThrowable>(other.heldUnits);
            if (other.areaTargets != null) areaTargets = new List<GameObject>(other.areaTargets);
        }
    }

    public BasicInfo info = new BasicInfo();
    public Modifiers modifiers = new Modifiers();
    public TrajectoryState state = new TrajectoryState();

    public List<ImpactAction> actions = new List<ImpactAction>();

    public float GetScaledEffectValue(float baseValue)
    {
        if (baseValue <= 0) return 0;
        return baseValue * modifiers.chargeMultiplier * 
               modifiers.treasurePowerMultiplier * modifiers.abilityMultiplier * modifiers.gemPowerMultiplier;
    }

    // [신규] 기본 데미지(5.0)는 데미지 증폭 배율(거리 패널티 등)만 적용
    public float GetScaledBaseDamage()
    {
        if (modifiers.baseDamage <= 0) return 0;
        return modifiers.baseDamage * modifiers.gemDamageMultiplier;
    }

    // [신규] 전사 추가 데미지는 효율 증폭 배율(시너지 등) 적용
    public float GetScaledBonusDamage()
    {
        return GetScaledEffectValue(modifiers.bonusDamage);
    }
    
    // [신규] 최종 합산 데미지
    public float GetFinalDamage()
    {
        return GetScaledBaseDamage() + GetScaledBonusDamage();
    }

    public float GetScaledRadius()
    {
        float radius = 3.0f;
        foreach (var a in actions)
        {
            if (a is ArcherAction archer) radius = archer.radius;
        }
        return GetScaledEffectValue(radius);
    }

    public int GetTotalExecutionCount()
    {
        int bonus = 0;
        foreach (var a in actions)
        {
            if (a is MagicianAction magi) bonus += magi.repeatCount;
        }
        return 1 + bonus + modifiers.treasureRepeatBonus;
    }

    public bool HasAction<T>() where T : ImpactAction
    {
        return actions.Exists(a => a is T);
    }
}
