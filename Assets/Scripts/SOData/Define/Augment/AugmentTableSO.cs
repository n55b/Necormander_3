using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 페널티(증강) 등급. 숫자가 클수록 가혹하고, 보상도 그만큼 크다.
/// P0 은 '아무 페널티도 안 받는 선택지'라서 페널티 목록에는 절대 등장하지 않는다 — 보상만 있다.
/// </summary>
public enum AugmentTier { P0 = 0, P1 = 1, P2 = 2, P3 = 3 }

/// <summary>
/// 페널티가 실제로 무엇을 건드리는가. 표에 있던 17개 항목이 이 9종으로 전부 표현된다
/// (같은 종류에 값만 다른 것들이라 종류를 늘릴 이유가 없었다).
///
/// 새 페널티를 추가할 때 여기 없는 종류가 필요하면, 항목을 하나 늘리고
/// ActiveAugment 의 switch 두 곳(플레이어 보정 / 적 보정)에 케이스를 추가하면 된다.
/// </summary>
public enum AugmentPenaltyKind
{
    PlayerDamageTaken = 0,    // 플레이어가 받는 피해 +value%
    MinionCooldown = 1,       // 소환수(R) 쿨다운 +value%
    PlayerMoveSpeed = 2,      // 플레이어 이동속도 value% (음수로 적는다)
    PlayerBasicAttack = 3,    // 플레이어 평타 배율 value% (음수로 적는다)
    PlayerDashCooldown = 4,   // 대쉬 쿨다운 +value%
    EnemyMaxHp = 5,           // 적 최대 체력 +value%
    EnemyAttack = 6,          // 적 공격력 +value%
    EnemyCountPerWave = 7,    // 웨이브당 적 수 +value 마리
    EnemyCountEarlyWaves = 8, // 앞 waveLimit 웨이브 한정 적 수 +value 마리
}

/// <summary>보상 종류. Item 은 아이템 종류가 늘어날 때까지 테이블에서 빼둔다(AugmentTableSO.allowItemRewards).</summary>
public enum AugmentRewardKind { Gold = 0, HealPercent = 1, MaxHpFlat = 2, Item = 3 }

/// <summary>페널티 한 줄. 표의 (ID / 이름 / 대상 / 효과 / 표시 문구) 를 그대로 담는다.</summary>
[System.Serializable]
public class AugmentPenalty
{
    [Tooltip("표의 ID. 코드는 안 쓰고 에디터에서 찾기 편하라고 두는 것.")]
    public string id = "P1-01";
    public string displayName = "저린 육신";
    public AugmentTier tier = AugmentTier.P1;
    public AugmentPenaltyKind kind = AugmentPenaltyKind.PlayerDamageTaken;

    [Tooltip("퍼센트 계열은 %, 적 수 계열은 '마리'. 감소는 음수로 적는다(이동속도 -10 등).")]
    public float value = 25f;

    [Tooltip("EnemyCountEarlyWaves 전용. 앞에서 몇 웨이브까지 적용할지.")]
    public int waveLimit = 2;

    [Tooltip("카드에 뜨는 문구. 수치를 그대로 쓰지 않고 분위기로 알려준다.")]
    public string flavor = "상처가 조금 깊다";

    /// <summary>
    /// 카드에 같이 띄울 실제 수치. flavor("발이 조금 무겁다") 밑에 "이동속도 -10%" 로 붙는다.
    /// 표를 따로 적어두지 않고 kind + value 에서 만들어 낸다 — 손으로 적으면 수치를 고쳤을 때
    /// 문구만 옛날 값으로 남는 종류의 거짓말이 생긴다.
    /// </summary>
    public string Describe()
    {
        int v = Mathf.RoundToInt(value);
        string signed = v >= 0 ? $"+{v}" : v.ToString();
        return kind switch
        {
            AugmentPenaltyKind.PlayerDamageTaken   => $"받는 피해 {signed}%",
            AugmentPenaltyKind.MinionCooldown      => $"소환수 쿨다운 {signed}%",
            AugmentPenaltyKind.PlayerMoveSpeed     => $"이동속도 {signed}%",
            AugmentPenaltyKind.PlayerBasicAttack   => $"평타 배율 {signed}%",
            AugmentPenaltyKind.PlayerDashCooldown  => $"대쉬 쿨다운 {signed}%",
            AugmentPenaltyKind.EnemyMaxHp          => $"적 체력 {signed}%",
            AugmentPenaltyKind.EnemyAttack         => $"적 공격력 {signed}%",
            AugmentPenaltyKind.EnemyCountPerWave   => $"웨이브당 적 {signed}마리",
            AugmentPenaltyKind.EnemyCountEarlyWaves => $"첫 {waveLimit}웨이브 적 {signed}마리",
            _ => "",
        };
    }

    /// <summary>플레이어가 아니라 적 쪽을 강화하는 페널티인지. 카드 색/문구를 가를 때만 쓴다.</summary>
    public bool TargetsEnemy =>
        kind == AugmentPenaltyKind.EnemyMaxHp || kind == AugmentPenaltyKind.EnemyAttack ||
        kind == AugmentPenaltyKind.EnemyCountPerWave || kind == AugmentPenaltyKind.EnemyCountEarlyWaves;
}

/// <summary>보상 한 줄. 등급이 같은 것끼리 페널티와 무작위로 짝지어진다.</summary>
[System.Serializable]
public class AugmentReward
{
    public AugmentTier tier = AugmentTier.P1;
    public AugmentRewardKind kind = AugmentRewardKind.Gold;

