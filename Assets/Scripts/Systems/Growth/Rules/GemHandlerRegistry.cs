using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 보석 효과 핸들러의 기본 인터페이스입니다.
/// </summary>
public interface IGemEffectHandler
{
    GemUniqueType HandledType { get; }
    void OnEquipped();
    void OnUnequipped();
}

/// <summary>
/// 장착된 보석 핸들러들을 관리하는 매니저 클래스입니다.
/// InventoryManager나 GemTreeManager에서 보석 장착/해제 시 호출합니다.
/// </summary>
public static class GemHandlerRegistry
{
    // 활성화된 모든 핸들러 인스턴스를 저장하는 리스트 (중복 보석 장착 지원)
    private static List<IGemEffectHandler> _activeHandlers = new List<IGemEffectHandler>();

    // 모든 핸들러의 인스턴스를 미리 생성해두는 팩토리 딕셔너리 (필요할 때만 OnEquipped 호출)
    private static Dictionary<GemUniqueType, Func<IGemEffectHandler>> _handlerFactory = new Dictionary<GemUniqueType, Func<IGemEffectHandler>>();

    public static void RegisterHandlerFactory(GemUniqueType type, Func<IGemEffectHandler> factory)
    {
        if (!_handlerFactory.ContainsKey(type))
        {
            _handlerFactory[type] = factory;
        }
    }

    /// <summary>
    /// 게임 시작 시 한 번 호출하여 모든 활성 보석 핸들러 팩토리를 등록합니다.
    /// </summary>
    public static void InitializeAllHandlers()
    {
        // 공통 시너지 증폭 핸들러 초기화 (항상 켜져 있음)
        SynergyDamageAmplifier.Initialize();
        
        // 스태미너 및 강속구 시너지
        StaminaSynergyHandler.Initialize();
        FastballSynergyHandler.Initialize();

        // 스태미너 보석
        RegisterHandlerFactory(GemUniqueType.CatchBreath, () => new CatchBreathHandler());
        RegisterHandlerFactory(GemUniqueType.HarvestOfDeath, () => new EmptyGemHandler(GemUniqueType.HarvestOfDeath));
        RegisterHandlerFactory(GemUniqueType.BasicFitness, () => new BasicFitnessHandler());
        RegisterHandlerFactory(GemUniqueType.EndlessVitality, () => new EndlessVitalityHandler());
        RegisterHandlerFactory(GemUniqueType.OverflowingThrow, () => new OverflowingThrowHandler());
        RegisterHandlerFactory(GemUniqueType.OrderedBreath, () => new OrderedBreathHandler());
        RegisterHandlerFactory(GemUniqueType.ThrowOverload, () => new ThrowOverloadHandler());
        RegisterHandlerFactory(GemUniqueType.MasterOfRapidFire, () => new MasterOfRapidFireHandler());
        RegisterHandlerFactory(GemUniqueType.LimitBreak, () => new LimitBreakHandler());
        RegisterHandlerFactory(GemUniqueType.EfficientThrow, () => new EfficientThrowHandler());

        // 강속구 보석
        RegisterHandlerFactory(GemUniqueType.SetPosition, () => new SetPositionHandler());
        RegisterHandlerFactory(GemUniqueType.Windup, () => new WindupHandler());
        RegisterHandlerFactory(GemUniqueType.MagicPitchFireball, () => new MagicPitchFireballHandler());
        RegisterHandlerFactory(GemUniqueType.MagicPitchArirangBall, () => new MagicPitchArirangBallHandler());
        RegisterHandlerFactory(GemUniqueType.Closer, () => new CloserHandler());
        RegisterHandlerFactory(GemUniqueType.ExperiencedPitcher, () => new ExperiencedPitcherHandler());

        // 투포환 보석
        RegisterHandlerFactory(GemUniqueType.SiegeMode, () => new EmptyGemHandler(GemUniqueType.SiegeMode));
        RegisterHandlerFactory(GemUniqueType.JustThrowIt, () => new EmptyGemHandler(GemUniqueType.JustThrowIt));
        RegisterHandlerFactory(GemUniqueType.Ballistics, () => new EmptyGemHandler(GemUniqueType.Ballistics));
        RegisterHandlerFactory(GemUniqueType.Monocle, () => new EmptyGemHandler(GemUniqueType.Monocle));

        // 큰손 보석
        RegisterHandlerFactory(GemUniqueType.DemonHandPower, () => new EmptyGemHandler(GemUniqueType.DemonHandPower));
        RegisterHandlerFactory(GemUniqueType.HumanWaveTactics, () => new EmptyGemHandler(GemUniqueType.HumanWaveTactics));
        RegisterHandlerFactory(GemUniqueType.TwinFusion, () => new EmptyGemHandler(GemUniqueType.TwinFusion));
        RegisterHandlerFactory(GemUniqueType.MobMentality, () => new EmptyGemHandler(GemUniqueType.MobMentality));
        RegisterHandlerFactory(GemUniqueType.SwiftRelocation, () => new EmptyGemHandler(GemUniqueType.SwiftRelocation));
        RegisterHandlerFactory(GemUniqueType.Afterimage, () => new EmptyGemHandler(GemUniqueType.Afterimage));
        RegisterHandlerFactory(GemUniqueType.AllMine, () => new EmptyGemHandler(GemUniqueType.AllMine));
        RegisterHandlerFactory(GemUniqueType.Golemizing, () => new EmptyGemHandler(GemUniqueType.Golemizing));
    }

    /// <summary>
    /// 현재 보석 트리에 장착된 보석 목록을 기반으로 핸들러들을 재설정합니다.
    /// InventoryManager.OnGemTreeUpdated 등에서 호출해야 합니다.
    /// </summary>
    public static void RefreshActiveHandlers(List<GemUniqueType> equippedGems)
    {
        // 1. 기존에 켜져 있던 모든 핸들러 일괄 해제
        foreach (var handler in _activeHandlers)
        {
            handler.OnUnequipped();
        }
        _activeHandlers.Clear();

        // 2. 새롭게 장착된 보석 핸들러 모두 켜기 (중복 포함)
        foreach (var gemType in equippedGems)
        {
            if (_handlerFactory.TryGetValue(gemType, out var factory))
            {
                IGemEffectHandler handler = factory.Invoke();
                handler.OnEquipped();
                _activeHandlers.Add(handler);
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[GemHandlerRegistry]</color> {gemType} 타입의 핸들러가 등록되지 않았습니다.");
            }
        }
    }

    /// <summary>
    /// 수동으로 모든 핸들러를 강제 해제합니다. (씬 종료 시 등)
    /// </summary>
    public static void ClearAll()
    {
        foreach (var handler in _activeHandlers)
        {
            handler.OnUnequipped();
        }
        _activeHandlers.Clear();
    }
}
