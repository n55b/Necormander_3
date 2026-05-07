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

    public class Modifiers
    {
        public float modeMultiplier = 1.0f;
        public float chargeMultiplier = 1.0f;
        public float treasurePowerMultiplier = 1.0f;
        public float abilityMultiplier = 1.0f;
        public int treasureRepeatBonus = 0;
        
        // [추가] 보석 등으로 인한 디버프 부여 데이터 (스택형)
        public Dictionary<DebuffStackType, float> debuffStacks = new Dictionary<DebuffStackType, float>();

        public void CopyFrom(Modifiers other)
        {
            modeMultiplier = other.modeMultiplier;
            chargeMultiplier = other.chargeMultiplier;
            treasurePowerMultiplier = other.treasurePowerMultiplier;
            abilityMultiplier = other.abilityMultiplier;
            treasureRepeatBonus = other.treasureRepeatBonus;
            debuffStacks = new Dictionary<DebuffStackType, float>(other.debuffStacks);
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

    public BasicInfo info = new BasicInfo();
    public Modifiers modifiers = new Modifiers();
    public TrajectoryState state = new TrajectoryState();

    public List<ImpactAction> actions = new List<ImpactAction>();

    public float GetScaledValue(float baseValue)
    {
        if (baseValue <= 0) return 0;
        return baseValue * modifiers.modeMultiplier * modifiers.chargeMultiplier * 
               modifiers.treasurePowerMultiplier * modifiers.abilityMultiplier;
    }

    public float GetScaledRadius()
    {
        float radius = 3.0f;
        foreach (var a in actions)
        {
            if (a is ArcherAction archer) radius = archer.radius;
        }
        return radius;
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
