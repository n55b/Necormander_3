using UnityEngine;
using System.Collections.Generic;

public class PlayerSkillController : MonoBehaviour
{
    /// <summary>현재 스킬은 취소하지 않고 활성 슬롯의 남은 재사용 대기시간만 없앤다.</summary>
    public void ResetMainSummonCooldown() => _summonCooldownEnd[ActiveSlot] = 0f;

    [Header("소환수 스왑 (임시값)")]
    [SerializeField, Min(0f), Tooltip("Space 스왑의 재사용 대기시간(초).")]
    private float swapCooldown = 30f;
    [SerializeField, Tooltip("스왑할 때 스왑 전 소환수의 대쉬 공격을 발동한다. 대쉬 공격이 없는 소환수는 스킬로 대체하며, 스킬 쿨타임을 확인하지도 돌리지도 않는다.")]
    private bool swapTriggersSkill = true;

    [Header("필드 소환수 AI (임시값)")]
    [SerializeField, Min(0.05f), Tooltip("소환수 기본 공격 주기(초). 활성·비활성 소환수 모두 각자 수행한다.")]
    private float autoAttackInterval = 2f;
    [SerializeField, Min(0f), Tooltip("소환수 위치 기준 적 발견 범위(유닛). 이 안의 적에게 다가가 공격한다.")]
    private float autoAttackRange = 6f;
    [SerializeField, Min(0f), Tooltip("소환수 기본 공격 피해 = 플레이어 공격력 × 소환수 고유 배율 × 강화 × 이 값.")]
    private float autoAttackDamageMultiplier = 1f;
    [SerializeField, Min(0f), Tooltip("적과 겹치지 않게 유지하는 최소 거리(유닛). 적 콜라이더 크기만큼 더 띄우며, 이보다 붙으면 물러난 뒤 공격한다.")]
    private float minEnemyGap = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("적에게 다가가거나 플레이어를 쫓아갈 때의 이동 속도(유닛/초).")]
    private float chaseSpeed = 7f;
    [SerializeField, Min(0.1f), Tooltip("전투가 아닐 때 플레이어와 이 거리보다 멀어지면 쫓아가기 시작한다(유닛). 그 안에서는 멈춰 있는다.")]
    private float followDistance = 2f;
    [SerializeField, Min(0f), Tooltip("쫓아가다 플레이어와 이 거리까지 가까워지면 멈춘다(유닛). 동행 거리보다 작아야 한다.")]
    private float followStopDistance = 1.4f;
    [SerializeField, Min(0.1f), Tooltip("이동 가속도(유닛/초²). 클수록 출발·정지·방향 전환이 빠르다.")]
    private float moveAcceleration = 60f;
    [SerializeField, Min(0f), Tooltip("소환수 두 마리가 서로 벌리는 거리(유닛). 0 이면 끈다.")]
    private float separationDistance = 1f;
    [SerializeField, Min(0f), Tooltip("타겟이 플레이어에게서 이 거리보다 멀어지면 전투를 풀고 플레이어에게 돌아온다(유닛).")]
    private float combatLeashDistance = 12f;
    [SerializeField, Min(0f), Tooltip("플레이어와 이 거리보다 멀어지면 곁으로 순간이동한다(문 이동 등).")]
    private float followTeleportDistance = 14f;

    [Header("플레이어 행동 연계 (임시값)")]
    [SerializeField, Min(0f), Tooltip("우클릭 스킬/스왑 대쉬 공격 때 소환수 소환수가 플레이어 위치로 이동하는 시간(초). 행동은 도착 후 나간다.")]
    private float linkMoveDuration = 0.08f;
    [SerializeField, Min(0f), Tooltip("행동이 끝난 뒤 일반 AI 로 돌아가기까지의 지연(초).")]
    private float linkReturnDelay = 0.3f;

    public float AutoAttackInterval => autoAttackInterval;
    public float AutoAttackRange => autoAttackRange;
    public float AutoAttackDamageMultiplier => autoAttackDamageMultiplier;
    public float ChaseSpeed => chaseSpeed;
    public float MinEnemyGap => minEnemyGap;
    public float FollowStopDistance => Mathf.Min(followStopDistance, followDistance);
    public float FollowDistance => followDistance;
    public float MoveAcceleration => moveAcceleration;
    public float SeparationDistance => separationDistance;
    public float CombatLeashDistance => combatLeashDistance;
    public float FollowTeleportDistance => followTeleportDistance;
    public float LinkMoveDuration => linkMoveDuration;
    public float LinkReturnDelay => linkReturnDelay;

    [Header("Equipped Summon (Auto-Synced) — 활성 소환수")]
    [SerializeField] private MainMinionDataSO mainSummon;

    // 슬롯별 쿨타임. 종료 시각으로 들고 있으므로 비활성 상태에서도 그대로 줄어든다.
    private readonly float[] _summonCooldownEnd = new float[InventoryManager.SLOT_COUNT];
    private readonly float[] _summonCooldownDuration = new float[InventoryManager.SLOT_COUNT];
    private readonly MainMinionDataSO[] _slotData = new MainMinionDataSO[InventoryManager.SLOT_COUNT];
    private readonly SummonFieldAgent[] _fieldAgents = new SummonFieldAgent[InventoryManager.SLOT_COUNT];
    private float _swapReadyTime;

    private static int InventoryActiveSlot => InventoryManager.Instance != null ? InventoryManager.Instance.ActiveSummonSlot : 0;
    public int ActiveSlot => InventoryActiveSlot;

    /// <summary>활성 소환수가 행동 중인가. DPS 미터 표시용.</summary>
    public bool IsMainSummonBusy => GetFieldAgent(ActiveSlot) != null && GetFieldAgent(ActiveSlot).IsLinking;

    /// <summary>우클릭 액티브를 담당하는 활성 소환수. 없으면 null.</summary>
    public MainMinionDataSO MainSummon => mainSummon;

    public float SwapCooldownDuration => swapCooldown;
    public float SwapCooldownRemaining => Mathf.Max(0f, _swapReadyTime - Time.time);
    public SummonFieldAgent GetFieldAgent(int slot) => IsValidSlot(slot) ? _fieldAgents[slot] : null;

    private static bool IsValidSlot(int slot) => slot >= 0 && slot < InventoryManager.SLOT_COUNT;

    /// <summary>슬롯 인덱스로 소환수를 읽는다. UI 가 슬롯을 순회할 때 사용.</summary>
    public MinionDataSO GetEquippedMinion(int slotIndex)
        => InventoryManager.Instance != null ? InventoryManager.Instance.GetSummonAt(slotIndex) : null;

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
        for (int i = 0; i < _fieldAgents.Length; i++)
            if (_fieldAgents[i] != null) Destroy(_fieldAgents[i].gameObject);
    }

    public void SyncWithInventory()
    {
        var inventory = InventoryManager.Instance;
        if (inventory == null) return;

        mainSummon = inventory.MainSummon;
        for (int i = 0; i < InventoryManager.SLOT_COUNT; i++)
        {
            var data = inventory.GetSummonAt(i);
            if (data == _slotData[i]) continue;
            // 진화는 같은 소환수라 쿨타임을 이어가고, 다른 소환수로 교체되면 새 칸처럼 시작한다.
            if (!InventoryManager.IsSameFamily(data, _slotData[i]))
            {
                _summonCooldownEnd[i] = 0f;
                _summonCooldownDuration[i] = 0f;
            }
            _slotData[i] = data;
            if (_fieldAgents[i] != null) Destroy(_fieldAgents[i].gameObject);
            _fieldAgents[i] = null;
        }
        if (!Application.isPlaying) return;
        for (int i = 0; i < InventoryManager.SLOT_COUNT; i++)
            if (_slotData[i] != null && _fieldAgents[i] == null)
                _fieldAgents[i] = SummonFieldAgent.Spawn(this, _slotData[i], i);
    }

    /// <summary>Space: 스왑 전 소환수의 대쉬 공격을 발동하고 활성 소환수를 바꾼다. 소환수 2개를 장착했고 스왑 쿨타임이 끝났을 때만 동작한다.</summary>
    public bool TrySwapSummon()
    {
        var inventory = InventoryManager.Instance;
        if (inventory == null || inventory.EquippedSummonCount < InventoryManager.SLOT_COUNT) return false;
        if (Time.time < _swapReadyTime) return false;

        // 플레이어 모션만 끊는다. 이미 나간 소환수 시전자/판정은 건드리지 않는다.
        var melee = GetComponent<MeleeCombatController>();
        if (melee != null && melee.IsAttacking) melee.CancelPlayerAttack();

        _swapReadyTime = Time.time + swapCooldown;
        // 활성 소환수를 바꾸기 전에 시전해야 스왑 전 소환수의 공격이 나간다.
        if (swapTriggersSkill) CastSwapAttack();
        inventory.SetActiveSummonSlot(1 - inventory.ActiveSummonSlot);
        return true;
    }

    /// <summary>
    /// 활성 소환수의 필드 개체를 플레이어 위치로 불러 행동을 수행한다.
    /// 필드 개체가 없으면 이동 없이 바로 수행한다.
    /// </summary>
    public void RunSummonLink(int slot, System.Func<MinionSkillCaster> perform)
    {
        var agent = GetFieldAgent(slot);
        if (agent != null) agent.PlayLink(perform);
        else perform?.Invoke();
    }

    private void CancelForMinionSkill(int slot)
    {
        var melee = GetComponent<MeleeCombatController>();
        if (melee != null && melee.IsAttacking) melee.CancelPlayerAttack();
        GetFieldAgent(slot)?.CancelBasicAttack();
    }

    /// <summary>스왑 전 소환수의 대쉬 공격. 대쉬 공격이 없는 소환수는 우선 스킬로 대체한다. 쿨타임과 무관하다.</summary>
    private void CastSwapAttack()
    {
        int slot = ActiveSlot;
        var data = mainSummon;
        var dodge = GetComponent<MeleeDodgeController>();
        if (data == null || CollectTargets().Count == 0) return;
        if (dodge == null || !dodge.CanCastSummonDashAttack(data))
        {
            CastActiveSkill(transform, true);
            return;
        }
        GetFieldAgent(slot)?.CancelBasicAttack();
        RunSummonLink(slot, () => dodge.CastSummonDashAttack(data));
    }

    private static List<Transform> CollectTargets()
    {
        // 스킬이 조준할 후보. 살아있는 적 전체를 넘기고, 실제 선별은 스킬 쪽에서 한다.
        var targets = new List<Transform>();
        foreach (var enemy in CharacterStatus.ActiveEnemies)
        {
            if (enemy == null) continue;
            var health = enemy.GetComponent<CharacterHealth>() ?? enemy.GetComponentInParent<CharacterHealth>();
            if (health != null && health.IsDead) continue;
            targets.Add(enemy.transform);
        }
        return targets;
    }

    /// <summary>
    /// 우클릭: 활성 소환수의 스킬을 조건 없이 발동한다. 쿨타임만 본다.
    /// 시전은 필드 소환수가 플레이어 위치에 도착한 뒤 나가고, 그 사이 스왑해도 이 소환수의 스킬로 끝까지 나간다.
    /// </summary>
    public void ExecuteMinionSkill(Transform playerTransform) => CastActiveSkill(playerTransform, false);

    /// <param name="fromSwap">true 면 스왑 발동. 쿨타임 중이어도 나가고, 슬롯 쿨타임을 바꾸지 않는다.</param>
    private void CastActiveSkill(Transform playerTransform, bool fromSwap)
    {
        int slot = ActiveSlot;
        var minionData = mainSummon;
        if (minionData == null || minionData.minionSkill == null) return;
        if (!fromSwap && Time.time < _summonCooldownEnd[slot]) return;
        // 칠 대상이 없으면 허공에 눌러 쿨타임을 날리지 않는다.
        if (CollectTargets().Count == 0) return;

        if (!fromSwap)
        {
            var casterStat = playerTransform != null ? playerTransform.GetComponent<CharacterStat>() : null;
            float minionCd = casterStat != null
                ? casterStat.ApplySkillCooldown(minionData.minionSkill.cooldownTime)
                : minionData.minionSkill.cooldownTime;
            // [증강 페널티] 일반 SKILL_CDR 적용 후, 소환수 전용 페널티를 추가한다.
            minionCd *= ActiveAugment.MinionCooldownMult;
            _summonCooldownEnd[slot] = Time.time + minionCd;
            _summonCooldownDuration[slot] = minionCd;
        }
        CancelForMinionSkill(slot);

        RunSummonLink(slot, () =>
        {
            var caster = CastMinionSkill(minionData, playerTransform, CollectTargets());
            if (caster == null && !fromSwap) _summonCooldownEnd[slot] = 0f; // 이동 중 대상이 사라져 시전 실패 → 쿨타임 환불
            return caster;
        });
        Debug.Log($"<color=green>[PSC]</color> Summon Skill Executed: {minionData.minionName} (slot {slot})");
    }

    /// <summary>
    /// 소환수를 시전 시점에만 실체화시켜 스킬을 쓰게 하고 알아서 소멸시킨다.
    /// 실체는 MinionSkillCaster(빈 오브젝트 + 코루틴 러너)이고, 외형은 스킬의 skillAnimVisual 이
    /// 그 자식으로 붙어서 담당한다.
    /// </summary>
    /// <returns>실제로 시전했으면 시전자, 실패하면 null.</returns>
    private MinionSkillCaster CastMinionSkill(MainMinionDataSO minionData, Transform playerTransform, List<Transform> targets)
    {
        if (playerTransform == null) return null;
        var caster = MinionSkillCaster.Spawn(minionData, playerTransform.position);
        bool cast = minionData.minionSkill.Execute(caster.transform, minionData, targets);
        if (!cast) { if (caster != null) Destroy(caster.gameObject); return null; } // 시전 실패 시 빈 시전자를 3초씩 남기지 않는다

        PolishFeedback.Play(FeedbackCue.MinionCommand, playerTransform.position, Vector2.up, PolishFeedback.MinionTier(minionData), caster.FeedbackAnchor!=null?caster.FeedbackAnchor:caster.transform);
        return caster;
    }

    /// <summary>활성 소환수 액티브의 남은 쿨타임.</summary>
    public float GetMainSummonCooldownRemaining() => GetSummonCooldownRemaining(ActiveSlot);

    public float GetSummonCooldownRemaining(int slot)
        => IsValidSlot(slot) ? Mathf.Max(0f, _summonCooldownEnd[slot] - Time.time) : 0f;

    /// <summary>슬롯의 마지막 시전에 적용된 쿨타임 길이(쿨감 반영). UI 비율 계산용.</summary>
    public float GetSummonCooldownDuration(int slot)
        => IsValidSlot(slot) ? _summonCooldownDuration[slot] : 0f;
}
