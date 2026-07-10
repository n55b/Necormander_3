using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어/미니언 스킬들이 공유하는 전투 판정·이동 유틸리티.
/// 기존에 여러 스킬 SO에 복붙돼 있던 PushEnemy / 벽 클램프 / 조준 / 이동 / 스탯 해석을 일원화한다.
/// </summary>
public static class SkillCombatUtil
{
    /// <summary>공격자 GameObject 가 적(Enemy)인지 판정한다. (CharacterStat.IsEnemy 우선, 없으면 Enemy 레이어)</summary>
    public static bool IsEnemyAttacker(GameObject attacker)
    {
        if (attacker == null) return false;
        var stat = attacker.GetComponent<CharacterStat>() ?? attacker.GetComponentInParent<CharacterStat>();
        if (stat != null) return stat.IsEnemy;
        return attacker.layer == Layers.Enemy;
    }

    /// <summary>CharacterHealth 로부터 CharacterStat 을 3단 폴백으로 해석한다.</summary>
    public static CharacterStat ResolveStat(CharacterHealth health)
    {
        if (health == null) return null;
        return health.GetComponent<CharacterStat>()
            ?? health.GetComponentInParent<CharacterStat>()
            ?? health.GetComponentInChildren<CharacterStat>();
    }

    /// <summary>Collider2D 로부터 CharacterHealth 를 해석한다. (자식 우선, 없으면 부모)</summary>
    public static CharacterHealth ResolveHealth(Collider2D col)
    {
        if (col == null) return null;
        return col.GetComponentInChildren<CharacterHealth>() ?? col.GetComponentInParent<CharacterHealth>();
    }

    /// <summary>fromWorld 기준 마우스 조준 방향(월드, 정규화). 0이면 오른쪽.</summary>
    public static Vector2 GetAimDir(Vector2 fromWorld)
    {
        Vector2 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = (mouse - fromWorld).normalized;
        return dir == Vector2.zero ? Vector2.right : dir;
    }

    /// <summary>벽/장애물을 파고들지 않도록 CircleCast 로 제동한 목적지를 반환한다.</summary>
    public static Vector2 ClampToWall(Vector2 from, Vector2 dir, float distance, float radius = 0.4f)
    {
        RaycastHit2D hit = Physics2D.CircleCast(from, radius, dir, distance, Layers.WallObstacle);
        if (hit.collider != null) return hit.point + hit.normal * (radius * 1.02f);
        return from + dir * distance;
    }

    /// <summary>mover 를 from→to 로 duration 동안 Lerp 이동시킨다.</summary>
    public static IEnumerator MoveOverTime(Transform mover, Vector2 from, Vector2 to, float duration)
    {
        if (duration <= 0f) { if (mover != null) mover.position = to; yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            if (mover == null) yield break;
            mover.position = Vector2.Lerp(from, to, t / duration);
            yield return null;
        }
        if (mover != null) mover.position = to;
    }

    /// <summary>
    /// 적을 pushDir 로 force 만큼 duration 동안 밀어낸다.
    /// 슈퍼아머면 아머만 깎고 종료, 아니면 (옵션) 취약 부여 후 벽에 클램프하며 이동.
    /// 기존 5개 스킬(Tetsuzanko/FlickerJab/GuardBreak/NeedleSobat/Leap)에 복붙돼 있던 로직을 통합한 것.
    /// </summary>
    public static IEnumerator PushEnemy(Transform enemy, Vector2 pushDir, float force, float duration,
                                        bool applyVulnerability = true, float superArmorDamage = 30f)
    {
        if (enemy == null) yield break;

        // 넉백 경직 및 돌진/공격 인터럽트: 수동 Lerp 이동을 타므로 물리 힘은 0으로 전달(충돌 예방)
        var entity = enemy.GetComponent<BaseEntity>() ?? enemy.GetComponentInChildren<BaseEntity>();
        if (entity != null) entity.ApplyKnockback(Vector2.zero);

        var status = enemy.GetComponentInChildren<CharacterStatus>() ?? enemy.GetComponentInParent<CharacterStatus>();
        if (status != null)
        {
            if (status.HasSuperArmor) { status.DamageSuperArmor(superArmorDamage); yield break; }
            if (applyVulnerability) status.ApplyVulnerability(true); // 실제로 밀려나므로 밀기에 묶인 취약 부여 작동
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        Vector2 targetPos = startPos + pushDir * force;
        int obstacleMask = Layers.WallObstacle;

        // 몬스터 콜라이더 크기로 충돌 반지름 산정
        var enemyCol = enemy.GetComponent<Collider2D>();
        float checkRadius = 0.3f;
        if (enemyCol != null)
        {
            if (enemyCol is CircleCollider2D circle) checkRadius = circle.radius * enemy.localScale.x;
            else checkRadius = Mathf.Max(enemyCol.bounds.extents.x, enemyCol.bounds.extents.y);
        }

        while (elapsed < duration)
        {
            if (enemy == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            Vector2 nextPos = Vector2.Lerp(startPos, targetPos, t);
            Vector2 moveDir = nextPos - (Vector2)enemy.position;
            float moveDist = moveDir.magnitude;

            if (moveDist > 0.001f)
            {
                RaycastHit2D hit = Physics2D.CircleCast(enemy.position, checkRadius, moveDir.normalized, moveDist, obstacleMask);
                if (hit.collider != null)
                {
                    // 충돌 지점에서 벽 바깥 법선 방향으로 반지름+안전오차만큼 밀착
                    enemy.position = hit.point + hit.normal * (checkRadius * 1.02f);
                    yield break;
                }
                enemy.position = nextPos;
            }
            yield return null;
        }

        if (enemy != null)
        {
            Vector2 moveDir = targetPos - (Vector2)enemy.position;
            float moveDist = moveDir.magnitude;
            if (moveDist > 0.001f)
            {
                RaycastHit2D hit = Physics2D.CircleCast(enemy.position, checkRadius * 0.9f, moveDir.normalized, moveDist, obstacleMask);
                enemy.position = hit.collider != null ? (Vector2)hit.centroid : targetPos;
            }
        }
    }
}
