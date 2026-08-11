using System;
using UnityEngine;

/// <summary>
/// 보스 패턴들이 공유하는 "카운터 게이지"(슈퍼아머 형태의 파훼 게이지) 공용 컴포넌트.
/// 기획 문서 표현: "카운터가 가능할 때, 적의 체력바에 카운터 게이지 부여(슈퍼아머 개념과 비슷).
/// 해당 게이지를 모두 소모했을 경우 적에게 즉시 행동을 취소하고, 그로기 상태 부여."
///
/// CharacterHealth(같은 오브젝트)의 인스턴스 이벤트 OnDamageReceived 를 구독해서
/// "플레이어에게서 온 피해"만 게이지에 반영한다. 방어력 계산 전 원시 피해량을 그대로 깎으므로
/// 무기 세팅에 따라 파훼 속도가 달라진다(빌드가 좋을수록 카운터도 잘 먹는다는 기획 의도와 일치).
///
/// 새 보스를 만들 때도 그대로 재사용 가능한 공용 시스템이다.
/// </summary>
[RequireComponent(typeof(CharacterHealth))]
public class BossCounterGauge : MonoBehaviour
{
    public bool IsOpen { get; private set; }
    public float MaxGauge { get; private set; }
    public float CurrentGauge { get; private set; }
    public float Ratio => MaxGauge > 0f ? Mathf.Clamp01(CurrentGauge / MaxGauge) : 0f;

    /// <summary>게이지가 열리거나/닫히거나/피격으로 줄어들 때마다 호출된다. UI가 구독.</summary>
    public event Action OnGaugeChanged;

    /// <summary>게이지가 0이 되어 파훼(카운터 성공)됐을 때 정확히 1회 호출된다.</summary>
    public event Action OnGaugeBroken;

    private CharacterHealth _health;

    private void Awake()
    {
        _health = GetComponent<CharacterHealth>();
    }

    private void OnEnable()
    {
        if (_health != null) _health.OnDamageReceived += HandleDamageReceived;
    }

    private void OnDisable()
    {
        if (_health != null) _health.OnDamageReceived -= HandleDamageReceived;
    }

    /// <summary>
    /// 카운터 파훼 창을 연다.
    /// </summary>
    /// <param name="maxGauge">플레이어가 깎아야 할 총 피해량. 즉발 판정(페이크 카운터의 노란 자세 등)이 필요하면
    /// 아주 작은 값(예: 1)을 넣어 사실상 "아무 공격이나 한 대"로 즉시 파훼되게 만들 수 있다.</param>
    public void OpenWindow(float maxGauge)
    {
        MaxGauge = Mathf.Max(0.01f, maxGauge);
        CurrentGauge = MaxGauge;
        IsOpen = true;
        OnGaugeChanged?.Invoke();
    }

    /// <summary>파훼되지 않고 창이 자연 종료됐을 때(패턴이 다음 단계로 넘어갈 때) 호출한다.</summary>
    public void CloseWindow()
    {
        if (!IsOpen && CurrentGauge <= 0f) return;
        IsOpen = false;
        CurrentGauge = 0f;
        OnGaugeChanged?.Invoke();
    }

    private void HandleDamageReceived(DamageInfo info)
    {
        if (!IsOpen) return;

        // 플레이어 계열 피해(평타/스킬/대쉬/패링)만 게이지에 반영한다.
        // 상태이상 도트, 가시 자해 등은 걸리지 않는다.
        if (!DamageRules.IsPlayerSourced(info.category)) return;
        if (info.amount <= 0f) return;

        CurrentGauge -= info.amount;
        OnGaugeChanged?.Invoke();

        if (CurrentGauge <= 0f)
        {
            CurrentGauge = 0f;
            IsOpen = false;

            // [버그 수정 — 파훼 직후 게이지 바가 화면에 남던 문제]
            // IsOpen 이 false 로 바뀌는 이 순간에도 변경 알림을 쏴야 UI 가 숨을 수 있다.
            // 위쪽 Invoke 는 아직 IsOpen == true 일 때 나갔고, 뒤이어 패턴이 부르는 CloseWindow() 는
            // "이미 닫혔고 게이지도 0" 이라는 이유로 조기 return 해서 이벤트를 쏘지 않는다.
            // 그래서 파훼로 끝난 패턴에서는 UI 갱신 알림이 한 번도 안 가는 구멍이 있었다.
            OnGaugeChanged?.Invoke();
            OnGaugeBroken?.Invoke();
        }
    }
}
