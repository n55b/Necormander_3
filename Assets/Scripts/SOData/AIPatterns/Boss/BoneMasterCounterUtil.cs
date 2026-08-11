using System.Collections;
using UnityEngine;

/// <summary>
/// 본 마스터 페이즈 1/2가 공유하는 "카운터 & 페이크 카운터" 패턴 로직.
/// 보스 몸이 노란색(진짜 카운터 찬스) 또는 빨간색(페이크, 역카운터)으로 빛나고,
/// 플레이어는 반응 시간 안에 색을 구분해 대응해야 한다.
///
/// - 노란색: 반응 시간 내에 보스에게 피해를 주면 성공 → 보스 강한 경직.
/// - 빨간색: 반응 시간 내에 보스를 공격하면 역으로 카운터당함 → 플레이어 피해 + 약한 경직(0.75초).
///           아무것도 안 하면(공격하지 않으면) 페널티 없이 지나간다.
///
/// [핵심 버그 수정] NavMeshAgent가 붙어있는 상태에서 entity.transform.position을 직접 대입하면
/// 에이전트 내부 상태와 어긋나 나중에 "튕기는" 버그가 생긴다. controller.WarpTo()(내부적으로
/// NavMeshAgent.Warp() 사용)를 거쳐서 위치를 고정한다.
/// </summary>
public static class BoneMasterCounterUtil
{
    /// <summary>
    /// 코루틴은 out 파라미터를 못 쓰므로, 결과를 담아 돌려줄 그릇을 호출측이 만들어 넘긴다.
    /// 호출측(패턴)이 "파훼당했으니 딜타임을 더 준다" 같은 후처리를 하려면 이 정보가 필요하다.
    /// </summary>
    public class Result
    {
        /// <summary>진짜(노란) 카운터 창을 플레이어가 파훼했는가.</summary>
        public bool Countered;
        /// <summary>파훼로 실제로 건 그로기 시간(초). Countered 가 false 면 0.</summary>
        public float GroggyDuration;
    }

