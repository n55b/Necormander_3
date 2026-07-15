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
    public float AtkIntervalReduction => Passive?.atkIntervalReduction ?? 0f;
    public float BasicAttackDamageBonus => Passive?.basicAttackDamageBonus ?? 0f;

    private void Awake() => _player = GetComponent<PlayerController>();

    private void OnEnable()
    {
        RoomInstance.OnAnyRoomCleared += HandleRoomCleared;
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated += HandleSubChanged;
        HandleSubChanged(); // 이미 장착된 상태로 시작할 수 있다
    }

    private void OnDisable()
    {
        RoomInstance.OnAnyRoomCleared -= HandleRoomCleared;
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated -= HandleSubChanged;
    }

    /// <summary>서브 소환수가 새로 장착됐을 때 1회성 효과(획득 시 회복)를 쏜다.</summary>
    private void HandleSubChanged()
    {
        var inven = InventoryManager.Instance;
        var sub = inven != null ? inven.SubSummon : null;
        if (sub == _lastSub) return; // 같은 소환수로 재동기화된 것뿐이면 무시

        _lastSub = sub;
        if (sub?.subPassive == null) return;

        // MaxHP 보너스가 방금 붙었을 수 있으므로 회복 전에 스탯을 갱신시킨다.
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
