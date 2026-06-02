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
    private static Dictionary<GemUniqueType, IGemEffectHandler> _activeHandlers = new Dictionary<GemUniqueType, IGemEffectHandler>();

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
    }

    /// <summary>
    /// 현재 보석 트리에 장착된 보석 목록을 기반으로 핸들러들을 재설정합니다.
    /// InventoryManager.OnGemTreeUpdated 등에서 호출해야 합니다.
    /// </summary>
    public static void RefreshActiveHandlers(List<GemUniqueType> equippedGems)
    {
        // 1. 기존에 켜져 있던 핸들러 중, 이번에 장착 해제된 핸들러 끄기
        List<GemUniqueType> toRemove = new List<GemUniqueType>();
        foreach (var kvp in _activeHandlers)
        {
            if (!equippedGems.Contains(kvp.Key))
            {
                kvp.Value.OnUnequipped();
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var key in toRemove)
        {
            _activeHandlers.Remove(key);
        }

        // 2. 새롭게 장착된 보석 핸들러 켜기
        foreach (var gemType in equippedGems)
        {
            if (!_activeHandlers.ContainsKey(gemType))
            {
                if (_handlerFactory.TryGetValue(gemType, out var factory))
                {
                    IGemEffectHandler handler = factory.Invoke();
                    handler.OnEquipped();
                    _activeHandlers.Add(gemType, handler);
                }
                else
                {
                    // 아직 구현되지 않은 핸들러거나 폐기(Deprecated)된 보석입니다.
                    // Debug.LogWarning($"[GemHandlerRegistry] {gemType}의 핸들러가 등록되지 않았습니다.");
                }
            }
        }
    }

    /// <summary>
    /// 수동으로 모든 핸들러를 강제 해제합니다. (씬 종료 시 등)
    /// </summary>
    public static void ClearAll()
    {
        foreach (var handler in _activeHandlers.Values)
        {
            handler.OnUnequipped();
        }
        _activeHandlers.Clear();
    }
}
