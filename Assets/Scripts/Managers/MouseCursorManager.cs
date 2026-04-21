using UnityEngine;

public class MouseCursorManager : MonoBehaviour
{
    [Header(" [ Cursor SO ] ")]
    [SerializeField] MouseCursorSO defaultCursorSO;
    [SerializeField] MouseCursorSO TargetCursorSO;
    [SerializeField] MouseCursorSO ThrowCursorSO;
    [SerializeField] MouseCursorSO currentCursorSO;

    private void Awake()
    {
        currentCursorSO = defaultCursorSO;
    }

    public void UpdateCursor(CursorType cursorType, GameObject obj)
    {
        switch (cursorType)
        {
            case CursorType.Default:
                ChangeCursorSO(defaultCursorSO, obj);
                break;
            case CursorType.Targeting:
                ChangeCursorSO(TargetCursorSO, obj);
                break;
            case CursorType.Throw:
                ChangeCursorSO(ThrowCursorSO, obj);
                break;
        }
    }

    private void ChangeCursorSO(MouseCursorSO _type, GameObject _obj)
    {
        if (currentCursorSO != null) currentCursorSO.OutEffect();
        currentCursorSO = _type;
        if (currentCursorSO != null) currentCursorSO.OnEffect(_obj);
    }
}
