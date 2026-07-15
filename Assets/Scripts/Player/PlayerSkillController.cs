using UnityEngine;
using System.Collections.Generic;

public class PlayerSkillController : MonoBehaviour
{
    public enum SkillSlot { Q = 0, E = 1, R = 2 }

    [Header("Equipped Minion Data (Auto-Synced)")]
    [SerializeField] private MinionDataSO[] equippedMinions = new MinionDataSO[3];

    [Header("Equipped Player Skills (Q/E/R, 독립 장착)")]
    [SerializeField] private PlayerSkillSO[] equippedPlayerSkills = new PlayerSkillSO[3];

    private float[] playerSkillCooldownEnds = new float[3];
    private float[] minionSkillCooldownEnds = new float[3];

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
        // 아래에서 바로 동기화 함수가 다시 채워주므로 값을 잃지 않는다.
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
        // 이벤트 등록 및 최종 데이터 확정 동기화 (Awake 이후에 채워진 데이터 동기화 보장)
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnMinionUpdated += SyncWithInventory;
            SyncWithInventory();
        }

        if (PlayerSkillInventoryManager.Instance != null)
        {
            PlayerSkillInventoryManager.Instance.OnPlayerSkillUpdated += SyncPlayerSkillsFromInventory;
            SyncPlayerSkillsFromInventory();
        }
    }

    private void OnDestroy()
    {
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated -= SyncWithInventory;

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

    public void ExecutePlayerSkill(SkillSlot slot, Transform playerTransform)
    {
        var skill = equippedPlayerSkills[(int)slot];
        if (skill == null)
        {
            Debug.Log($"<color=gray>[PlayerSkillController]</color> Empty player skill slot {slot}.");
            return;
        }

        if (Time.time < playerSkillCooldownEnds[(int)slot])
        {
            Debug.Log($"<color=orange>[Skill]</color> {skill.skillName} 쿨타임 중입니다!");
            return;
        }

        playerSkillCooldownEnds[(int)slot] = Time.time + skill.cooldownTime;
        Debug.Log($"<color=green>[Player Skill]</color> 플레이어가 '{skill.skillName}' 스킬을 사용했습니다! (슬롯: {slot})");
        skill.ExecuteSkill(playerTransform);
    }

    /// <summary>
    /// 스페이스바: 장착된 소환수의 스킬을 조건 없이 발동한다. 쿨타임만 본다.
    /// 소환수는 필드에 상주하지 않으므로 시전 시점에 임시로 실체화했다가 소멸시킨다.
    /// </summary>
    public void ExecuteMinionSkill(Transform playerTransform)
    {
        int slotIndex = FindReadyMinionSlot();
        if (slotIndex < 0) return;

        var minionData = equippedMinions[slotIndex];
        minionSkillCooldownEnds[slotIndex] = Time.time + minionData.minionSkill.cooldownTime;

        // 스킬이 조준할 후보. 살아있는 적 전체를 넘기고, 실제 선별은 스킬 쪽에서 한다.
        var targets = new List<Transform>();
        foreach (var enemy in CharacterStatus.ActiveEnemies)
        {
            if (enemy == null) continue;
            var health = enemy.GetComponent<CharacterHealth>() ?? enemy.GetComponentInParent<CharacterHealth>();
            if (health != null && health.IsDead) continue;
            targets.Add(enemy.transform);
        }

        SpawnTransientMinionAndCast(minionData, playerTransform, targets);
        Debug.Log($"<color=green>[PSC]</color> Minion Skill Executed: {minionData.minionName}");
    }

    private int FindReadyMinionSlot()
    {
        for (int i = 0; i < equippedMinions.Length; i++)
        {
            var data = equippedMinions[i];
            if (data == null || data.minionSkill == null) continue;
            if (Time.time < minionSkillCooldownEnds[i]) continue;
            return i;
        }
        return -1;
    }

    private void SpawnTransientMinionAndCast(MinionDataSO minionData, Transform playerTransform, List<Transform> targets)
    {
        // 평소 소환되는 미니언이 없는 구조이므로, 스킬 사용 시점에 임시 미니언을 생성해 시전 후 소멸시킨다.
        if (GameManager.Instance == null || GameManager.Instance.dataManager == null || minionData.minionType == CommandData.None)
        {
            // Fallback: 미니언 실체 없이 플레이어 위치에서 시전
            minionData.minionSkill.ExecuteSkill(playerTransform, null, targets);
            return;
        }

        GameObject obj = GameManager.Instance.dataManager.CreateUnit(minionData, playerTransform.position);
        if (obj == null) return;

        AllyController tempAlly = obj.GetComponent<AllyController>();
        if (tempAlly == null) return;

        tempAlly.player = playerTransform;
        tempAlly.SetBattleState(true);

        // 적들이 타겟팅하지 못하게 무적 + FlyingObject 레이어(투사체 통과 / AI 타겟 제외)
        if (tempAlly.Stats != null && tempAlly.Stats.Health != null)
            tempAlly.Stats.Health.Invincible = true;

        int flyingLayer = Layers.FlyingObject;
        if (flyingLayer != -1) SetLayerRecursive(obj, flyingLayer);

        tempAlly.EnterSkillState();
        minionData.minionSkill.ExecuteSkill(tempAlly.transform, null, targets);
        StartCoroutine(DestroyTempMinionAfterDelay(obj, tempAlly, 1.5f));
    }

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private System.Collections.IEnumerator DestroyTempMinionAfterDelay(GameObject obj, AllyController ally, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (ally != null) ally.ExitSkillState();
        if (obj != null) Destroy(obj);
    }

    // --- UI 연동을 위한 외부 접근용 함수 ---
    public float GetPlayerSkillCooldownRemaining(SkillSlot slot)
        => Mathf.Max(0f, playerSkillCooldownEnds[(int)slot] - Time.time);

    public float GetMinionSkillCooldownRemaining(SkillSlot slot)
        => Mathf.Max(0f, minionSkillCooldownEnds[(int)slot] - Time.time);
}
