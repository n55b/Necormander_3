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
    public static IEnumerator Run(
        BaseEntity entity,
        BoneMasterController controller,
        float reactionWindow,
        float successStunDuration = 2.5f,
        float fakeCounterPlayerStun = 0.75f,
        float fakeCounterPunishDamage = 3f,
        string patternLabel = "패턴: 카운터 & 페이크 카운터")
    {
        Vector2 origin = entity.transform.position; // 카운터 패턴 내내 완전히 제자리 고정

        bool isFake = Random.value < 0.5f;
        Color flashColor = isFake ? Color.red : Color.yellow;

        controller?.SetVisualFlash(flashColor);
        controller?.SetStateText($"{patternLabel} - {(isFake ? "카운터 찬스?! (주의!)" : "카운터 찬스!")}", flashColor);

        var gauge = controller != null ? controller.CounterGauge : null;
        bool brokenByPlayer = false;
        void OnBroken() => brokenByPlayer = true;

        bool redPunishTriggered = false;

        if (!isFake)
        {
            if (gauge != null)
            {
                gauge.OnGaugeBroken += OnBroken;
                gauge.OpenWindow(1f);
            }
        }
        else
        {
            DamageEventBus.OnBeforeDamageCalculated += RedPunishHandler;
        }

        float timer = 0f;
        while (timer < reactionWindow)
        {
            if (!isFake && brokenByPlayer) break;
            if (controller != null) controller.WarpTo(origin);
            else entity.transform.position = origin;
            timer += Time.deltaTime;
            yield return null;
        }

        if (!isFake && gauge != null)
        {
            gauge.OnGaugeBroken -= OnBroken;
            gauge.CloseWindow();
        }
        if (isFake)
        {
            DamageEventBus.OnBeforeDamageCalculated -= RedPunishHandler;
        }

        controller?.ClearVisualFlash();

        if (!isFake && brokenByPlayer)
        {
            controller?.SetStateText($"{patternLabel} - 카운터 성공! 경직!", Color.cyan);
            controller?.ApplyGroggy(successStunDuration);
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

        void RedPunishHandler(CharacterHealth target, ref DamageInfo info)
        {
            if (controller == null || target != controller.Health) return;
            if (!DamageRules.IsPlayerSourced(info.category)) return;
            if (info.amount <= 0f) return;

            redPunishTriggered = true;
            info.amount = 0f;

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
