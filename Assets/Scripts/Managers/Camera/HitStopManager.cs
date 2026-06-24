using System.Collections;
using UnityEngine;

/// <summary>
/// 히트스탑(짧은 전역 프레임 정지) 관리.
///
/// 의도적으로 강타(Smash) / 처형(Execute)급 타격에만 사용합니다.
/// 일반 평타나 약한 연계 스킬에 걸면 "끊기는 느낌"이 들기 때문에,
/// 호출 지점을 늘릴 때는 반드시 강한 타격인지 먼저 확인하세요.
///
/// 호출 경로:
///   1. 강타(Smash) 연계 스킬 → MinionActionSkillSO.ExecuteSkill() (actionType == ApplySmash) → DoHitStop()
///   2. 처형(Execute) 발생 → CharacterHealth.GetDamage() 처형 체크 구간 → DoHitStop()
/// </summary>
public class HitStopManager : MonoBehaviour
{
    public static HitStopManager Instance;

    [Header("히트스탑 기본값")]
    [Tooltip("duration 인자를 안 넘겼을 때 기본 정지 시간(초). 0.05~0.1 권장.")]
    [SerializeField] private float defaultDuration = 0.08f;

    [Tooltip("정지 동안의 timeScale. 0에 가까울수록 강하게 멈춤. 완전히 0으로 두면 일부 코루틴 타이밍이 깨질 수 있어 0.02 권장.")]
    [SerializeField] private float freezeScale = 0.02f;

    [Tooltip("히트스탑이 허용되는 최대 시간(초). 실수로 큰 값이 들어와도 이 이상은 못 넘게 막음.")]
    [SerializeField] private float maxDuration = 0.15f;

    private Coroutine _activeHitStop;
    private float _originalTimeScale = 1f;

    public void Initialize()
    {
        Instance = this;
    }

    /// <summary>
    /// 히트스탑 발동. duration을 생략하면 defaultDuration 사용.
    /// 겹쳐서 호출되면 기존 정지를 새 정지로 갈아탑니다(누적되어 길어지지 않음).
    /// </summary>
    public void DoHitStop(float duration = -1f)
    {
        float d = duration > 0f ? duration : defaultDuration;
        d = Mathf.Min(d, maxDuration);
        if (d <= 0f) return;
        Debug.Log($"<color=cyan>[HitStop]</color> 발동! duration={d}, freezeScale={freezeScale}");


        if (_activeHitStop != null)
        {
            StopCoroutine(_activeHitStop);
        }
        else
        {
            _originalTimeScale = Time.timeScale;
        }

        _activeHitStop = StartCoroutine(HitStopRoutine(d));
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        Time.timeScale = freezeScale;
        yield return new WaitForSecondsRealtime(duration); // realtime이라 timeScale 영향을 안 받음
        Time.timeScale = _originalTimeScale;
        _activeHitStop = null;
    }

    private void OnDisable()
    {
        // 씬 전환 등으로 매니저가 꺼질 때 timeScale이 멈춰있는 상태로 남지 않도록 안전 복구
        if (_activeHitStop != null)
        {
            Time.timeScale = _originalTimeScale;
        _activeHitStop = null;
        Debug.Log($"<color=cyan>[HitStop]</color> 종료, timeScale 복원됨: {Time.timeScale}");
    }
    }
}
