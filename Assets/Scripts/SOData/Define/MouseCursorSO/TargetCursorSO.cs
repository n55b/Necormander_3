using UnityEngine;

[CreateAssetMenu(fileName = "TargetCursorSO", menuName = "Scriptable Objects/MouseCursorSO/TargetCursorSO")]
public class TargetCursorSO : MouseCursorSO
{
    [SerializeField] private Material _material;
    [SerializeField] private Material _basicMaterial;
    [SerializeField] private SpriteRenderer _spr;

     public override void OnEffect(GameObject obj)
    {
        base.OnEffect(obj);
        SpriteRenderer spr = obj.GetComponentInChildren<SpriteRenderer>();
        _spr = spr;
        spr.material = _material;
        Cursor.SetCursor(cursorTexture, cursorHotspot, CursorMode.Auto);
    }

    public override void OutEffect()
    {
        _spr.material = _basicMaterial;
    }
}
