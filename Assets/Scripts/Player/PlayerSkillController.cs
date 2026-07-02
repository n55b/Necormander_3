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

    [Header("Equipped Player Skills (Q/E/R, 독립 장착)")]
    [SerializeField] private PlayerSkillSO[] equippedPlayerSkills = new PlayerSkillSO[3];


    [Header("Queue Settings")]
    public float skillTimeout = 1.5f;

    private float[] playerSkillCooldownEnds = new float[3];
    private float[] minionSkillCooldownEnds = new float[3];

    private Queue<PendingMinionSkill> skillQueue = new Queue<PendingMinionSkill>();
    private PendingMinionSkill currentPendingSkill;

    public event Action<PendingMinionSkill> OnQueueUpdated;
    public event Action OnQueueChanged;  // 큐 구성 변경 시 (추가/제거/타임아웃)

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

    public PlayerSkillSO GetEquippedPlayerSkill(int index)
    {
        if (index < 0 || index >= equippedPlayerSkills.Length) return null;
        return equippedPlayerSkills[index];
    }

public void SetEquippedPlayerSkill(int index, PlayerSkillSO skill)
    {
        // Delegate to the source of truth (PlayerSkillInventoryManager) instead of touching the local cache directly.
        // This ensures OnPlayerSkillUpdated fires correctly, keeping other listeners (e.g. UI) in sync too.
        if (PlayerSkillInventoryManager.Instance != null)
            PlayerSkillInventoryManager.Instance.Equip(index, skill);
        else
            Debug.LogWarning("<color=orange>[PlayerSkillController]</color> PlayerSkillInventoryManager.Instance is null, equip request was not applied.");
    }



    private void Awake()
    {
                // [Fix] 직렬화된 배열 길이가 3이 아니면 인덱스 예외가 발생한다. 강제로 새로 만들어서 방지한다.
        // 아래에서 바로 동기화 함수가 다시 채워주므로 값을 잃지 않는다.전하다. 난다웳을 쟁합니다.
        // 둘 다 원당은 아래에서 더 목 동기화되머로 여기서 재생성해도 안전합니다.
        if (equippedMinions == null || equippedMinions.Length != 3) equippedMinions = new MinionDataSO[3];
        if (equippedPlayerSkills == null || equippedPlayerSkills.Length != 3) equippedPlayerSkills = new PlayerSkillSO[3];
        // Awake에서 동기화하면, 같은 프레임 내 UI Initialize() 시점엔 이미 equippedMinions가 채워진 상태
        if (InventoryManager.Instance != null)
            SyncWithInventory();

        if (PlayerSkillInventoryManager.Instance != null)
            SyncPlayerSkillsFromInventory();
    }

    private void Start()
    {
        // 이벤트 등록만 담당 (Awake에서 이미 1회 동기화됨)
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated += SyncWithInventory;

        if (PlayerSkillInventoryManager.Instance != null)
            PlayerSkillInventoryManager.Instance.OnPlayerSkillUpdated += SyncPlayerSkillsFromInventory;
    }

    private void OnDestroy()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnMinionUpdated -= SyncWithInventory;
        }

        if (PlayerSkillInventoryManager.Instance != null)
            PlayerSkillInventoryManager.Instance.OnPlayerSkillUpdated -= SyncPlayerSkillsFromInventory;
    }

    public void SyncPlayerSkillsFromInventory()
    {
        if (PlayerSkillInventoryManager.Instance == null) return;

        for (int i = 0; i < equippedPlayerSkills.Length; i++)
            equippedPlayerSkills[i] = PlayerSkillInventoryManager.Instance.GetEquipped(i);

        Debug.Log("<color=cyan>[PlayerSkillController]</color> Sync PlayerSkillInventory -> Q,E,R slots complete.");
    }

    
