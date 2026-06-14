using UnityEngine;
using System.Collections.Generic;
using System;

public class PendingMinionSkill
{
    public MinionDataSO minionData;
    public PlayerSkillController.SkillSlot slot;
    public float timeRemaining;
    public List<Transform> validTargets;

    public PendingMinionSkill(MinionDataSO data, PlayerSkillController.SkillSlot slot, float timeout)
    {
        this.minionData = data;
        this.slot = slot;
        this.timeRemaining = timeout;
        this.validTargets = new List<Transform>();
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
    public event Action                     OnQueueChanged;  // 큐 구성 변경 시 (추가/제거/타임아웃)

    // 큐 전체 스냅쌏 (currentPendingSkill + 대기열, 순서 유지)
    public List<PendingMinionSkill> GetAllPendingSkills()
    {
        var result = new List<PendingMinionSkill>();
        if (currentPendingSkill != null) result.Add(currentPendingSkill);
        result.AddRange(skillQueue);
        return result;
    }
// UI에서 미니언 정보를 읽기 위한 public getter
public MinionDataSO GetEquippedMinion(int index)
{
    if (index < 0 || index >= equippedMinions.Length) return null;
    return equippedMinions[index];
}


private void Awake()
    {
        // Awake에서 동기화하면, 같은 프레임 내 UI Initialize() 시점엔 이미 equippedMinions가 채워진 상태
        if (InventoryManager.Instance != null)
            SyncWithInventory();
    }

    private void Start()
    {
        // 이벤트 등록만 담당 (Awake에서 이미 1회 동기화됨)
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated += SyncWithInventory;
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
        // currentPendingSkill 타임아웃
        if (currentPendingSkill != null)
        {
            currentPendingSkill.timeRemaining -= Time.deltaTime;
            if (currentPendingSkill.timeRemaining <= 0f)
            {
                Debug.Log($"<color=orange>[PSC]</color> {currentPendingSkill.minionData.minionName} timeout!");
                ProcessNextInQueue();
            }
        }

        // 대기열 항목들도 각자 timeRemaining 감소
        bool anyExpired = false;
        foreach (var p in skillQueue)
        {
            p.timeRemaining -= Time.deltaTime;
            if (p.timeRemaining <= 0f) anyExpired = true;
        }
        // 만료된 항목 정리
        if (anyExpired)
        {
            var temp = new Queue<PendingMinionSkill>();
            foreach (var p in skillQueue)
                if (p.timeRemaining > 0f) temp.Enqueue(p);
            skillQueue = temp;
            OnQueueChanged?.Invoke();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F1)) { Debug.Log("[Debug] Force Strike!");    OnKeywordApplied(SkillKeyword.Strike); }
        if (Input.GetKeyDown(KeyCode.F2)) { Debug.Log("[Debug] Force Corrosion!"); OnKeywordApplied(SkillKeyword.Corrosion); }
#endif
    }

public void OnKeywordApplied(SkillKeyword keyword, Transform target = null)
    {
        bool added = false;
        for (int i = 0; i < 3; i++)
        {
            var minionData = equippedMinions[i];
            if (minionData == null || minionData.minionSkill == null) continue;
            if (minionData.minionSkill.reactKeyword != keyword) continue;
            if (Time.time < minionSkillCooldownEnds[i]) continue;

            bool found = false;
            foreach (var pending in skillQueue)
            {
                if (pending.minionData == minionData)
                {
                    if (target != null && !pending.validTargets.Contains(target))
                        pending.validTargets.Add(target);
                    found = true;
                    break;
                }
            }
            if (!found && currentPendingSkill != null && currentPendingSkill.minionData == minionData)
            {
                if (target != null && !currentPendingSkill.validTargets.Contains(target))
                    currentPendingSkill.validTargets.Add(target);
                found = true;
            }

            if (!found)
            {
                var newPending = new PendingMinionSkill(minionData, (SkillSlot)i, skillTimeout);
                if (target != null) newPending.validTargets.Add(target);
                skillQueue.Enqueue(newPending);
                added = true;
                OnQueueChanged?.Invoke();
                Debug.Log($"<color=magenta>[PSC]</color> {minionData.minionName} queued! ({keyword})");
            }
        }

        if (added && currentPendingSkill == null)
            ProcessNextInQueue();
    }

private void ProcessNextInQueue()
    {
        if (skillQueue.Count > 0)
        {
            currentPendingSkill = skillQueue.Dequeue();
            OnQueueUpdated?.Invoke(currentPendingSkill);
            OnQueueChanged?.Invoke();
        }
        else
        {
            currentPendingSkill = null;
            OnQueueUpdated?.Invoke(null);
            OnQueueChanged?.Invoke();
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
        int slotIndex  = (int)currentPendingSkill.slot;

        if (minionData != null && minionData.minionSkill != null)
        {
            if (Time.time < minionSkillCooldownEnds[slotIndex])
            {
                Debug.Log($"<color=gray>[PSC]</color> {minionData.minionName} on cooldown. Skipping.");
                ProcessNextInQueue();
                return;
            }
            minionSkillCooldownEnds[slotIndex] = Time.time + minionData.minionSkill.cooldownTime;
            minionData.minionSkill.ExecuteSkill(playerTransform, null, currentPendingSkill.validTargets);
            Debug.Log($"<color=green>[PSC]</color> Minion Skill Executed: {minionData.minionName}");
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
