using System.Collections.Generic;
using UnityEngine;

public class CharacterStatus : MonoBehaviour
{
    private class SlowInstance { public string EffectId; public float Reduction; public float EndTime; }
    private class SpeedBuffInstance { public string EffectId; public float Increase; public float EndTime; }
    private class ShieldInstance { public float RemainingAmount; public float EndTime; public ShieldInstance(float amount, float duration){ RemainingAmount = amount; EndTime = Time.time + duration; }}

    private List<SlowInstance> _activeSlows = new List<SlowInstance>();
    private List<SpeedBuffInstance> _activeSpeedBuffs = new List<SpeedBuffInstance>();
    private List<ShieldInstance> _shieldInstances = new List<ShieldInstance>();
    private float _cachedMoveSpeedMultiplier = 1f;
    private float _cachedTotalShield = 0f;

    public bool LastAttackMissed = false; // [추가] 궁수 발이부중 기믹용 (이전 기본 공격이 빗나갔는지 여부)
    public float MoveSpeedMultiplier => _cachedMoveSpeedMultiplier;
    public float TotalShield => _cachedTotalShield;
    public bool IsElite { get; set; } // [추가] 엘리트 유닛 여부

    [Header("Super Armor Settings")]
    [SerializeField] private bool _hasSuperArmor = false;
    [SerializeField] private float _superArmorGauge = 0f;
    [SerializeField] private float _maxSuperArmorGauge = 100f;

    public bool HasSuperArmor => _hasSuperArmor;
    public float SuperArmorGauge => _superArmorGauge;
    public float MaxSuperArmorGauge => _maxSuperArmorGauge;

    public void ApplySuperArmor(float amount = 100f)
    {
        _hasSuperArmor = true;
        _superArmorGauge = amount;
        _maxSuperArmorGauge = amount;
    }

    /// <summary>
    /// 슈퍼아머 게이지를 깎는다. 0 이 되면 그대로 부서지고 끝 — 추가 보상은 없다.
    /// 재생도 없다. 한 번 부서지면 이 유닛이 죽을 때까지 돌아오지 않는다(설계 확정).
    /// </summary>
    public void DamageSuperArmor(float amount)
    {
        if (!_hasSuperArmor) return;
        _superArmorGauge = Mathf.Max(0f, _superArmorGauge - amount);
        if (_superArmorGauge <= 0f)
        {
            _hasSuperArmor = false;
            Debug.Log($"<color=red>[Super Armor]</color> {gameObject.name}의 슈퍼아머 파괴!");
        }
    }

    private Dictionary<DebuffBoolType, float> _boolTimers = new Dictionary<DebuffBoolType, float>();
    private Dictionary<DebuffBoolType, int> _boolTiers = new Dictionary<DebuffBoolType, int>();

    /// <summary>
    /// 상태이상이 터졌을 때 띄울 한글 라벨. FloatingTextSpawner 가 듣는다.
    ///
    /// [주의] 지금은 쏘는 쪽이 없어서 CS0067 경고가 뜬다 — 구 취약 소모("기절!"/"격파!"/"강타!")가
    /// 유일한 발신처였는데 같이 지워졌다. Phase 5 에서 신규 상태이상이 다시 쏘면 경고가 사라진다.
    /// 듣는 쪽(FloatingTextSpawner)은 그대로 살아 있으니 연결만 하면 된다.
    /// </summary>
    public event System.Action<string> OnDebuffPopped;

    [SerializeField] private Base_DebuffUITerminal debuffTerminal;

    private CharacterStat _stat;
    public static List<CharacterStatus> ActiveEnemies = new List<CharacterStatus>();
    public static List<CharacterStatus> ActiveAllies = new List<CharacterStatus>();

    public void Init(CharacterStat stat)
    {
        _stat = stat;
        if (IsEnemyTarget)
        {
            if (!ActiveEnemies.Contains(this)) ActiveEnemies.Add(this);
        }
        else
        {
            if (!ActiveAllies.Contains(this)) ActiveAllies.Add(this);
        }
    }

