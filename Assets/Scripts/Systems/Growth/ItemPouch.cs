using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 아이템 주머니. "주머니에 들어 있으면 곧 장착 상태"다 — 보관칸/착용칸 구분은 없다.
///
/// 장비(PlayerSkillInventoryManager.EquippedEquipment)와는 완전히 별개다. 서로 참조하지 않는다.
/// InventoryManager 와 같은 GameObject 에 붙여 두고 GameManager 가 Initialize() 를 불러 준다
/// (Awake 에 의존하면 스크립트 실행 순서가 미정이라 다른 매니저가 Instance 를 못 잡는다 —
///  PlayerSkillInventoryManager 와 같은 이유, 같은 패턴).
///
/// [효과가 적용되는 두 경로]
///   · ItemStatEffect        → 플레이어 CharacterStat.Mods 에 붙인다. source 키는 이 매니저 하나(this).
///                             주머니가 바뀔 때마다 RemoveSource(this) 후 전부 다시 계산한다(멱등).
///   · ItemDamageBonusEffect → DamageEventBus.OnBeforeDamageCalculated 에서 얹는다.
///                             '적에게 주는 피해'만 대상이다(아래 가드 참조).
/// </summary>
public class ItemPouch : MonoBehaviour
{
    public static ItemPouch Instance;


    [Header("디버그 (배포 시 반드시 끈다)")]
    [Tooltip("끄면 아래 목록을 무시하고 빈 주머니로 시작한다. InventoryManager 의 " +
             "useDebugStartingInventory 와 같은 역할.")]
    [SerializeField] private bool useDebugStartingItems = true;

    [Tooltip("게임 시작 시 넣을 아이템. 동일 아이템을 반복 등록하면 최대 중첩까지 지급한다.")]
    [SerializeField] private List<ItemSO> debugStartingItems = new List<ItemSO>();

    // 개별 사본을 저장해 기존 세이브/조건부 효과 상태를 보존하고, UI에서 종류별로 묶는다.
    private readonly List<ItemSO> _slots = new List<ItemSO>();
    private int slotCount => _slots.Count;

    // SO는 공유 데이터다. 누적/일시 버프는 보유한 칸별로 관리하고 이동 시 함께 옮긴다.
    private readonly List<int> _nonCritHits = new List<int>();
    private readonly List<float> _lastGuardAt = new List<float>();
    private RoomInstance _shieldRoom;
    public int Revision { get; private set; }
    private readonly object _conditionalSource = new object();
    private CharacterStat _boundStat;
    private PlayerParryController _guard;
    private float _conditionalChance = float.NaN;
    private float _conditionalDamage = float.NaN;

    // 초기화 순서와 무관하게 UI가 Revision 변경 때만 목록을 다시 그린다.

    public int SlotCount => slotCount;

    // ── 초기화 ────────────────────────────────────────────────────────
    /// <summary>GameManager 의 초기화 시퀀스에서 직접 호출된다.</summary>
    public void Initialize(bool isLoadedGame)
    {
        Instance = this;

        // 세이브에서 이어 하는 경우엔 LoadFromData 가 채우므로 디버그 아이템을 넣지 않는다.
        if (useDebugStartingItems && !isLoadedGame)
        {
            foreach (var so in debugStartingItems)
                if (so != null && !TryAdd(so))
                    Debug.LogWarning($"<color=orange>[ItemPouch]</color> 디버그 아이템 '{so.DisplayName}' — 최대 중첩에 도달했다.");
        }

        Refresh();
    }

    private void OnEnable()
    {
        DamageEventBus.OnBeforeDamageCalculated += HandleBeforeDamage;
        DamageEventBus.OnAttackHitResolved += HandleAttackHit;
        RoomInstance.OnAnyRoomCleared += HandleRoomCleared;
        RoomInstance.OnPlayerEnteredRoom += HandleRoomEntered;
        ResetCombatState();
        Refresh();
    }

