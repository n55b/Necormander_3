using UnityEngine;

/// <summary>
/// 플레이어가 상호작용할 수 있는 모든 오브젝트가 구현해야 하는 인터페이스입니다.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// 상호작용이 가능한 상태일 때 표시될 프롬프트 텍스트입니다. (예: "Press Q to Open")
    /// </summary>
    string InteractionPrompt { get; }

    /// <summary>
    /// 상호작용을 실행합니다.
    /// </summary>
    /// <returns>상호작용에 성공하면 true를 반환합니다.</returns>
    bool Interact(GameObject interactor);
}
