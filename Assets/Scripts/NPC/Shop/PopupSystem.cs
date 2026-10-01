using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
    private static readonly List<Graphic> _graphicBuffer = new List<Graphic>();
    private static readonly Vector3[] _corners = new Vector3[4];
    private const float UiIconGap = 0.05f; // 툴팁 위쪽 끝과 F 아이콘 아래쪽 끝 사이 간격(월드 유닛)

    /// <summary>
    /// F 아이콘을 대상 위에 띄운다. 대상 아래에 켜진 월드 스페이스 UI(이름 툴팁 등)가 있으면
    /// 그 UI의 바로 위 가운데에, 없으면 콜라이더 위 offset 지점에 놓는다.
    /// 그래서 어떤 상호작용 대상이든 [F] / [툴팁] 이 위아래 한 쌍으로 붙는다.
    /// 매 프레임 호출되므로 툴팁 크기(이름 길이, 분해 진행 줄)가 바뀌어도 따라간다.
    /// </summary>
    public static void ShowInteractionIcon(Collider2D target, float offset)
    {
        if (target == null) { HideInteractionIcon(); return; }
        if (!EnsureInteractionIcon()) return;

        _interactionIcon.gameObject.SetActive(true);

        Bounds col = target.bounds;
        float x = col.center.x;
        float y = col.max.y + offset;

        if (TryGetWorldUIBounds(target.transform, out Bounds ui))
        {
            // 아이콘 피벗이 중앙이 아니어도 아이콘의 '아래 끝'이 툴팁 위에 오도록 맞춘다.
            float pivotToBottom = _interactionIcon.transform.position.y - _interactionIcon.bounds.min.y;
            x = ui.center.x;
            y = Mathf.Max(ui.max.y, col.max.y) + UiIconGap + pivotToBottom;
        }

        _interactionIcon.transform.position = new Vector3(x, y, target.transform.position.z);
    }

    /// <summary>대상 아래에서 현재 보이는 월드 스페이스 UI 그래픽들을 감싸는 월드 bounds.</summary>
    private static bool TryGetWorldUIBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        root.GetComponentsInChildren(false, _graphicBuffer);
        foreach (var g in _graphicBuffer)
        {
            if (!g.enabled || g.color.a <= 0f) continue;
            var canvas = g.canvas;
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) continue;

            g.rectTransform.GetWorldCorners(_corners);
            for (int i = 0; i < 4; i++)
            {
                if (!found) { bounds = new Bounds(_corners[i], Vector3.zero); found = true; }
                else bounds.Encapsulate(_corners[i]);
            }
        }
        _graphicBuffer.Clear();
        return found;
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
