using UnityEngine;

public class ActiveSkillManager : MonoBehaviour
{
    private PlayerController _player;
    
    private IActiveSkill _skillSlot1; // Q
    private IActiveSkill _skillSlot2; // E

    // Q/E 키 홀드 상태 플래그
    private bool _isHoldingSlot1;
    private bool _isHoldingSlot2;

    public IActiveSkill SkillSlot1 => _skillSlot1;
    public IActiveSkill SkillSlot2 => _skillSlot2;

    public void Initialize(PlayerController player)
    {
        _player = player;

        // [임시 테스트용] 스킬 자동 장착
        if (_skillSlot1 == null)
        {
            EquipSkill(new DashChargeSkill(), 1);
        }
        
        if (_skillSlot2 == null)
        {
            EquipSkill(new LeapStrikeSkill(), 2);
        }
    }

    public void EquipSkill(IActiveSkill skill, int slotIndex)
    {
        if (slotIndex == 1)
        {
            if (_skillSlot1 != null && _skillSlot1.IsActive) _skillSlot1.OnDeactivate();
            _skillSlot1 = skill;
            if (_skillSlot1 != null) _skillSlot1.Initialize(_player);
        }
        else if (slotIndex == 2)
        {
            if (_skillSlot2 != null && _skillSlot2.IsActive) _skillSlot2.OnDeactivate();
            _skillSlot2 = skill;
            if (_skillSlot2 != null) _skillSlot2.Initialize(_player);
        }
    }

    // PlayerController에서 InputAction.CallbackContext를 통해 호출
    public void HandleSkill1Input(bool isStarted, bool isCanceled)
    {
        if (_skillSlot1 == null) return;

        if (isStarted)
        {
            _isHoldingSlot1 = true;
            _skillSlot1.OnInputStart();
        }
        else if (isCanceled)
        {
            _isHoldingSlot1 = false;
            _skillSlot1.OnInputRelease();
        }
    }

    public void HandleSkill2Input(bool isStarted, bool isCanceled)
    {
        if (_skillSlot2 == null) return;

        if (isStarted)
        {
            _isHoldingSlot2 = true;
            _skillSlot2.OnInputStart();
        }
        else if (isCanceled)
        {
            _isHoldingSlot2 = false;
            _skillSlot2.OnInputRelease();
        }
    }

    private void Update()
    {
        if (_skillSlot1 != null)
        {
            if (_isHoldingSlot1) _skillSlot1.OnInputHold();
            _skillSlot1.UpdateSkill();
        }

        if (_skillSlot2 != null)
        {
            if (_isHoldingSlot2) _skillSlot2.OnInputHold();
            _skillSlot2.UpdateSkill();
        }
    }

    public bool HandleLeftClick()
    {
        bool handled = false;
        if (_skillSlot1 != null && _skillSlot1.IsActive) handled |= _skillSlot1.HandleLeftClick();
        if (_skillSlot2 != null && _skillSlot2.IsActive) handled |= _skillSlot2.HandleLeftClick();
        return handled;
    }
}
