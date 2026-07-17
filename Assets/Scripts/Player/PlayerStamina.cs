using System;
using UnityEngine;

/// <summary>
/// 플레이어 스태미나를 관리하는 컴포넌트입니다.
///
/// [26/07/17] 투척 철거로 '소비처가 없는 자원'이 됐다. 유일한 소비자가 투척이었다.
/// 클래스를 남긴 건 Q/E 스킬이 나중에 쓸 수 있어서다 — 쓸 땐 ConsumeRawStamina 를 부르면 된다.
/// 지금은 최대치까지 차오른 채로 가만히 있는다(현재 Player Melee 구조에선 UI 에도 안 뜬다).
/// </summary>
public class PlayerStamina : MonoBehaviour
{
    [Header("Base Settings")]
    [SerializeField] private float defaultMaxStamina = 100f;
    [SerializeField] private float defaultRegenRate = 3f; // 초당 회복량

    // 모디파이어(보너스) 값들
    [HideInInspector] public float maxStaminaBonus = 0f;
    [HideInInspector] public float regenRateBonus = 0f;
    [HideInInspector] public float outOfCombatRegenBonus = 0f;
    [HideInInspector] public float deadMinionRegenBonus = 0f;
    
    // 한계돌파 등 특수 상태
    [HideInInspector] public float negativeLimit = 0f; // 기본 0, 한계돌파시 -50
    [HideInInspector] public bool hasStaminaSynergyMax = false; // 6시너지 활성화 여부

    private float _currentStamina;

    // 계산된 최종 스탯 프로퍼티
    public float MaxStamina => defaultMaxStamina + maxStaminaBonus; 
    

    public float RegenRate
    {
        get
        {
            float baseRegen = defaultRegenRate + regenRateBonus + deadMinionRegenBonus;
            
            // 비전투 보너스
            if (GameManager.Instance.PLAYERCONTROLLER.IsOutOfCombat)
                baseRegen += outOfCombatRegenBonus;
            
            // 음수 페널티 (침식 상태)
            float erosionMultiplier = (_currentStamina < 0) ? 0.5f : 1.0f;
            
            // 시너지(6) 등 최종 배율 곱연산
            float synergyMulti = 1.0f;
            if (hasStaminaSynergyMax)
            {
                float ratio = Mathf.Clamp01(_currentStamina / MaxStamina);
                synergyMulti = Mathf.Lerp(2.0f, 1.0f, ratio);
            }

            return baseRegen * erosionMultiplier * synergyMulti;
        }
    }
    
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

    // [추가] 능력이 스태미너를 직접 차감할 때 사용
    public void ConsumeRawStamina(float amount)
    {
        _currentStamina -= amount;
        NotifyStaminaChanged();
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
