using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerGuardBreak", menuName = "Necromancer/Skills/Player/Physical/GuardBreak")]
public class PlayerGuardBreakSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 3f;
    public float hitWidth = 2f;
    public float damageMultiplier = 1.2f; // 기본 공격력의 120%
    public float knockbackForce = 4f;
    public float knockbackDuration = 0.2f;
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (hitBoxPrefab != null)
        {
            Vector2 attackCenter = startPos;
            BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(hitDistance, hitWidth, 1f);
            
            float finalDamage = player.Stat.ATK * damageMultiplier;
            DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Guard Break!");
            
            bool hasInvokedKeyword = false;
            System.Action<CharacterHealth> onHit = (health) => {
                if (!hasInvokedKeyword) {
                    hasInvokedKeyword = true;
                    Debug.Log($"<color=cyan>[Physical]</color> '{skillName}' 적중! (호출: Vulnerability)");
                }
                
                var stat = health.GetComponent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();

                if (stat != null)
                {
                    // 넉백 처리 (최상단 transform 기준)
                    player.StartCoroutine(PushEnemy(stat.transform.root, dir));
                }
            };

            box.Init(info, LayerMask.GetMask("Enemy"), 0.2f, 0f, true, onHit);
        }
    }

    private IEnumerator PushEnemy(Transform enemy, Vector2 pushDir)
    {
        if (enemy == null) yield break;

        var status = enemy.GetComponentInChildren<CharacterStatus>();
        if (status == null) status = enemy.GetComponentInParent<CharacterStatus>();
        if (status != null)
        {
            if (status.HasSuperArmor)
            {
                status.DamageSuperArmor(30f);
                yield break;
            }
            // [추가] 실제로 밀려나므로 밀기(Push)에 묶여있는 취약 부여 작동!
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
                RaycastHit2D hit = Physics2D.CircleCast(enemy.position, checkRadius * 0.9f, moveDir.normalized, moveDist, obstacleMask);
                if (hit.collider != null)
                {
                    enemy.position = hit.centroid;
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