    private void OnDisable()
    {
        DamageEventBus.OnBeforeDamageCalculated -= HandleBeforeDamage;
        DamageEventBus.OnAttackHitResolved -= HandleAttackHit;
        RoomInstance.OnAnyRoomCleared -= HandleRoomCleared;
        RoomInstance.OnPlayerEnteredRoom -= HandleRoomEntered;
        UnbindPlayer();
    }

    private void Update()
    {
        if (_boundStat != PlayerStat()) Refresh();
        UpdateConditionalStats();
    }

    private void ResetCombatState()
    {
        for (int i = 0; i < slotCount; i++) ResetSlotState(i);
    }

    private void ResetSlotState(int slot)
    {
        _nonCritHits[slot] = 0;
        _lastGuardAt[slot] = float.NegativeInfinity;
    }

    private void UnbindPlayer()
    {
        if (_guard != null) _guard.OnParrySuccess -= HandleGuardSuccess;
        if (_boundStat != null)
        {
            _boundStat.Mods.RemoveSource(_conditionalSource);
            _boundStat.Mods.RemoveSource(this);
            _boundStat.Status?.SetShield(this, 0f);
        }
        _shieldRoom = null;
        _guard = null;
        _boundStat = null;
        _conditionalChance = _conditionalDamage = float.NaN;
        ResetCombatState();
    }

    // ── 조회 ──────────────────────────────────────────────────────────
    /// <summary>저장 순서의 개별 사본. 빈 칸이면 null.</summary>
    public ItemSO Get(int slot)
        => (slot < 0 || slot >= slotCount) ? null : _slots[slot];

    public bool Contains(ItemSO item)
    {
        if (item == null) return false;
        for (int i = 0; i < slotCount; i++)
            if (_slots[i] == item) return true;
        return false;
    }

    public int CountOf(ItemSO item)
    {
        if (item == null) return 0;
        int count = 0;
        foreach (var owned in _slots) if (owned == item) count++;
        return count;
    }

    public bool CanAdd(ItemSO item) => item != null && CountOf(item) < item.MaxStack;

    public void GetUniqueItems(List<ItemSO> result)
    {
        result.Clear();
        var seen = new HashSet<ItemSO>();
        foreach (var item in _slots) if (item != null && seen.Add(item)) result.Add(item);
    }

    /// <summary>첫 빈 칸 번호. 없으면 -1.</summary>
    public int FirstEmptySlot()
    {
        for (int i = 0; i < slotCount; i++) if (_slots[i] == null) return i;
        return -1;
    }

    // ── 변경 ──────────────────────────────────────────────────────────
    /// <summary>아이템별 최대 중첩 안에서 추가한다. 보유 종류/전체 개수에는 상한이 없다.</summary>
    public bool TryAdd(ItemSO so)
    {
        if (!CanAdd(so)) return false;
        int slot = FirstEmptySlot();
        if (slot < 0)
        {
            slot = _slots.Count;
            _slots.Add(null);
            _nonCritHits.Add(0);
            _lastGuardAt.Add(float.NegativeInfinity);
        }

        _slots[slot] = so;
        ResetSlotState(slot);
        Revision++;
        Refresh();
        Debug.Log($"<color=cyan>[ItemPouch]</color> '{so.DisplayName}' 습득 → {slot}번 칸");
        return true;
    }

    /// <summary>개별 사본을 제거한다(데이터 조작/검증용). 게임 UI에서는 호출하지 않는다.</summary>
    public ItemSO RemoveAt(int slot)
    {
        if (slot < 0 || slot >= slotCount) return null;
        var so = _slots[slot];
        if (so == null) return null;

        _slots[slot] = null;
        ResetSlotState(slot);
        Revision++;
        Refresh();
        return so;
    }

    /// <summary>저장 순서와 조건부 상태를 함께 옮긴다. UI 정렬은 데이터 순서를 바꾸지 않는다.</summary>
    public void Swap(int a, int b)
    {
        if (a == b) return;
        if (a < 0 || a >= slotCount || b < 0 || b >= slotCount) return;

        (_slots[a], _slots[b]) = (_slots[b], _slots[a]);
        (_nonCritHits[a], _nonCritHits[b]) = (_nonCritHits[b], _nonCritHits[a]);
        (_lastGuardAt[a], _lastGuardAt[b]) = (_lastGuardAt[b], _lastGuardAt[a]);
        Revision++;
        Refresh();
    }

