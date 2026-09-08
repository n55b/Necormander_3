using UnityEngine;
using System.Collections.Generic;

public class PlayerSkillController : MonoBehaviour
{

    [Header("Equipped Summon (Auto-Synced) — 메인 1")]
    [SerializeField] private MainMinionDataSO mainSummon;
    // [26/08/15] 서브 소환수 삭제. 1번 슬롯은 우클릭 칸이 됐고, 우클릭은 소환수가 아니라
    // PlayerParryController 가 InventoryManager 에서 직접 읽으므로 여기서 캐시할 게 없다.

    private float _mainSummonCooldownEnd;

    // Space 로 실체화한 미니언의 시전자. 살아있는 동안 "미니언 바쁨" → 평타 3타(마무리)가 밴된다.
    // Destroy(애니 종료) 되면 Unity fake-null 로 이 참조도 == null 이 되므로 별도 초기화가 필요 없다.
    private MinionSkillCaster _activeMinionCaster;

    /// <summary>메인 소환수가 Space 스킬로 실체화해 시전 중인가. MeleeCombatController 가 3타 밴 판정에 쓴다.</summary>
    public bool IsMainSummonBusy => _activeMinionCaster != null;

    /// <summary>Space 액티브 + 대쉬/평타 변화를 담당하는 소환수. 없으면 null.</summary>
    public MainMinionDataSO MainSummon => mainSummon;

    /// <summary>
    /// 슬롯 인덱스로 소환수를 읽는다. UI 가 슬롯을 순회할 때 사용.
    /// 소환수는 이제 메인 1마리뿐이라 0번 외에는 전부 null 이다
    /// (1번은 우클릭 칸이고, 그건 소환수가 아니라 InventoryManager.EquippedRightClick 으로 읽는다).
    /// </summary>
    public MinionDataSO GetEquippedMinion(int slotIndex)
    {
        if (slotIndex == InventoryManager.SLOT_MAIN) return mainSummon;
        return null;
    }

    private void Awake() => SyncWithInventory();
    private void Start()
    {
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated += SyncWithInventory;
        SyncWithInventory();
    }
    private void OnDestroy()
    {
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated -= SyncWithInventory;
    }

    public void SyncWithInventory()
    {
        if (InventoryManager.Instance == null) return;

        // 슬롯이 역할 고정이므로 앞에서부터 채우지 않고 역할별로 직접 읽는다.
        mainSummon = InventoryManager.Instance.MainSummon;

        var rc = InventoryManager.Instance.EquippedRightClick;
        Debug.Log($"<color=cyan>[PlayerSkillController]</color> Sync Inventory -> Main: {(mainSummon != null ? mainSummon.minionName : "없음")}, 우클릭: {(rc != null ? rc.ResolveTitle() : "없음")}");
    }

    private void CancelForMinionSkill()
    {
        var melee = GetComponent<MeleeCombatController>();
        if (melee != null && melee.IsInAttackAction) melee.CancelAttack();
    }

    /// <summary>
    /// Space: 장착된 소환수의 스킬을 조건 없이 발동한다. 쿨타임만 본다.
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
        // [증강 페널티] 일반 SKILL_CDR 적용 후, 소환수 전용 페널티를 추가한다.
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
        CancelForMinionSkill(); // Space 은 같은 미니언을 쓰니 평타 + 진행 중이던 마무리까지 회수(한 마리 유지)
        return true;
    }

    /// <summary>메인 소환수 액티브(Space)의 남은 쿨타임.</summary>
    public float GetMainSummonCooldownRemaining()
        => Mathf.Max(0f, _mainSummonCooldownEnd - Time.time);
}
