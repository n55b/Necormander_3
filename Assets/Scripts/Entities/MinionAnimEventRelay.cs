using UnityEngine;

/// <summary>
/// 소환수 애니메이션 클립의 Animation Event 를 코드로 넘겨주는 중계기.
///
/// [Aseprite 에서 저작하는 법]
/// 타격 프레임의 셀(cel)을 선택하고 user data 에 `event:MinionHit` 이라고 적으면 끝이다.
/// 유니티의 aseprite 임포터가 그걸 읽어서 그 프레임에 AnimationEvent(functionName = "MinionHit")
/// 를 자동으로 심어준다 (AsepriteImporter.ExtractEventStringFromCells → AnimationClipGeneration.AddAnimationEvents).
/// 재임포트해도 계속 유지된다 — aseprite 파일이 원본이기 때문이다.
///
/// Animation Event 는 애니메이션되는 오브젝트에 붙은 컴포넌트에서 '이름으로' 메서드를 찾는다.
/// 그래서 이벤트 이름은 아래 메서드 이름 하나로 고정이다: MinionHit.
/// </summary>
public class MinionAnimEventRelay : MonoBehaviour
{
    /// <summary>타격 프레임에 도달했을 때. 없으면 아무 일도 안 일어난다(에러 아님).</summary>
    public System.Action OnHit;

    /// <summary>Aseprite 셀 user data 의 `event:MinionHit` 이 이 메서드를 부른다.</summary>
    public void MinionHit() => OnHit?.Invoke();
}