    // ── 스탯 효과 ─────────────────────────────────────────────────────
    private static CharacterStat PlayerStat()
        => GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null
            ? GameManager.Instance.PLAYERCONTROLLER.Stat : null;

    /// <summary>
    /// 주머니 내용이 바뀌었으니 스탯 보정을 전부 다시 계산한다.
    /// 층 이동/로드로 플레이어가 새로 생겼을 때도 이걸 부르면 된다(멱등).
    /// </summary>
    public void Refresh()
    {
        var stat = PlayerStat();
        if (_boundStat != stat)
        {
            UnbindPlayer();
            _boundStat = stat;
            if (stat != null)
            {
                _guard = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerParryController>();
                if (_guard != null) _guard.OnParrySuccess += HandleGuardSuccess;
            }
        }
        if (stat == null) return;

        // 보정을 다시 걸기 '전'의 최대 체력. 아래에서 늘어난 만큼 현재 체력도 같이 올린다.
        float maxBefore = stat.MAXHP;

        stat.Mods.RemoveSource(this); // 아이템 전체를 한 키로 걷어내고
        var counted = new HashSet<ItemSO>();
        for (int i = 0; i < slotCount; i++)
        {
            var so = _slots[i];
            if (so == null || so.effects == null) continue;
            bool firstCopy = counted.Add(so);
            foreach (var e in so.effects)
            {
                if (e is ItemStatEffect se) se.Apply(stat, this);
                else if (firstCopy && e is ItemDashChargeEffect charge)
                    stat.Mods.AddFlat(this, StatType.DashCharges, CountOf(so) / Mathf.Max(1, charge.copiesPerCharge));
                else if (firstCopy && e is ItemCritOverflowEffect)
                    stat.Mods.AddFlat(this, StatType.CritOverflowConversion, 1f);
            }
        }
        UpdateConditionalStats();

        // [최대 체력 변화를 현재 체력에 그대로 반영]
        // 최대치가 +10 되면 현재 체력도 +10, 빠지면 -10. '증감분'을 따라가므로 버렸다 집었다를
        // 반복해도 이득이 없다(+10 뒤 -10 = 0). 풀피로 채우는 ResetHP() 를 쓰면 그게 곧 무한 회복이 된다.
        //
        // CurHP>0 가드: Health.Init 이 아직 안 돌았으면 CurHP 가 0 이고, SetHP(0) 은 isDead 를 켜버린다.
        // 지금 호출 순서(플레이어 Awake 에서 Setup)로는 안 걸리지만, 순서가 한 번 바뀌면
        // '스폰하자마자 사망'이 되는 종류의 사고라 조건 하나로 막아둔다.
        if (stat.Health == null || stat.Health.CurHP <= 0f) return;

        float delta = stat.MAXHP - maxBefore;
        // 최대치가 줄어드는 쪽일 때 현재 체력이 0 이하로 내려가 죽는 건 막는다.
        // (체력 아이템을 빼는 것만으로 죽으면 안 된다.)
        float target = Mathf.Max(1f, stat.Health.CurHP + delta);
        stat.Health.SetHP(target); // SetHP 가 새 최대치로 clamp 하고 체력바까지 갱신한다
    }

