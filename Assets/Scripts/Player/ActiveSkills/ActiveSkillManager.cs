using UnityEngine;

public class ActiveSkillManager : MonoBehaviour
{
    private PlayerController _player;
    private IActiveSkill _currentSkill;
    public IActiveSkill ActiveSkill => _currentSkill;

    public void Initialize(PlayerController player)
    {
        _player = player;
    }

    public void EquipSkill(IActiveSkill skill)
    {
        if (_currentSkill != null && _currentSkill.IsActive)
        {
            _currentSkill.OnDeactivate();
        }
        
        _currentSkill = skill;
        if (_currentSkill != null)
        {
            _currentSkill.Initialize(_player);
        }
    }

    public void CheckInput()
    {
        if (_currentSkill == null) return;

        // Q 키를 누르면 스킬 토글
        if (UnityEngine.InputSystem.Keyboard.current != null && 
            UnityEngine.InputSystem.Keyboard.current.qKey.wasPressedThisFrame)
        {
            if (_currentSkill.IsActive)
            {
                _currentSkill.OnDeactivate();
            }
            else
            {
                if (!_currentSkill.IsOnCooldown)
                {
                    _currentSkill.OnActivate();
                }
                else
                {
                    Debug.Log($"<color=orange>[Skill]</color> {_currentSkill.SkillName} 쿨타임 중입니다.");
                }
            }
        }
    }

    private void Update()
    {
        if (_currentSkill != null)
        {
            _currentSkill.UpdateSkill();
        }
    }

    public bool HandleLeftClick()
    {
        if (_currentSkill != null && _currentSkill.IsActive)
        {
            return _currentSkill.HandleLeftClick();
        }
        return false;
    }
}