    private void OnDestroy()
    {
        if (ActiveEnemies.Contains(this))
        {
            ActiveEnemies.Remove(this);
        }
        if (ActiveAllies.Contains(this))
        {
            ActiveAllies.Remove(this);
        }
    }

    private bool IsEnemyTarget => _stat != null && _stat.IsEnemy;

    private void Update()
    {
        UpdateInstances();
        UpdateDebuffs();
    }

    private void UpdateInstances()
    {
        float multiplier = 1.0f;
        for (int i = _activeSlows.Count - 1; i >= 0; i--)
        {
            if (Time.time > _activeSlows[i].EndTime) { _activeSlows.RemoveAt(i); continue; }
            multiplier *= (1.0f - _activeSlows[i].Reduction);
        }
        for (int i = _activeSpeedBuffs.Count - 1; i >= 0; i--)
        {
            if (Time.time > _activeSpeedBuffs[i].EndTime) { _activeSpeedBuffs.RemoveAt(i); continue; }
            multiplier *= (1.0f + _activeSpeedBuffs[i].Increase);
        }

        _cachedMoveSpeedMultiplier = Mathf.Max(0.1f, multiplier);

        float sum = 0;
        for (int i = _shieldInstances.Count - 1; i >= 0; i--)
        {
            if (Time.time > _shieldInstances[i].EndTime || _shieldInstances[i].RemainingAmount <= 0) 
            { 
                if (Time.time > _shieldInstances[i].EndTime && _shieldInstances[i].RemainingAmount > 0)
                {
                    ExplodeExpiredShield(_shieldInstances[i].RemainingAmount);
                }
                _shieldInstances.RemoveAt(i); 
                continue; 
            }
            sum += _shieldInstances[i].RemainingAmount;
        }
        _cachedTotalShield = sum;
    }

    private void UpdateDebuffs()
    {
        float dt = Time.deltaTime;

        List<DebuffBoolType> boolKeys = new List<DebuffBoolType>(_boolTimers.Keys);
        foreach (var key in boolKeys)
        {
            if (_boolTimers[key] > 0)
            {
                _boolTimers[key] -= dt;
                if (_boolTimers[key] <= 0)
                {
                    _boolTimers[key] = 0f;
                    _boolTiers[key] = 0;
                    debuffTerminal?.RemoveIcon(key);
                }
            }
        }
    }

    public void ApplySlow(string id, float reduction, float duration)
    {
        var existing = _activeSlows.Find(s => s.EffectId == id);
        if (existing != null) { existing.Reduction = Mathf.Max(existing.Reduction, reduction); existing.EndTime = Time.time + duration; }
        else { _activeSlows.Add(new SlowInstance { EffectId = id, Reduction = reduction, EndTime = Time.time + duration }); }
    }

    public void ApplySpeedBuff(string id, float increase, float duration)
    {
        var existing = _activeSpeedBuffs.Find(s => s.EffectId == id);
        if (existing != null) { existing.Increase = Mathf.Max(existing.Increase, increase); existing.EndTime = Time.time + duration; }
        else { _activeSpeedBuffs.Add(new SpeedBuffInstance { EffectId = id, Increase = increase, EndTime = Time.time + duration }); }
    }

    public void AddShield(float amount, float duration) { _shieldInstances.Add(new ShieldInstance(amount, duration)); UpdateInstances(); }

    private void ExplodeExpiredShield(float amount)
    {
        if (IsEnemyTarget || InventoryManager.Instance == null) return;
        
        // [이벤트 버스] 쉴드 폭발 시 추가 이펙트 처리 (추후 연동 시 여기에 이벤트 추가)
    }

