using UnityEngine;

/// <summary>
/// Attach to the player's Body, alongside its Animator. Equipment overrides must
/// share that Animator's original base controller and replace only visual clips.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class PlayerEquipmentVisualController : MonoBehaviour
{
    [SerializeField] private Animator bodyAnimator;

    private RuntimeAnimatorController _originalController;
    private RuntimeAnimatorController _baseController;
    private RuntimeAnimatorController _appliedController;
    private PlayerSkillInventoryManager _inventory;
    private EquipmentSO _lastEquipment;
    private AnimatorOverrideController _lastOverride;
    private AnimatorOverrideController _warnedInvalidOverride;
    private System.Action _equipmentUpdated;
    private bool _hasSynchronized;

    private void Reset()
    {
        bodyAnimator = GetComponent<Animator>();
    }

    private void Awake()
    {
        _equipmentUpdated = SynchronizeEquipment;
        CaptureOriginalController();
    }

    private void OnEnable()
    {
        CaptureOriginalController();
        RefreshInventorySubscription();
        SynchronizeEquipment();
    }

    private void Update()
    {
        // Instance is assigned by Initialize(), not Awake(), and may be replaced
        // between scenes. Polling this reference also handles late initialization.
        RefreshInventorySubscription();
        if (_originalController == null)
            CaptureOriginalController();

        var equipment = _inventory != null ? _inventory.Weapon : null;
        var animationOverride = equipment != null ? equipment.handAnimationOverride : null;
        if (!_hasSynchronized || equipment != _lastEquipment || animationOverride != _lastOverride)
            SynchronizeEquipment();
    }

    private void CaptureOriginalController()
    {
        if (bodyAnimator == null)
            bodyAnimator = GetComponent<Animator>();
        if (bodyAnimator == null || _originalController != null)
            return;

        _originalController = bodyAnimator.runtimeAnimatorController;
        _baseController = GetBaseController(_originalController);
    }

    private void RefreshInventorySubscription()
    {
        var current = PlayerSkillInventoryManager.Instance;
        // Normalize Unity's destroyed-object null so the old managed delegate is
        // detached even when the static Instance still points at a destroyed object.
        if (current == null)
            current = null;
        if (ReferenceEquals(current, _inventory))
            return;

        Unsubscribe();
        _inventory = current;
        if (_inventory != null)
            _inventory.OnEquipmentUpdated += _equipmentUpdated;
        SynchronizeEquipment();
    }

    private void Unsubscribe()
    {
        // The event is a managed field; detach even from a destroyed Unity object.
        if (!ReferenceEquals(_inventory, null))
            _inventory.OnEquipmentUpdated -= _equipmentUpdated;
        _inventory = null;
    }

    private void SynchronizeEquipment()
    {
        if (bodyAnimator == null || _originalController == null)
        {
            _hasSynchronized = false;
            return;
        }

        _lastEquipment = _inventory != null ? _inventory.Weapon : null;
        _lastOverride = _lastEquipment != null ? _lastEquipment.handAnimationOverride : null;
        _hasSynchronized = true;

        RuntimeAnimatorController desired = _originalController;
        if (_lastOverride != null)
        {
            if (_baseController != null && GetBaseController(_lastOverride) == _baseController)
            {
                desired = _lastOverride;
                _warnedInvalidOverride = null;
            }
            else if (_warnedInvalidOverride != _lastOverride)
            {
                _warnedInvalidOverride = _lastOverride;
                Debug.LogWarning("Equipment hand override must use the Body Animator's original base controller. Using the original visuals.", this);
            }
        }
        else
        {
            _warnedInvalidOverride = null;
        }

        ApplyController(desired);
    }

    private void ApplyController(RuntimeAnimatorController controller)
    {
        if (bodyAnimator == null || bodyAnimator.runtimeAnimatorController == controller)
            return;

        float speed = bodyAnimator.speed;
        // Unity preserves the state machine when switching overrides of the same
        // base controller. Do not Rebind, Play or manually Update: these can reset
        // normalized time, interrupt a transition or dispatch animation events.
        // Clip generation must also preserve clip lengths, events and body curves.
        bodyAnimator.runtimeAnimatorController = controller;
        bodyAnimator.speed = speed;
        _appliedController = controller;
    }

    private static RuntimeAnimatorController GetBaseController(RuntimeAnimatorController controller)
    {
        // The cap prevents malformed/cyclic override chains from hanging gameplay.
        for (int depth = 0; depth < 32 && controller is AnimatorOverrideController; depth++)
            controller = ((AnimatorOverrideController)controller).runtimeAnimatorController;
        return controller is AnimatorOverrideController ? null : controller;
    }

    private void OnDisable()
    {
        Unsubscribe();
        _hasSynchronized = false;
        _lastEquipment = null;
        _lastOverride = null;

        // Restore only the controller we own; don't overwrite another system's edit.
        if (bodyAnimator != null && _appliedController != null &&
            bodyAnimator.runtimeAnimatorController == _appliedController)
            ApplyController(_originalController);
        _appliedController = null;
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}
