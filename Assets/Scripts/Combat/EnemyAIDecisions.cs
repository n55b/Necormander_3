public enum EnemyFollowAction { Wait, ContinueRetreat, BeginRetreat, Chase }
public enum EliteAction { None, Special, BasicAttack, Chase }

public static class EnemyAIDecisions
{
    public static AIState StandardState(AIState current, bool attacking, bool hasTarget, bool escortingPlayer,
        float distance, float escortDistance, float attackDistance, bool attackReady)
    {
        if (current == AIState.Attack && attacking) return current;
        if (!hasTarget) return AIState.Idle;
        if (escortingPlayer) return distance > escortDistance ? AIState.Follow : AIState.Idle;
        return attackReady && distance <= attackDistance ? AIState.Attack : AIState.Follow;
    }

    public static EnemyFollowAction Follow(bool rangedCooldown, bool targetWithinRetreatRange, bool retreatPathFinished)
    {
        if (!rangedCooldown) return EnemyFollowAction.Chase;
        if (!targetWithinRetreatRange) return EnemyFollowAction.Wait;
        return retreatPathFinished ? EnemyFollowAction.BeginRetreat : EnemyFollowAction.ContinueRetreat;
    }

    public static EliteAction Elite(bool specialDue, bool attacking, bool basicAttackReady)
    {
        if (specialDue) return EliteAction.Special;
        if (attacking) return EliteAction.None;
        return basicAttackReady ? EliteAction.BasicAttack : EliteAction.Chase;
    }

    public static AIState BoneMasterPhaseOne(bool recoveryLocked, bool inRange, bool chaseTimeRemaining)
    {
        // The first phase may commit to a long-range pattern after its chase limit.
        return recoveryLocked || (!inRange && chaseTimeRemaining) ? AIState.Follow : AIState.Attack;
    }

    public static AIState BoneMasterPhaseTwo(bool outOfRange, bool executionDone, bool recoveryLocked)
    {
        // The one-time execution opens the phase as soon as the player enters range.
        if (outOfRange) return AIState.Follow;
        if (!executionDone) return AIState.Skill;
        return recoveryLocked ? AIState.Follow : AIState.Attack;
    }
}
