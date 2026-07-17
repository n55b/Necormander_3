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

    // ── 상태이상 컨테이너 ─────────────────────────────────────────────
    // 5종이 독립적으로 동시 존재한다. 구 구조는 슬롯이 하나(_currentDebuffType)라 다른 걸
    // 걸면 기존 게 터지고 새 건 증발했는데, 그 규칙이 통째로 사라졌다.
    private class StatusInstance
    {
        public float EndTime;       // 만료 시각. 재적중하면 갱신된다.
        public int Stacks;          // 비폭만 쓴다.
        public float NextTickTime;  // 중독 전용. '절대 격자' — 아래 UpdateStatuses 주석 참조.
    }
    private readonly Dictionary<StatusType, StatusInstance> _statuses = new Dictionary<StatusType, StatusInstance>();

    /// <summary>상태이상이 터졌을 때 띄울 한글 라벨. FloatingTextSpawner 가 듣는다.</summary>
    public event System.Action<string> OnDebuffPopped;

    [Header("비폭 폭발")]
    [Tooltip("비폭이 터질 때 쓸 원형 히트박스. 비우면 폭발이 피해를 못 준다. " +
             "(Center Skill Hitbox Circle Prefab — 콜라이더 반지름 0.5 전제)")]
    [SerializeField] private BaseHitBox bloodPopExplosionPrefab;

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
        UpdateStatuses();
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

    private static readonly List<StatusType> _statusScratch = new List<StatusType>();

    private void UpdateStatuses()
    {
        if (_statuses.Count == 0) return;

        _statusScratch.Clear();
        _statusScratch.AddRange(_statuses.Keys); // 순회 중 제거하므로 키를 복사해서 돈다

        foreach (var type in _statusScratch)
        {
            if (!_statuses.TryGetValue(type, out var inst)) continue;

            // 중독 틱. '절대 격자'라 재적중해도 다음 틱이 밀리지 않는다.
            // 예약 시각을 '지금 + 주기'로 다시 잡으면, 다단히트가 1초에 5번 갱신할 때
            // 틱이 계속 뒤로 밀려서 중독 피해가 하나도 안 들어간다. (BaseHitBox 에서 똑같은
            // 드리프트 버그를 이미 한 번 잡았다 — 같은 함정이다.)
            if (type == StatusType.Poison && Time.time >= inst.NextTickTime)
            {
                inst.NextTickTime += StatusRules.POISON_TICK_INTERVAL;
                DealSelfDamage(StatusRules.POISON_TICK_DAMAGE, DamageType.Poison, "중독");
            }

            if (Time.time >= inst.EndTime)
            {
                RemoveStatus(type);
            }
        }
    }

    /// <summary>자기 자신에게 상태이상 피해를 넣는다. 공격자는 없다(도트라 귀속이 애매함).</summary>
    private void DealSelfDamage(float amount, DamageType type, string popup)
    {
        var health = GetComponent<CharacterHealth>() ?? GetComponentInChildren<CharacterHealth>();
        if (health != null && !health.IsDead)
            health.GetDamage(new DamageInfo(amount, type, null, false, 1f, false, popup));
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

    // ── 상태이상 API ─────────────────────────────────────────────────

    /// <summary>
    /// 상태이상을 건다. 이미 걸려 있으면 지속시간이 갱신되고, 스택형(비폭)이면 스택이 쌓인다.
    /// 슈퍼아머가 있으면 이동 방해 계열(기절/빙결)은 씹힌다 — 저장되지 않으므로 나중에 다시 걸어야 한다.
    /// </summary>
    /// <param name="duration">0 이하면 StatusRules.DURATION(5초)을 쓴다. 경직처럼 짧은 건 직접 넘긴다.</param>
    public void ApplyStatus(StatusType type, float duration = 0f, int stacks = 1)
    {
        if (_stat != null && _stat.IsDead) return;
        if (_hasSuperArmor && StatusRules.BlockedBySuperArmor(type)) return;

        if (duration <= 0f) duration = StatusRules.DURATION;

        if (!_statuses.TryGetValue(type, out var inst))
        {
            inst = new StatusInstance();
            _statuses[type] = inst;
            // 중독의 첫 틱은 '건 시점 + 주기'다. 여기서만 잡고, 갱신 때는 절대 안 건드린다.
            inst.NextTickTime = Time.time + StatusRules.POISON_TICK_INTERVAL;
        }

        inst.EndTime = Time.time + duration; // 재적중 = 지속시간만 갱신

        if (StatusRules.IsStacking(type))
        {
            inst.Stacks += stacks;
            if (inst.Stacks >= StatusRules.BLOODPOP_THRESHOLD)
            {
                DetonateBloodPop();
                return;
            }
        }

        debuffTerminal?.UpdateUI(type, inst.Stacks);
    }

    public bool HasStatus(StatusType type) => _statuses.ContainsKey(type);

    public int GetStacks(StatusType type)
        => _statuses.TryGetValue(type, out var inst) ? inst.Stacks : 0;

    public void RemoveStatus(StatusType type)
    {
        if (!_statuses.Remove(type)) return;
        debuffTerminal?.RemoveIcon(type);
    }

    /// <summary>행동이 막혀 있는가. 기절/빙결/경직 중 하나라도 걸려 있으면 true.</summary>
    public bool IsActionBlocked
    {
        get
        {
            foreach (var kv in _statuses)
                if (StatusRules.PreventsAction(kv.Key)) return true;
            return false;
        }
    }

    // ── 피격 훅 (CharacterHealth 가 부른다) ───────────────────────────

    /// <summary>
    /// 직접 피해를 받았을 때 상태이상이 반응하는 지점. 빙결 해제와 출혈 추가 피해가 여기서 나간다.
    ///
    /// [철칙] 상태이상 피해(Fixed 계열)는 여기 못 들어온다 — DamageRules.TriggersBleed 가 막는다.
    /// 안 그러면 출혈의 +2 가 스스로를 트리거해서 무한 재귀가 난다.
    /// 같은 이유로 중독 틱이 빙결을 깨지도 않는다(얼려놓자마자 도트가 깨버리면 이상하다).
    /// </summary>
    public void OnDirectDamageTaken()
    {
        // 빙결: 맞으면 고정 피해를 터뜨리고 즉시 풀린다.
        if (HasStatus(StatusType.Freeze))
        {
            RemoveStatus(StatusType.Freeze);
            OnDebuffPopped?.Invoke("빙결 파괴!");
            DealSelfDamage(StatusRules.FREEZE_BREAK_DAMAGE, DamageType.Freeze, "빙결");
        }

        // 출혈: 맞을 때마다 추가 고정 피해. 한 방에 여러 피해가 겹쳐도 1회다.
        if (HasStatus(StatusType.Bleed))
        {
            DealSelfDamage(StatusRules.BLEED_HIT_DAMAGE, DamageType.Bleed, "출혈");
        }
    }

    /// <summary>
    /// 비폭 폭발. 자신과 주변에 고정 피해를 주고 스택을 0 으로 되돌린다.
    /// [철칙] 폭발 피해는 DamageType.BloodPop 이라 다른 적의 비폭 스택을 쌓지 않는다 —
    /// 안 그러면 연쇄 폭발이 무한히 번진다.
    /// </summary>
    private void DetonateBloodPop()
    {
        RemoveStatus(StatusType.BloodPop);
        OnDebuffPopped?.Invoke("비폭!");

        if (bloodPopExplosionPrefab == null)
        {
            Debug.LogWarning($"[비폭] {gameObject.name}: bloodPopExplosionPrefab 이 비어 있어 폭발이 피해를 못 줍니다.");
            return;
        }

        // 함정(TrapBombBarrel)과 같은 방식: 히트박스를 미리 띄우고 startDelay 동안 빨간 원이
        // 차오르게 해서 예고한다. 프리팹 콜라이더 반지름이 0.5 인 걸 전제로 스케일을 잡는다.
        var box = Instantiate(bloodPopExplosionPrefab, transform.position, Quaternion.identity);
        box.transform.localScale = new Vector3(StatusRules.BLOODPOP_RADIUS * 2f, StatusRules.BLOODPOP_RADIUS * 2f, 1f);

        // 폭발은 '터진 유닛이 속한 진영의 반대'가 아니라 그 유닛과 같은 진영을 친다 —
        // 적에게 걸린 비폭이니 적을 때린다. 자신도 범위에 들어가므로 같이 맞는다.
        LayerMask mask = IsEnemyTarget ? Layers.EnemyMask : Layers.PlayerArmy;
        var info = new DamageInfo(StatusRules.BLOODPOP_DAMAGE, DamageType.BloodPop, null, false, 1f, false, "비폭");
        box.Init(info, mask, 0.2f, StatusRules.BLOODPOP_FUSE, isAlly: !IsEnemyTarget);
    }



    public void ClearStatus()
    {
        _activeSlows.Clear(); _activeSpeedBuffs.Clear(); _shieldInstances.Clear();
        _statuses.Clear();
        _cachedMoveSpeedMultiplier = 1f; _cachedTotalShield = 0f;

        // 슈퍼아머도 여기서 지운다. 예전엔 안 지워서, 오브젝트가 재사용되면 이전 유닛의
        // 슈퍼아머를 그대로 물려받았다.
        _hasSuperArmor = false; _superArmorGauge = 0f;

        // UI 갱신
        debuffTerminal?.RemoveAll();
    }
}
