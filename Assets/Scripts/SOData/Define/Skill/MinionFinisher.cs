using UnityEngine;

/// <summary>
/// 메인 소환수가 플레이어 평타 콤보의 마지막에 넣는 마무리 일격.
///
/// 설계 3.3: "플레이어 기본 공격 콤보 회수 + 1을 하여 마지막에 소환수의 마무리 일격이 발동
/// (평타 2타로 줄여주셈)". 즉 메인 소환수가 없으면 앞 2타만 빠르게 반복하고, 있으면
/// 3타 타이밍에 이것이 대신 나간다. 이때 플레이어는 아무것도 하지 않고 Idle 로 있는다.
///
/// [타이밍 규칙] '초'를 갖는 건 castDuration 하나뿐이고 나머지는 전부 그 비율이다.
/// 애니메이션 재생 속도도 castDuration 에 맞춰 스케일된다. 그래서 나중에 공속 같은 걸로
/// 시전이 빨라지면 castDuration 만 줄이면 애니메이션과 타격 시점이 같은 비율로 따라온다.
/// 타격 시점을 초로 박아두면 그때 애니만 빨라지고 타격은 제자리에 남아 다시 어긋난다.
/// </summary>
[System.Serializable]
public class MinionFinisher
{
    [Header("피해")]
    [Tooltip("몇 번 때릴지. 0 이면 마무리 일격이 없는 것으로 치고 콤보가 2타로 끝난다.")]
    public int hitCount = 1;

    [Tooltip("타당 피해 = 소환수 ATK x 이 값.")]
    public float damageMultiplier = 1f;

    [Header("범위")]
    [Tooltip("히트박스 크기(유닛). x = 사거리, y = 폭.")]
    public Vector2 hitBoxSize = new Vector2(3f, 2f);

    [Tooltip("조준 방향으로 소환수를 얼마나 밀지. 소환수 스프라이트는 좌우로만 뒤집히므로, " +
             "대각선 조준은 히트박스를 기울이는 대신 소환 위치를 그쪽으로 밀어서 맞춘다.")]
    public float spawnOffset = 1f;

    [Header("타이밍 (초는 castDuration 하나뿐)")]
    [Tooltip("마무리 일격 전체 시전 시간(초). 애니메이션이 이 길이에 정확히 맞도록 재생 속도가 조절된다.")]
    public float castDuration = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("시전 시간 대비 타격이 시작되는 지점. 애니메이션의 임팩트 프레임에 맞춘다.")]
    public float hitStartRatio = 0.4f;

    [Range(0f, 1f)]
    [Tooltip("시전 시간 대비 타격이 끝나는 지점. hitCount 가 이 구간 안에 균등 배분된다.")]
    public float hitEndRatio = 0.7f;

    [Header("연출")]
    [Tooltip("마무리 일격 시 재생할 소환수 비주얼. 비워두면 소환수가 안 보인다.")]
    public GameObject visual;

    [Tooltip("visual 에서 재생할 애니메이터 상태 이름. 비우면 기본 상태. (예: MeleeDoll=Slash, DashDoll=Attack)")]
    public string animState = "";

    [Tooltip("동시에 겹쳐 재생할 이펙트 상태 이름. 비우면 없음. (예: DashDoll=Effect)")]
    public string effectState = "";

    [Tooltip("적에게 경직을 주는지.")]
    public bool causesHitstun = true;

    [Tooltip("넉백 세기. 0 이면 없음.")]
    public float knockbackForce = 4f;

    [Tooltip("슈퍼아머 삭감량.")]
    public float superArmorDamage = 30f;

    /// <summary>실제로 발동할 내용이 있는가.</summary>
    public bool IsValid => hitCount > 0;

    /// <summary>타격이 시작되기까지의 지연(초). castDuration 이 줄면 같이 준다.</summary>
    public float HitDelay => castDuration * Mathf.Clamp01(hitStartRatio);

    /// <summary>타격이 유지되는 시간(초). 다단히트면 이 안에 균등 배분된다.</summary>
    public float HitWindow => Mathf.Max(0.02f, castDuration * Mathf.Clamp01(hitEndRatio - hitStartRatio));
}
