using UnityEngine;

/// <summary>
/// 유닛의 스탯 데이터 보관 및 관련 컴포넌트들을 통합 관리하는 HUB 클래스입니다.
/// </summary>
[RequireComponent(typeof(CharacterStatus), typeof(CharacterHealth), typeof(CharacterVisualFeedback))]
public class CharacterStat : MonoBehaviour
{
    [Header("캐릭터 기본 스탯")]
    [SerializeField] private float baseMaxHP = 100f;
    [SerializeField] private float baseAtk = 10f;
    [SerializeField] private float baseAtkSpd = 1f;
    [SerializeField] private float baseAtkRange = 2f;
    [SerializeField] private float baseDef = 0f;
    [SerializeField] private float baseMoveSpeed = 5f;

    // 분리된 컴포넌트 참조
    private CharacterStatus _status;
    private CharacterHealth _health;
    private CharacterVisualFeedback _visual;
    private bool _isInitialized = false;

    // 외부 참조용 프로퍼티 (기존 코드와 호환 유지)
    public float MAXHP => baseMaxHP;
    public float CURHP => (_health != null) ? _health.CurHP : baseMaxHP;
    public float ATK => baseAtk;
    public float ATKSPD => baseAtkSpd;
    public float ATKRANGE => baseAtkRange;
    public float DEF => baseDef;
    public float MOVESPEED => baseMoveSpeed * (_status != null ? _status.MoveSpeedMultiplier : 1f);
    
    public bool IsDead => _health != null && _health.IsDead;
    public bool Invincible 
    { 
        get => _health != null && _health.Invincible; 
        set { if (_health != null) _health.Invincible = value; } 
    }
    public float SHIELDAMOUNT => _status != null ? _status.TotalShield : 0f;
    
    // [복구] 외부 스크립트용 프로퍼티
    public Color OriginalColor => _visual != null ? _visual.OriginalColor : Color.white;

    // [중앙집중형 초기화] 부모(BaseEntity)에 의해 명시적으로 호출됨
    public void Setup()
    {
        if (_isInitialized) return;

        _status = GetComponent<CharacterStatus>();
        _health = GetComponent<CharacterHealth>();
        _visual = GetComponent<CharacterVisualFeedback>();

        if (_visual != null) _visual.Init(_health, _status);
        if (_health != null) _health.Init(this, _status);

        _isInitialized = true;
    }

    public void InitializeStats(MinionDataSO data)
    {
        Setup(); // 안전장치: 초기화 안 되어 있으면 수행

        if (data != null)
        {
            baseMaxHP = data.maxHP;
            baseAtk = data.attack;
            baseAtkSpd = data.attackSpeed;
            baseAtkRange = data.attackRange;
            baseDef = data.defense;
            baseMoveSpeed = data.moveSpeed;
        }
        
        if (_health != null) _health.ResetHP();
        if (_status != null) _status.ClearStatus();
        if (_visual != null) _visual.ResetVisuals();
    }

    // --- 통로(Facade) 메서드들 ---
    public void GetDamage(DamageInfo info) => _health.GetDamage(info);
    public void Heal(float amount) => _health.Heal(amount);
    public void AddShield(float amount, float duration) => _status.AddShield(amount, duration);
    public void ApplySlow(string id, float reduction, float duration) => _status.ApplySlow(id, reduction, duration);
    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f) => _status.ApplyKnockback(dir, force, duration);
    public void BreakShield(float amount) => _status.ConsumeShield(amount);
    public void SetShieldVFX(GameObject vfx) => _visual.SetShieldVFX(vfx);
    public void SetCCVFX(GameObject vfx) => _visual.SetCCVFX(vfx);
    public void ResetVisualFeedback() => _visual.ResetVisuals();

    public void ApplySplitStats()
    {
        baseMaxHP *= 0.5f;
        baseAtk *= 0.5f;
        if (_health != null) _health.ResetHP();
    }
}
