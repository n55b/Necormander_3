using UnityEngine;

// ---------------------------------------------------------
// [?쒕꼫吏 ?몃뱾?? ?ㅽ깭誘몃꼫
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
// 200. CatchBreath (??怨좊Ⅴ湲?
// 鍮꾩쟾???곹깭: ?ㅽ깭誘몃꼫 ?먯뿰 ?뚮났??+1
// ---------------------------------------------------------
public class CatchBreathHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.CatchBreath;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.outOfCombatRegenBonus += 1f;
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.outOfCombatRegenBonus -= 1f;
    }
}

// ---------------------------------------------------------
// 201. HarvestOfDeath (二쎌쓬???섑솗)
// ?뚰솚?섍? 二쎌뿀???? 二쎌? ?섎쭔???먯뿰 ?뚮났??利앷?
// ---------------------------------------------------------
public class HarvestOfDeathHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.HarvestOfDeath;
    private AllyManager _allyManager;
    private PlayerStamina _stamina;
    private int _deadCount = 0;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
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
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
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
// 202. BasicFitness (湲곗큹泥대젰 媛뺥솕)
// ?ㅽ깭誘몃꼫 理쒕?移?利앷? (+20)
// ---------------------------------------------------------
public class BasicFitnessHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.BasicFitness;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.maxStaminaBonus += 20f;
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.maxStaminaBonus -= 20f;
    }
}

// ---------------------------------------------------------
// 203. EndlessVitality (?딆엫?녿뒗 ?쒕젰)
// ?ㅽ깭誘몃꼫 ?먯뿰 ?뚮났??利앷? (+0.5)
// ---------------------------------------------------------
public class EndlessVitalityHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.EndlessVitality;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.regenRateBonus += 0.5f;
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.regenRateBonus -= 0.5f;
    }
}

// ---------------------------------------------------------
// 204. OverflowingThrow (?섏튂???ъ쿃)
// ?뚮え??利앷? (+5), ?ъ쿃 ?④낵 利앷? (+25%)
// ---------------------------------------------------------
public class OverflowingThrowHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.OverflowingThrow;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.throwCostBonus += 5f;
            pc.bonusThrowEffectMultiplier += 0.25f;
        }
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.throwCostBonus -= 5f;
            pc.bonusThrowEffectMultiplier -= 0.25f;
        }
    }
}

// ---------------------------------------------------------
// 205. OrderedBreath (?뺣룉???④껐)
// ?ㅽ깭誘몃꼫 ?뚮え??媛먯냼 (-3)
// ---------------------------------------------------------
public class OrderedBreathHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.OrderedBreath;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.throwCostBonus -= 3f;
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.throwCostBonus += 3f;
    }
}

// ---------------------------------------------------------
// 206. ThrowOverload (?ъ쿃 怨쇰???
// ?ъ쿃????留덈떎 ?뚮え?섎뒗 ?ㅽ깭誘몃꼫 1???ъ쿃 ?④낵 2% 利앷?
// (?숈쟻 怨꾩궛 ?꾩슂 -> OnRecipeCreated ???ъ슜)
// ---------------------------------------------------------
public class ThrowOverloadHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.ThrowOverload;
    private ThrowController _throwCtrl;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        _throwCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponentInChildren<ThrowController>();
        if (_throwCtrl != null) _throwCtrl.OnRecipeCreated += ModifyRecipe;
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        if (_throwCtrl != null) _throwCtrl.OnRecipeCreated -= ModifyRecipe;
    }

    private void ModifyRecipe(ThrowRecipe recipe)
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null)
        {
            var throwCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponentInChildren<ThrowController>();
            int count = throwCtrl != null ? throwCtrl.HeldObjectsCount : 1;
            float bonus = stamina.GetThrowCost(count) * 0.02f;
            recipe.modifiers.gemPowerMultiplier += bonus;
        }
    }
}

// ---------------------------------------------------------
// 207. MasterOfRapidFire (?띿궗???媛)
// ?뚮え??7 媛먯냼, ?ъ쿃 ?④낵 30% 媛먯냼
// ---------------------------------------------------------
public class MasterOfRapidFireHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.MasterOfRapidFire;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.throwCostBonus -= 7f;
            pc.bonusThrowEffectMultiplier -= 0.30f;
        }
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.throwCostBonus += 7f;
            pc.bonusThrowEffectMultiplier += 0.30f;
        }
    }
}

// ---------------------------------------------------------
// 208. LimitBreak (?쒓퀎?뚰뙆)
// ?ㅽ깭誘몃굹媛 ?뚯닔源뚯? ?꾨떖?????덉쓬 (理쒕? -50). ?? ?뚯닔????移⑥떇(?뚮났???덈컲)
// ---------------------------------------------------------
public class LimitBreakHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.LimitBreak;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.negativeLimit = 50f;
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.negativeLimit = 0f;
    }
}

// ---------------------------------------------------------
// 209. EfficientThrow (?⑥쑉?곸씤 ?ъ쿃)
// 理쒕? ?ㅽ깭誘몃꼫 -40, ?ъ쿃 ?④낵 60% 利앷?
// ---------------------------------------------------------
public class EfficientThrowHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.EfficientThrow;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.maxStaminaBonus -= 40f;
            pc.bonusThrowEffectMultiplier += 0.60f;
        }
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.STAMINA != null)
        {
            pc.STAMINA.maxStaminaBonus += 40f;
            pc.bonusThrowEffectMultiplier -= 0.60f;
        }
    }
}
