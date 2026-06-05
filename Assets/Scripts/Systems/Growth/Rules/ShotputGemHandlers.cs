
// ---------------------------------------------------------
// 더미 핸들러 (다른 매니저에서 효과를 직접 처리하는 보석들용)
// ---------------------------------------------------------
public class EmptyGemHandler : IGemEffectHandler
{
    public GemUniqueType HandledType { get; }
    public EmptyGemHandler(GemUniqueType type) { HandledType = type; }
    public void OnEquipped() { }
    public void OnUnequipped() { }
}

// ---------------------------------------------------------
// 시즈 모드 액티브 스킬 장착 핸들러
// ---------------------------------------------------------
public class SiegeModeHandler : IGemEffectHandler
{
    public GemUniqueType HandledType => GemUniqueType.SiegeMode;
    private SiegeModeSkill _skillInstance;

    public void OnEquipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var activeSkillManager = GameManager.Instance.PLAYERCONTROLLER.GetComponent<ActiveSkillManager>();
        if (activeSkillManager != null)
        {
            if (_skillInstance == null) _skillInstance = new SiegeModeSkill();
            activeSkillManager.EquipSkill(_skillInstance);
        }
    }

    public void OnUnequipped()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        var activeSkillManager = GameManager.Instance.PLAYERCONTROLLER.GetComponent<ActiveSkillManager>();
        if (activeSkillManager != null && activeSkillManager.ActiveSkill == (IActiveSkill)_skillInstance)
        {
            activeSkillManager.EquipSkill(null);
        }
    }
}