    [Tooltip("Gold=골드, HealPercent=최대 체력의 %, MaxHpFlat=최대 체력 고정 증가. Item 은 값 무시.")]
    public float value = 15f;

    public string Describe() => kind switch
    {
        AugmentRewardKind.Gold => $"골드 +{Mathf.RoundToInt(value)}",
        AugmentRewardKind.HealPercent => $"체력 {Mathf.RoundToInt(value)}% 회복",
        AugmentRewardKind.MaxHpFlat => $"최대 체력 +{Mathf.RoundToInt(value)}",
        AugmentRewardKind.Item => "아이템 1개",
        _ => "",
    };
}

/// <summary>페널티 하나 + 보상 하나로 묶인 카드 한 장. penalty 가 null 이면 P0(무위험 선택지).</summary>
public class AugmentOffer
{
    public AugmentPenalty penalty;
    public AugmentReward reward;

    public AugmentTier Tier => penalty != null ? penalty.tier : AugmentTier.P0;
    public string Title => penalty != null ? penalty.displayName : "무위험";
    public string Flavor => penalty != null ? penalty.flavor : "아무것도 짊어지지 않는다";
    /// <summary>페널티의 실제 수치. P0 은 페널티가 없으니 빈 줄.</summary>
    public string EffectText => penalty != null ? penalty.Describe() : "";
    public string RewardText => reward != null ? reward.Describe() : "";
}

/// <summary>
/// 증강 선택 방의 전체 테이블. 페널티/보상 목록과 카드 굴리는 규칙이 전부 여기 들어 있다.
/// DataManager 의 인스펙터에 꽂아두면 ActiveAugment 가 알아서 읽어 간다.
/// </summary>
[CreateAssetMenu(fileName = "AugmentTable", menuName = "Necromancer/Augment/Augment Table")]
public class AugmentTableSO : ScriptableObject
{
    [Header("카드 굴리기 규칙")]
    [Tooltip("상단에 뜨는 카드 수. 하단 P0 버튼은 여기 안 센다.")]
    [Range(1, 6)] public int cardCount = 4;

    [Tooltip("켜면 카드 중 최소 한 장은 반드시 P3(가혹)이 나온다.")]
    public bool guaranteeP3 = true;

    [Tooltip("아이템 보상을 후보에 넣을지. 아이템 종류가 적어서 지금은 꺼둔다 — 로직은 이미 다 있다.")]
    public bool allowItemRewards = false;

    [Header("페널티 목록")]
    public List<AugmentPenalty> penalties = new List<AugmentPenalty>();

    [Header("보상 목록 (등급이 같은 페널티와 무작위로 묶인다)")]
    public List<AugmentReward> rewards = new List<AugmentReward>();

    /// <summary>등급 표기. 카드 상단에 그대로 찍힌다.</summary>
    public static string TierLabel(AugmentTier t) => t switch
    {
        AugmentTier.P0 => "P0 · 무위험",
        AugmentTier.P1 => "P1 · 경미",
        AugmentTier.P2 => "P2 · 중간",
        _ => "P3 · 가혹",
    };

    /// <summary>등급 색. 메서드 이름을 Color 로 두면 UnityEngine.Color 타입이 가려져 컴파일이 깨진다.</summary>
    public static Color TierColor(AugmentTier t) => t switch
    {
        AugmentTier.P0 => new Color(0.75f, 0.75f, 0.75f),
        AugmentTier.P1 => new Color(0.45f, 0.85f, 0.50f),
        AugmentTier.P2 => new Color(1.00f, 0.70f, 0.25f),
        _ => new Color(1.00f, 0.32f, 0.30f),
    };

    /// <summary>
    /// 이번 증강 방에 띄울 카드들을 굴린다. 같은 페널티가 두 장 나오지 않는다.
    /// guaranteeP3 면 첫 장은 무조건 P3, 나머지는 P1~P3 중 무작위.
    /// </summary>
    public List<AugmentOffer> RollOffers()
    {
        var pool = new List<AugmentPenalty>();
        foreach (var p in penalties)
            if (p != null && p.tier != AugmentTier.P0) pool.Add(p);

        var result = new List<AugmentOffer>();
        if (pool.Count == 0) return result;

        if (guaranteeP3)
        {
            var p3 = pool.FindAll(p => p.tier == AugmentTier.P3);
            if (p3.Count > 0)
            {
                var pick = p3[Random.Range(0, p3.Count)];
                pool.Remove(pick);
                result.Add(new AugmentOffer { penalty = pick, reward = RollReward(pick.tier) });
            }
        }

        while (result.Count < cardCount && pool.Count > 0)
        {
            var pick = pool[Random.Range(0, pool.Count)];
            pool.Remove(pick);
            result.Add(new AugmentOffer { penalty = pick, reward = RollReward(pick.tier) });
        }

        // P3 보장 카드가 항상 맨 앞에 앉으면 위치로 정답을 외우게 된다. 섞어서 내보낸다.
        for (int i = 0; i < result.Count; i++)
        {
            int j = Random.Range(i, result.Count);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    /// <summary>하단 버튼용 P0 선택지. 페널티 없이 P0 보상만 받는다.</summary>
    public AugmentOffer RollNoRiskOffer()
        => new AugmentOffer { penalty = null, reward = RollReward(AugmentTier.P0) };

    private AugmentReward RollReward(AugmentTier tier)
    {
        var candidates = new List<AugmentReward>();
        foreach (var r in rewards)
        {
            if (r == null || r.tier != tier) continue;
            if (r.kind == AugmentRewardKind.Item && !allowItemRewards) continue;
            candidates.Add(r);
        }
        return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
    }
}
