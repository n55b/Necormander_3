using UnityEngine;

/// <summary>
/// 그래프 없이 수학 공식으로만 포물선 높이를 조절하는 컴포넌트입니다.
/// </summary>
public class ArcMovement : MonoBehaviour
{
    [SerializeField] private Transform visualTransform; 

    private float _currentDuration;
    private float _totalDuration;
    private float _maxHeight;
    private bool _isFlying;
    private float _currentHeight;

    // --- 추적 관련 필드 추가 ---
    private Transform _targetTransform;
    private float _initialDistance;
    private bool _isTracking;

    public bool IsFlying => _isFlying;
    public float CurrentHeight => _currentHeight;

    private void Update()
    {
        if (!_isFlying) return;

        float progress = 0f;

        if (_isTracking && _targetTransform != null)
        {
            float currentDist = Vector2.Distance(transform.position, _targetTransform.position);
            // 시작 시의 거리 대비 현재 거리로 진행률 계산 (0 -> 1)
            progress = Mathf.Clamp01(1f - (currentDist / _initialDistance));

            // 타겟에 충분히 근접하면 착지
            if (currentDist < 0.2f)
            {
                Land();
                return;
            }
        }
        else
        {
            _currentDuration += Time.deltaTime;
            progress = _currentDuration / _totalDuration;

            if (progress >= 1f)
            {
                Land();
                return;
            }
        }

        // [수학 공식] 포물선 공식: h = 4 * H * t * (1 - t)
        _currentHeight = 4f * _maxHeight * progress * (1f - progress);
        
        if (visualTransform != null)
        {
            visualTransform.localPosition = new Vector3(0, _currentHeight, 0);
        }
    }

    public void StartArc(float duration, float maxHeight)
    {
        _totalDuration = duration;
        _maxHeight = maxHeight;
        _currentDuration = 0f;
        _isFlying = true;
        _isTracking = false;
        _targetTransform = null;
    }

    /// <summary>
    /// 타겟을 추적하며 거리에 비례하여 높이를 조절하는 포물선 이동을 시작합니다.
    /// </summary>
    public void StartTrackingArc(Transform target, float maxHeight)
    {
        if (target == null)
        {
            StartArc(1.0f, maxHeight); // 폴백
            return;
        }

        _targetTransform = target;
        _maxHeight = maxHeight;
        _initialDistance = Vector2.Distance(transform.position, target.position);
        if (_initialDistance < 0.1f) _initialDistance = 0.1f; // 0 나누기 방지

        _isFlying = true;
        _isTracking = true;
        _currentDuration = 0f;

        // [추가] 타겟이 도중에 파괴될 경우를 대비한 시간 기반 폴백 값 설정
        _totalDuration = 2.0f; 
    }

    public void StopArc()
    {
        if (_isFlying)
        {
            Land();
        }
    }

    private void Land()
    {
        _isFlying = false;
        _currentHeight = 0f;

        if (visualTransform != null)
        {
            visualTransform.localPosition = Vector3.zero;
        }
        
        SendMessage("OnLanded", SendMessageOptions.DontRequireReceiver);
    }
}
