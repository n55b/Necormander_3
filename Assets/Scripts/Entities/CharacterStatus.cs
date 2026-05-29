using System.Collections.Generic;
using UnityEngine;

public class CharacterStatus : MonoBehaviour
{
    private class SlowInstance { public string EffectId; public float Reduction; public float EndTime; }
    private class ShieldInstance { public float RemainingAmount; public float EndTime; public ShieldInstance(float amount, float duration){ RemainingAmount = amount; EndTime = Time.time + duration; }}

    private List<SlowInstance> _activeSlows = new List<SlowInstance>();
    private List<ShieldInstance> _shieldInstances = new List<ShieldInstance>();
    private float _cachedMoveSpeedMultiplier = 1f;
    private float _cachedTotalShield = 0f;

    public float MoveSpeedMultiplier => _cachedMoveSpeedMultiplier;
    public float TotalShield => _cachedTotalShield;
    public bool IsElite { get; set; } // [추가] 엘리트 유닛 여부

    private Dictionary<DebuffStackType, float> _debuffStacks = new Dictionary<DebuffStackType, float>();
    private Dictionary<DebuffStackType, float> _stackTimers = new Dictionary<DebuffStackType, float>();
    private Dictionary<DebuffBoolType, float> _boolTimers = new Dictionary<DebuffBoolType, float>();

    private const float STACK_DURATION = 10.0f;
    private float _poisonTimer = 0f;
    private const float POISON_INTERVAL = 3.0f;

    [SerializeField] private Base_DebuffUITerminal debuffTerminal;
    
    private CharacterStat _stat;
    public static List<CharacterStatus> ActiveEnemies = new List<CharacterStatus>();

    // [유니크] 녹슬어 버린 갑옷 (RustedArmor) 타격 횟수 카운터
    public int CorrosionHitCount { get; set; } = 0;

    public void Init(CharacterStat stat)
    {
        _stat = stat;
        if (IsEnemyTarget && !ActiveEnemies.Contains(this))
        {
            ActiveEnemies.Add(this);
        }
    }

    private void OnDestroy()
    {
        if (ActiveEnemies.Contains(this))
        {
            ActiveEnemies.Remove(this);
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
        _cachedMoveSpeedMultiplier = Mathf.Max(0.1f, multiplier);

        float sum = 0;
        for (int i = _shieldInstances.Count - 1; i >= 0; i--)
        {
            if (Time.time > _shieldInstances[i].EndTime || _shieldInstances[i].RemainingAmount <= 0) { _shieldInstances.RemoveAt(i); continue; }
            sum += _shieldInstances[i].RemainingAmount;
        }
        _cachedTotalShield = sum;
    }

    private void UpdateDebuffs()
    {
        float dt = Time.deltaTime;

        // [수정] 노쇠(Senility) 지속 시간 유지 로직: 노화 스택이 남아있으면 계속 5초 갱신
        if (GetDebuffStack(DebuffStackType.Aging) > 0 && GetDebuffBool(DebuffBoolType.Senility))
        {
            _boolTimers[DebuffBoolType.Senility] = 5.0f;
        }

        List<DebuffBoolType> boolKeys = new List<DebuffBoolType>(_boolTimers.Keys);
        foreach (var key in boolKeys)
        {
            if (_boolTimers[key] > 0)
            {
                _boolTimers[key] -= dt;
                if (_boolTimers[key] <= 0)
                {
                    _boolTimers[key] = 0f;
                    debuffTerminal.RemoveIcon(key); 
                    
                    if (key == DebuffBoolType.Senility)
                    {
                        Debug.Log($"<color=#BC8F8F>[Debuff]</color> <b>{gameObject.name}</b>: Senility Expired.");
                    }
                }
            }
        }

        List<DebuffStackType> stackKeys = new List<DebuffStackType>(_stackTimers.Keys);
        foreach (var key in stackKeys)
        {
            if (_stackTimers[key] > 0)
            {
                _stackTimers[key] -= dt;
                if (_stackTimers[key] <= 0) 
                {
                    _stackTimers[key] = 0;
                    _debuffStacks[key] = 0;

                    // UI 갱신
                    debuffTerminal.RemoveIcon(key);
                }
            }
        }

        UpdatePoisonTick(dt);
    }

    private void UpdatePoisonTick(float dt)
    {
        int poisonStack = GetDebuffStack(DebuffStackType.Poison);
        if (poisonStack > 0)
        {
            float interval = GemRuleSystem.GetPoisonInterval(IsEnemyTarget);

            _poisonTimer += dt;
            if (_poisonTimer >= interval)
            {
                _poisonTimer = 0f;
                var health = GetComponentInChildren<CharacterHealth>();
                if (health != null)
                {
                    // [수정] 중독 피해량: 스택의 25%, 최소 1 대미지
                    float damage = Mathf.Max(1f, poisonStack * 0.25f);
                    health.GetDamage(new DamageInfo(damage, DamageType.Fixed, null));

                    // [유니크] 초록색 체액 (GreenFluid): 독 틱 피해 발생 시 30% 확률로 던질 수 있는 포션 스폰
                    var inven = InventoryManager.Instance;
                    if (inven != null && inven.HasUniqueEffect(GemUniqueType.GreenFluid))
                    {
                        if (UnityEngine.Random.value <= 0.3f)
                        {
                            SpawnPoisonPotion(transform.position);
                        }
                    }
                }
            }
        }
        else { _poisonTimer = 0f; }
    }

    private void SpawnPoisonPotion(Vector3 position)
    {
        var registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        GameObject potionObj = null;
        Vector3 spawnPos = position + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(-0.5f, 0.5f), 0f);

        if (registry != null && registry.poisonPotionPrefab != null)
        {
            potionObj = Instantiate(registry.poisonPotionPrefab, spawnPos, Quaternion.identity);
            
            // 만약 프리팹에 해당 컴포넌트가 없다면 부착 (일반적으로 프리팹에 미리 부착하는 것이 좋음)
            if (potionObj.GetComponent<PoisonPotionThrowable>() == null)
            {
                var collider = potionObj.GetComponent<Collider2D>();
                if (collider == null) 
                {
                    var circle = potionObj.AddComponent<CircleCollider2D>();
                    circle.radius = 0.5f;
                    circle.isTrigger = true;
                }
                potionObj.AddComponent<PoisonPotionThrowable>();
            }
        }
        else
        {
            // 런타임에 기본 Sphere를 생성하고 컴포넌트 부착
            potionObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            potionObj.name = "PoisonPotionThrowable";
            
            potionObj.transform.position = spawnPos;
            potionObj.transform.localScale = Vector3.one * 0.4f;
            
            var renderer = potionObj.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = new Color(0.2f, 0.8f, 0.2f); // 초록색
            
            potionObj.layer = LayerMask.NameToLayer("Default");
            
            // SphereCollider는 CreatePrimitive에서 자동 생성됨
            var collider = potionObj.GetComponent<Collider2D>();
            if (collider == null) 
            {
                var circle = potionObj.AddComponent<CircleCollider2D>();
                circle.radius = 0.5f;
                circle.isTrigger = true;
            }

            potionObj.AddComponent<PoisonPotionThrowable>();
        }
    }

