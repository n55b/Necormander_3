using UnityEngine;

/// <summary>
/// 애니 페이즈 하나에 '위치'를 얹은 것. animSequence 의 특정 태그가 재생되는 '동안'
/// 미니언 인형을 offset 으로 이동시킨다 (진입 지점 ≠ 종료 지점 연출).
///
/// - tag    : 이동을 걸 태그 이름. animSequence 에 이 태그가 들어 있어야 발동한다.
///            코드는 특정 네이밍에 하드 의존하지 않는다 — 여기 적은 이름이 aseprite 태그와 같기만 하면 된다.
/// - offset : 이 페이즈가 '끝날 때' 인형의 위치(스폰 지점 기준 오프셋, 유닛). x 는 바라보는 방향에 맞춰 자동 반전
///            (오른쪽을 볼 때 기준으로 적으면 됨).
/// - snap   : true = 페이즈 시작 시 즉시 이동(순간이동), false = 이전 위치 → offset 부드럽게(lerp).
///
/// 여러 페이즈를 animSequence 순서대로 이으면 스폰 → o1 → o2 … 로 연쇄 이동한다.
/// movePhases 를 비우면 인형은 안 움직인다(= 기존 동작 그대로).
/// </summary>
[System.Serializable]
public class AnimPhase
{
    public string tag;
    public Vector2 offset;
    public bool snap = false;
}
