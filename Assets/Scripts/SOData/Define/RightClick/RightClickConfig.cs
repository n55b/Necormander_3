using UnityEngine;

/// <summary>
/// 플레이어 우클릭의 종류. 한 번에 하나만 장착한다.
/// </summary>
public enum RightClickType
{
    /// <summary>우클릭이 아무것도 안 한다. 정상 플레이에선 나오지 않는다(기본값이 패링) —
    /// 나중에 '우클릭 봉인' 같은 디버프를 넣을 자리로 남겨둔다.</summary>
    None = 0,
    /// <summary>패링 — 날아오는 '원거리' 투사체를 반사한다. 반사체는 적을 때린다.</summary>
    Parry,
    /// <summary>카운터 — '근접' 공격을 무효화하고, 그 피해량만큼 때린 적에게 되돌린다. 범위 내 적은 경직.</summary>
    Counter,
    /// <summary>가드 — 꾹 누르는 방어 자세. 바라보는 방향에서 오는 피해를 근/원 가리지 않고 감소시킨다.</summary>
    Guard,
}

/// <summary>
/// 우클릭 하나의 게임플레이 수치. <see cref="RightClickDataSO"/> 에셋 안에 인라인으로 저작한다.
///
/// [26/08/15] 원래 이름은 MinionRightClick 이었다. 서브 소환수가 우클릭을 '부여'하는 구조였기
/// 때문인데, 서브 소환수가 삭제되면서 우클릭이 플레이어 본인의 영구 능력이 됐다. 소환수와 아무
/// 관계가 없어져서 이름에서 Minion 을 뺐다.
///
/// 애니메이션은 여기 없다. 세 종류 모두 플레이어의 기존 Parry / Parry_Success 모션을 그대로 쓰고,
/// 종류를 구분하는 건 텔레그래프 부채꼴의 색(sectorColor)뿐이다.
///
/// 판정은 두 방향으로 갈린다:
///  · 패링       = 능동. 창이 열린 동안 매 프레임 주변에서 Projectile 을 찾는다.
///  · 카운터/가드 = 수동. 창이 열린 동안 나에게 들어오는 근접 피해를 기다린다.
/// </summary>
[System.Serializable]
public class RightClickConfig
{
    [Header("종류")]
    public RightClickType type = RightClickType.None;

    [Header("타이밍")]
    [Tooltip("판정이 열려 있는 시간(초). 이 안에 조건이 맞아야 성공. 밸런스 조정 대상.\n" +
             "카운터/가드는 '한 프레임짜리 피격'을 받아내는 거라 패링(투사체가 반경 안에 여러 프레임 " +
             "머무름)보다 훨씬 넉넉해야 한다 — 옛 패링값 0.05는 60fps에서 3프레임이라 사실상 예측 게임이었다.")]
    public float activeDuration = 0.25f;

    [Tooltip("실패했을 때의 후딜(초). 성공하면 이 딜레이 없이 즉시 다시 쓸 수 있다.\n" +
             "이 시간 동안은 우클릭이 통째로 씹히므로, 크게 잡으면 '연타로 만회'가 불가능해진다.")]
    public float recoveryDuration = 0.3f;

    [Tooltip("우클릭 중 이동 속도 배율. 1 이면 감속 없음.")]
    public float moveSpeedMultiplier = 0.3f;

    [Header("범위")]
    [Tooltip("판정 반경(유닛). 패링은 이 안의 투사체를, 카운터/가드는 이 안의 공격자를 받는다.")]
    public float radius = 2.5f;

    [Tooltip("조준 방향 기준 판정 각도(도). 360 이면 전방위 — 뒤에서 맞아도 받아친다.")]
    public float angle = 160f;

    [Header("연출")]
    [Tooltip("텔레그래프 부채꼴 색. 테두리는 이 색에서 알파만 올려 파생한다. " +
             "이 부채꼴은 BaseHitBox 가 아니라서 중앙 팔레트(HitBoxColorConfigSO)를 타지 않는다 — 여기가 최종.\n" +
             "아이콘 스프라이트를 안 지정하면 이 색으로 임시 아이콘을 만들어 쓴다(RightClickIconFactory).")]
    public Color sectorColor = new Color(0.2f, 0.6f, 1f);

    [Header("카운터 전용")]
    [Tooltip("되돌리는 피해 = 막아낸 피해량 x 이 값. 반사 피해는 패링 갈래(DamageCategory.Parry)라 " +
             "플레이어의 치명타·물리 증폭을 그대로 탄다.")]
    public float reflectMultiplier = 1f;

    [Tooltip("성공 시 판정 반경 안의 모든 적에게 거는 기절 시간(초). 0 이면 안 건다.\n" +
             "되받아친 그 적뿐 아니라 범위 전체가 대상이다.")]
    public float stunDuration = 0f;

    [Header("가드 전용")]
    [Tooltip("바라보는 방향에서 오는 피해 감소율. 0.5 = 50% 감소. 근거리/원거리를 가리지 않는다.\n" +
             "가드는 판정창이 아니라 '꾹 누르고 있는 동안' 유지되는 자세다 — activeDuration/" +
             "recoveryDuration 은 가드에선 쓰이지 않는다.")]
    [Range(0f, 1f)] public float damageReduction = 0.5f;

    /// <summary>실제로 발동할 내용이 있는가.</summary>
    public bool IsValid => type != RightClickType.None;

    /// <summary>꾹 눌러서 유지하는 자세인가(가드). 나머지 둘은 한 번 누르면 끝나는 판정창이다.</summary>
    public bool IsHold => type == RightClickType.Guard;

    /// <summary>카드/툴팁 한 줄. 없으면 null.</summary>
    public string Describe() => type switch
    {
        RightClickType.Parry   => "우클릭: 패링 — 원거리 공격을 반사",
        RightClickType.Counter => "우클릭: 카운터 — 근접 공격을 무효화하고 되돌려줌",
        RightClickType.Guard   => $"우클릭(홀드): 가드 — 바라보는 방향의 피해 {damageReduction * 100f:0}% 감소",
        _ => null,
    };
}