    /// <param name="graceTime">
    /// 발광이 시작되고 <b>판정이 열리기까지의 유예 시간</b>(초).
    /// 이 동안은 노랑이든 빨강이든 아무 판정도 없다 — 때려도 파훼가 안 되고, 역공도 안 당한다.
    ///
    /// 없으면 "빨갛게 변한 순간 이미 휘두르고 있던 공격"이 그대로 역공으로 처벌돼서, 반응이 아니라
    /// 운으로 갈린다(= 억까). 유예를 두면 색을 보고 손을 뗄 시간이 생긴다.
    ///
    /// ★ 유예는 <b>노랑에도 똑같이</b> 적용해야 한다. 빨강에만 주면 "일단 무조건 즉시 때린다"가
    ///   최적해가 되어버린다 — 노랑이면 공짜 파훼, 빨강이면 유예라 무손실이라서 기믹 자체가 무너진다.
    ///   양쪽에 같이 줘야 '색을 읽고 결정한다'는 원래 설계가 유지된다.
    /// </param>
    /// <param name="fakeChance">이번 카운터가 페이크(빨강)일 확률(0~1).</param>
    /// <param name="gaugeAmount">진짜(노랑) 창의 파훼 요구 피해량. 1이면 사실상 아무 공격이나 한 대.</param>
    public static IEnumerator Run(
        BaseEntity entity,
        BoneMasterController controller,
        float reactionWindow,
        float successStunDuration = 2.5f,
        float fakeCounterPlayerStun = 0.75f,
        float fakeCounterPunishDamage = 3f,
        string patternLabel = "패턴: 카운터 & 페이크 카운터",
        Result result = null,
        float graceTime = 1f,
        float fakeChance = 0.5f,
        float gaugeAmount = 1f,
        Color? realColor = null,
        Color? fakeColor = null)
    {
        if (result != null) { result.Countered = false; result.GroggyDuration = 0f; }

        Vector2 origin = entity.transform.position; // 카운터 패턴 내내 완전히 제자리 고정

        bool isFake = Random.value < Mathf.Clamp01(fakeChance);
        Color flashColor = isFake ? (fakeColor ?? Color.red) : (realColor ?? Color.yellow);

        var gauge = controller != null ? controller.CounterGauge : null;
        bool brokenByPlayer = false;
        void OnBroken() => brokenByPlayer = true;

        bool redPunishTriggered = false;

        // ── 1단계: 유예 — 색만 보여주고 판정은 아직 열지 않는다 ──────────────
        // 아웃라인이 깜빡이는 동안은 "아직 아니다", 깜빡임이 멎고 꽉 차면 "지금부터"다.
        //
        // 문구는 진짜/가짜가 완전히 같아야 한다. 예전엔 페이크일 때만 "(주의!)" 가 붙어서
        // 색을 볼 필요 없이 텍스트만 읽으면 정답이 나왔다 — 기믹이 통째로 무의미했다.
        controller?.SetStateText($"{patternLabel} - 카운터 찬스?!", flashColor);

        float grace = Mathf.Max(0f, graceTime);
        float gt = 0f;
        while (gt < grace)
        {
            controller?.PulseCounterOutline(flashColor, gt);
            HoldPosition();
            gt += Time.deltaTime;
            yield return null;
        }

        // ── 2단계: 판정 개시 ────────────────────────────────────────────
        controller?.ShowCounterOutline(flashColor);
        controller?.SetStateText($"{patternLabel} - 지금!", flashColor);

        if (!isFake)
        {
            if (gauge != null)
            {
                gauge.OnGaugeBroken += OnBroken;
                gauge.OpenWindow(Mathf.Max(0.01f, gaugeAmount));
            }
        }
        else
        {
            DamageEventBus.OnBeforeDamageCalculated += RedPunishHandler;
        }

        try
        {
            float timer = 0f;
            while (timer < reactionWindow)
            {
                if (!isFake && brokenByPlayer) break;
                HoldPosition();
                timer += Time.deltaTime;
                yield return null;
            }
        }
        finally
        {
            // 이 코루틴은 창이 열린 채로 끊길 수 있다 — 부위 파괴(BoneMasterController.BreakNextPart)가
            // StopAllCoroutines() 를 부르고, 창 도중에도 도트 피해 같은 비(非)플레이어 피해는
            // 무효화를 안 거치고 들어가 부위 파괴선을 넘길 수 있다. 그때 구독이 남으면 핸들러가
            // 영원히 살아서 보스가 받는 모든 플레이어 피해를 0으로 만든다(= 보스가 불사신이 된다).
            if (isFake) DamageEventBus.OnBeforeDamageCalculated -= RedPunishHandler;
            else if (gauge != null) gauge.OnGaugeBroken -= OnBroken;
        }

        if (!isFake && gauge != null)
        {
            gauge.CloseWindow();
        }

        controller?.ClearCounterOutline();

        if (!isFake && brokenByPlayer)
        {
            controller?.SetStateText($"{patternLabel} - 카운터 성공! 경직!", Color.cyan);
            controller?.ApplyGroggy(successStunDuration);
            if (result != null) { result.Countered = true; result.GroggyDuration = successStunDuration; }
        }
        else if (isFake && redPunishTriggered)
        {
            controller?.SetStateText($"{patternLabel} - 역카운터!", Color.magenta);
        }
        else
        {
            controller?.SetStateText($"{patternLabel} - 종료", Color.white);
        }

        yield break;

        // 카운터 패턴 내내 보스는 완전히 제자리다. NavMeshAgent 가 붙어 있으므로 transform 직접 대입이
        // 아니라 WarpTo(내부적으로 NavMeshAgent.Warp)를 거쳐야 나중에 "튕기는" 문제가 안 생긴다.
        void HoldPosition()
        {
            if (controller != null) controller.WarpTo(origin);
            else entity.transform.position = origin;
        }

        void RedPunishHandler(CharacterHealth target, ref DamageInfo info)
        {
            if (controller == null || target != controller.Health) return;
            if (!DamageRules.IsPlayerSourced(info.category)) return;
            if (info.amount <= 0f) return;

            // 페이크 카운터(빨간 창)는 "치면 안 되는 창"이므로 피해 무효화는 무조건 먼저 한다.
            redPunishTriggered = true;
            info.amount = 0f;

            // 다만 역공은 대쉬 무적 중인 공격에는 걸지 않는다. BossCombat.TryDamage 가 모든 보스
            // 피해에 걸어 두는 Player_Dash 가드와 같은 규칙인데, 여기는 GetDamage 를 직접 부르는
            // 유일한 경로라 그 가드를 못 탄다. 역공 피해는 CharacterHealth 가 막아 주지만
            // Hitstun 은 아무 가드도 안 거쳐서, 대쉬로 파고들면 0.75초 완전 락아웃이 걸렸다.
            if (info.attacker != null && info.attacker.layer == Layers.PlayerDash) return;

            if (info.attacker != null)
            {
                var attackerHealth = info.attacker.GetComponentInParent<CharacterHealth>();
                if (attackerHealth == null) attackerHealth = info.attacker.GetComponentInChildren<CharacterHealth>();
                if (attackerHealth != null && !attackerHealth.IsDead)
                {
                    var punish = new DamageInfo(
                        fakeCounterPunishDamage,
                        DamageType.Physical,
                        entity != null ? entity.gameObject : null,
                        category: DamageCategory.EnemyBoss,
                        causesHitstun: true
                    );
                    attackerHealth.GetDamage(punish);

                    var attackerStatus = info.attacker.GetComponentInParent<CharacterStatus>();
                    if (attackerStatus == null) attackerStatus = info.attacker.GetComponentInChildren<CharacterStatus>();
                    attackerStatus?.ApplyStatus(StatusType.Hitstun, fakeCounterPlayerStun);
                }
            }
        }
    }
}
