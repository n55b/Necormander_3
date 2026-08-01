using UnityEngine;
using System.Collections.Generic;

public class PlayerSkillController : MonoBehaviour
{
    public enum SkillSlot { Q = 0, E = 1, R = 2 }

    [Header("Equipped Summons (Auto-Synced) — 메인 1 + 서브 1")]
    [SerializeField] private MainMinionDataSO mainSummon;
    [SerializeField] private SubMinionDataSO subSummon;

    [Header("Equipped Player Skills (Q/E/R, 독립 장착)")]
    [SerializeField] private PlayerSkillSO[] equippedPlayerSkills = new PlayerSkillSO[3];

    private float[] playerSkillCooldownEnds = new float[3];
    private float _mainSummonCooldownEnd;

    // R 로 실체화한 미니언의 시전자. 살아있는 동안 "미니언 바쁨" → 평타 3타(마무리)가 밴된다.
    // Destroy(애니 종료) 되면 Unity fake-null 로 이 참조도 == null 이 되므로 별도 초기화가 필요 없다.
    private MinionSkillCaster _activeMinionCaster;

    /// <summary>메인 소환수가 R 스킬로 실체화해 시전 중인가. MeleeCombatController 가 3타 밴 판정에 쓴다.</summary>
    public bool IsMainSummonBusy => _activeMinionCaster != null;

    /// <summary>R키 액티브 + 대쉬/평타 변화를 담당하는 소환수. 없으면 null.</summary>
    public MainMinionDataSO MainSummon => mainSummon;
    /// <summary>상시 패시브만 제공하는 소환수. 실체화하지 않는다. 없으면 null.</summary>
    public SubMinionDataSO SubSummon => subSummon;

    /// <summary>
    /// 슬롯 인덱스로 소환수를 읽는다 (0 = 메인, 1 = 서브). UI 가 슬롯을 순회할 때 사용.
    /// 범위 밖은 null — 소환수는 2마리가 전부다.
    /// </summary>
    public MinionDataSO GetEquippedMinion(int slotIndex)
    {
        if (slotIndex == InventoryManager.SLOT_MAIN) return mainSummon;
        if (slotIndex == InventoryManager.SLOT_SUB) return subSummon;
        return null;
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

        // 슬롯이 역할 고정이므로 앞에서부터 채우지 않고 역할별로 직접 읽는다.
        mainSummon = InventoryManager.Instance.MainSummon;
        subSummon = InventoryManager.Instance.SubSummon;

        Debug.Log($"<color=cyan>[PlayerSkillController]</color> Sync Inventory -> Main: {(mainSummon != null ? mainSummon.minionName : "없음")}, Sub: {(subSummon != null ? subSummon.minionName : "없음")}");
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

        // 쿨감은 Q/E/R/대쉬에 전부 먹는다. 합연산이고 상한이 없다(기획 확정) —
        // 100% 를 찍으면 이론상 즉시 재사용이 된다.
        var pStat = playerTransform != null ? playerTransform.GetComponent<CharacterStat>() : null;
        float cd = pStat != null ? pStat.ApplySkillCooldown(skill.cooldownTime) : skill.cooldownTime;
        playerSkillCooldownEnds[(int)slot] = Time.time + cd;
        Debug.Log($"<color=green>[Player Skill]</color> 플레이어가 '{skill.skillName}' 스킬을 사용했습니다! (슬롯: {slot})");
        CancelPlayerMelee(); // 평캔: Q/E 는 플레이어 평타만 끊고 미니언 마무리는 남긴다
        skill.ExecuteSkill(playerTransform);
    }

    /// <summary>평캔(Q/E·플레이어 스킬용): 진행 중이던 플레이어 평타(1·2타)만 취소한다. 미니언 마무리는
    /// 남긴다 — 미니언은 자기들끼리(R)만 회수한다. 스킬이 실제로 발동될 때만 부른다(쿨/무효타는 캔슬 안 함).</summary>
    private void CancelPlayerMelee()
    {
        var melee = GetComponent<MeleeCombatController>();
        if (melee != null && melee.IsAttacking) melee.CancelPlayerAttack();
    }

