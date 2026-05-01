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
        
        // [수정] 자식 오브젝트에 있을 경우 부모(Root)에서 스프라이트 탐색
        _sr = GetComponentInParent<SpriteRenderer>();
        if (_sr == null) _sr = GetComponentInChildren<SpriteRenderer>();
        if (_sr == null) // 마지막 수단: 부모의 모든 자식 탐색
        {
            var root = transform.parent;
            if (root != null) _sr = root.GetComponentInChildren<SpriteRenderer>();
        }

        if (_sr != null) _originalColor = _sr.color;

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
