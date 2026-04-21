using UnityEngine;

// 던질 때 빼고 마우스 커서는 동일
public enum CursorType { Default, Targeting, Throw }

public class MouseManager : MonoBehaviour
{
    private MouseCursorManager cursorManager;

    [Header(" [ Layer Mask ] ")]
    [SerializeField] LayerMask allyLayer;

    [Header(" [ Settings ] ")]
    [SerializeField] float raycastDistance = 100f;
    [SerializeField] CursorType currentType = CursorType.Default;

    [Header(" [ Under The Mouse ] ")]
    [SerializeField] GameObject hoverObject;

    public GameObject HoverObject => hoverObject;

    private void Awake()
    {
        cursorManager = GetComponent<MouseCursorManager>();
    }

    private void Update()
    {
        DetectObjectUnderMouse();
    }

    private void DetectObjectUnderMouse()
    {
        CursorType newType = CursorType.Default;
        GameObject newHoverObject = null;

        // 1. 소환 모드 우선 체크
        if (GameManager.Instance.PLAYERCONTROLLER.SUMCONTROLLER.IsSummoningMode)
        {
            newType = CursorType.Throw;
        }
        else
        {
            // 2. 2D 마우스 위치 레이캐스트
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero, 0f, allyLayer);

            if (hit.collider != null)
            {
                newType = CursorType.Targeting;
                newHoverObject = hit.transform.gameObject;
            }
        }

        // 3. 상태 변화가 있을 때만 업데이트
        if (newType != currentType || newHoverObject != hoverObject)
        {
            currentType = newType;
            hoverObject = newHoverObject;
            cursorManager.UpdateCursor(currentType, hoverObject);
        }
    }
}