public void SyncWithInventory()
    {
        if (InventoryManager.Instance == null) return;

        int slotIndex = 0;
        for (int i = 0; i < 3; i++) equippedMinions[i] = null;

        foreach (var slot in InventoryManager.Instance.Slots)
        {
            if (slotIndex >= 3) break;

            if (!slot.IsEmpty && slot.EquippedMinion != null)
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
        if (Input.GetKeyDown(KeyCode.F1)) { Debug.Log("[Debug] Force Strike!"); OnKeywordApplied(SkillKeyword.Strike); }
        if (Input.GetKeyDown(KeyCode.F2)) { Debug.Log("[Debug] Force Debuff!"); OnKeywordApplied(SkillKeyword.Debuff); }
#endif
    }

    private SkillSlot lastUsedPlayerSkillSlot = SkillSlot.Q;

    public void OnKeywordApplied(SkillKeyword keyword, Transform target = null)
    {
        bool added = false;

        // 탐색 순서: 트리거를 발생시킨(가장 최근에 사용한) 슬롯부터 큐에 넣고, 나머지는 Q-E-R 순환 순서대로
        int firstSlot = (int)lastUsedPlayerSkillSlot;
        int[] searchOrder = new int[3];
        searchOrder[0] = firstSlot;
        searchOrder[1] = (firstSlot + 1) % 3;
        searchOrder[2] = (firstSlot + 2) % 3;

        foreach (int i in searchOrder)
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
        lastUsedPlayerSkillSlot = slot;

        var skill = equippedPlayerSkills[(int)slot];
        if (skill != null)
        {
            if (Time.time < playerSkillCooldownEnds[(int)slot])
            {
                Debug.Log($"<color=orange>[Skill]</color> {skill.skillName} 쿨타임 중입니다!");
                return;
            }

            playerSkillCooldownEnds[(int)slot] = Time.time + skill.cooldownTime;
            Debug.Log($"<color=green>[Player Skill]</color> 플레이어가 '{skill.skillName}' 스킬을 사용했습니다! (슬롯: {slot})");
            skill.ExecuteSkill(playerTransform);
        }
        else
        {
            Debug.Log($"<color=gray>[PlayerSkillController]</color> Empty player skill slot {slot}.");
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
                Debug.Log($"<color=gray>[PSC]</color> {minionData.minionName} on cooldown. Skipping.");
                ProcessNextInQueue();
                return;
            }
            minionSkillCooldownEnds[slotIndex] = Time.time + minionData.minionSkill.cooldownTime;

            // [수정] 평소 소환되는 미니언이 없는 구조이므로, 스킬 사용 시점에 임시 미니언을 생성하여 스킬 시전 후 소멸시킵니다.
            if (GameManager.Instance != null && GameManager.Instance.dataManager != null && minionData.minionType != CommandData.None)
            {
                Vector3 spawnPos = playerTransform.position;
                GameObject obj = GameManager.Instance.dataManager.CreateUnit(minionData, spawnPos);
                if (obj != null)
                {
                    AllyController tempAlly = obj.GetComponent<AllyController>();
                    if (tempAlly != null)
                    {
                        tempAlly.player = playerTransform;
                        tempAlly.SetBattleState(true);
                        
                        // 1. 적들이 타겟팅 못하게 무적 처리
                        if (tempAlly.Stats != null && tempAlly.Stats.Health != null)
                        {
                            tempAlly.Stats.Health.Invincible = true;
                        }
                        
                        // 2. 레이어를 FlyingObject로 변경하여 투사체 충돌 패스 및 AI 타겟팅 제외
                        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
                        if (flyingLayer != -1)
                        {
                            SetLayerRecursive(obj, flyingLayer);
                        }
                        
                        // 3. AI 동작 차단 및 스킬 상태 돌입 (애니메이션 동기화 포함)
                        tempAlly.EnterSkillState();
                        
                        // 4. 미니언 스킬 실행
                        minionData.minionSkill.ExecuteSkill(tempAlly.transform, null, currentPendingSkill.validTargets);
                        
                        // 5. 일정 시간 후 소멸 처리 (1.5초 후 파괴)
                        StartCoroutine(DestroyTempMinionAfterDelay(obj, tempAlly, 1.5f));
                    }
                }
            }
            else
            {
                // Fallback (for non-minion bound skills or no data manager)
                minionData.minionSkill.ExecuteSkill(playerTransform, null, currentPendingSkill.validTargets);
            }
            Debug.Log($"<color=green>[PSC]</color> Minion Skill Executed (Transient): {minionData.minionName}");
        }
        ProcessNextInQueue();
    }

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }

    private System.Collections.IEnumerator DestroyTempMinionAfterDelay(GameObject obj, AllyController ally, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (ally != null)
        {
            ally.ExitSkillState();
        }
        if (obj != null)
        {
            Destroy(obj);
        }
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
