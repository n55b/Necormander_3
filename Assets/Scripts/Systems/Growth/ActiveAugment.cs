using UnityEngine;

/// <summary>
/// 증강 방에서 고른 증강 한 장의 런타임 상태.
///
/// [수명]
///   페널티 — 그 방 전투 동안만. 전투 시작 직전에 Apply(), 방이 클리어되는 순간 ClearPenalty().
///   보상   — 런 영구. 방을 깬 뒤 보상 상자를 열 때 GrantPendingReward() 로 지급된다.
///
/// MonoBehaviour 가 아니라 static 인 이유: 페널티를 읽어야 하는 곳(적 스폰, 소환수 쿨다운,
/// 피해 계산)이 죄다 다른 계층에 흩어져 있어서, 참조를 인스펙터로 물리면 한 군데만 빠뜨려도
/// 조용히 안 걸린다. 대신 씬을 다시 로드해도 static 은 안 죽으므로 GameManager 가
/// 초기화할 때 ResetAll() 을 반드시 불러 줘야 한다.
/// </summary>
public static class ActiveAugment
{
    /// <summary>스탯 보정을 걸 때 쓰는 source 키. 이 하나로 전부 걷어낸다.</summary>
    private static readonly object SourceKey = new object();

    private static AugmentPenalty _penalty;
    private static AugmentReward _pendingReward;

    // 매 프레임/매 스폰마다 읽히는 값들이라 미리 곱해서 들고 있는다.
    private static float _playerDamageTakenMult = 1f;
    private static float _enemyMaxHpBonus = 0f;   // 0.15 = +15%
    private static float _enemyAttackBonus = 0f;

    /// <summary>소환수(R) 쿨다운 배수. 1 = 그대로, 1.15 = 15% 길어짐.</summary>
    public static float MinionCooldownMult { get; private set; } = 1f;

    public static AugmentPenalty CurrentPenalty => _penalty;
    public static bool HasPendingReward => _pendingReward != null;

    // ── 적용 / 해제 ────────────────────────────────────────────────────
    /// <summary>카드를 골랐을 때. 페널티를 즉시 걸고, 보상은 방을 깰 때까지 들고 있는다.</summary>
    public static void Apply(AugmentOffer offer)
    {
        ClearPenalty();
        _pendingReward = null; // 지난 방에서 안 받아 간 보상이 이 방으로 새지 않게
        if (offer == null) return;

        _pendingReward = offer.reward;
        _penalty = offer.penalty;
        if (_penalty == null) // P0 = 페널티 없음
        {
            Debug.Log("<color=cyan>[Augment]</color> P0(무위험) 선택 — 페널티 없음.");
            return;
        }

        float v = _penalty.value;
        switch (_penalty.kind)
        {
            case AugmentPenaltyKind.PlayerDamageTaken:
                _playerDamageTakenMult = 1f + v / 100f;
                DamageEventBus.OnBeforeDamageCalculated += HandleBeforeDamage;
                break;

            case AugmentPenaltyKind.MinionCooldown:
                MinionCooldownMult = 1f + v / 100f;
                break;

            case AugmentPenaltyKind.PlayerMoveSpeed:
                PlayerMods()?.AddPercent(SourceKey, StatType.MoveSpeed, v / 100f);
                break;

            case AugmentPenaltyKind.PlayerBasicAttack:
                // BASIC_ATK_MULT 는 Percent 를 안 읽고 Flat 만 더한다(베이스 1.0 기준).
                // -10 → -0.1 → 1.0 이 0.9 가 되어 결과적으로 -10%.
                PlayerMods()?.AddFlat(SourceKey, StatType.BasicAttackMultiplier, v / 100f);
                break;

            case AugmentPenaltyKind.PlayerDashCooldown:
                // 최종 쿨은 base * (1 - DASH_CDR) 이라, 쿨감을 음수로 넣으면 그대로 쿨이 늘어난다.
                // +100% → 쿨감 -100 → base * 2.
                PlayerMods()?.AddFlat(SourceKey, StatType.DashCooldownReduction, -v);
                break;

            case AugmentPenaltyKind.EnemyMaxHp:
                _enemyMaxHpBonus = v / 100f;
                break;

            case AugmentPenaltyKind.EnemyAttack:
                _enemyAttackBonus = v / 100f;
                break;

            // 적 수 계열은 값만 들고 있다가 NormalRoomEvent 가 ExtraEnemiesForWave 로 물어본다.
            case AugmentPenaltyKind.EnemyCountPerWave:
            case AugmentPenaltyKind.EnemyCountEarlyWaves:
                break;
        }

        Debug.Log($"<color=orange>[Augment]</color> 페널티 '{_penalty.displayName}'({_penalty.id}) 적용 — {_penalty.flavor}");
    }

    /// <summary>전투가 끝났다. 페널티만 걷어내고 보상은 남긴다.</summary>
    public static void ClearPenalty()
    {
        if (_penalty != null && _penalty.kind == AugmentPenaltyKind.PlayerDamageTaken)
            DamageEventBus.OnBeforeDamageCalculated -= HandleBeforeDamage;

        PlayerMods()?.RemoveSource(SourceKey);

        _penalty = null;
        _playerDamageTakenMult = 1f;
        _enemyMaxHpBonus = 0f;
        _enemyAttackBonus = 0f;
        MinionCooldownMult = 1f;
    }

