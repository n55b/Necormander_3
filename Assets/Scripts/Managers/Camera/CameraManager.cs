using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 카메라 흔들림 / 줌 관리.
///
/// 흔들림 발동 경로:
///   1. 근접 공격 / 투척 히트 → DamageEventBus.OnDamageReceived → ShakeOnHit() (약한 흔들림)
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

    [Header("흔들림 강도")]
    [Tooltip("DamageEventBus 피격 시 기본 흔들림 강도")]
    [SerializeField] private float hitShakeForce   = 1f;
    [Tooltip("플레이어가 피격당할 때 흔들림 강도")]
    [SerializeField] private float hurtShakeForce  = 1.5f;

    // ─── 초기화 ──────────────────────────────────────────────────────
    public void Initialize()
    {
        Instance       = this;
        _impulseSource = GetComponent<CinemachineImpulseSource>();
        _vcam          = GetComponent<CinemachineCamera>();
        if (_vcam != null) _defaultOrthoSize = _vcam.Lens.OrthographicSize;

        // 피격 이벤트 구독
        DamageEventBus.OnDamageReceived += OnDamageReceived;

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

    private void OnDestroy()
    {
        DamageEventBus.OnDamageReceived -= OnDamageReceived;
    }

    // ─── 흔들림 ──────────────────────────────────────────────────────
    /// <summary>외부(SkillSO 등)에서 직접 호출. force로 강도 조절.</summary>
    public void HitShakeCamera(float force = 1f)
    {
        if (_impulseSource == null) return;
        _impulseSource.GenerateImpulseWithForce(force);
    }

    // ─── DamageEventBus 구독: 플레이어 공격이 적에게 맞을 때 ─────────
    private void OnDamageReceived(CharacterHealth target, DamageInfo info)
    {
        if (_impulseSource == null) return;

        // 플레이어 판정은 반드시 '루트' 태그로. CharacterHealth는 자식 CharacterStatStuff(Untagged)에 붙어 있어서
        // 그 오브젝트의 태그를 보면 영원히 false가 된다(= hurtShakeForce가 죽은 코드였던 원인).
        if (target.transform.root.CompareTag("Player"))
        {
            // 플레이어가 피격당했을 때의 카메라 흔들림
            _impulseSource.GenerateImpulseWithForce(hurtShakeForce);
            return;
        }

        if (info.amount <= 0f) return;

        // 미니언 평타 제외 (스킬은 SkillSO.ShakeCamera로 별도 처리)
        if (info.attacker != null && info.attacker.CompareTag("Player"))
        {
            Debug.Log("적 히트 카메라 흔들림");
            _impulseSource.GenerateImpulseWithForce(hitShakeForce);
        }
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
