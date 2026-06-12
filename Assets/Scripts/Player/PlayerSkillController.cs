using UnityEngine;
using System.Collections.Generic;
using System;

public class PendingMinionSkill
{
    public MinionDataSO minionData;
    public PlayerSkillController.SkillSlot slot;
    public float timeRemaining;

    public PendingMinionSkill(MinionDataSO data, PlayerSkillController.SkillSlot slot, float timeout)
    {
        this.minionData = data;
        this.slot = slot;
        this.timeRemaining = timeout;
    }
}

public class PlayerSkillController : MonoBehaviour
{
    public enum SkillSlot { Q = 0, E = 1, R = 2 }

    [Header("Equipped Minion Data (Auto-Synced)")]
    [SerializeField] private MinionDataSO[] equippedMinions = new MinionDataSO[3];

    [Header("Queue Settings")]
    public float skillTimeout = 1.5f;
    
    private float[] playerSkillCooldownEnds = new float[3];
    private float[] minionSkillCooldownEnds = new float[3];
    
    private Queue<PendingMinionSkill> skillQueue = new Queue<PendingMinionSkill>();
    private PendingMinionSkill currentPendingSkill;

    public event Action<PendingMinionSkill> OnQueueUpdated;

    private void Start()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnMinionUpdated += SyncWithInventory;
            SyncWithInventory();
        }
    }

    private void OnDestroy()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnMinionUpdated -= SyncWithInventory;
        }
    }

    public void SyncWithInventory()
    {
        if (InventoryManager.Instance == null) return;

        int slotIndex = 0;
        for (int i = 0; i < 3; i++) equippedMinions[i] = null;

        foreach (var slot in InventoryManager.Instance.Slots)
        {
            if (slotIndex >= 3) break;

            if (!slot.IsEmpty && slot.EquippedLineage != null)
            {
                equippedMinions[slotIndex] = slot.GetCurrentMinionData();
                slotIndex++;
            }
        }
        Debug.Log("<color=cyan>[PlayerSkillController]</color> Sync Inventory -> Q,E,R slots complete.");
    }

    private void Update()
    {
        if (currentPendingSkill != null)
        {
            currentPendingSkill.timeRemaining -= Time.deltaTime;
            if (currentPendingSkill.timeRemaining <= 0f)
            {
                Debug.Log($"<color=orange>[PlayerSkillController]</color> {currentPendingSkill.minionData.minionName} timeout!");
                ProcessNextInQueue();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("<color=red>[Debug]</color> Force Strike!");
            OnKeywordApplied(SkillKeyword.Strike);
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            Debug.Log("<color=red>[Debug]</color> Force Corrosion!");
            OnKeywordApplied(SkillKeyword.Corrosion);
        }
#endif
    }

    public void OnKeywordApplied(SkillKeyword keyword)
    {
        bool added = false;
        for (int i = 0; i < 3; i++)
        {
            var minionData = equippedMinions[i];
            if (minionData != null && minionData.minionSkill != null)
            {
                if (minionData.minionSkill.reactKeyword == keyword)
                {
                    if (Time.time < minionSkillCooldownEnds[i])
                    {
                        Debug.Log($"<color=gray>[PlayerSkillController]</color> {minionData.minionName} is on cooldown!");
                        continue;
                    }

                    var newPending = new PendingMinionSkill(minionData, (SkillSlot)i, skillTimeout);
                    skillQueue.Enqueue(newPending);
                    added = true;
                    Debug.Log($"<color=magenta>[PlayerSkillController]</color> {minionData.minionName} queued! (Reacts: {keyword})");
                }
            }
        }

        if (added && currentPendingSkill == null)
        {
            ProcessNextInQueue();
        }
    }

    private void ProcessNextInQueue()
    {
        if (skillQueue.Count > 0)
        {
            currentPendingSkill = skillQueue.Dequeue();
            OnQueueUpdated?.Invoke(currentPendingSkill);
        }
        else
        {
            currentPendingSkill = null;
            OnQueueUpdated?.Invoke(null);
        }
    }

    public PendingMinionSkill GetCurrentPendingSkill() => currentPendingSkill;

    public void ExecutePlayerSkill(SkillSlot slot, Transform playerTransform)
    {
        var minionData = equippedMinions[(int)slot];
        if (minionData != null && minionData.playerSkill != null)
        {
            if (Time.time < playerSkillCooldownEnds[(int)slot])
            {
                Debug.Log($"<color=gray>[PlayerSkillController]</color> Player Skill {slot} is on cooldown!");
                return;
            }

            playerSkillCooldownEnds[(int)slot] = Time.time + minionData.playerSkill.cooldownTime;
            minionData.playerSkill.ExecuteSkill(playerTransform);
        }
        else
        {
            Debug.Log($"<color=gray>[PlayerSkillController]</color> Empty slot {slot}.");
        }
    }

    public void ExecuteNextMinionSkill(Transform playerTransform)
    {
        if (currentPendingSkill == null) return;

        var minionData = currentPendingSkill.minionData;
        int slotIndex = (int)currentPendingSkill.slot;

        if (minionData != null && minionData.minionSkill != null)
        {
            if (Time.time < minionSkillCooldownEnds[slotIndex])
            {
                Debug.Log($"<color=gray>[PlayerSkillController]</color> {minionData.minionName} is on cooldown! Skipping queue.");
                ProcessNextInQueue();
                return;
            }

            minionSkillCooldownEnds[slotIndex] = Time.time + minionData.minionSkill.cooldownTime;
            minionData.minionSkill.ExecuteSkill(playerTransform);
            Debug.Log($"<color=green>[PlayerSkillController]</color> Minion Skill Executed!");
        }

        ProcessNextInQueue();
    }

    // --- UI 연동을 위한 외부 접근용 함수 ---
    public float GetPlayerSkillCooldownRemaining(SkillSlot slot)
    {
        return Mathf.Max(0f, playerSkillCooldownEnds[(int)slot] - Time.time);
    }

    public float GetMinionSkillCooldownRemaining(SkillSlot slot)
    {
        return Mathf.Max(0f, minionSkillCooldownEnds[(int)slot] - Time.time);
    }
}
