using UnityEngine;

public abstract class MouseCursorSO : ScriptableObject
{
    [SerializeField] protected Texture2D cursorTexture;
    [SerializeField] protected Vector2 cursorHotspot = Vector2.zero;

    public virtual void OnEffect(GameObject obj) {    }
    public virtual void OutEffect() {}
}
