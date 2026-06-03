using UnityEngine;

// ---------------------------------------------------------
// [?쒕꼫吏 ?몃뱾?? 媛뺤냽援?// ---------------------------------------------------------
public class FastballSynergyHandler
{
    private float _lastEfficiency = 0f;
    private static FastballSynergyHandler _instance;

    public static void Initialize()
    {
        if (_instance == null)
        {
            _instance = new FastballSynergyHandler();
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.OnGemTreeUpdated += _instance.UpdateSynergy;
            }
        }
    }

    private void UpdateSynergy()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (InventoryManager.Instance == null) return;
        
        int count = InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Fastball);
        int level = GemSynergyLogic.GetLevel(count);

        pc.chargeEfficiencyMultiplier -= _lastEfficiency;

        if (level >= 2) _lastEfficiency = 0.75f;
        else if (level >= 1) _lastEfficiency = 0.50f;
        else _lastEfficiency = 0f;

        pc.chargeEfficiencyMultiplier += _lastEfficiency;
    }
}

// ---------------------------------------------------------
// 210. SetPosition (???ъ???
// 李⑥쭠 ?쒓컙 -0.1珥? 吏곴뎄 ?λ젰 ?⑥쑉 -2%
// ---------------------------------------------------------
public class SetPositionHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.SetPosition;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.bonusThrowChargeTime -= 0.1f;
            pc.chargeEfficiencyMultiplier -= 0.02f;
        }
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.bonusThrowChargeTime += 0.1f;
            pc.chargeEfficiencyMultiplier += 0.02f;
        }
    }
}

// ---------------------------------------------------------
// 211. Windup (??몃뱶??
// 李⑥쭠 ?쒓컙 +0.5珥? 吏곴뎄 ?λ젰 ?⑥쑉 +2%
// ---------------------------------------------------------
public class WindupHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.Windup;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.bonusThrowChargeTime += 0.5f;
            pc.chargeEfficiencyMultiplier += 0.02f;
        }
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.bonusThrowChargeTime -= 0.5f;
            pc.chargeEfficiencyMultiplier -= 0.02f;
        }
    }
}

// ---------------------------------------------------------
// 212. MagicPitchFireball (留덇뎄: ?뚯씠?대낵)
// ?꾩옱 吏곴뎄瑜??섏??붾뜲 ?꾩슂??李⑥쭠 ?쒓컙 1珥덈떦 吏곴뎄 諛곗쑉 10% 利앷?
// (?숈쟻 怨꾩궛)
// ---------------------------------------------------------
public class MagicPitchFireballHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.MagicPitchFireball;
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
        if (recipe.info.isDirect && _throwCtrl != null)
        {
            float chargeTime = _throwCtrl.ChargeTime;
            float bonus = chargeTime * 0.10f; // 1珥덈떦 10%
            recipe.modifiers.gemPowerMultiplier += bonus;
        }
    }
}

// ---------------------------------------------------------
// 213. MagicPitchArirangBall (留덇뎄: ?꾨━?묐낵)
// 吏곴뎄???꾩슂??李⑥쭠 ?쒓컙 -0.5珥? ?⑥쑉 -40%
// ---------------------------------------------------------
public class MagicPitchArirangBallHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.MagicPitchArirangBall;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.bonusThrowChargeTime -= 0.5f;
            pc.chargeEfficiencyMultiplier -= 0.40f;
        }
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.bonusThrowChargeTime += 0.5f;
            pc.chargeEfficiencyMultiplier += 0.40f;
        }
    }
}

// ---------------------------------------------------------
// 214. Closer (?대줈?)
// 214. Closer (?대줈?€)
// 理쒕? 李⑥쭠 ?곹븳??5珥덈줈 利앷?
// ---------------------------------------------------------
public class CloserHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.Closer;

    private ThrowController _throwCtrl;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.overchargeTimeLimit += 4.0f;
        }

        _throwCtrl = pc.GetComponentInChildren<ThrowController>();
        if (_throwCtrl != null) _throwCtrl.OnRecipeCreated += ModifyRecipe;
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.overchargeTimeLimit -= 4.0f;
        }

        if (_throwCtrl != null) _throwCtrl.OnRecipeCreated -= ModifyRecipe;
    }

    private void ModifyRecipe(ThrowRecipe recipe)
    {
        if (_throwCtrl != null && _throwCtrl.InputHandler.OverchargeRatio > 0f)
        {
            // 오버차지 비율에 따라 선형적으로 최대 +50% 효과 부여
            recipe.modifiers.gemPowerMultiplier += 0.50f * _throwCtrl.InputHandler.OverchargeRatio;
        }
    }
}

// ---------------------------------------------------------
// 215. ExperiencedPitcher (숙련된 투수)
// 직구 차징 중 이동 속도 감소율이 25%가 됩니다. (기본 50%)
// ---------------------------------------------------------
public class ExperiencedPitcherHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.ExperiencedPitcher;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.chargeMoveSpeedMultiplier += 0.25f;
        }
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            pc.chargeMoveSpeedMultiplier -= 0.25f;
        }
    }
}
