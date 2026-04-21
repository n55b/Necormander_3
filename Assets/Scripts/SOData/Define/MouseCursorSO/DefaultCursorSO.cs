using UnityEngine;

[CreateAssetMenu(fileName = "DefaultCursorSO", menuName = "Scriptable Objects/MouseCursorSO/DefaultCursorSO")]
public class DefaultCursorSO : MouseCursorSO
{
    public override void OnEffect(GameObject obj)
    {
        Cursor.SetCursor(cursorTexture, cursorHotspot, CursorMode.Auto);
    }
    public override void OutEffect()
    {
    }
}