    // [추가] 상처 감염(WoundInfection) 유니크 보석 처리를 위한 타이머 앞당기기
    public void AdvancePoisonTimer(float amount)
    {
        if (GetDebuffStack(DebuffStackType.Poison) > 0)
        {
            _poisonTimer += amount;
            // 다음 프레임 UpdatePoisonTick()에서 _poisonTimer >= interval을 만족하면 바로 틱 피해가 들어감
        }
    }

    public void ApplySlow(string id, float reduction, float duration)
    {
        var existing = _activeSlows.Find(s => s.EffectId == id);
        if (existing != null) { existing.Reduction = Mathf.Max(existing.Reduction, reduction); existing.EndTime = Time.time + duration; }
        else { _activeSlows.Add(new SlowInstance { EffectId = id, Reduction = reduction, EndTime = Time.time + duration }); }
    }

    public void AddShield(float amount, float duration) { _shieldInstances.Add(new ShieldInstance(amount, duration)); UpdateInstances(); }

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

    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f)
    {
        Rigidbody2D rb = GetComponentInParent<Rigidbody2D>();
        if (rb != null) StartCoroutine(KnockbackRoutine(rb, dir, force, duration));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Rigidbody2D rb, Vector2 dir, float force, float duration)
    {
        float knockbackSpeed = force * 2.0f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (rb == null) yield break;
            rb.linearVelocity = dir * knockbackSpeed;
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    public void AddDebuffStack(DebuffStackType type, float amount)
    {
        // [특수] 시리고 아린 뼈 (또는 기타 차단 로직)
        if (type == DebuffStackType.Chill && GemRuleSystem.ShouldBlockChill(GetDebuffBool(DebuffBoolType.Frozen), IsEnemyTarget))
        {
            return;
        }

        // [시너지] 중독 추가 스택 등 보정
        if (type == DebuffStackType.Poison)
        {
            amount = GemRuleSystem.ModifyIncomingPoisonStack(amount, IsEnemyTarget);
        }

        if (!_debuffStacks.ContainsKey(type)) _debuffStacks[type] = 0f;
        _debuffStacks[type] += amount;

        // [시너지] 유지시간 연장 보정
        float duration = STACK_DURATION;
        if (type == DebuffStackType.Poison) duration = GemRuleSystem.GetPoisonDuration(IsEnemyTarget);
        _stackTimers[type] = duration;
        
        float maxStack = GetMaxStack(type);
        _debuffStacks[type] = Mathf.Min(_debuffStacks[type], maxStack);

        // UI 업데이트
        debuffTerminal.UpdateUI(type, _debuffStacks[type]);

        // [로그 강화] 디버프 종류별 색상 지정
        string color = "white";
        switch (type)
        {
            case DebuffStackType.Poison: color = "#32CD32"; break; // LimeGreen
            case DebuffStackType.Chill: color = "#00BFFF"; break; // DeepSkyBlue
            case DebuffStackType.Execute: color = "#FF4500"; break; // OrangeRed
            case DebuffStackType.BloodPop: color = "#FF00FF"; break; // Magenta
            case DebuffStackType.Aging: color = "#BC8F8F"; break; // RosyBrown
        }

        Debug.Log($"<color={color}>[Debuff]</color> <b>{gameObject.name}</b>: {type} +{amount:F1} (Current: <b>{_debuffStacks[type]:F1}/{maxStack}</b>)");

        HandleStackTrigger(type);
    }

    private float GetMaxStack(DebuffStackType type)
    {
        switch (type)
        {
            case DebuffStackType.Poison: return 100f; // [수정] 20 -> 100
            case DebuffStackType.Chill: return GemRuleSystem.GetMaxChillStack(IsEnemyTarget);
            case DebuffStackType.Aging: return GemRuleSystem.GetMaxAgingStack(IsEnemyTarget);
            case DebuffStackType.BloodPop: return 1000f; 
            case DebuffStackType.Execute: return 1000f;
            default: return 999f;
        }
    }

    private void HandleStackTrigger(DebuffStackType type)
    {
        switch (type)
        {
            case DebuffStackType.Chill:
                float threshold = GemRuleSystem.GetMaxChillStack(IsEnemyTarget);
                if (_debuffStacks[type] >= threshold)
                {
                    SetDebuffBool(DebuffBoolType.Frozen, 3.0f);
                    
                    // [유니크] 절대영도 (AbsoluteZero)
                    ChillUniqueManager.Instance?.TriggerAbsoluteZero(transform.position);
                    
                    // [시너지] 환급 로직 적용
                    _debuffStacks[type] = GemRuleSystem.GetFreezeRefundStacks(IsEnemyTarget);
                    _stackTimers[type] = STACK_DURATION;

                    // [시너지] 동결 시 체력 비례 고정 피해 로직
                    if (GemRuleSystem.HasFreezeFixedDamage(IsEnemyTarget))
                    {
                        var health = GetComponentInChildren<CharacterHealth>();
                        if (health != null)
                        {
                            float percent = GemRuleSystem.GetChillFreezeDamagePercentage(IsElite);
                            float freezeDmg = Mathf.Max(1f, health.CurHP * percent);
                            health.GetDamage(new DamageInfo(freezeDmg, DamageType.Fixed, null));
                        }
                    }
                }
                break;
            case DebuffStackType.Aging:
                // [노화] 스택 상한 도달 시 노쇠(Senility) 발동
                float agingMax = GemRuleSystem.GetMaxAgingStack(IsEnemyTarget);
                if (_debuffStacks[type] >= agingMax)
                {
                    SetDebuffBool(DebuffBoolType.Senility, 5.0f);
                }

                // [유니크] 노인을 위한 나라는 없다: 즉사 체크
                if (GemRuleSystem.ShouldAgingInstaKill(_debuffStacks[type], IsEnemyTarget))
                {
                    if (!IsElite) 
                    {
                        var health = GetComponentInChildren<CharacterHealth>();
                        if (health != null) health.GetDamage(new DamageInfo(health.CurHP + 999f, DamageType.Fixed, null));
                    }
                }
                break;
        }
    }

    public int GetDebuffStack(DebuffStackType type)
    {
        return _debuffStacks.ContainsKey(type) ? Mathf.FloorToInt(_debuffStacks[type]) : 0;
    }

    public void SetDebuffBool(DebuffBoolType type, float duration)
    {
        if (!_boolTimers.ContainsKey(type)) _boolTimers[type] = 0f;
        _boolTimers[type] = Mathf.Max(_boolTimers[type], duration);

        // [추가] Bool 타입 디버프도 UI에 아이콘 표시 (부식 등)
        debuffTerminal.UpdateUI(type, 0f); 
        
        if (type == DebuffBoolType.Corroded)
        {
            Debug.Log($"<color=#FFD700>[Debuff]</color> <b>{gameObject.name}</b>: Corroded Applied! (Duration: {duration}s)");
        }
    }

    public bool GetDebuffBool(DebuffBoolType type)
    {
        return _boolTimers.ContainsKey(type) && _boolTimers[type] > 0;
    }

    public void ForceUnfreeze()
    {
        if (_boolTimers.ContainsKey(DebuffBoolType.Frozen))
        {
            _boolTimers[DebuffBoolType.Frozen] = 0f;
            debuffTerminal.RemoveIcon(DebuffBoolType.Frozen);
            Debug.Log($"<color=#00FFFF>[Debuff]</color> <b>{gameObject.name}</b>: Frozen Shattered!");
        }
    }

    public void ClearStatus()
    {
        _activeSlows.Clear(); _shieldInstances.Clear(); _debuffStacks.Clear(); _stackTimers.Clear(); _boolTimers.Clear();
        _cachedMoveSpeedMultiplier = 1f; _cachedTotalShield = 0f;

        // UI 갱신
        debuffTerminal.RemoveAll();
    }
}