    /// <summary>R(미니언 스킬)용: 같은 미니언을 재사용하므로 평타 + 진행 중이던 마무리 소환수까지 회수(한 마리 유지).</summary>
    private void CancelForMinionSkill()
    {
        var melee = GetComponent<MeleeCombatController>();
        if (melee != null && melee.IsInAttackAction) melee.CancelAttack();
    }

    /// <summary>
    /// R키: 장착된 소환수의 스킬을 조건 없이 발동한다. 쿨타임만 본다.
    /// 소환수는 필드에 상주하지 않으므로 시전 시점에 임시로 실체화했다가 소멸시킨다.
    /// </summary>
    public void ExecuteMinionSkill(Transform playerTransform)
    {
        if (mainSummon == null || mainSummon.minionSkill == null) return;
        if (Time.time < _mainSummonCooldownEnd) return;

        var minionData = mainSummon;

        // 스킬이 조준할 후보. 살아있는 적 전체를 넘기고, 실제 선별은 스킬 쪽에서 한다.
        var targets = new List<Transform>();
        foreach (var enemy in CharacterStatus.ActiveEnemies)
        {
            if (enemy == null) continue;
            var health = enemy.GetComponent<CharacterHealth>() ?? enemy.GetComponentInParent<CharacterHealth>();
            if (health != null && health.IsDead) continue;
            targets.Add(enemy.transform);
        }

        // 쿨타임은 '실제로 시전됐을 때만' 먹인다. 칠 대상이 없어 스킬이 스스로 취소하면
        // 허공에 눌러 쿨타임을 통째로 날리는 일이 없어야 한다.
        if (!CastMinionSkill(minionData, playerTransform, targets)) return;

        var casterStat = playerTransform != null ? playerTransform.GetComponent<CharacterStat>() : null;
        float minionCd = casterStat != null
            ? casterStat.ApplySkillCooldown(minionData.minionSkill.cooldownTime)
            : minionData.minionSkill.cooldownTime;
        // [증강 페널티] '소환수 쿨다운 +N%'. 스탯(SKILL_CDR)은 Q/E 와 공유라 여기서만 따로 곱한다 —
        // 스탯으로 넣으면 소환수만 느려져야 할 페널티가 플레이어 스킬까지 같이 늦춘다.
        minionCd *= ActiveAugment.MinionCooldownMult;
        _mainSummonCooldownEnd = Time.time + minionCd;
        Debug.Log($"<color=green>[PSC]</color> Minion Skill Executed: {minionData.minionName}");
    }

    /// <summary>
    /// 소환수를 시전 시점에만 실체화시켜 스킬을 쓰게 하고 알아서 소멸시킨다.
    /// 실체는 MinionSkillCaster(빈 오브젝트 + 코루틴 러너)이고, 외형은 스킬의 skillAnimVisual 이
    /// 그 자식으로 붙어서 담당한다. 필드를 돌아다니지 않으므로 AI/NavMesh/전투 스탯이 필요 없다.
    /// </summary>
    /// <returns>실제로 시전했으면 true. false 면 쿨타임을 먹이지 않는다.</returns>
    private bool CastMinionSkill(MainMinionDataSO minionData, Transform playerTransform, List<Transform> targets)
    {
        var caster = MinionSkillCaster.Spawn(minionData, playerTransform.position);
        bool cast = minionData.minionSkill.Execute(caster.transform, minionData, targets);
        if (!cast && caster != null) { Destroy(caster.gameObject); return false; } // 시전 실패 시 빈 시전자를 3초씩 남기지 않는다

        // 시전 성공 → 이 시전자가 살아있는 동안 "미니언 바쁨"(평타 3타 밴). 애니 끝나 Destroy 되면 자동 해제.
        _activeMinionCaster = caster;
        CancelForMinionSkill(); // R 은 같은 미니언을 쓰니 평타 + 진행 중이던 마무리까지 회수(한 마리 유지)
        return true;
    }

    // --- UI 연동을 위한 외부 접근용 함수 ---
    public float GetPlayerSkillCooldownRemaining(SkillSlot slot)
        => Mathf.Max(0f, playerSkillCooldownEnds[(int)slot] - Time.time);

    /// <summary>메인 소환수 액티브(R키)의 남은 쿨타임.</summary>
    public float GetMainSummonCooldownRemaining()
        => Mathf.Max(0f, _mainSummonCooldownEnd - Time.time);
}
