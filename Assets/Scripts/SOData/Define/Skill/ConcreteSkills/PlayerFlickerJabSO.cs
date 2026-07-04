using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 플리커 잽: 전방의 리치가 긴 빠른 잽. 기본 공격력의 100% 피해 + 밀쳐냄.
// 현재 이동 속도가 빠를수록 피해량이 최대 50%까지 증가 (이동속도 6에서 최대치).
[CreateAssetMenu(fileName = "PlayerFlickerJab", menuName = "Necromancer/Skills/Player/Physical/FlickerJab")]
public class PlayerFlickerJabSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 3.5f; // 리치가 긴 편
    public float hitWidth = 1.5f;
    public float damageMultiplier = 1.0f; // 기본 공격력의 100%
    public float maxSpeedBonus = 0.5f;    // 이동속도 보너스 최대치 (+50%)
    public float speedForMaxBonus = 6f;   // 이 속도에서 보너스 최대치 도달
    public float knockbackForce = 4f;
    public float knockbackDuration = 0.2f;

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        if (hitBoxPrefab == null) return;

        // 현재 이동 속도(실제 물리 속도)에 따른 피해 보너스 계산
        float currentSpeed = 0f;
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) currentSpeed = rb.linearVelocity.magnitude;

        float speedRatio = Mathf.Clamp01(currentSpeed / Mathf.Max(0.01f, speedForMaxBonus));
        float speedBonus = speedRatio * maxSpeedBonus;
        float finalMultiplier = damageMultiplier + speedBonus;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 attackCenter = startPos;
        BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
        box.transform.localScale = new Vector3(hitDistance, hitWidth, 1f);

        float finalDamage = player.Stat.ATK * finalMultiplier;
        DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Flicker Jab!");

        List<Transform> pushedRoots = new List<Transform>();
        System.Action<CharacterHealth> onHit = (health) =>
        {
            var stat = health.GetComponent<CharacterStat>();
            if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
            if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();
            if (stat == null) return;

            Transform rootObj = stat.transform.root;
            if (!pushedRoots.Contains(rootObj))
            {
                pushedRoots.Add(rootObj);
                player.StartCoroutine(PushEnemy(rootObj, dir));
            }
        };

        box.Init(info, LayerMask.GetMask("Enemy"), 0.15f, 0f, true, onHit);
    }

    private IEnumerator PushEnemy(Transform enemy, Vector2 pushDir)
    {
        if (enemy == null) yield break;

        // [추가] 넉백 경직 및 돌진/공격 인터럽트 적용
        var entity = enemy.GetComponent<BaseEntity>() ?? enemy.GetComponentInChildren<BaseEntity>();
        if (entity != null)
        {
            entity.ApplyKnockback(Vector2.zero); // 수동 Lerp 이동을 타므로 물리 힘은 zero 전달해 충돌 예방
        }

        var status = enemy.GetComponentInChildren<CharacterStatus>();
        if (status == null) status = enemy.GetComponentInParent<CharacterStatus>();
        if (status != null)
        {
            if (status.HasSuperArmor)
            {
                status.DamageSuperArmor(30f);
                yield break;
            }
            status.ApplyVulnerability(true);
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        Vector2 targetPos = startPos + pushDir * knockbackForce;
        
        int obstacleMask = LayerMask.GetMask("Wall", "Obstacle");
        
        // 몬스터 콜라이더 크기 구하기
        var enemyCol = enemy.GetComponent<Collider2D>();
        float checkRadius = 0.3f;
        if (enemyCol != null)
        {
            if (enemyCol is CircleCollider2D circle) checkRadius = circle.radius * enemy.localScale.x;
            else checkRadius = Mathf.Max(enemyCol.bounds.extents.x, enemyCol.bounds.extents.y);
        }

        while (elapsed < knockbackDuration)
        {
            if (enemy == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / knockbackDuration;
            
            Vector2 nextPos = Vector2.Lerp(startPos, targetPos, t);
            Vector2 moveDir = nextPos - (Vector2)enemy.position;
            float moveDist = moveDir.magnitude;
            
            if (moveDist > 0.001f)
            {
                // 충돌 반지름 마진을 1.0f로 원의 축소를 방지하고 온전한 크기 검출
                RaycastHit2D hit = Physics2D.CircleCast(enemy.position, checkRadius * 1.0f, moveDir.normalized, moveDist, obstacleMask);
                if (hit.collider != null)
                {
                    // hit.centroid 대신 충돌지점에서 벽 바깥 법선(normal) 방향으로 반지름+안전오차 만큼 떨어진 포지션 밀착 지정
                    enemy.position = hit.point + hit.normal * (checkRadius * 1.02f);
                    yield break;
                }
                else
                {
                    enemy.position = nextPos;
                }
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
                if (hit.collider != null)
                {
                    enemy.position = hit.centroid;
                }
                else
                {
                    enemy.position = targetPos;
                }
            }
        }
    }
}