    // ── 조건부 피해 효과 ───────────────────────────────────────────────
    /// <summary>
    /// 피해 계산 직전에 아이템 보너스를 얹는다.
    ///
    /// [가드] '적에게 주는 피해'만 대상이다. 이 한 줄이 두 가지를 동시에 막는다:
    ///   · 적이 플레이어를 때리는 피해에 보너스가 붙는 것
    ///   · 플레이어가 얼었다 깨질 때 자기 빙결 파괴 피해가 +5 되는 것
    ///     (빙결 파괴는 attacker 가 null 이라 '누가 때렸나'로는 못 가른다)
    /// </summary>
    private void HandleBeforeDamage(CharacterHealth target, ref DamageInfo info)
    {
        if (target == null || target.Stat == null || !target.Stat.IsEnemy) return;

        // 이동 직후/버프 종료 프레임에도 실제 추첨은 최신 조건을 본다.
        if (_boundStat != PlayerStat()) Refresh();
        UpdateConditionalStats();

        for (int i = 0; i < slotCount; i++)
        {
            var so = _slots[i];
            if (so == null || so.effects == null) continue;
            foreach (var e in so.effects)
                if (e is ItemDamageBonusEffect de && de.Matches(target, info))
                    de.Apply(ref info);
        }
    }

    private void HandleAttackHit(CharacterHealth target, CharacterStat attacker, DamageInfo info, bool critical)
    {
        if (_boundStat == null || attacker != _boundStat || target == null || target.Stat == null
            || !target.Stat.IsEnemy || target.IsDead || !DamageRules.CanCrit(info)) return;

        int gold = 0;
        for (int i = 0; i < slotCount; i++)
        {
            var so = _slots[i];
            if (so == null || so.effects == null) continue;
            bool hasPity = false;
            foreach (var effect in so.effects)
            {
                if (effect is ItemCritPityEffect) hasPity = true;
                if (critical && effect is ItemCritGoldEffect coin) gold += Mathf.Max(0, coin.goldPerCrit);
            }
            if (hasPity) _nonCritHits[i] = critical ? 0 : _nonCritHits[i] + 1;
        }
        if (gold > 0 && InventoryManager.Instance != null) InventoryManager.Instance.AddGold(gold);
        UpdateConditionalStats();
    }

    private void HandleGuardSuccess()
    {
        for (int i = 0; i < slotCount; i++)
        {
            var so = _slots[i];
            if (so == null || so.effects == null) continue;
            foreach (var effect in so.effects)
                if (effect is ItemGuardCritEffect) { _lastGuardAt[i] = Time.time; break; }
        }
        // 일반/퍼펙트 모두 성공. 재성공은 지속시간만 갱신하고 같은 아이템의 효과를 중첩하지 않는다.
        UpdateConditionalStats();
    }

    private void UpdateConditionalStats()
    {
        if (_boundStat == null) return;
        float chance = 0f, damage = 0f;
        if (_boundStat.Health != null && !_boundStat.Health.IsDead && _boundStat.Health.CurHP > 0f)
        {
            for (int i = 0; i < slotCount; i++)
            {
                var so = _slots[i];
                if (so == null || so.effects == null) continue;
                foreach (var effect in so.effects)
                {
                    if (effect is ItemCritPityEffect pity)
                        chance += Mathf.Max(0f, pity.chancePerNonCrit) * _nonCritHits[i];
                    else if (effect is ItemGuardCritEffect guard && Time.time - _lastGuardAt[i] < guard.duration)
                    {
                        chance += Mathf.Max(0f, guard.chanceBonus);
                        damage += Mathf.Max(0f, guard.damageBonus);
                    }
                    else if (effect is ItemNearbyCritEffect nearby && HasNearbyEnemies(nearby))
                        damage += Mathf.Max(0f, nearby.damageBonus);
                }
            }
        }
        else ResetCombatState();

        // F5 스탯 표시도 같은 최종값을 본다. 값이 변할 때만 보정 목록을 갱신한다.
        if (chance == _conditionalChance && damage == _conditionalDamage) return;
        _boundStat.Mods.RemoveSource(_conditionalSource);
        if (chance != 0f) _boundStat.Mods.AddFlat(_conditionalSource, StatType.CritChance, chance);
        if (damage != 0f) _boundStat.Mods.AddFlat(_conditionalSource, StatType.CritDamage, damage);
        _conditionalChance = chance;
        _conditionalDamage = damage;
    }

