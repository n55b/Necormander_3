using UnityEngine;

/// <summary>
/// 미니언(소환수/적 공용)의 공통 마스터 데이터.
/// 직접 만들 수 없다 — 반드시 MainMinionDataSO / SubMinionDataSO / EnemyMinionDataSO 중 하나여야 한다.
/// 역할은 타입이 곧 역할이므로 role 필드는 없다.
/// </summary>
public abstract class MinionDataSO : ScriptableObject
{
    [Header("Basic Information")]
    public CommandData minionType;
    public string minionName;
    public Sprite minionIcon;   // 대가리만 달린 이미지

    [Header("기본 능력치")]
    [Tooltip("소환수는 이 값에 스킬 배수를 곱해 피해를 낸다. 적은 CharacterStat 이 그대로 쓴다.")]
    public float attack = 10f;

    [Header("UI & Reward Settings")]
    public int shopCost = 150;
    public GrowthItemData rewardItemData;

    // ── 카드/툴팁 표시 ────────────────────────────────────────────────
    // UI 가 minionSkill 을 직접 헤집으면 서브 소환수(액티브 없음)에서 전부 빈칸이 된다.
    // 무엇을 보여줄지는 각 타입이 스스로 답한다.

    /// <summary>카드/툴팁 제목.</summary>
    public virtual string ResolveTitle() => minionName;

    /// <summary>카드/툴팁 아이콘.</summary>
    public virtual Sprite ResolveIcon() => minionIcon;

    /// <summary>
    /// 카드/툴팁 설명. rewardItemData.description 을 채우면 그게 항상 우선한다(수동 오버라이드).
    /// 비워두면 파생 타입이 알아서 만들어낸다.
    /// </summary>
    public virtual string ResolveDescription()
        => (rewardItemData != null && !string.IsNullOrEmpty(rewardItemData.description))
            ? rewardItemData.description
            : null;
}
