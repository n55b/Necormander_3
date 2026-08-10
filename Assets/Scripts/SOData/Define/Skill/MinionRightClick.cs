using UnityEngine;

/// <summary>
/// 서브 소환수가 플레이어의 우클릭에 부여하는 행동. 서브 1마리당 1종.
/// </summary>
public enum MinionRightClickType
{
    /// <summary>장착된 서브가 없거나 아직 우클릭을 안 정한 상태. 우클릭이 아무것도 안 한다.</summary>
    None = 0,
    /// <summary>패링 — 날아오는 '원거리' 투사체를 반사한다. 반사체는 적을 때린다.</summary>
    Parry,
    /// <summary>카운터 — '근접' 공격을 무효화하고, 그 피해량만큼 때린 적에게 되돌린다.</summary>
    Counter,
    /// <summary>가드 — '근접' 공격을 무효화하고, 그 피해량만큼 보호막을 얻는다.</summary>
    Guard,
}

/// <summary>
/// 서브 소환수가 우클릭을 어떻게 바꾸는지 — 게임플레이 부분만.
///
/// 애니메이션은 여기 없다. 세 종류 모두 플레이어의 기존 Parry / Parry_Success 모션을 그대로 쓰고,
/// 종류를 구분하는 건 텔레그래프 부채꼴의 색(sectorColor)뿐이다. 서브 소환수는 실체화 경로가
/// 아예 없어서(필드에 나올 방법이 없다) 소환수 모션을 붙이려면 별도 작업이 필요하다.
///
/// 판정은 두 방향으로 갈린다:
///  · 패링       = 능동. 창이 열린 동안 매 프레임 주변에서 Projectile 을 찾는다.
///  · 카운터/가드 = 수동. 창이 열린 동안 나에게 들어오는 근접 피해를 기다린다.
/// </summary>
[System.Serializable]
public class MinionRightClick
{
    [Header("종류")]
    public MinionRightClickType type = MinionRightClickType.None;

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
             "이 부채꼴은 BaseHitBox 가 아니라서 중앙 팔레트(HitBoxColorConfigSO)를 타지 않는다 — 여기가 최종.")]
    public Color sectorColor = new Color(0.2f, 0.6f, 1f);

    [Header("카운터 전용")]
    [Tooltip("되돌리는 피해 = 막아낸 피해량 x 이 값. 반사 피해는 패링 갈래(DamageCategory.Parry)라 " +
             "플레이어의 치명타·물리 증폭을 그대로 탄다.")]
    public float reflectMultiplier = 1f;

    [Header("가드 전용")]
    [Tooltip("얻는 보호막 지속시간(초). 이미 보호막이 있으면 그것들의 남은 시간도 여기에 맞춰 갱신된다.")]
    public float shieldDuration = 5f;

    [Tooltip("보호막 총량 상한 = 최대 체력 x 이 비율. 상한을 넘긴 상태에서 성공하면 시간만 갱신된다.")]
    public float shieldMaxHpRatio = 0.5f;

    /// <summary>실제로 발동할 내용이 있는가.</summary>
    public bool IsValid => type != MinionRightClickType.None;

    /// <summary>이 종류가 '맞아주는' 쪽인가(수동 판정). 패링만 능동이다.</summary>
    public bool IsReactive => type == MinionRightClickType.Counter || type == MinionRightClickType.Guard;

    /// <summary>카드/툴팁 한 줄. 없으면 null.</summary>
    public string Describe() => type switch
    {
        MinionRightClickType.Parry   => "우클릭: 패링 — 원거리 공격을 반사",
        MinionRightClickType.Counter => "우클릭: 카운터 — 근접 공격을 무효화하고 되돌려줌",
        MinionRightClickType.Guard   => "우클릭: 가드 — 근접 공격을 무효화하고 보호막 획득",
        _ => null,
    };
}
