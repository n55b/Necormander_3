using System.Collections.Generic;
using UnityEngine;

public enum TargetingMode { Self, Target, Area }

/// <summary>
/// 투척 시 미니언들의 조합을 분석한 결과물(순수 데이터 및 액션 묶음)입니다.
/// 아키텍처 개선을 위해 성격에 따라 데이터가 그룹화되어 있습니다.
/// </summary>
public class ThrowRecipe
{
    /// <summary>
    /// 투척의 맥락과 기본 설정을 담는 그룹입니다.
    /// </summary>
    public class BasicInfo
    {
        public TargetingMode targetingMode = TargetingMode.Self;
        public Team targetTeam = Team.Enemy;
        public GameObject finalTarget; 
        public Vector2 impactPoint;    
        public float chargeRatio;
        public bool isImmediateApplied = false;
        public bool isMissed = false; // [추가] 타겟 적중 실패 여부

        public void CopyFrom(BasicInfo other)
        {
            targetingMode = other.targetingMode;
            targetTeam = other.targetTeam;
            finalTarget = other.finalTarget;
            impactPoint = other.impactPoint;
            chargeRatio = other.chargeRatio;
            isImmediateApplied = other.isImmediateApplied;
            isMissed = other.isMissed;
        }
    }

    /// <summary>
    /// 각종 강화 시스템에서 계산된 수치적 배율 그룹입니다.
    /// </summary>
    public class Modifiers
    {
        public float modeMultiplier = 1.0f;
        public float chargeMultiplier = 1.0f;
        public float treasurePowerMultiplier = 1.0f; 
        public float abilityMultiplier = 1.0f; 
        public int treasureRepeatBonus = 0;

        public void CopyFrom(Modifiers other)
        {
            modeMultiplier = other.modeMultiplier;
            chargeMultiplier = other.chargeMultiplier;
            treasurePowerMultiplier = other.treasurePowerMultiplier;
            abilityMultiplier = other.abilityMultiplier;
            treasureRepeatBonus = other.treasureRepeatBonus;
        }
    }

    /// <summary>
    /// 비행 중 실시간으로 변화하는 궤적 및 연쇄 상태 그룹입니다.
    /// </summary>
    public class TrajectoryState
    {
        public int bounceCount = 0;
        public bool isBouncing = false;
        public int pierceCount = 0;
        public int maxPierce = 0;
        public bool isMaster = true;
        
        public List<GameObject> hitTargets = new List<GameObject>();
        public List<IThrowable> heldUnits = new List<IThrowable>();

        public void CopyFrom(TrajectoryState other)
        {
            bounceCount = other.bounceCount;
            isBouncing = other.isBouncing;
            pierceCount = other.pierceCount;
            maxPierce = other.maxPierce;
            isMaster = other.isMaster;
            hitTargets = new List<GameObject>(other.hitTargets);
            heldUnits = new List<IThrowable>(other.heldUnits);
        }
    }

    // 그룹 인스턴스
    public BasicInfo info = new BasicInfo();
    public Modifiers modifiers = new Modifiers();
    public TrajectoryState state = new TrajectoryState();

    // 액션 리스트는 최상위에 유지 (자주 접근함)
    public List<ImpactAction> actions = new List<ImpactAction>();

    /// <summary>
    /// 효과의 최종 위력 수치를 계산합니다.
    /// </summary>
    public float GetScaledValue(float baseValue)
    {
        if (baseValue <= 0) return 0;
        return baseValue * modifiers.modeMultiplier * modifiers.chargeMultiplier * 
               modifiers.treasurePowerMultiplier * modifiers.abilityMultiplier;
    }

    /// <summary>
    /// 광역 모드일 때의 최종 범위를 계산합니다.
    /// </summary>
    public float GetScaledRadius()
    {
        float radius = 3.0f;
        foreach (var a in actions)
        {
            if (a is ArcherAction archer) radius = archer.radius;
        }
        return radius;
    }

    /// <summary>
    /// 효과를 총 몇 번 실행할지 결정합니다.
    /// </summary>
    public int GetTotalExecutionCount()
    {
        int bonus = 0;
        foreach (var a in actions)
        {
            if (a is MagicianAction magi) bonus += magi.repeatCount;
        }
        return 1 + bonus + modifiers.treasureRepeatBonus;
    }

    /// <summary>
    /// 특정 타입의 액션이 포함되어 있는지 확인합니다.
    /// </summary>
    public bool HasAction<T>() where T : ImpactAction
    {
        return actions.Exists(a => a is T);
    }
}
