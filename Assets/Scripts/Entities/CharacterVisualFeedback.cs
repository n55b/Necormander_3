using UnityEngine;
using System.Collections;

/// <summary>
/// 유닛의 피격 반짝임, VFX(보호막 등) 시각적 피드백을 담당하는 컴포넌트입니다.
/// </summary>
public class CharacterVisualFeedback : MonoBehaviour
{
    private CharacterHealth _health;
    private CharacterStatus _status;
    private SpriteRenderer _sr;
    private Color _originalColor;
    private Coroutine _flashCoroutine;

    private GameObject _shieldVFXInstance;
    private GameObject _ccVFXInstance;

    public Color OriginalColor => _originalColor;

    public void Init(CharacterHealth health, CharacterStatus status)
    {
        _health = health;
        _status = status;
        
        // [개선] 이름을 사용하지 않고 계층 구조를 횡단하여 SpriteRenderer 탐색
        // 1. 현재 컴포넌트에서 시작
        _sr = GetComponent<SpriteRenderer>();

        // 2. 찾지 못했다면 최상위 부모(Root)를 찾은 후 그 아래의 모든 자식 탐색
        // (이 방식은 CharacterStatStuff와 Visual이 형제 관계여도 Root를 통해 찾을 수 있게 해줍니다)
        if (_sr == null)
        {
            Transform root = transform.root;
            _sr = root.GetComponentInChildren<SpriteRenderer>();
        }

        if (_sr != null) 
        {
            _originalColor = _sr.color;
            Debug.Log($"<color=cyan>[VisualFeedback]</color> {gameObject.name}: SpriteRenderer found on <b>{_sr.gameObject.name}</b> (Root: {transform.root.name})");
        }
        else
        {
            Debug.LogWarning($"<color=red>[VisualFeedback]</color> {gameObject.name}: SpriteRenderer NOT found in any hierarchy!");
        }

        // 이벤트 구독
        _health.OnDamageTaken += PlayHitFlash;
        _health.OnHeal += PlayHealFlash;
    }

    private void OnDestroy()
    {
        if (_health != null)
        {
            _health.OnDamageTaken -= PlayHitFlash;
            _health.OnHeal -= PlayHealFlash;
        }
    }

    private void Update()
    {
        UpdateStatusVFX();
    }

    private void UpdateStatusVFX()
    {
        if (_status == null) return;

        // 보호막 VFX 관리
        if (_status.TotalShield < 0.01f && _shieldVFXInstance != null)
        {
            Destroy(_shieldVFXInstance);
            _shieldVFXInstance = null;
        }
    }

    public void SetShieldVFX(GameObject vfx)
    {
        if (_shieldVFXInstance != null) Destroy(_shieldVFXInstance);
        _shieldVFXInstance = vfx;
    }

    public void SetCCVFX(GameObject vfx)
    {
        if (_ccVFXInstance != null) Destroy(_ccVFXInstance);
        _ccVFXInstance = vfx;
    }

    private void PlayHitFlash()
    {
        if (_status != null && _status.TotalShield > 0) StartFlash(Color.cyan); // 보호막 피격
        else StartFlash(Color.black); // 일반 피격
    }

    private void PlayHealFlash() => StartFlash(Color.green);

    private void StartFlash(Color color)
    {
        if (_sr == null) return;
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        _flashCoroutine = StartCoroutine(FlashRoutine(color));
    }

    private IEnumerator FlashRoutine(Color color)
    {
        _sr.color = color;
        yield return new WaitForSeconds(0.1f);
        _sr.color = _originalColor;
        _flashCoroutine = null;
    }

    public void ResetVisuals()
    {
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        if (_sr != null) _sr.color = _originalColor;
    }
}
