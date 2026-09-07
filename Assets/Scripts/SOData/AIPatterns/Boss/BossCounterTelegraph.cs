using System.Collections;
using UnityEngine;

/// <summary>
/// 보스 패턴의 <b>예고 구간</b>을 통째로 대신 돌려주는 공용 헬퍼. 0830 수정안의 핵심이다.
///
/// 카운터는 더 이상 별도 패턴이 아니라 <b>예고의 성질</b>이다. 패턴은 예고를 시작하면서
/// "이번 예고는 노랑/빨강/카운터불가 중 무엇인가"만 정하고, 그 뒤 판정이 나갈 때까지
/// 기다리는 일은 전부 여기가 한다.
///
///   노랑(<see cref="Kind.Real"/>) — 예고 중에 때리면 파훼. 패턴이 취소되고 보스가 경직한다.
///   빨강(<see cref="Kind.Fake"/>) — 예고 중 실제 피격마다 보스가 회복한다. 시전 시간은 유지한다.
///   무채색(<see cref="Kind.None"/>) — 카운터 불가. 그냥 시간만 재는 예고.
///
/// <see cref="BossCounterGauge"/>가 노랑 파훼 창 / 빨강 회복 창의 수명을 함께 관리한다.
/// 회색은 어느 창도 열지 않는다. 사망·페이즈 전환·취소 시 CloseWindow 로 모두 닫는다.
///
/// <para>
/// [반드시 지킬 것] 이 코루틴은 창이 열린 채로 끊길 수 있다(부위 파괴 → StopAllCoroutines,
/// 페이즈 전환, 사망). try/finally 로 구독을 반드시 푼다 — 안 풀면 핸들러가 영원히 살아남아
/// 다음 패턴의 파훼가 엉뚱한 곳에서 잡힌다.
/// </para>
/// </summary>
public static class BossCounterTelegraph
{
    public enum Kind
    {
        /// <summary>카운터 불가. 무채색으로 시간만 예고한다.</summary>
        None,
        /// <summary>진짜 카운터(노랑). 때리면 패턴 취소 + 보스 경직.</summary>
        Real,
        /// <summary>페이크(빨강). 때리면 보스가 회복한다. 시전 시간은 변하지 않는다.</summary>
        Fake,
    }

    /// <summary>
    /// 코루틴은 out 파라미터를 못 쓰므로 결과 그릇을 호출측이 만들어 넘긴다.
    /// 패턴은 <see cref="Countered"/> 와 <see cref="Hijacked"/> 를 반드시 확인하고 분기해야 한다 —
    /// 확인 안 하면 취소된 패턴이 그대로 판정을 내보낸다.
    /// </summary>
    public class Result
    {
        /// <summary>이번 예고가 무엇이었는가.</summary>
        public Kind Kind;
        /// <summary>노랑을 파훼당했다 → 패턴을 취소하고 경직으로 넘어가야 한다.</summary>
        public bool Countered;
        /// <summary>외부 요인(사망 / 부위 파괴 / 페이즈 전환)으로 끊겼다 → 아무것도 하지 말고 빠져야 한다.</summary>
        public bool Hijacked;
        /// <summary>예고가 시작된 뒤 흐른 시간(초). onTick 안에서 '끝나기 직전'을 잡는 데 쓴다.</summary>
        public float Elapsed;
    }

    /// <summary>
    /// 이번 예고의 성질을 뽑는다. 매 예고마다 독립 추첨이라 빨강이 연달아 나올 수 있다
    /// (0830 회의 확정 — 편중이 심하면 그때 연속 방지를 넣기로 했다).
    /// </summary>
    /// <param name="counterable">이 패턴이 애초에 카운터 가능한가. false 면 무조건 <see cref="Kind.None"/>.</param>
    public static Kind Roll(bool counterable, float realChance, float fakeChance)
    {
        if (!counterable) return Kind.None;
        float roll = Random.value;
        float real = Mathf.Clamp01(realChance);
        if (roll < real) return Kind.Real;
        return roll < real + Mathf.Clamp(fakeChance, 0f, 1f - real) ? Kind.Fake : Kind.None;
    }

    /// <summary>성질에 맞는 색. 바닥 전조와 머리 위 인디케이터가 같은 색을 써야 신호가 하나로 읽힌다.</summary>
    public static Color ColorOf(Kind kind, Color real, Color fake, Color none)
        => kind == Kind.Real ? real : kind == Kind.Fake ? fake : none;

    /// <summary>
    /// 예고를 켜고, <paramref name="duration"/> 동안 기다린다.
    /// 머리 위 인디케이터는 여기가 켜고 끈다 — 호출측은 바닥 전조만 책임지면 된다.
    /// </summary>
    /// <param name="duration">판정이 나갈 때까지의 시간. 패턴이 <b>실제로 기다리는 값과 같은 식</b>이어야 한다.</param>
    /// <param name="dir">공격 방향. 방향이 없는 패턴은 <c>default</c>.</param>
    /// <param name="onTick">매 프레임 호출. 위치 고정(Warp)이나 애니 뒤 박자 트리거를 여기서 한다.</param>
    public static IEnumerator Run(
        BaseEntity entity,
        BoneMasterController controller,
        float duration,
        Vector2 dir,
        Kind kind,
        Color color,
        float gaugeAmount,
        Result result,
        System.Action onTick = null,
        float fakeHealPerHit = 5f)
    {
        if (result != null)
        {
            result.Kind = kind;
            result.Countered = false;
            result.Hijacked = false;
            result.Elapsed = 0f;
        }

        // [함정] 하이재킹 판정을 AIState.Skill 고정으로 하면 안 된다. 통합 이후 보스 패턴은
        // 기본 공격 경로(AIState.Attack)로도 돌기 때문에, 고정 비교를 하면 시작하자마자 끊긴 걸로
        // 오인해서 모든 패턴이 예고만 띄우고 사라진다. '시작할 때의 상태에서 바뀌었는가'로 본다.
        AIState startState = entity.CurrentState;

        BossAttackIndicator.Begin(entity, duration, dir, color);

        BossCounterGauge gauge = controller != null ? controller.CounterGauge : null;
        bool broken = false;
        void OnBroken() => broken = true;

        if (gauge != null)
        {
            gauge.CloseWindow();
            if (kind == Kind.Real)
            {
                gauge.OnGaugeBroken += OnBroken;
                gauge.OpenWindow(Mathf.Max(0.01f, gaugeAmount));
            }
            else if (kind == Kind.Fake) gauge.OpenHealingWindow(fakeHealPerHit);
        }

        try
        {
            float t = 0f;
            while (t < duration)
            {
                // 사망 / 부위 파괴 / 페이즈 전환은 전부 CurrentState 를 바꾼다.
                if (entity == null || entity.CurrentState != startState
                    || (entity.Stats != null && entity.Stats.Health != null && entity.Stats.Health.IsDead))
                {
                    if (result != null) result.Hijacked = true;
                    break;
                }
                if (broken) break;

                if (result != null) result.Elapsed = t;
                onTick?.Invoke();
                t += Time.deltaTime;
                yield return null;
            }
        }
        finally
        {
            if (gauge != null)
            {
                gauge.OnGaugeBroken -= OnBroken;
                gauge.CloseWindow();
            }
            BossAttackIndicator.Stop(entity);
        }

        if (result == null || result.Hijacked) yield break;

        if (broken)
        {
            if (kind == Kind.Real) result.Countered = true;
        }
    }
}
