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

    [Header("Shake Clamping")]
    [Tooltip("이 시간(초) 안에 들어온 히트는 힘을 새로 쌓지 않고 최댓값만 반영한다. 여러 명 동시 타격 시 임펄스가 겹쳐 어지러워지는 것을 막는다.")]
    [SerializeField] private float shakeWindow = 0.15f;
    [Tooltip("한 윈도우 안에서 허용하는 최대 힘.")]
    [SerializeField] private float maxForcePerWindow = 2.5f;

    private float _windowStartTime = -999f;
private float _windowMaxForce  = 0f;

    // ─── 초기화 ──────────────────────────────────────────────────────
    public void Initialize()
    {
        Instance       = this;
        _impulseSource = GetComponent<CinemachineImpulseSource>();
        _vcam          = GetComponent<CinemachineCamera>();
        if (_vcam != null) _defaultOrthoSize = _vcam.Lens.OrthographicSize;

        Debug.Log("<color=cyan>[CameraManager]</color> Initialized.");
    }

    /// <summary>텔레포트 이동 시 카메라 즉시 순간이동. 부르는 쪽은 플레이어를 넘겨도 된다.
    ///
    /// [함정] Cinemachine 은 넘긴 target 이 Follow 와 '참조가 같지' 않으면 워프를 통째로 무시한다
    /// (CinemachineCamera / CinemachineFollow / Confiner2D 전부 target == Follow 로 걸러낸다).
    /// 그런데 vcam 이 따라다니는 건 플레이어가 아니라 그 자식 CameraTarget 이라,
    /// 플레이어를 넘기면 워프가 조용히 씹히고 카메라가 damping 으로 천천히 따라온다
    /// = 방 이동에서 페이드 인이 끝난 뒤에 카메라가 스르륵 움직이는 게 보인다.
    /// 그래서 여기서 실제 Follow 대상으로 바꿔 넘긴다. CameraTarget 은 플레이어의 자식이니 delta 는 같다.</summary>
    public void WarpCamera(Transform target, Vector3 delta)
    {
        if (_vcam == null) return;

        if (_vcam.Follow != null) target = _vcam.Follow;

        _vcam.OnTargetObjectWarped(target, delta);
    }

    // ─── 흔들림 ──────────────────────────────────────────────────────
    /// <summary>외부(CameraShaker, SkillSO 등)에서 직접 호출. force로 강도 조절.</summary>
    public void HitShakeCamera(float force = 1f)
    {
        if (_impulseSource == null) return;

        float now = Time.time;

        // shakeWindow(초) 지난후 새 히트면 윈도우 리셋.
        if (now - _windowStartTime > shakeWindow)
        {
            _windowStartTime = now;
            _windowMaxForce  = 0f;
        }

        // 이번 힘이 이 윈도우에서 이미 낸 최대치보다 작거나 같으면 무시(추가 임펄스 발사 안 함) → 겹쳐서 증폭되는 것을 막는다.
        if (force <= _windowMaxForce) return;

        float cappedTarget = Mathf.Min(force, maxForcePerWindow);
        float delta = cappedTarget - _windowMaxForce;
        _windowMaxForce = cappedTarget;

        _impulseSource.GenerateImpulseWithForce(delta);
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
