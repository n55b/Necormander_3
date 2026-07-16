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

    [Header("타이밍 — '초'를 갖는 건 castDuration 하나뿐")]
    [Tooltip("마무리 일격 전체 시전 시간(초). animSequence 전체가 이 길이에 정확히 맞도록 재생 속도가 조절된다. " +
             "나중에 공속 등으로 시전이 빨라지면 이 값만 줄이면 애니메이션과 타격 시점이 같이 따라온다.")]
    public float castDuration = 0.9f;

    [Tooltip("순서대로 재생할 Aseprite 태그. 예: Start, Slash, End / " +
             "aseprite 임포터는 태그마다 상태를 만들어놓고 트랜지션을 하나도 안 걸기 때문에, " +
             "여기 적지 않은 태그는 영원히 재생되지 않는다.")]
    public string[] animSequence;

    [Tooltip("이 태그가 재생되는 '동안'만 판정이 열린다. 예: Slash / " +
             "타격 타이밍을 초나 비율로 박지 않는 이유 — 태그 경계가 이미 그림에 찍힌 마커다. " +
             "아티스트가 Start 길이를 바꿔도 판정이 알아서 따라온다.")]
    public string damageState = "";

    [Tooltip("태그로 준비/타격이 안 나뉘는 경우에 쓴다(예: DashDoll 은 Attack 하나에 다 들어있음). " +
             "Aseprite 에서 타격 프레임 셀의 user data 에 `event:OnHitEvent` 을 적으면 임포터가 " +
             "그 프레임에 이벤트를 심어준다. 여기에 OnHitEvent 를 적으면 그 순간 판정이 열린다. " +
             "비워두면 damageState(태그) 방식을 쓴다. 적으면 이쪽이 우선.")]
    public string hitEvent = "";

    [Range(0f, 1f)]
    [Tooltip("hitEvent 방식일 때만 사용. 판정이 열려 있는 시간(전체 시전 시간 대비 비율).")]
    public float hitWindowRatio = 0.15f;

    [Header("연출")]
    [Tooltip("마무리 일격 시 재생할 소환수 비주얼. 비워두면 소환수가 안 보인다.")]
    public GameObject visual;

    [Tooltip("위 시퀀스와 동시에 겹쳐 재생할 이펙트 태그. 비우면 없음. (예: DashDoll 의 Effect)")]
    public string effectState = "";

    [Tooltip("적에게 경직을 주는지.")]
    public bool causesHitstun = true;

    [Tooltip("넉백 세기. 0 이면 없음.")]
    public float knockbackForce = 4f;

    [Tooltip("슈퍼아머 삭감량.")]
    public float superArmorDamage = 30f;

    /// <summary>실제로 발동할 내용이 있는가.</summary>
    public bool IsValid => hitCount > 0;

    /// <summary>hitEvent 방식일 때 판정이 열려 있는 시간(초).</summary>
    public float EventHitWindow => Mathf.Max(0.02f, castDuration * Mathf.Clamp01(hitWindowRatio));
}
