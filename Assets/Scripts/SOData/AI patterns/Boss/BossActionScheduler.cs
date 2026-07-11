using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "기본 공격을 interval초 반복 → 강제 인터럽트하고 특수 패턴 1개 → 다시 기본 반복"의 타이밍·선택 로직만 담당.
/// 기본은 직전과 안 겹치게, 특수는 전 종류를 한 번씩 쓸 때까지 무반복(풀 소진 후 리필).
///
/// 시간 판정은 Time.deltaTime 누적이 아니라 "기본 페이즈로 돌아온 절대시각(Time.time)" 기준이라,
/// 공격 중(IsAttacking) Execute가 뜸하게 불려도 정확하다. (기존 EliteCharger 의 _basicPhaseStartTime 계산과 동일)
///
/// 특정 보스에 묶이지 않는 순수 로직이라 다른 보스도 그대로 재사용한다.
/// </summary>
public class BossActionScheduler
{
    private readonly int _basicCount;
    private readonly int _specialCount;

    private int _lastBasic = -1;
    private readonly List<int> _specialPool = new List<int>();
    private float _basicPhaseStart;

    public BossActionScheduler(int basicCount, int specialCount)
    {
        _basicCount = Mathf.Max(1, basicCount);
        _specialCount = Mathf.Max(1, specialCount);
    }

    /// <summary>기본 페이즈 시작 시각을 now로 리셋. (Init / 특수 패턴 종료 직후 호출)</summary>
    public void ResetBasicPhase(float now) => _basicPhaseStart = now;

    /// <summary>지금(now) 특수 패턴을 발동할 때가 됐는가. interval은 호출측이 넘긴다(페이즈2 등 동적 간격 지원).</summary>
    public bool ShouldTriggerSpecial(float now, float interval) => now - _basicPhaseStart >= interval;

    /// <summary>직전과 겹치지 않는 다음 기본 공격 인덱스.</summary>
    public int NextBasic()
    {
        if (_basicCount == 1) return 0;
        int next;
        do { next = Random.Range(0, _basicCount); } while (next == _lastBasic);
        _lastBasic = next;
        return next;
    }

    /// <summary>무반복 풀에서 다음 특수 패턴 인덱스. 풀이 비면 전 종류로 리필.</summary>
    public int NextSpecial()
    {
        if (_specialPool.Count == 0)
            for (int i = 0; i < _specialCount; i++) _specialPool.Add(i);
        int idx = Random.Range(0, _specialPool.Count);
        int picked = _specialPool[idx];
        _specialPool.RemoveAt(idx);
        return picked;
    }
}
