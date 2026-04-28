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

    public bool IsFlying => _isFlying;
    public float CurrentHeight => _currentHeight;

    private void Update()
    {
        if (!_isFlying) return;

        _currentDuration += Time.deltaTime;
        float progress = _currentDuration / _totalDuration; // 0에서 1까지 흐름

        if (progress >= 1f)
        {
            Land();
            return;
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
