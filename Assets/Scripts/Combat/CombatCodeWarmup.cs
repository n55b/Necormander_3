#if ENABLE_MONO && !UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using AstroNuts.Monsters;

// Acquire native entry points during dungeon entry. No hits, random rolls or gameplay events run.
internal static class CombatCodeWarmup
{
    private static readonly Type[] Types =
    {
        typeof(BaseHitBox), typeof(CharacterHealth), typeof(DamageRules), typeof(DamageEventBus),
        typeof(CharacterVisualFeedback), typeof(PolishFeedback), typeof(WorldSparkField),
        typeof(CameraManager), typeof(CombatHudMotion), typeof(PixelActorMotion), typeof(WorldAtmosphere),
        typeof(FloatingTextSpawner), typeof(FloatingTextManager), typeof(TextFloating),
        typeof(TMPTextEffectPlayer), typeof(PooledHitEffect), typeof(PlayerCombatSoundController),
        typeof(MonsterDeathHandler), typeof(FinisherFlight), typeof(RewardProcessor),
        typeof(GroundItem), typeof(GroundRewardLabel), typeof(FeedbackSurface), typeof(PopupSystem),
        typeof(ImpactCameraOffset), typeof(FeedbackCatalog), typeof(FeedbackBurst), typeof(PlayerFeedback),
        typeof(WorldDepthOrder), typeof(SoundManager), typeof(PlayerAttackSoundData),
        typeof(VisualSquashStretch), typeof(StatusEffectPalette), typeof(TrapBombBarrel),
        typeof(EquipmentInventoryManager)
    };
    private static bool ready;

    internal static IEnumerator Prepare()
    {
        if (ready) yield break;
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                      BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var slice = Stopwatch.StartNew();
        foreach (var type in Types)
        {
            foreach (var method in type.GetMethods(declared))
            {
                if (method.IsAbstract || method.ContainsGenericParameters ||
                    (method.GetMethodImplementationFlags() & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL ||
                    (method.Attributes & MethodAttributes.PinvokeImpl) != 0) continue;
                method.MethodHandle.GetFunctionPointer();
                if (slice.Elapsed.TotalMilliseconds < 2) continue;
                yield return null;
                slice.Restart();
            }
            // Iterator bodies live on compiler-generated types, separately from the factory methods above.
            if (type == typeof(BaseHitBox) || type == typeof(TrapBombBarrel))
                foreach (var nested in type.GetNestedTypes(BindingFlags.NonPublic))
                    if (typeof(IEnumerator).IsAssignableFrom(nested))
                        nested.GetMethod("MoveNext", declared)?.MethodHandle.GetFunctionPointer();
        }
        ready = true;
    }
}
#endif
