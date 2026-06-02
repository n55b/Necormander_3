using UnityEngine;

// ---------------------------------------------------------
// [시너지 핸들러] 스태미너
// ---------------------------------------------------------
public class StaminaSynergyHandler
{
    private PlayerStamina _stamina;
    private float _lastRegenBonus = 0f;
    private float _lastMaxBonus = 0f;
    
    private static StaminaSynergyHandler _instance;

    public static void Initialize()
    {
        if (_instance == null)
        {
            _instance = new StaminaSynergyHandler();
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.OnGemTreeUpdated += _instance.UpdateSynergy;
            }
        }
    }

    private void UpdateSynergy()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        if (_stamina == null) _stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (_stamina == null || InventoryManager.Instance == null) return;
        
        int count = InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Stamina);
        int level = GemSynergyLogic.GetLevel(count);

        _stamina.regenRateBonus -= _lastRegenBonus;
        _stamina.maxStaminaBonus -= _lastMaxBonus;

        _lastRegenBonus = (level >= 1) ? 1f : 0f;
        _lastMaxBonus = (level >= 2) ? 20f : 0f;
        _stamina.hasStaminaSynergyMax = (level >= 3);

        _stamina.regenRateBonus += _lastRegenBonus;
        _stamina.maxStaminaBonus += _lastMaxBonus;
    }
}

// ---------------------------------------------------------
// 200. CatchBreath (숨 고르기)
// 비전투 상태: 스태미너 자연 회복량 +1
// ---------------------------------------------------------
public class CatchBreathHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.CatchBreath;

    public void OnEquipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.outOfCombatRegenBonus += 1f;
    }

    public void OnUnequipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.outOfCombatRegenBonus -= 1f;
    }
}

// ---------------------------------------------------------
// 201. HarvestOfDeath (죽음의 수확)
// 소환수가 죽었을 때, 죽은 수만큼 자연 회복량 증가
// ---------------------------------------------------------
public class HarvestOfDeathHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.HarvestOfDeath;
    private AllyManager _allyManager;
    private PlayerStamina _stamina;
    private int _deadCount = 0;

    public void OnEquipped()
    {
        _allyManager = Object.FindFirstObjectByType<AllyManager>();
        _stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        
        if (_allyManager != null)
        {
            _allyManager.OnAllyRespawnStart += HandleAllyDeath;
            _allyManager.OnAllyRespawned += HandleAllyRespawn;
        }
    }

    public void OnUnequipped()
    {
        if (_allyManager != null)
        {
            _allyManager.OnAllyRespawnStart -= HandleAllyDeath;
            _allyManager.OnAllyRespawned -= HandleAllyRespawn;
        }
        if (_stamina != null)
        {
            _stamina.deadMinionRegenBonus -= _deadCount;
        }
        _deadCount = 0;
    }

    private void HandleAllyDeath(AllyManager.MinionInfo info)
    {
        _deadCount++;
        if (_stamina != null) _stamina.deadMinionRegenBonus += 1f;
    }

    private void HandleAllyRespawn(AllyManager.MinionInfo info)
    {
        if (_deadCount > 0)
        {
            _deadCount--;
            if (_stamina != null) _stamina.deadMinionRegenBonus -= 1f;
        }
    }
}

// ---------------------------------------------------------
// 202. BasicFitness (기초체력 강화)
// 스태미너 최대치 증가 (+20)
// ---------------------------------------------------------
public class BasicFitnessHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.BasicFitness;

    public void OnEquipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.maxStaminaBonus += 20f;
    }

    public void OnUnequipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.maxStaminaBonus -= 20f;
    }
}

// ---------------------------------------------------------
// 203. EndlessVitality (끊임없는 활력)
// 스태미너 자연 회복량 증가 (+0.5)
// ---------------------------------------------------------
public class EndlessVitalityHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.EndlessVitality;

    public void OnEquipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.regenRateBonus += 0.5f;
    }

    public void OnUnequipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.regenRateBonus -= 0.5f;
    }
}

// ---------------------------------------------------------
// 204. OverflowingThrow (넘치는 투척)
// 소모량 증가 (+5), 투척 효과 증가 (+25%)
// ---------------------------------------------------------
public class OverflowingThrowHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.OverflowingThrow;

    public void OnEquipped()
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.throwCostBonus += 5f;
            pc.bonusThrowEffectMultiplier += 0.25f;
        }
    }

    public void OnUnequipped()
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.throwCostBonus -= 5f;
            pc.bonusThrowEffectMultiplier -= 0.25f;
        }
    }
}

// ---------------------------------------------------------
// 205. OrderedBreath (정돈된 숨결)
// 스태미너 소모량 감소 (-3)
// ---------------------------------------------------------
public class OrderedBreathHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.OrderedBreath;

    public void OnEquipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.throwCostBonus -= 3f;
    }

    public void OnUnequipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.throwCostBonus += 3f;
    }
}

// ---------------------------------------------------------
// 206. ThrowOverload (투척 과부화)
// 투척할 때 마다 소모되는 스태미너 1당 투척 효과 2% 증가
// (동적 계산 필요 -> OnRecipeCreated 훅 사용)
// ---------------------------------------------------------
public class ThrowOverloadHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.ThrowOverload;
    private ThrowController _throwCtrl;

    public void OnEquipped()
    {
        _throwCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponentInChildren<ThrowController>();
        if (_throwCtrl != null) _throwCtrl.OnRecipeCreated += ModifyRecipe;
    }

    public void OnUnequipped()
    {
        if (_throwCtrl != null) _throwCtrl.OnRecipeCreated -= ModifyRecipe;
    }

    private void ModifyRecipe(ThrowRecipe recipe)
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null)
        {
            float bonus = stamina.ThrowCost * 0.02f;
            recipe.modifiers.gemPowerMultiplier += bonus;
        }
    }
}

// ---------------------------------------------------------
// 207. MasterOfRapidFire (속사의 대가)
// 소모량 7 감소, 투척 효과 30% 감소
// ---------------------------------------------------------
public class MasterOfRapidFireHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.MasterOfRapidFire;

    public void OnEquipped()
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.throwCostBonus -= 7f;
            pc.bonusThrowEffectMultiplier -= 0.30f;
        }
    }

    public void OnUnequipped()
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.throwCostBonus += 7f;
            pc.bonusThrowEffectMultiplier += 0.30f;
        }
    }
}

// ---------------------------------------------------------
// 208. LimitBreak (한계돌파)
// 스태미나가 음수까지 도달할 수 있음 (최대 -50). 단, 음수일 때 침식(회복량 절반)
// ---------------------------------------------------------
public class LimitBreakHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.LimitBreak;

    public void OnEquipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.negativeLimit = 50f;
    }

    public void OnUnequipped()
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.negativeLimit = 0f;
    }
}

// ---------------------------------------------------------
// 209. EfficientThrow (효율적인 투척)
// 최대 스태미너 -40, 투척 효과 60% 증가
// ---------------------------------------------------------
public class EfficientThrowHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.EfficientThrow;

    public void OnEquipped()
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.maxStaminaBonus -= 40f;
            pc.bonusThrowEffectMultiplier += 0.60f;
        }
    }

    public void OnUnequipped()
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.maxStaminaBonus += 40f;
            pc.bonusThrowEffectMultiplier -= 0.60f;
        }
    }
}