    private bool HasNearbyEnemies(ItemNearbyCritEffect effect)
    {
        float radius = Mathf.Max(0f, effect.radius);
        Vector2 origin = _boundStat.transform.position;
        int count = 0;
        foreach (var status in CharacterStatus.ActiveEnemies)
        {
            if (status == null || !status.gameObject.activeInHierarchy) continue;
            var health = status.GetComponent<CharacterHealth>();
            if (health == null || health.IsDead || health.CurHP <= 0f) continue;
            if (((Vector2)status.transform.position - origin).sqrMagnitude > radius * radius) continue;
            if (++count >= Mathf.Max(1, effect.minEnemies)) return true;
        }
        return false;
    }
    // ── 방 클리어 회복 ────────────────────────────────────────────────
    /// <summary>
    /// 방이 클리어될 때 ItemRoomClearHealEffect 를 가진 아이템만큼 회복시킨다.
    ///
    /// [26/09/09] 예전엔 RoomInstance.MarkCleared 가 아이템과 무관하게 무조건 10 회복했다.
    /// 이제 그 리소스는 전부 아이템을 거친다 — 주머니가 비어 있으면 한 톨도 차지 않는다.
    ///
    /// 회복량을 칸별로 다 더한 뒤 Heal 을 한 번만 부른다 — 체력바/플로팅 텍스트가
    /// 아이템 갯수만큼 중복으로 뜨는 걸 막기 위해서다.
    /// </summary>
    private void HandleRoomCleared(RoomInstance room)
    {
        var health = GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null
            ? GameManager.Instance.PLAYERCONTROLLER.GetComponentInChildren<CharacterHealth>()
            : null;
        if (health == null || health.IsDead) return;

        float total = 0f;
        for (int i = 0; i < slotCount; i++)
        {
            var so = _slots[i];
            if (so == null || so.effects == null) continue;
            foreach (var e in so.effects)
                if (e is ItemRoomClearHealEffect he) total += he.ResolveAmount(health);
        }

        if (total <= 0f) return;
        health.Heal(total);
    }


    private void HandleRoomEntered(RoomInstance room)
    {
        if (room == null || room == _shieldRoom) return;
        if (_boundStat != PlayerStat()) Refresh();
        if (_boundStat == null || _boundStat.Status == null) return;
        _shieldRoom = room;
        float amount = 0f;
        foreach (var item in _slots)
            if (item != null && item.effects != null)
                foreach (var effect in item.effects)
                    if (effect is ItemRoomEntryShieldEffect shield) amount += Mathf.Max(0f, shield.amount);
        _boundStat.Status.SetShield(this, amount);
    }

    // ── 저장 ──────────────────────────────────────────────────────────
    public void SaveToData(SaveData data)
    {
        data.pouchSlotCount = slotCount;
        data.pouchItemNames = new List<string>();
        for (int i = 0; i < slotCount; i++)
            data.pouchItemNames.Add(_slots[i] != null ? _slots[i].name : "");
    }

    public void LoadFromData(SaveData data)
    {
        if (data == null) return;

        var registry = GameManager.Instance != null && GameManager.Instance.dataManager != null
            ? GameManager.Instance.dataManager.GET_GROWTH_REGISTRY()
            : null;

        _slots.Clear();
        _nonCritHits.Clear();
        _lastGuardAt.Clear();

        if (registry != null && registry.items != null && data.pouchItemNames != null)
        {
            for (int i = 0; i < data.pouchItemNames.Count; i++)
            {
                string n = data.pouchItemNames[i];
                if (string.IsNullOrEmpty(n)) continue;
                var item = registry.items.Find(it => it != null && it.name == n);
                if (item == null) { Debug.LogWarning($"[ItemPouch] 저장된 아이템을 찾지 못함: {n}"); continue; }
                // 구 세이브의 중복 사본도 보존한다. 새 습득에만 현재 최대 중첩을 적용한다.
                _slots.Add(item);
                _nonCritHits.Add(0);
                _lastGuardAt.Add(float.NegativeInfinity);
            }
        }

        Revision++;
        Refresh();
    }
}
