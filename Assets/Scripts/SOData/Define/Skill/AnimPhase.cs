using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 애니 페이즈 하나에 '위치'를 얹은 것. animSequence 의 특정 태그가 재생되는 '동안'
/// 미니언 인형을 offset 으로 이동시킨다 (진입 지점 ≠ 종료 지점 연출).
///
/// - tag    : 이동을 걸 태그 이름. 이게 곧 '재생할 클립'이기도 하다(MinionAnimSet.sequence 항목).
///            코드는 특정 네이밍에 하드 의존하지 않는다 — 여기 적은 이름이 aseprite 태그와 같기만 하면 된다.
/// - offset : 이 페이즈가 '끝날 때' 인형의 위치(스폰 지점 기준 오프셋, 유닛). x 는 바라보는 방향에 맞춰 자동 반전
///            (오른쪽을 볼 때 기준으로 적으면 됨). 0 이면 이 태그에선 안 움직인다(= 순수 재생).
/// - snap   : true = 페이즈 시작 시 즉시 이동(순간이동), false = 이전 위치 → offset 부드럽게(lerp).
///
/// 여러 페이즈를 순서대로 이으면 스폰 → o1 → o2 … 로 연쇄 이동한다.
/// </summary>
[System.Serializable]
public class AnimPhase
{
    public string tag;
    public Vector2 offset;
    public bool snap = false;
}

/// <summary>
/// 미니언 한 '행동'(스페이스바 스킬 / 평타 마무리)의 애니메이션 설정 한 벌.
///
/// [왜 여기 있나] 예전엔 이 필드들이 MinionSkillSO(스킬)와 MinionFinisher(마무리)에 '똑같이 중복'돼
/// 있었다. 이제 미니언(MainMinionDataSO)이 skillAnim / basicAnim 두 벌을 들고, 스킬·마무리 로직은
/// 여기서 읽는다 → 기획자가 애니메이션을 미니언 한 곳에서 전부 설정한다.
///
/// [sequence] 재생할 태그 순서 + (태그별 선택적) 위치 이동을 '한 리스트'에 담는다.
/// 예전의 animSequence(string[]) + movePhases(별도 리스트, 문자열 매칭)를 합친 것 — 태그 이름을
/// 두 군데 적을 필요가 없어졌다. 1개면 그거 한 번, 여러 개면 순서대로 연속 재생.
/// aseprite 임포터는 태그마다 상태를 만들고 트랜지션을 하나도 안 걸어, 여기 안 적은 태그는 영원히 안 나온다.
/// </summary>
[System.Serializable]
public class MinionAnimSet
{
    [Tooltip("재생할 aseprite 프리팹(애니메이터 포함). 비우면 인형은 안 보이고 히트박스만 나간다.")]
    public GameObject visual;

    [Tooltip("전체 시전 시간(초). 애니 전체가 이 길이에 정확히 맞도록 재생 속도가 조절된다. 속도 손잡이. " +
             "0 이면 1초로 친다. 공속 등으로 시전이 빨라지면 이 값만 줄이면 애니와 타격이 같이 따라온다.")]
    public float duration = 0f;

    [Tooltip("재생할 태그 순서 + (선택) 태그별 이동. 1개=한 번, 여러 개=순서대로 연속. " +
             "여기 안 적은 태그는 영원히 재생되지 않는다. 각 항목 offset 이 0 이면 안 움직이고(순수 재생), " +
             "값을 주면 그 태그가 재생되는 '동안' 그 위치로 이동한다(snap 체크 시 즉시).")]
    public List<AnimPhase> sequence = new List<AnimPhase>();

    [Tooltip("동시에 '겹쳐' 재생할 이펙트 태그 — 타이밍이 아니라 연출 레이어다. 코드가 visual 을 한 벌 더 복제해 " +
             "본체 뒤에 깔고 시전시간에 맞춰 같이 굴린다. 예: DashDoll 의 Skill_Attack_Effect(본체 Skill_Attack 과 겹쳐 재생). " +
             "숨은 기능: OnHitEvent 를 본체가 아니라 이 이펙트 클립에 박아도 타격 타이밍으로 인식한다. 이펙트 레이어 없으면 비움.")]
    public string effectState = "";

    [Tooltip("판정을 열 '타이밍'을 태그로 지정 — 이 태그가 재생되는 '동안'만 히트박스가 열린다. " +
             "예: MeleeDoll 이 Start(준비)/Slash(때리는 중)/End(마무리)면 Slash 를 적는다(준비·마무리엔 안 맞음). " +
             "★폴백용★ — hitEvent 를 클립에 실제로 박아뒀으면 그게 이기고 이 값은 무시된다. 이벤트를 아직 안 박았을 때만 쓴다. " +
             "hitEvent·damageState 둘 다 비면 시퀀스 전체를 판정창으로 친다(부정확, 경고 뜸).")]
    public string damageState = "";

    [Tooltip("(이벤트 방식, 권장) 보통 OnHitEvent. Aseprite 타격 프레임 셀 user data 에 `event:OnHitEvent` 를 " +
             "박아야 실제로 동작한다. OnHitEvent 하나 = 1타(2타면 2번 박음). 없으면 damageState(태그) 방식으로 폴백. 적으면 이쪽이 우선.")]
    public string hitEvent = "";

    [Range(0f, 1f)]
    [Tooltip("판정 창 길이 = 전체 시전시간(duration) × 이 비율. 예: 1s × 0.15 = 0.15초. " +
             "★대부분 안 쓰인다★: OnHitEvent 마다 1타(단발)면 이 값 무관. OnHitEvent+OnAttackEndEvent 둘 다 박으면 " +
             "그 '두 이벤트 사이 실제 간격'을 창으로 쓴다. 이 비율이 실제로 쓰이는 건 애니메이터/컨트롤러가 없는 " +
             "정적·임시 미니언일 때뿐(그림에 타이밍 정보가 없어서 이 길이만큼 판정을 연다. 다단히트면 그 창에 hitCount 균등 배분). " +
             "애니가 제대로 박힌 미니언이면 신경 안 써도 됨.")]
    public float hitWindowRatio = 0.15f;

    /// <summary>0 이면 1초로 친다(기존 skillAnimDuration / castDuration 폴백과 동일).</summary>
    public float ResolvedDuration => duration > 0f ? duration : 1f;

    /// <summary>hitEvent 방식일 때 판정이 열려 있는 시간(초).</summary>
    public float EventHitWindow => Mathf.Max(0.02f, ResolvedDuration * Mathf.Clamp01(hitWindowRatio));

    /// <summary>
    /// 시퀀스의 태그 이름만 순서대로 뽑아 배열로. 캐스터 내부 헬퍼들이 string[] 를 받기 때문.
    /// 빈/공백 태그 항목은 건너뛴다 — 인스펙터에서 리스트에 항목만 추가하고 태그를 안 적은 '빈 칸'이
    /// 끼어도, 그게 '태그 하나짜리 시퀀스'로 취급돼 자동 클립 선택 폴백을 막는 일이 없도록.
    /// (빈 칸만 있으면 결과가 빈 배열 → 캐스터가 OnHitEvent 클립을 자동으로 골라 재생하고 relay 를 붙인다.)
    /// </summary>
    public string[] SequenceTags()
    {
        if (sequence == null || sequence.Count == 0) return System.Array.Empty<string>();
        var list = new List<string>(sequence.Count);
        foreach (var p in sequence)
            if (p != null && !string.IsNullOrWhiteSpace(p.tag)) list.Add(p.tag);
        return list.ToArray();
    }
}
