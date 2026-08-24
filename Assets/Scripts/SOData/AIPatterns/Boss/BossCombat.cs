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
    private static readonly int DashLayer = LayerMask.NameToLayer("Player_Dash");

    public static bool TryDamage(Collider2D col, DamageInfo info)
    {
        if (col == null) return false;
        if (col.gameObject.layer == DashLayer) return false;

        // [사망한 공격자의 판정 무효화] 보스가 사망한 뒤에도 남아있는 지연 프레임이나 미회수 판정으로 인한 피해 차단
        if (info.attacker != null)
        {
            CharacterHealth atkHealth = info.attacker.GetComponentInChildren<CharacterHealth>();
            if (atkHealth == null) atkHealth = info.attacker.GetComponentInParent<CharacterHealth>();
            if (atkHealth != null && atkHealth.IsDead) return false;
        }

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

    /// <summary>origin에서 dir 방향으로 length만큼 뻗는 직사각(레인) 범위 안의 대상(targetMask)에게 1회 피해. width는 레인의 폭.</summary>
    public static void DealLane(Vector2 origin, Vector2 dir, float length, float width, LayerMask targetMask, DamageInfo info)
    {
        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Vector2 center = origin + d * (length * 0.5f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, new Vector2(length, width), angle, targetMask);
        foreach (var hit in hits)
        {
            TryDamage(hit, info);
        }
    }

    /// <summary>
    /// <see cref="DealLane"/>과 같은 레인 판정이지만, <paramref name="already"/>에 기록된 대상은 건너뛰고
    /// 새로 맞은 대상만 그 집합에 추가한다. <b>여러 프레임에 걸쳐 이동하는 판정</b>(박치기 돌격 등)이
    /// 매 프레임 호출돼도 같은 대상을 두 번 때리지 않게 하기 위한 것.
    ///
    /// 돌진은 "지나간 자리"가 판정이라 정지 패턴처럼 끝에서 한 번만 때리면 몸을 관통당해도 안 맞는다.
    /// 그래서 매 프레임 prev→next 구간을 이걸로 훑고, 중복만 이 집합으로 막는다.
    /// (같은 이유로 벽 접촉 검사도 IsTouchingThornWallSwept 로 구간을 훑는다 — 동일한 문법이다.)
    /// </summary>
    /// <returns>이번 호출로 새로 피해를 입은 대상 수.</returns>
    public static int DealLaneOnce(Vector2 origin, Vector2 dir, float length, float width,
                                   LayerMask targetMask, DamageInfo info, HashSet<GameObject> already)
    {
        if (length <= 0.0001f) return 0;

        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Vector2 center = origin + d * (length * 0.5f);

        int hitCount = 0;
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, new Vector2(length, width), angle, targetMask);
        foreach (var hit in hits)
        {
            if (hit == null) continue;

            // 중복 판정은 콜라이더가 아니라 '루트 게임오브젝트' 기준이다. 플레이어는 본체/대쉬용 등
            // 콜라이더가 여러 개라, 콜라이더로 기억하면 같은 대상을 두 번 때릴 수 있다.
            GameObject key = hit.transform.root.gameObject;
            if (already != null && already.Contains(key)) continue;

            if (!TryDamage(hit, info)) continue;

            already?.Add(key);
            hitCount++;
        }
        return hitCount;
    }

    /// <summary>
    /// center를 중심으로 반지름(radiusX, radiusY)인 타원 범위 안의 대상(targetMask)에게 1회 피해.
    /// (도약 & 내려찍기 착지 지점 등 원형이 아닌 타원 범위가 필요할 때 쓴다.)
    /// </summary>
    public static void DealEllipse(Vector2 center, float radiusX, float radiusY, LayerMask targetMask, DamageInfo info)
    {
        float maxRadius = Mathf.Max(radiusX, radiusY);
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, maxRadius, targetMask);
        foreach (var hit in hits)
        {
            Vector2 local = (Vector2)hit.transform.position - center;
            float nx = radiusX > 0.001f ? local.x / radiusX : 0f;
            float ny = radiusY > 0.001f ? local.y / radiusY : 0f;
            if (nx * nx + ny * ny > 1f) continue;
            TryDamage(hit, info);
        }
    }

    /// <summary>
    /// center에서 facingDir 방향을 중심으로 halfAngleDegrees(반각) 이내, 반경 radius인 부채꼴(콘) 범위 안의
    /// 대상(targetMask)에게 1회 피해. halfAngleDegrees=90이면 반달(180도) 모양이 된다.
    /// (창 휩쓸기를 전방위 원이 아니라 정면 반달로 만들 때 쓴다.)
    /// </summary>
    public static void DealCone(Vector2 center, Vector2 facingDir, float radius, float halfAngleDegrees, LayerMask targetMask, DamageInfo info)
    {
        Vector2 dir = facingDir.sqrMagnitude > 0.0001f ? facingDir.normalized : Vector2.right;
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, targetMask);
        foreach (var hit in hits)
        {
            Vector2 toHit = (Vector2)hit.transform.position - center;
            if (toHit.sqrMagnitude < 0.0001f) { TryDamage(hit, info); continue; } // 정중앙(거의 겹침)은 항상 포함
            float angle = Vector2.Angle(dir, toHit);
            if (angle > halfAngleDegrees) continue;
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
            onExpand?.Invoke(cur);
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
