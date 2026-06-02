using System;
using UnityEngine;

public static class StatusEventBus
{
    public delegate void DebuffAppliedHandler(CharacterStatus status, DebuffStackType type, ref float amount);
    public static event DebuffAppliedHandler OnBeforeDebuffApplied;

    public delegate void DebuffMaxStackHandler(CharacterStatus status, DebuffStackType type);
    public static event DebuffMaxStackHandler OnMaxStackReached;

    public delegate void CorrosionStackHandler(CharacterStatus status, ref bool shouldBlock);
    public static event CorrosionStackHandler OnBeforeCorrosionApplied;

    public delegate void BloodPopExplodeBeforeDamageHandler(CharacterHealth sourceHealth, ref float finalDamage, ref float explosionRadius);
    public static event BloodPopExplodeBeforeDamageHandler OnBloodPopExplodeBeforeDamage;

    public delegate void BloodPopExplodeAfterDamageHandler(CharacterHealth sourceHealth, float finalDamage, float explosionRadius, Collider2D[] hitTargets);
    public static event BloodPopExplodeAfterDamageHandler OnBloodPopExplodeAfterDamage;

    public static void TriggerBeforeDebuffApplied(CharacterStatus status, DebuffStackType type, ref float amount)
    {
        OnBeforeDebuffApplied?.Invoke(status, type, ref amount);
    }

    public static void TriggerMaxStackReached(CharacterStatus status, DebuffStackType type)
    {
        OnMaxStackReached?.Invoke(status, type);
    }

    public static bool TriggerBeforeCorrosionApplied(CharacterStatus status)
    {
        bool shouldBlock = false;
        OnBeforeCorrosionApplied?.Invoke(status, ref shouldBlock);
        return shouldBlock;
    }

    public static void TriggerBloodPopExplodeBeforeDamage(CharacterHealth sourceHealth, ref float finalDamage, ref float explosionRadius)
    {
        OnBloodPopExplodeBeforeDamage?.Invoke(sourceHealth, ref finalDamage, ref explosionRadius);
    }

    public static void TriggerBloodPopExplodeAfterDamage(CharacterHealth sourceHealth, float finalDamage, float explosionRadius, Collider2D[] hitTargets)
    {
        OnBloodPopExplodeAfterDamage?.Invoke(sourceHealth, finalDamage, explosionRadius, hitTargets);
    }
}
