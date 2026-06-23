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
    private Color _originalBaseColor;
    private bool _hasSavedOriginalBaseColor = false;
    private Coroutine _hitFlashCoroutine;

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

            // Prefer the dedicated "Body" child by name first - multiple SpriteRenderers exist
            // under root (hands, silhouettes, icon), so a blind "first match" search is unreliable.
            Transform bodyChild = root.Find("Body");
            if (bodyChild != null) _sr = bodyChild.GetComponent<SpriteRenderer>();

            // Fallback: old behavior if no "Body" child exists (e.g. other character prefabs)
            if (_sr == null)
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
        _health.OnDeath += PlayDeathVisual;
    }

    private void OnDestroy()
    {
        if (_health != null)
        {
            _health.OnDamageTaken -= PlayHitFlash;
            _health.OnHeal -= PlayHealFlash;
            _health.OnDeath -= PlayDeathVisual;
        }
    }

    private void PlayDeathVisual()
    {
        if (_sr == null) return;
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        
        // 사망 시 약간 어둡고 투명하게 (혹은 회색조)
        _sr.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
    }

    private void Update()
    {
        UpdateStatusVFX();
        UpdateSuperArmorColor();
    }

    private void UpdateSuperArmorColor()
    {
        if (_status == null || _sr == null) return;

        if (!_hasSavedOriginalBaseColor)
        {
            _originalBaseColor = _originalColor;
            _hasSavedOriginalBaseColor = true;
        }

        if (_status.HasSuperArmor)
        {
            float maxSA = _status.MaxSuperArmorGauge;
            float currentSA = _status.SuperArmorGauge;
            float ratio = maxSA > 0f ? (currentSA / maxSA) : 0f;

            // 찐한 파란색 정의 (Deep Blue)
            Color superArmorColor = new Color(0.1f, 0.3f, 1f, 1f);
            
            // 원래 베이스 색상과 찐한 파란색을 비율에 따라 Lerp
            Color lerpedColor = Color.Lerp(_originalBaseColor, superArmorColor, ratio);
            
            SetBaseColor(lerpedColor);
        }
        else
        {
            if (_originalColor != _originalBaseColor)
            {
                SetBaseColor(_originalBaseColor);
            }
        }
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

    private void PlayHitFlash(float damage)
    {
        if (_status != null && _status.TotalShield > 0.01f) StartFlash(Color.cyan); // 보호막 피격
        else 
        {
            if (_hitFlashCoroutine != null)
            {
                StopCoroutine(_hitFlashCoroutine);
                if (_sr != null) _sr.material.SetFloat("_HitFlash", 0f);
            }
            _hitFlashCoroutine = StartCoroutine(FlashRoutine()); // 일반 피격

            // [수정] 레이어 체크 대신 root 태그를 사용하여 플레이어 판정 (안정성 강화)
            bool isPlayer = gameObject.CompareTag("Player") || transform.root.CompareTag("Player");
            
            if(isPlayer)
            {
                // [수정] 인스턴스 널 체크 추가하여 NRE 방지
                if (GameManager.Instance != null) GameManager.Instance.TimeStopTimer(0.05f); 
                if (GameManager.Instance != null) GameManager.Instance.ChangeVignetteColor(0.05f, Color.red);
            }
        }
    }

    private IEnumerator FlashRoutine()
    {
        if (_sr == null) yield break; // [추가] 널 체크

        _sr.material.SetFloat("_HitFlash", 0.5f);
        yield return new WaitForSeconds(0.1f);       
        if (_sr != null) _sr.material.SetFloat("_HitFlash", 0f);

        StartFlash(Color.grey); 
    }

    private void PlayHealFlash() => StartFlash(Color.green);

    private void StartFlash(Color color)
    {
        if (_sr == null) return;
        if (_flashCoroutine != null) 
        {
            StopCoroutine(_flashCoroutine);
            _sr.color = _originalColor; // 강제 종료 시 색상 원상복구 보장
        }
        _flashCoroutine = StartCoroutine(FlashRoutine(color));
    }

    private IEnumerator FlashRoutine(Color color)
    {
        _sr.color = color;
        yield return new WaitForSeconds(0.1f);
        _sr.color = _originalColor;
        _flashCoroutine = null;
    }

    public void SetBaseColor(Color newColor)
    {
        _originalColor = newColor;
        if (_sr != null && _flashCoroutine == null)
        {
            _sr.color = newColor;
        }
    }

    public void ResetVisuals()
    {
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        if (_sr != null) _sr.color = _originalColor;
    }
}
