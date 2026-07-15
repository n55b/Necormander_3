using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 카메라 흔들림 / 줌 '엔진'. 실제로 흔드는 일만 한다 — 언제 흔들지는 판단하지 않는다.
///
/// 흔들림 발동 경로:
///   1. 피격 → CharacterHealth가 ShakeSignal을 쏨 → CameraShaker(카메라 리그에 부착) → HitShakeCamera()
///   2. 스킬 발동 → SkillSO.ShakeCamera(force) → HitShakeCamera(force) (스킬별 강도)
///
/// 구르기 흔들림: 없음 (의도적 제외)
/// </summary>
public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance;

    [Header("Cinemachine")]
    private CinemachineImpulseSource _impulseSource;
    private CinemachineCamera        _vcam;
    private float                    _defaultOrthoSize;

    // ─── 초기화 ──────────────────────────────────────────────────────
    public void Initialize()
    {
        Instance       = this;
        _impulseSource = GetComponent<CinemachineImpulseSource>();
        _vcam          = GetComponent<CinemachineCamera>();
        if (_vcam != null) _defaultOrthoSize = _vcam.Lens.OrthographicSize;

        Debug.Log("<color=cyan>[CameraManager]</color> Initialized.");
    }

    // [추가] 텔레포트 이동 시 카메라 즉시 순간이동 기능
    public void WarpCamera(Transform target, Vector3 delta)
    {
        if (_vcam != null)
        {
            _vcam.OnTargetObjectWarped(target, delta);
        }
    }

    // ─── 흔들림 ──────────────────────────────────────────────────────
    /// <summary>외부(CameraShaker, SkillSO 등)에서 직접 호출. force로 강도 조절.</summary>
    public void HitShakeCamera(float force = 1f)
    {
        if (_impulseSource == null) return;
        _impulseSource.GenerateImpulseWithForce(force);
    }

    // ─── 줌 ──────────────────────────────────────────────────────────
    public void SetZoom(bool zoomOut)
    {
        if (_vcam == null) return;
        var lens = _vcam.Lens;
        lens.OrthographicSize = zoomOut ? _defaultOrthoSize * 1.3f : _defaultOrthoSize;
        _vcam.Lens = lens;
    }
}
