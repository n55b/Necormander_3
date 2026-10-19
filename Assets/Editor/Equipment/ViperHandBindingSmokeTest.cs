using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Isolated EditMode lifecycle smoke check; no gameplay equip/passive calls.</summary>
public static class ViperHandBindingSmokeTest
{
    [MenuItem("Tools/Necromander/Test Viper Equipment Binding")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run this isolated test outside Play Mode.");
        var originalSingleton = PlayerSkillInventoryManager.Instance;
        GameObject inventoryObject = null, replacementObject = null, body = null;
        PlayerEquipmentVisualController visual = null;
        EquipmentSO ordinary = null;
        try
        {
            var viper = AssetDatabase.LoadAssetAtPath<EquipmentSO>("Assets/SOData/Equipment/1015/Viper.asset");
            var baseline = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Player/Player_Animator.controller");
            if (viper == null || viper.handAnimationOverride == null || baseline == null)
                throw new InvalidOperationException("Run Viper setup first.");
            ordinary = ScriptableObject.CreateInstance<EquipmentSO>();
            inventoryObject = Hidden("Viper binding test inventory");
            var inventory = inventoryObject.AddComponent<PlayerSkillInventoryManager>();
            SetEquipment(inventory, viper);
            inventory.Initialize();
            body = Hidden("Viper binding test Body");
            var animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = baseline;
            animator.speed = 1.7f;
            visual = body.AddComponent<PlayerEquipmentVisualController>();
            // EditMode does not run ordinary MonoBehaviour lifecycle. Invoke only
            // this component's lifecycle, never gameplay initialization/update.
            Invoke(visual, "Awake"); Invoke(visual, "OnEnable");
            Check(animator.runtimeAnimatorController == viper.handAnimationOverride, "initial/spawn equipment");
            Check(Mathf.Approximately(animator.speed, 1.7f), "speed preservation");
            SetEquipment(inventory, ordinary); inventory.OnEquipmentUpdated?.Invoke();
            Check(animator.runtimeAnimatorController == baseline, "switch back to unconfigured equipment");
            SetEquipment(inventory, viper); inventory.OnEquipmentUpdated?.Invoke();
            Check(animator.runtimeAnimatorController == viper.handAnimationOverride, "equip Viper event");
            Invoke(visual, "OnDisable");
            Check(animator.runtimeAnimatorController == baseline && inventory.OnEquipmentUpdated == null, "disable restoration/unsubscribe");
            PlayerSkillInventoryManager.Instance = null;
            Invoke(visual, "OnEnable");
            Check(animator.runtimeAnimatorController == baseline, "missing inventory fallback");
            inventory.Initialize(); Invoke(visual, "Update");
            Check(animator.runtimeAnimatorController == viper.handAnimationOverride, "late Initialize");
            replacementObject = Hidden("Viper binding replacement inventory");
            var replacement = replacementObject.AddComponent<PlayerSkillInventoryManager>();
            SetEquipment(replacement, ordinary); replacement.Initialize(); Invoke(visual, "Update");
            Check(animator.runtimeAnimatorController == baseline && inventory.OnEquipmentUpdated == null, "manager replacement and old subscription cleanup");
            SetEquipment(replacement, viper); replacement.OnEquipmentUpdated?.Invoke();
            Check(animator.runtimeAnimatorController == viper.handAnimationOverride, "replacement manager event");
            SetEquipment(replacement, null); replacement.OnEquipmentUpdated?.Invoke();
            Check(animator.runtimeAnimatorController == baseline, "unequipped fallback");
            Debug.Log("[Viper] Isolated binding smoke check passed: initial equipment, switch-back, events, disable/re-enable, late initialization, manager replacement, null equipment and speed. This is not a Play Mode/animation evaluation test.");
        }
        finally
        {
            if (visual != null) Invoke(visual, "OnDisable");
            if (body != null) UnityEngine.Object.DestroyImmediate(body);
            if (inventoryObject != null) UnityEngine.Object.DestroyImmediate(inventoryObject);
            if (replacementObject != null) UnityEngine.Object.DestroyImmediate(replacementObject);
            if (ordinary != null) UnityEngine.Object.DestroyImmediate(ordinary);
            PlayerSkillInventoryManager.Instance = originalSingleton;
        }
    }

    private static GameObject Hidden(string name) => new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
    private static void SetEquipment(PlayerSkillInventoryManager inventory, EquipmentSO equipment)
    {
        // Bypass EquipEquipment intentionally: the test must not mutate live player
        // stats, passives, hit hooks, registries, saves, or scene state.
        typeof(PlayerSkillInventoryManager).GetField("_equipped", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(inventory, equipment == null ? null : new EquipmentInstance { baseData = equipment });
    }
    private static void Invoke(PlayerEquipmentVisualController target, string method)
        => typeof(PlayerEquipmentVisualController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Viper binding check failed: " + label);
    }
}
