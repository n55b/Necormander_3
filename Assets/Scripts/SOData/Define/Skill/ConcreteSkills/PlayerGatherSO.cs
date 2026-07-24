using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerGather", menuName = "Necromancer/Skills/Player/B_Gather_StatusEffect")]
public class PlayerGatherSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float gatherRadius = 4f;
    public float gatherDuration = 0.15f; // 좀 더 짧고 강하게
    public float damageMultiplier = 0.6f; // 기본 공격력의 60%
    public float maxRange = 6f;
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);
        player.StartSkillCasting(GatherRoutine(player));
    }

    private IEnumerator GatherRoutine(PlayerController player)
    {
        float hitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        if (hitDelay > 0f) yield return new WaitForSeconds(hitDelay);

        if (player == null) yield break;

        PlaySkillSound();
        ShakeCamera();

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 offset = mousePos - (Vector2)player.transform.position;
        Vector2 dir = offset.normalized;
        if (dir == Vector2.zero) dir = Vector2.right;
        
        // 최대 범위 제한 적용 (평타처럼)
        Vector2 center = (Vector2)player.transform.position + Vector2.ClampMagnitude(offset, maxRange);


        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (hitBoxPrefab != null)
        {
            BaseHitBox box = Instantiate(hitBoxPrefab, center, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(gatherRadius * 2f, gatherRadius * 2f, 1f);
            
            float finalDamage = ResolveDamage(player.Stat, damageMultiplier);
            DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, 1f, "Gather!");
            
            box.Init(info, Layers.EnemyMask, 0.1f, 0f, true);
        }

        // 시각 및 데미지는 HitBox가 주지만, 물리적으로 끌어당기는 것은 직접 수행
        Collider2D[] cols;
        
        // 프리팹에 붙어있는 콜라이더가 원형인지 사각형인지 시스템이 스스로 판별하여 적용!
        bool isCircle = false;
        if (hitBoxPrefab != null)
        {
            var prefabCol = hitBoxPrefab.GetComponent<Collider2D>();
            if (prefabCol is CircleCollider2D) isCircle = true;
        }

        if (isCircle)
        {
            cols = Physics2D.OverlapCircleAll(center, gatherRadius, Layers.EnemyMask);
        }
        else
        {
            cols = Physics2D.OverlapBoxAll(center, new Vector2(gatherRadius * 2f, gatherRadius * 2f), angle, Layers.EnemyMask);
        }
        List<Transform> targetsToMove = new List<Transform>();
        List<Vector2> startPositions = new List<Vector2>();

        foreach (var col in cols)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health == null) health = col.GetComponentInParent<CharacterHealth>();

            if (health != null && !health.IsDead)
            {
                var stat = health.GetComponent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();

                if (stat != null)
                {
                    Transform rootObj = stat.transform.root;
                    // 중복 방지 (같은 캐릭터의 여러 콜라이더가 잡혔을 경우)
                    if (!targetsToMove.Contains(rootObj))
                    {
                        targetsToMove.Add(rootObj);
                        startPositions.Add(rootObj.position);
                    }
                }
            }
        }

        // [추가] 당겨지는 모든 대상에게 공격 취소 및 경직 인터럽트 적용
        for (int i = 0; i < targetsToMove.Count; i++)
        {
            if (targetsToMove[i] != null)
            {
                var entity = targetsToMove[i].GetComponent<BaseEntity>() ?? targetsToMove[i].GetComponentInChildren<BaseEntity>();
                if (entity != null)
                {
                    entity.ApplyKnockback(Vector2.zero); // 수동 Lerp 이동을 진행하므로 물리 힘은 zero 전달해 충돌 예방
                }
            }
        }

        if (targetsToMove.Count > 0) SkillCombatUtil.NotifyEnemyDisplaced(); // 끌기 성공 → 발동형 버프 트리거

        float elapsed = 0f;
        Vector2 lineOrigin = player.transform.position;
        Vector2 lineDir = dir;

        while (elapsed < gatherDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / gatherDuration);
            
            for (int i = 0; i < targetsToMove.Count; i++)
            {
                if (targetsToMove[i] != null)
                {
                    Vector2 p = startPositions[i];
                    Vector2 v = p - lineOrigin;
                    float dot = Vector2.Dot(v, lineDir);
                    Vector2 closestPointOnLine = lineOrigin + lineDir * dot;

                    Vector2 nextPos = Vector2.Lerp(p, closestPointOnLine, t);
                    Vector2 moveDir = nextPos - (Vector2)targetsToMove[i].position;
                    float moveDist = moveDir.magnitude;

                    if (moveDist > 0.001f)
                    {
                        var col = targetsToMove[i].GetComponent<Collider2D>();
                        float checkRadius = 0.3f;
                        if (col != null)
                        {
                            if (col is CircleCollider2D circle) checkRadius = circle.radius * targetsToMove[i].localScale.x;
                            else checkRadius = Mathf.Max(col.bounds.extents.x, col.bounds.extents.y);
                        }

                        int obstacleMask = Layers.WallMask | Layers.UnsteppableMask;
                        // 충돌 반지름 마진 1.0f 적용으로 벽/낭떠러지 관통 예방
                        RaycastHit2D hit = Physics2D.CircleCast(targetsToMove[i].position, checkRadius * 1.0f, moveDir.normalized, moveDist, obstacleMask);
                        if (hit.collider != null)
                        {
                            // 벽 바깥 법선 안전 오프셋 마진으로 고정
                            targetsToMove[i].position = hit.point + hit.normal * (checkRadius * 1.02f);
                        }
                        else
                        {
                            targetsToMove[i].position = nextPos;
                        }
                    }
                }
            }
            yield return null;
        }
    }
}
