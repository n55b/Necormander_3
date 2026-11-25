using UnityEngine;

[CreateAssetMenu(fileName = "TargetCursorSO", menuName = "Scriptable Objects/MouseCursorSO/TargetCursorSO")]
public class TargetCursorSO : MouseCursorSO
{
    [SerializeField] private Material _material; // 외곽선용 머티리얼
    
    private SpriteRenderer _currentSpr;
    private Material _originalMaterial;

    public override void OnEffect(GameObject obj)
    {
        base.OnEffect(obj);
        
        if (_currentSpr != null) OutEffect();

        if (obj != null)
        {
            _currentSpr = obj.GetComponentInChildren<SpriteRenderer>();
            if (_currentSpr != null)
            {
                _originalMaterial = _currentSpr.sharedMaterial;
                _currentSpr.sharedMaterial = _material;
            }
        }
        
        Cursor.SetCursor(cursorTexture, cursorHotspot, CursorMode.Auto);
    }

    public override void OutEffect()
    {
        if (_currentSpr != null && _currentSpr.sharedMaterial == _material)
            _currentSpr.sharedMaterial = _originalMaterial;
        _currentSpr = null;
        _originalMaterial = null;
    }
}
