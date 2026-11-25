using System.Collections.Generic;
using UnityEngine;

// One clock drives independently anchored HUD groups. Distances and amplitudes use the native game frame.
[DefaultExecutionOrder(500)]
public sealed class CombatHudMotion : MonoBehaviour
{
    private static readonly List<HudMotionElement> elements = new();
    private PlayerController player;
    private static CombatHudMotion bound;
    private float quakeTime = -10, quakeStrength;
    internal static void GroundImpact(float strength)
    {
        if (bound == null || !bound.isActiveAndEnabled) return;
        bound.quakeTime = Time.unscaledTime;
        bound.quakeStrength = Mathf.Clamp(strength, 0, 4);
    }

    private float swingTime = -10, hurtTime = -10, hurtStrength;
    private Vector2 swing, hurt;

    internal static void Register(HudMotionElement item) => elements.Add(item);
    internal static void Unregister(HudMotionElement item) => elements.Remove(item);
    internal void Bind(PlayerController owner)
    {
        Unbind();
        player = owner;
        if (owner == null) return;
        bound = this;
        DamageEventBus.OnAttackHitResolved += Strike;
        DamageEventBus.OnDamageReceived += Hit;
    }

    private void Strike(CharacterHealth target, CharacterStat attacker, DamageInfo hit, bool critical)
    {
        if (player == null || attacker != player.Stat || target == null || hit.isRedirected ||
            !DamageRules.IsPlayerSourced(hit.category) || hit.category == DamageCategory.Debuff) return;
        swingTime = Time.unscaledTime;
        swing = ((Vector2)(player.transform.position - target.transform.position)).normalized *
                (hit.feedbackComboFinish ? 1.4f : critical ? 1f : .55f);
    }

    private void Hit(CharacterHealth health, DamageInfo info)
    {
        if (player == null || player.Stat == null || health != player.Stat.Health || info.amount <= 0) return;
        Vector2 point = player.transform.position;
        var from = info.hitFrom ?? (info.attacker != null ? (Vector2)info.attacker.transform.position : point - Vector2.up);
        hurt = (point - from).normalized;
        hurtTime = Time.unscaledTime;
        bool heavy = info.category == DamageCategory.EnemyBoss || info.category == DamageCategory.EnemyElite ||
            info.amount >= health.MaxHP * .17f;
        hurtStrength = heavy ? 3.8f : 2.2f;
    }

    internal static float Ring(float age)
    {
        const float duration = .40f;
        if (age <= 0 || age >= duration) return 0;
        float settle = 1f - Mathf.SmoothStep(0, 1, age / duration);
        return Mathf.Sin(21f * age) * Mathf.Exp(-4f * age) * settle;
    }

    private void LateUpdate()
    {
        if (bound != this) return;
        float strength = UISkinSettings.MotionOn && player != null && !PlayerController.IsGameplayPaused &&
            PolishFeedback.ExistingEnabled(ExistingFeedbackCue.UIShake)
            ? CameraSettingsSO.Instance?.hudMotionStrength ?? 1f : 0f;
        float now = Time.unscaledTime;
        var skin = UISkinSettings.Active;
        float idleAngle = now * (Mathf.PI * 2 / Mathf.Max(2, skin != null ? skin.idlePeriodSeconds : 3.4f));
        float idleAmplitude = skin != null ? skin.idleStrength : .85f;
        foreach (var element in elements)
        {
            if (element.Behavior == HudMotionElement.Motion.Pinned || strength <= 0) { element.Rest(); continue; }
            float phase = idleAngle + element.Phase;
            if (element.Behavior == HudMotionElement.Motion.Idle)
            {
                float amplitude = Mathf.Min(.8f, .70f * idleAmplitude * strength);
                element.Offset(Vector2.up * (amplitude * Mathf.Sin(phase)));
                continue;
            }
            Vector2 pose = new Vector2(.20f * Mathf.Sin(phase), .68f * Mathf.Cos(phase)) * idleAmplitude;
            float hurtAge = now - hurtTime, quakeAge = now - quakeTime;
            if (hurtAge < .57f || quakeAge < .57f)
            {
                Vector2 radial = element.FrameCenter - new Vector2(PixelViewport.Width, PixelViewport.Height) * .5f;
                float delay = Mathf.Min(.17f, radial.magnitude / 2100f);
                pose += Vector2.Lerp(radial.normalized, hurt, .27f) * (hurtStrength * Ring(hurtAge - delay));
                pose += radial.normalized * (quakeStrength * Ring(quakeAge - delay));
            }
            pose += swing * Ring(now - swingTime);
            element.Offset(pose * (strength * element.Response));
        }
    }
    private void Unbind()
    {
        if (bound == this) bound = null;
        DamageEventBus.OnAttackHitResolved -= Strike;
        DamageEventBus.OnDamageReceived -= Hit;
    }
    private void OnDisable()
    {
        if (bound == this) foreach (var element in elements) element.Rest();
        hurtTime = swingTime = quakeTime = -10;
    }
    private void OnDestroy() => Unbind();
}