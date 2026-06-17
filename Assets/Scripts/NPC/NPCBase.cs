using UnityEngine;

/// <summary>
/// 모든 NPC의 베이스 클래스. IInteractable을 구현합니다.
///
/// PlayerController.CheckForInteractable()이 매 프레임 OverlapCircle로 감지하고
/// OnFocused / OnLostFocus / Interact를 직접 호출합니다.
/// → Trigger 콜라이더 불필요. NPC에는 IInteractable 감지용 CircleCollider2D만 남겨두면 됩니다.
///
/// 하위 클래스 사용법:
///   public class ShopNPC : NPCBase
///   {
///       public override string InteractionPrompt => "E : 상점 열기";
///       public override bool Interact(GameObject interactor) { ... return true; }
///   }
/// </summary>
public class NPCBase : MonoBehaviour, IInteractable
{
    [Header("Popup UI System")]
    [SerializeField] protected PopupSystem popupSystem;

    [Header("NPC UI Name")]
    [SerializeField] public string name = "";

    // ─── IInteractable ────────────────────────────────────────────────
    public virtual string InteractionPrompt => "F : 상호작용";

    /// <summary>
    /// 플레이어가 E키를 눌렀을 때 호출됩니다.
    /// 서브클래스에서 override해서 대화/상점/스킬세팅 등 원하는 동작을 구현하세요.
    /// </summary>
    public virtual bool Interact(GameObject interactor)
    {
        popupSystem?.ShowPopup();
        return true;
    }

    /// <summary>
    /// PlayerController가 이 NPC를 가장 가까운 대상으로 선택했을 때 호출됩니다.
    /// 기본 동작: 상호작용 아이콘 표시
    /// </summary>
    public virtual void OnFocused(GameObject interactor)
    {
        popupSystem?.ShowIcon(InteractionPrompt);
    }

    /// <summary>
    /// 플레이어가 범위를 벗어나거나 더 가까운 다른 대상이 생겼을 때 호출됩니다.
    /// 기본 동작: 아이콘 숨김 + 팝업 닫기
    /// </summary>
    public virtual void OnLostFocus(GameObject interactor)
    {
        popupSystem?.HideIcon();
        popupSystem?.HidePopup();
    }

    // ─── 기존 호환 ────────────────────────────────────────────────────
    /// <summary>기존 코드에서 InteractNPC()를 직접 호출하던 곳과의 호환.</summary>
    public void InteractNPC() => Interact(null);
}
