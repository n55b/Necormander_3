using UnityEngine;
using TMPro;

/// <summary>
/// NPC 팝업 UI 제어 (상호작용 아이콘 + 팝업 패널).
/// </summary>
public class PopupSystem : MonoBehaviour
{
    [Header("Popup Panel")]
    [SerializeField] private GameObject popupPanel;

    [Header("Interact Icon")]
    [SerializeField] public GameObject InetractIcon; // 기존 오타 유지 (Inspector 연결 호환)

    [Header("Prompt Text (선택)")]
    [SerializeField] private TextMeshProUGUI promptText;

    private string npcName;

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

    // ─── 아이콘 ──────────────────────────────────────────────────────
    public void ShowIcon(string prompt = "")
    {
        if (InetractIcon != null) InetractIcon.SetActive(true);
        if (promptText   != null) promptText.text = prompt;
    }

    public void HideIcon()
    {
        if (InetractIcon != null) InetractIcon.SetActive(false);
        if (promptText   != null) promptText.text = "";
    }
}
