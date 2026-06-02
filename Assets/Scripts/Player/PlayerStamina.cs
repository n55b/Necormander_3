using System;
using UnityEngine;

/// <summary>
/// 플레이어 스태미나를 관리하는 컴포넌트입니다.
/// </summary>
public class PlayerStamina : MonoBehaviour
{
    [Header("Stamina Settings")]
    [SerializeField] private float defaultMaxStamina = 100f;
    [SerializeField] private float defaultThrowCost = 15f;
    [SerializeField] private float defaultRegenRate = 3f; // 초당 회복량

    private float _currentStamina;

    // 프로퍼티 (추후 시너지 효과 등에 의해 변경될 수 있도록 Getter 형태로 열어둠)
    public float MaxStamina => defaultMaxStamina; 
    public float ThrowCost => defaultThrowCost;
    public float RegenRate => defaultRegenRate;
    
    public float CurrentStamina => _currentStamina;

    public event Action<float, float> OnStaminaChanged; // (current, max)
    public event Action OnStaminaInsufficient; // 스태미나가 부족할 때 호출 (UI 피드백용)

    private void Start()
    {
        _currentStamina = MaxStamina;
        NotifyStaminaChanged();
    }

    private void Update()
    {
        if (_currentStamina < MaxStamina)
        {
            _currentStamina += RegenRate * Time.deltaTime;
            if (_currentStamina > MaxStamina)
            {
                _currentStamina = MaxStamina;
            }
            NotifyStaminaChanged();
        }
    }

    public bool CanThrow()
    {
        return _currentStamina >= ThrowCost;
    }

    public void ConsumeStamina()
    {
        if (_currentStamina >= ThrowCost)
        {
            _currentStamina -= ThrowCost;
            NotifyStaminaChanged();
        }
    }

    public void TriggerInsufficientFeedback()
    {
        OnStaminaInsufficient?.Invoke();
    }

    private void NotifyStaminaChanged()
    {
        OnStaminaChanged?.Invoke(_currentStamina, MaxStamina);
    }
}
