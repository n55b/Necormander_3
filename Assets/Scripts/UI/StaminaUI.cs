using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class StaminaUI : MonoBehaviour
{
    private PlayerStamina _playerStamina;
    
    [SerializeField] private Image _fillImage;
    [SerializeField] private Image _backgroundImage;

    private Coroutine _flashCoroutine;
    private Sprite _generatedSprite;
    private Texture2D _generatedTexture;

    public void Initialize(PlayerStamina staminaSystem)
    {
        _playerStamina = staminaSystem;
        if (_playerStamina != null)
        {
            _playerStamina.OnStaminaChanged += HandleStaminaChanged;
            _playerStamina.OnStaminaInsufficient += HandleStaminaInsufficient;
        }

        CreateStaminaBarUI();
        HandleStaminaChanged(_playerStamina.CurrentStamina, _playerStamina.MaxStamina);
    }

    private void OnDestroy()
    {
        if (_playerStamina != null)
        {
            _playerStamina.OnStaminaChanged -= HandleStaminaChanged;
            _playerStamina.OnStaminaInsufficient -= HandleStaminaInsufficient;
        }

        if (_generatedSprite != null) Destroy(_generatedSprite);
        if (_generatedTexture != null) Destroy(_generatedTexture);
    }

    private void CreateStaminaBarUI()
    {
        // 프리팹 등을 통해 인스펙터에서 이미 할당되어 있다면 자동 생성 건너뛰기
        if (_fillImage != null && _backgroundImage != null) return;

        // 최상위 Canvas 찾기
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // 스태미나 바 부모 (배경) 생성
        GameObject bgObj = new GameObject("StaminaBar_BG");
        bgObj.transform.SetParent(canvas.transform, false);
        
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        // 우측 하단 고정
        bgRect.anchorMin = new Vector2(1f, 0f);
        bgRect.anchorMax = new Vector2(1f, 0f);
        bgRect.pivot = new Vector2(1f, 0f);
        bgRect.anchoredPosition = new Vector2(-50f, 50f);
        bgRect.sizeDelta = new Vector2(200f, 20f);

        _backgroundImage = bgObj.AddComponent<Image>();
        _backgroundImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

        // 스태미나 바 필(채우기) 생성
        GameObject fillObj = new GameObject("StaminaBar_Fill");
        fillObj.transform.SetParent(bgObj.transform, false);

        RectTransform fillRect = fillObj.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;

        _fillImage = fillObj.AddComponent<Image>();
        _fillImage.sprite = CreateWhiteSprite();
        _fillImage.color = new Color(0.2f, 0.8f, 0.2f, 1f); // 녹색 계열
        _fillImage.type = Image.Type.Filled;
        _fillImage.fillMethod = Image.FillMethod.Horizontal;
        _fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        _fillImage.fillAmount = 1f;
    }

    private void HandleStaminaChanged(float current, float max)
    {
        if (_fillImage != null)
        {
            if (current >= 0)
            {
                // 정상 상태: 녹색, 현재 스태미나 / 최대치
                _fillImage.color = new Color(0.2f, 0.8f, 0.2f, 1f);
                _fillImage.fillAmount = Mathf.Clamp01(current / max);
            }
            else
            {
                // 침식(음수) 상태: 보라색, 절대값(현재 스태미나) / 음수 한계치
                _fillImage.color = new Color(0.6f, 0.1f, 0.8f, 1f);
                
                float negLimit = _playerStamina != null ? _playerStamina.negativeLimit : 50f;
                if (negLimit > 0)
                {
                    _fillImage.fillAmount = Mathf.Clamp01(Mathf.Abs(current) / negLimit);
                }
                else
                {
                    _fillImage.fillAmount = 1f;
                }
            }
        }
    }

    private void HandleStaminaInsufficient()
    {
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        _flashCoroutine = StartCoroutine(FlashRedRoutine());
    }

    private IEnumerator FlashRedRoutine()
    {
        if (_fillImage == null || _backgroundImage == null) yield break;

        Color originalFill = _fillImage.color;
        Color originalBg = _backgroundImage.color;

        // 빨간색 깜빡임
        _fillImage.color = Color.red;
        _backgroundImage.color = new Color(0.5f, 0f, 0f, 0.8f);

        yield return new WaitForSeconds(0.1f);

        _fillImage.color = originalFill;
        _backgroundImage.color = originalBg;

        yield return new WaitForSeconds(0.1f);

        _fillImage.color = Color.red;
        _backgroundImage.color = new Color(0.5f, 0f, 0f, 0.8f);

        yield return new WaitForSeconds(0.1f);

        // 원래 색 복구 (스태미나 바 기본색)
        _fillImage.color = new Color(0.2f, 0.8f, 0.2f, 1f);
        _backgroundImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

        _flashCoroutine = null;
    }

    private Sprite CreateWhiteSprite()
    {
        _generatedTexture = new Texture2D(1, 1);
        _generatedTexture.SetPixel(0, 0, Color.white);
        _generatedTexture.Apply();
        _generatedSprite = Sprite.Create(_generatedTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _generatedSprite;
    }
}