    public float ConsumeShield(float amount)
    {
        float remainingToConsume = amount;
        for (int i = 0; i < _shieldInstances.Count; i++)
        {
            float canTake = Mathf.Min(remainingToConsume, _shieldInstances[i].RemainingAmount);
            _shieldInstances[i].RemainingAmount -= canTake;
            remainingToConsume -= canTake;
            if (remainingToConsume <= 0) break;
        }
        UpdateInstances(); 
        return amount - remainingToConsume;
    }

    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f, bool isIronMountain = false)
    {
        if (HasSuperArmor) return; // [추가] 슈퍼아머 상태일 때는 넉백 불가
        Rigidbody2D rb = GetComponentInParent<Rigidbody2D>();
        if (rb != null) StartCoroutine(KnockbackRoutine(rb, dir, force, duration, isIronMountain));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Rigidbody2D rb, Vector2 dir, float force, float duration, bool isIronMountain)
    {
        bool isPlayer = gameObject.CompareTag("Player");
        bool hasVanguard = false;
        
        // [이벤트 버스] 넉백 전처리: force, duration 변조 기능 추가 가능

        float knockbackSpeed = force * 2.0f;
        float elapsed = 0f;
        int wallMask = Layers.WallMask;

        while (elapsed < duration)
        {
            if (rb == null) yield break;
            rb.linearVelocity = dir * knockbackSpeed;
            elapsed += Time.deltaTime;

            if (isIronMountain && !isPlayer)
            {
                Collider2D wallHit = Physics2D.OverlapCircle(transform.position, 0.4f, wallMask);
                if (wallHit != null)
                {
                    float damagePercent = IsElite ? 0.06f : 0.12f;
                    var health = GetComponentInChildren<CharacterHealth>();
                    if (health != null) health.GetDamage(new DamageInfo(health.CurHP * damagePercent, DamageType.Fixed, null));
                    break;
                }
            }

            if (hasVanguard)
            {
                Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, 1.0f);
                foreach(var hit in hits)
                {
                    int layerVal = hit.gameObject.layer;
                    bool isEnemy = layerVal == Layers.Enemy ||
                                   hit.CompareTag("Boss") ||
                                   (hit.TryGetComponent<BaseEntity>(out var entEnemy) && entEnemy.team == Team.Enemy);
                    if (isEnemy && hit.TryGetComponent<CharacterStat>(out var enemyStat))
                    {
                        var pc = GameManager.Instance.PLAYERCONTROLLER;
                        float damage = 10f;
                        if (pc != null)
                        {
                            var pcStat = pc.GetComponent<CharacterStat>();
                            if (pcStat != null) damage = pcStat.ATK * 1.5f;
                        }
                        enemyStat.Health.GetDamage(new DamageInfo(damage, DamageType.Physical, gameObject));
                    }
                    bool isAlly = layerVal == Layers.Player || 
                                  layerVal == Layers.Army || 
                                  layerVal == Layers.FlyingObject ||
                                  (hit.TryGetComponent<BaseEntity>(out var entAlly) && entAlly.team == Team.Ally);
                    if (isAlly && hit.TryGetComponent<CharacterStatus>(out var allyStat))
                    {
                        // Vanguard 버프 로직 (간단히 이속 버프로 대체 가능)
                        // TODO: 회피율 및 이속 버프
                    }
                }
            }

            yield return null;
        }
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    public void SetDebuffBool(DebuffBoolType type, float duration, int tier = 0)
    {
        if (!_boolTimers.ContainsKey(type)) _boolTimers[type] = 0f;
        _boolTimers[type] = Mathf.Max(_boolTimers[type], duration);
        _boolTiers[type] = tier;

        debuffTerminal?.UpdateUI(type, tier); 
    }

    public bool GetDebuffBool(DebuffBoolType type)
    {
        return _boolTimers.ContainsKey(type) && _boolTimers[type] > 0;
    }

    public int GetDebuffTier(DebuffBoolType type)
    {
        return _boolTiers.ContainsKey(type) ? _boolTiers[type] : 0;
    }



    public void ClearStatus()
    {
        _activeSlows.Clear(); _activeSpeedBuffs.Clear(); _shieldInstances.Clear();
        _boolTimers.Clear(); _boolTiers.Clear();
        _cachedMoveSpeedMultiplier = 1f; _cachedTotalShield = 0f;

        // 슈퍼아머도 여기서 지운다. 예전엔 안 지워서, 오브젝트가 재사용되면 이전 유닛의
        // 슈퍼아머를 그대로 물려받았다.
        _hasSuperArmor = false; _superArmorGauge = 0f;

        // UI 갱신
        debuffTerminal?.RemoveAll();
    }
}