    /// <summary>씬을 새로 로드했다. 페널티도 미지급 보상도 전부 버린다(GameManager 초기화에서 호출).</summary>
    public static void ResetAll()
    {
        ClearPenalty();
        _pendingReward = null;
    }

    // ── 페널티가 실제로 먹는 지점들 ─────────────────────────────────────
    /// <summary>피해 계산 직전. '플레이어가 받는 피해'만 키운다.</summary>
    private static void HandleBeforeDamage(CharacterHealth target, ref DamageInfo info)
    {
        if (target == null || target.Stat == null || !target.Stat.IsPlayer) return;
        info.amount *= _playerDamageTakenMult;
    }

    /// <summary>
    /// 방금 스폰된 적에게 체력/공격력 페널티를 얹는다. DataManager.CreateUnit 이 부른다 —
    /// 스폰 경로가 거기 하나뿐이라 분열로 생긴 적까지 전부 걸린다.
    /// [순서 주의] BaseEntity.Initialize → CharacterStat.InitializeStats 가 Mods.Clear() 를 하므로
    /// 반드시 그 뒤에 걸어야 하고, 체력을 늘렸으면 ResetHP 로 현재 체력도 새 최대치까지 채워야 한다.
    /// </summary>
    public static void ApplyToSpawnedEnemy(GameObject unit)
    {
        if (unit == null) return;
        if (_enemyMaxHpBonus == 0f && _enemyAttackBonus == 0f) return;

        var stat = unit.GetComponent<CharacterStat>() ?? unit.GetComponentInChildren<CharacterStat>();
        if (stat == null || !stat.IsEnemy) return;

        if (_enemyMaxHpBonus != 0f) stat.Mods.AddPercent(SourceKey, StatType.Health, _enemyMaxHpBonus);
        if (_enemyAttackBonus != 0f) stat.Mods.AddPercent(SourceKey, StatType.Attack, _enemyAttackBonus);

        // 이 적은 이 방에서만 살다 죽으므로 보정을 떼어낼 일이 없다. 최대 체력만 다시 채워 준다.
        if (_enemyMaxHpBonus != 0f && stat.Health != null) stat.Health.ResetHP();
    }

    /// <summary>이번 웨이브에 추가로 더 나올 적 수. 페널티가 없으면 0.</summary>
    public static int ExtraEnemiesForWave(int wave)
    {
        if (_penalty == null) return 0;
        return _penalty.kind switch
        {
            AugmentPenaltyKind.EnemyCountPerWave => Mathf.RoundToInt(_penalty.value),
            AugmentPenaltyKind.EnemyCountEarlyWaves =>
                wave <= _penalty.waveLimit ? Mathf.RoundToInt(_penalty.value) : 0,
            _ => 0,
        };
    }

    // ── 보상 ──────────────────────────────────────────────────────────
    /// <summary>증강 방 보상 상자를 열었다. 들고 있던 보상을 지급한다.</summary>
    public static void GrantPendingReward()
    {
        var reward = _pendingReward;
        _pendingReward = null;
        if (reward == null)
        {
            Debug.LogWarning("[Augment] 지급할 보상이 없다. 카드를 안 고르고 방을 깼거나 테이블에 해당 등급 보상이 비어 있다.");
            return;
        }

        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        var stat = player != null ? player.Stat : null;

        switch (reward.kind)
        {
            case AugmentRewardKind.Gold:
                InventoryManager.Instance?.AddGold(Mathf.RoundToInt(reward.value));
                break;

            case AugmentRewardKind.HealPercent:
                if (stat != null && stat.Health != null) stat.Health.Heal(stat.MAXHP * reward.value / 100f);
                break;

            case AugmentRewardKind.MaxHpFlat:
                // 런 영구 보너스는 InventoryManager 가 들고 있다(세이브도 거기서 같이 간다).
                // CharacterStat.MAXHP 가 매번 읽어 가므로 층을 넘겨 플레이어가 새로 생겨도 알아서 붙는다.
                if (InventoryManager.Instance != null)
                {
                    InventoryManager.Instance.AddAugmentMaxHp(reward.value);
                    // 최대치가 오른 만큼 현재 체력도 같이 올린다(주머니 아이템과 같은 규칙).
                    if (stat != null && stat.Health != null && stat.Health.CurHP > 0f)
                        stat.Health.SetHP(stat.Health.CurHP + reward.value);
                }
                break;

            case AugmentRewardKind.Item:
                var itemSO = RewardProcessor.PickRandomItem(
                    GameManager.Instance != null ? GameManager.Instance.dataManager : null);
                // 상점 구매와 같은 규칙 — 이미 받은 보상이니 빈 칸이 있으면 곧장 주머니로, 꽉 찼을 때만 바닥으로.
                if (itemSO != null && (ItemPouch.Instance == null || !ItemPouch.Instance.TryAdd(itemSO)))
                    GroundItem.Drop(itemSO, player != null ? player.transform.position : (Vector3?)null);
                break;
        }

        Announce(reward.Describe());
        Debug.Log($"<color=green>[Augment]</color> 보상 지급: {reward.Describe()}");
    }

    // ── 잡동사니 ──────────────────────────────────────────────────────
    private static StatModifierRegistry PlayerMods()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        return player != null && player.Stat != null ? player.Stat.Mods : null;
    }

    private static void Announce(string msg)
    {
        var mgr = FloatingTextManager.Instance;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (mgr == null || player == null) return;
        var t = mgr.GetFromPool();
        if (t != null) t.SetUp(msg, Color.white, player.transform);
    }
}
