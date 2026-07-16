using UnityEngine;

/// <summary>
/// 소환수 스킬 애니메이션의 Animation Event 를 코드로 넘겨주는 중계기.
///
/// 이벤트 이름은 이 프로젝트의 기존 규약을 그대로 따른다 (BaseEntity 의 [애니메이션 작업자 가이드라인]).
/// 적 공격 클립(Warrior_AnimClip_Attack 등)이 쓰는 것과 같은 이름이다:
///   · OnHitEvent        — 타격 판정이 들어가야 하는 프레임. 2타면 2번 박으면 된다.
///   · OnAttackEndEvent  — 다 휘두르고 원래 자세로 돌아온 프레임(후딜 끝).
///
/// [저작하는 두 가지 방법]
/// 1) Aseprite 안에서: 해당 프레임의 셀(cel) user data 에 `event:OnHitEvent` 라고 적는다.
///    유니티 aseprite 임포터가 읽어서 그 프레임에 AnimationEvent 를 자동으로 심어준다
///    (AsepriteImporter.ExtractEventStringFromCells → AnimationClipGeneration.AddAnimationEvents).
///    그림을 고쳐 재임포트해도 유지된다 — aseprite 파일이 원본이기 때문이다.
/// 2) 클립을 독립 .anim 으로 복제한 뒤 Animation 창에서 직접 이벤트를 찍는다 (적들과 동일한 방식).
///    대신 aseprite 와의 연결이 끊겨서 그림을 고치면 손으로 다시 맞춰야 한다.
///
/// aseprite 가 만들어준 클립은 '임포트 결과물'이라 Animation 창에서 편집할 수 없다. 1번이 그 우회로다.
///
/// Animation Event 는 애니메이션되는 오브젝트에 붙은 컴포넌트에서 '이름으로' 메서드를 찾으므로,
/// 메서드 이름이 곧 이벤트 이름이다.
/// </summary>
public class MinionAnimEventRelay : MonoBehaviour
{
    /// <summary>타격 프레임마다 호출된다. 클립에 OnHitEvent 를 N 번 박으면 N 번 불린다.</summary>
    public System.Action OnHit;

    /// <summary>후딜까지 끝난 프레임. 없으면 시퀀스가 끝날 때까지 기다린다.</summary>
    public System.Action OnAttackEnd;

    /// <summary>클립의 `OnHitEvent` 가 이 메서드를 부른다.</summary>
    public void OnHitEvent() => OnHit?.Invoke();

    /// <summary>클립의 `OnAttackEndEvent` 가 이 메서드를 부른다.</summary>
    public void OnAttackEndEvent() => OnAttackEnd?.Invoke();
}
