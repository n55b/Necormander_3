using UnityEngine;

/// <summary>
/// 장착된 서브 소환수의 상시 패시브를 플레이어에게 적용한다.
///
/// 설계 3.4: 서브 소환수는 실체화하지 않고 Space 와도 무관하며, 장착만으로 항상 켜져 있다.
/// 스탯 보너스는 CharacterStat / MeleeCombatController 가 여기서 읽어가고(pull),
/// 일회성/이벤트성 회복은 여기서 직접 쏜다(push).
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class SubSummonPassiveController : MonoBehaviour
{
    private PlayerController _player;
    private MinionDataSO _lastSub;

    // 씬을 넘어가도 '이미 획득한 서브'를 기억한다. 플레이어는 층 이동마다 재생성되므로
    // 인스턴스 필드에만 담으면 healOnAcquire 가 층마다 공짜로 다시 터진다.
    private static string _acquiredSubName;

    /// <summary>새 런을 시작할 때 호출. (사망/타이틀 복귀 등)</summary>
    public static void ResetAcquiredState() => _acquiredSubName = null;

    /// <summary>현재 장착된 서브 소환수의 패시브. 없으면 null.</summary>
    private MinionSubPassive Passive
    {
        get
        {
            var inven = InventoryManager.Instance;
            var sub = inven != null ? inven.SubSummon : null;
            return sub != null ? sub.subPassive : null;
        }
    }

    // --- CharacterStat / MeleeCombatController 가 읽어가는 값 ---

    public float MaxHpBonus => Passive?.maxHpBonus ?? 0f;
    public float AtkSpeedBonus => Passive?.atkSpeedBonus ?? 0f;
    public float BasicAttackDamageBonus => Passive?.basicAttackDamageBonus ?? 0f;
    public float SkillCooldownReduction => Passive?.skillCooldownReduction ?? 0f;

    private float _nextSkillHealTime;

    private void Awake() => _player = GetComponent<PlayerController>();

    private void OnEnable()
    {
        RoomInstance.OnAnyRoomCleared += HandleRoomCleared;
        DamageEventBus.OnDamageReceived += HandleDamageDealt;
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated += HandleSubChanged;
        HandleSubChanged(); // 이미 장착된 상태로 시작할 수 있다
    }

    private void OnDisable()
    {
        RoomInstance.OnAnyRoomCleared -= HandleRoomCleared;
        DamageEventBus.OnDamageReceived -= HandleDamageDealt;
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated -= HandleSubChanged;
    }

    /// <summary>
    /// 스킬 적중 시 회복. 버스는 '누가 맞았는지'만 알려주므로 내가 때린 게 맞는지를 여기서 가른다.
    /// 다단히트 스킬(연타/장판)이 한 번에 수십 번 터지므로 내부 쿨타임이 필수다.
    /// </summary>
    private void HandleDamageDealt(CharacterHealth target, DamageInfo info)
    {
        float amount = Passive?.healOnSkillHit ?? 0f;
        if (amount <= 0f) return;
        if (info.category != DamageCategory.Skill) return;
        if (target == null || target.transform.root == transform.root) return; // 내가 맞은 건 제외
        if (Time.time < _nextSkillHealTime) return;

        _nextSkillHealTime = Time.time + Mathf.Max(0f, Passive.healOnSkillHitCooldown);
        Heal(amount);
    }

    /// <summary>서브 소환수가 새로 장착됐을 때 1회성 효과(획득 시 회복)를 쏜다.</summary>
    private void HandleSubChanged()
    {
        var inven = InventoryManager.Instance;
        var sub = inven != null ? inven.SubSummon : null;
        if (sub == _lastSub) return; // 같은 소환수로 재동기화된 것뿐이면 무시
        _lastSub = sub;

        if (sub?.subPassive == null) { _acquiredSubName = null; return; }

        // 층을 넘어와 플레이어가 재생성된 경우엔 '이미 갖고 있던' 것이므로 획득 효과를 다시 주지 않는다.
        if (_acquiredSubName == sub.name) return;
        _acquiredSubName = sub.name;

        if (sub.subPassive.healOnAcquire > 0f)
            Heal(sub.subPassive.healOnAcquire);
    }

    private void HandleRoomCleared(RoomInstance room)
    {
        float amount = Passive?.healOnRoomClear ?? 0f;
        if (amount > 0f) Heal(amount);
    }

    private void Heal(float amount)
    {
        var health = _player != null ? _player.GetComponentInChildren<CharacterHealth>() : null;
        if (health != null && !health.IsDead) health.Heal(amount);
    }
}
