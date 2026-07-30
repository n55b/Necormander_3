using UnityEngine;

/// <summary>
/// F 를 '길게 눌러' 쓰는 두 번째 상호작용이 있는 오브젝트. IInteractable 과 같이 구현한다.
///
///   짧게 누르고 뗌 → IInteractable.Interact()  (기존 동작 그대로)
///   HoldSeconds 만큼 누름 → OnHoldComplete()   (그 뒤 손을 떼도 Interact 는 안 불린다)
///
/// 이 인터페이스가 없는 오브젝트(상점 NPC, 문, 상자 등)는 지금과 완전히 똑같이 동작한다 —
/// 누르는 순간 즉시 Interact 가 불리고, 길게 눌러도 아무 일도 안 생긴다.
/// 홀드 대상만 '떼는 순간' 판정으로 바뀐다(안 그러면 모든 상호작용이 손 뗄 때까지 밀린다).
///
/// 타이머는 PlayerController 가 굴린다 — 오브젝트마다 자기 타이머를 두면 포커스가 옮겨갔을 때
/// 리셋을 빠뜨리기 시작한다.
/// </summary>
public interface IHoldInteractable
{
    /// <summary>홀드 완료까지 필요한 시간(초). 0 이하면 홀드 기능이 꺼진다.</summary>
    float HoldSeconds { get; }

    /// <summary>홀드 진행률 0~1. 게이지 표시용. 취소되면 0 이 한 번 더 들어온다.</summary>
    void OnHoldProgress(float t01);

    /// <summary>홀드가 끝까지 찼을 때. 여기서 Interact 는 안 불린다.</summary>
    void OnHoldComplete(GameObject interactor);
}
