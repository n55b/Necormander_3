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

    // [26/07/17] attack 필드는 EnemyMinionDataSO 로 내려갔다.
    // 소환수(메인/서브)는 이제 플레이어의 ATK 를 그대로 가져다 쓰므로 자기 공격력이 없다.
    // 적만 자기 공격력이 필요해서 적 전용 데이터로 이사시켰다.

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
