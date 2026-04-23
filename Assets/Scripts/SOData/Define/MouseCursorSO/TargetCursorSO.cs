using UnityEngine;

[CreateAssetMenu(fileName = "TargetCursorSO", menuName = "Scriptable Objects/MouseCursorSO/TargetCursorSO")]
public class TargetCursorSO : MouseCursorSO
{
    [SerializeField] private Material _material; // 외곽선용 머티리얼
    [SerializeField] private Color _highlightColor = new Color(2f, 2f, 2f, 1f); // 눈에 띄게 밝은 흰색 (Alpha는 반드시 1)
    
    private SpriteRenderer _currentSpr;
    private Material _originalMaterial;
    private Color _originalColor;

    public override void OnEffect(GameObject obj)
    {
        base.OnEffect(obj);
        
        if (_currentSpr != null) OutEffect();

        if (obj != null)
        {
            _currentSpr = obj.GetComponentInChildren<SpriteRenderer>();
            if (_currentSpr != null)
            {
                // 1. 원래 상태 저장 (머티리얼과 색상 모두)
                _originalMaterial = _currentSpr.material;
                _originalColor = _currentSpr.color;

                // 2. 하이라이트 적용
                _currentSpr.material = _material;
                
                // [핵심] 알파값은 1로 고정하여 투명해지는 것을 막고, RGB 값만 높여서 밝게 만듭니다.
                _currentSpr.color = _highlightColor;
            }
        }
        
        Cursor.SetCursor(cursorTexture, cursorHotspot, CursorMode.Auto);
    }

    public override void OutEffect()
    {
        if (_currentSpr != null)
        {
            // 3. 원래 상태로 복구
            _currentSpr.material = _originalMaterial;
            _currentSpr.color = _originalColor;
            
            _currentSpr = null;
            _originalMaterial = null;
        }
    }
}
