using UnityEngine;

/// <summary>
/// 플레이어가 상호작용할 수 있는 모든 오브젝트가 구현해야 하는 인터페이스.
/// NPC, 문, 아이템, 함정 등 어떤 오브젝트든 이 인터페이스를 구현하면
/// PlayerInteraction이 동일한 방식으로 처리합니다.
/// </summary>
public interface IInteractable
{
    /// <summary>범위 안에 들어왔을 때 표시할 프롬프트 (예: "E : 대화")</summary>
    string InteractionPrompt { get; }

    /// <summary>플레이어가 인터랙트 키를 눌렀을 때 호출됩니다.</summary>
    public bool Interact(GameObject interactor);

    /// <summary>플레이어가 범위 안에 들어와서 이 오브젝트가 현재 대상으로 선택됐을 때 호출됩니다.</summary>
    public void OnFocused(GameObject interactor);

    /// <summary>플레이어가 범위를 벗어나거나 다른 오브젝트로 포커스가 이동했을 때 호출됩니다.</summary>
    public void OnLostFocus(GameObject interactor);
}
