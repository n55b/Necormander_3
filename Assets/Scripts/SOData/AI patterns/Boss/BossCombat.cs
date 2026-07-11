using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 패턴들이 공유하는 <b>단일 피해 판정 경로</b>.
/// 기존엔 각 패턴에 "무적/사망/대쉬무적(Player_Dash) 체크 후 GetDamage" 가 흩어져 복붙돼 있었고
/// (BaseHitBox 경로와도 이원화), i-frame 규칙이 여러 곳에 있어 바꿀 때 일부만 갱신될 위험이 있었다.
/// 여기로 모아 데미지 경로를 하나로 통일한다.
/// </summary>
public static class BossCombat
{
    // 대상 마스크는 호출측이 넘긴다: 보스는 항상 entity.opponentLayer 를 사용한다.
    // opponentLayer 는 BaseEntity.SetupLayers()가 team 기준으로 세팅한 값이라(적팀→PlayerArmy, 아군팀→EnemyMask)
    // 하드코딩 문자열/존재하지 않는 "Ally" 레이어 없이 team-정확하게 타겟팅된다.
    private static readonly int DashLayer = LayerMask.NameToLayer("Player_Dash");

    /// <summary>
    /// 콜라이더가 유효 피격 대상이면 피해를 주고 true. 대쉬 무적(Player_Dash 레이어)·무적·사망은 회피로 처리(false).
    /// </summary>
    public static bool TryDamage(Collider2D col, DamageInfo info)
    {
        if (col == null) return false;
        if (col.gameObject.layer == DashLayer) return false; // 대쉬 무적으로 완전 회피 (마스크가 놓쳐도 방어)
        CharacterHealth health = col.GetComponentInChildren<CharacterHealth>();
        if (health == null) health = col.GetComponentInParent<CharacterHealth>();
        if (health == null || health.IsDead || health.Invincible) return false;
        health.GetDamage(info);
        return true;
    }

    /// <summary>center 반경 radius 안의 모든 대상(targetMask)에게 1회 피해. excludeRadius(안전지대) 이내는 제외.</summary>
    public static void DealCircle(Vector2 center, float radius, LayerMask targetMask, DamageInfo info, float excludeRadius = 0f)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, targetMask);
        foreach (var hit in hits)
        {
            if (excludeRadius > 0f && Vector2.Distance(center, hit.transform.position) <= excludeRadius) continue;
            TryDamage(hit, info);
        }
    }

    /// <summary>
    /// 중심에서 maxRadius까지 duration에 걸쳐 퍼지는 링. 각 대상은 링 두께에 스치는 순간 1회만 onHit 콜백.
    /// (바닥 충격파 / 하울 링 등에서 공용. 시각 링은 호출측이 <see cref="BossTelegraph"/>로 그린다.)
    /// onHit(hit, dirFromCenter) — 피해든 넉백이든 호출측이 결정한다.
    /// </summary>
    public static IEnumerator ExpandingRing(Vector2 center, float maxRadius, float duration, float thickness,
                                            LayerMask targetMask, Action<Collider2D, Vector2> onHit, Action<float> onExpand = null)
    {
        HashSet<GameObject> already = new HashSet<GameObject>();
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float cur = Mathf.Lerp(0f, maxRadius, Mathf.Clamp01(t / duration));
            onExpand?.Invoke(cur); // 시각 링 확장 등 매 프레임 콜백 (판정 전에 호출 — 원본 순서 유지)
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, cur + thickness, targetMask);
            foreach (var hit in hits)
            {
                if (already.Contains(hit.gameObject)) continue;
                float d = Vector2.Distance(center, hit.transform.position);
                if (d < cur - thickness || d > cur + thickness) continue;
                already.Add(hit.gameObject);
                Vector2 dir = (Vector2)hit.transform.position - center;
                onHit?.Invoke(hit, dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up);
            }
            yield return null;
        }
    }
}
