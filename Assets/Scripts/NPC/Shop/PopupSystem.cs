using UnityEngine;

/// <summary>
/// NPC 팝업 패널과 모든 IInteractable의 공용 F 아이콘을 제어합니다.
/// </summary>
public class PopupSystem : MonoBehaviour
{
    [Header("Popup Panel")]
    [SerializeField] private GameObject popupPanel;

    private string npcName;
    private static SpriteRenderer _interactionIcon;

    private void Awake()
    {
        npcName = GetComponent<NPCBase>().name;
    }

    // ─── 팝업 ────────────────────────────────────────────────────────
    public void ShowPopup()
    {
        if (popupPanel != null && !popupPanel.activeSelf) 
        {
            UIPopUpManager.Instance.PopUpUI(popupPanel);
            if(npcName != null) UIEventBus.NotifyOpen(npcName);
        }
    }

    public void HidePopup()
    {
        if (popupPanel != null && popupPanel.activeSelf) 
        {
            UIPopUpManager.Instance.ClosePopUpUI();
            GameManager.Instance.SetTimeStop(false);
            if(npcName != null) UIEventBus.NotifyClose(npcName);
        }
    }

    public bool IsOpen => popupPanel != null && popupPanel.activeSelf;

    // ─── 모든 IInteractable 공용 아이콘 ──────────────────────────────
    public static void ShowInteractionIcon(Collider2D target, float offset)
    {
        if (target == null) { HideInteractionIcon(); return; }
        if (!EnsureInteractionIcon()) return;

        _interactionIcon.gameObject.SetActive(true);
        _interactionIcon.transform.position = new Vector3(target.bounds.center.x,
                                                          target.bounds.max.y + offset,
                                                          target.transform.position.z);
    }

    public static void HideInteractionIcon()
    {
        if (_interactionIcon != null) _interactionIcon.gameObject.SetActive(false);
    }

    public static void ReleaseInteractionIcon()
    {
        if (_interactionIcon != null) Destroy(_interactionIcon.gameObject);
        _interactionIcon = null;
    }

    private static bool EnsureInteractionIcon()
    {
        if (_interactionIcon != null) return true;

        Sprite[] sprites = Resources.LoadAll<Sprite>("Sprites/Icon_F");
        if (sprites.Length == 0)
        {
            Debug.LogWarning("[PopupSystem] Resources/Sprites/Icon_F.png 스프라이트를 찾지 못했습니다.");
            return false;
        }

        var iconObject = new GameObject("Interaction F Icon");
        _interactionIcon = iconObject.AddComponent<SpriteRenderer>();
        _interactionIcon.sprite = sprites[0];
        _interactionIcon.sortingLayerName = "FlyingObject";
        _interactionIcon.sortingOrder = 10000;
        return true;
    }
}
