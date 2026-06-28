using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum MinionActionType
{
    DamageOnly,             // 데미지만 입힘
    DamageAndPush,          // 데미지 + 밀치기
    DamageAndPull,          // 데미지 + 당기기
    ApplyStun,              // 기절 부여
    ApplyStrike,            // 격파 부여
    ApplySmash,             // 강타 부여
    StunExtension,          // 기절 시간 연장
    ApplyCorrosion          // 부식 부여 (전사 미니언 통합용)
}

[CreateAssetMenu(fileName = "MinionActionSkill", menuName = "Necromancer/Skills/Minion/ActionSkill")]
public class MinionActionSkillSO : MinionSkillSO
{
    public MinionActionType actionType;
    public bool useHitBox = false;
    public BaseHitBox hitBoxPrefab;
    public float hitRadius = 1.5f;
    public float damageMultiplier = 1.2f; 
    public float forceAmount = 4f; // 넉백/끌어당김 힘
    public float forceDuration = 0.2f;

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        var ally = user.GetComponent<AllyController>();
        if (ally == null || ally.Stats.Health.IsDead) return;

        Vector2 playerPos = user.position;
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            playerPos = GameManager.Instance.PLAYERCONTROLLER.transform.position;
        }

        // 1. 적절한 타겟 찾기 (플레이어 기준 가장 가까운 대상)
        Transform closestTarget = null;
        float minDist = float.MaxValue;
        
        if (validTargets != null && validTargets.Count > 0)
        {
            foreach (var vt in validTargets)
            {
                if (vt == null) continue;
                var health = vt.GetComponentInChildren<CharacterHealth>();
                if (health == null) health = vt.GetComponentInParent<CharacterHealth>();
                if (health != null && health.IsDead) continue;

                // 취약 연계 스킬이라면 타겟이 여전히 취약을 갖고 있는지 확인
                if (this.reactKeyword == SkillKeyword.Vulnerability)
                {
                    var status = vt.GetComponentInChildren<CharacterStatus>();
                    if (status == null) status = vt.GetComponentInParent<CharacterStatus>();
                    if (status == null || status.VulnerabilityStacks <= 0) continue;
                }

                float dist = Vector2.Distance(playerPos, vt.position);
                if (dist < minDist) { minDist = dist; closestTarget = vt; }
            }
        }

        if (closestTarget == null)
        {
            // validTargets가 없으면 그냥 마우스 위치로
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            closestTarget = new GameObject("TempTarget").transform;
            closestTarget.position = mousePos;
            Destroy(closestTarget.gameObject, 1f); // 임시 타겟 파괴
        }

        // 2. 텔레포트 및 넉백 방향 계산 (플레이어 기준)
        Vector2 dirFromPlayer = ((Vector2)closestTarget.position - playerPos).normalized;
        if (dirFromPlayer == Vector2.zero) dirFromPlayer = Vector2.right;

        Vector2 teleportPos;
        if (actionType == MinionActionType.DamageAndPull)
        {
            // 당기기의 경우 타겟 등 뒤로 이동
            teleportPos = (Vector2)closestTarget.position + dirFromPlayer * 0.5f;
        }
        else
        {
            // 나머지는 플레이어와 타겟 사이로 이동
            teleportPos = (Vector2)closestTarget.position - dirFromPlayer * 0.5f;
        }
        
        user.position = teleportPos;

        PlaySkillSound();
        ShakeCamera();
        DoHitStop();

        Debug.Log($"<color=cyan>[Minion Skill]</color> 미니언이 '{skillName}' 스킬을 사용했습니다! (대상: {closestTarget.name})");

        float finalDamage = ally.Stats.ATK * damageMultiplier;

        // 3. 공격 실행
        if (useHitBox && hitBoxPrefab != null)
        {
            float angle = Mathf.Atan2(dirFromPlayer.y, dirFromPlayer.x) * Mathf.Rad2Deg;
            BaseHitBox box = Instantiate(hitBoxPrefab, closestTarget.position, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(hitRadius * 2f, hitRadius * 2f, 1f);
            
            DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, user.gameObject, false, 1f, false, !string.IsNullOrEmpty(skillName) ? skillName : $"Action {actionType}");
            
            bool hasInvokedKeyword = false;
            System.Action<CharacterHealth> onHit = (health) => {
                var stat = health.GetComponent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();
                if (stat == null) return;

                if (!hasInvokedKeyword)
                {
                    hasInvokedKeyword = true;
                    Debug.Log($"<color=yellow>[MinionAction]</color> {actionType} 발동! (미니언 시전)");
                }

                ApplyActionEffect(stat, stat.transform.root, ally, dirFromPlayer, teleportPos);
            };
            box.Init(info, LayerMask.GetMask("Enemy"), 0.2f, 0f, true, onHit);
        }
        else
        {
            // 히트박스 없이 즉시 타격
            var health = closestTarget.GetComponentInChildren<CharacterHealth>();
            if (health == null) health = closestTarget.GetComponentInParent<CharacterHealth>();

            if (health != null && !health.IsDead)
            {
                DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, user.gameObject, false, 1f, false, !string.IsNullOrEmpty(skillName) ? skillName : $"Action {actionType}");
                health.GetDamage(info);
                
                var stat = health.GetComponent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();

                if (stat != null)
                {
                    ApplyActionEffect(stat, stat.transform.root, ally, dirFromPlayer, teleportPos);
                }
            }
        }
    }

    private void ApplyActionEffect(CharacterStat stat, Transform targetTransform, AllyController ally, Vector2 dirFromPlayer, Vector2 teleportPos)
    {
        switch (actionType)
        {
            case MinionActionType.DamageOnly:
                break;
            case MinionActionType.DamageAndPush:
                ally.StartCoroutine(PushEnemy(targetTransform, dirFromPlayer));
                break;
            case MinionActionType.DamageAndPull:
                ally.StartCoroutine(PushEnemy(targetTransform, -dirFromPlayer));
                break;
            case MinionActionType.ApplyStun:
                stat.Status.ApplyStatusEffect(SkillKeyword.Stun, ally.gameObject, false);
                break;
            case MinionActionType.ApplyStrike:
                stat.Status.ApplyStatusEffect(SkillKeyword.Strike, ally.gameObject, false);
                break;
            case MinionActionType.ApplySmash:
                stat.Status.ApplyStatusEffect(SkillKeyword.Smash, ally.gameObject, false);
                break;
            case MinionActionType.StunExtension:
                if (stat.Status.GetDebuffBool(DebuffBoolType.Stunned))
                {
                    stat.Status.SetDebuffBool(DebuffBoolType.Stunned, 1f); // 기절 시간 1초 추가
                }
                break;
            case MinionActionType.ApplyCorrosion:
                stat.Status.SetDebuffBool(DebuffBoolType.Corroded, 3f);
                stat.Status.ApplyDebuff(DebuffType.Corrosion, ally.gameObject, false);
                break;
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
            // 미니언 공격이므로 isPlayerApplied = false로 취약 부여
            status.ApplyVulnerability(false);
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        Vector2 targetPos = startPos + pushDir * forceAmount;
        
        int obstacleMask = LayerMask.GetMask("Wall", "Obstacle");
        
        // 몬스터 콜라이더 크기 구하기
        var enemyCol = enemy.GetComponent<Collider2D>();
        float checkRadius = 0.3f;
        if (enemyCol != null)
        {
            if (enemyCol is CircleCollider2D circle) checkRadius = circle.radius * enemy.localScale.x;
            else checkRadius = Mathf.Max(enemyCol.bounds.extents.x, enemyCol.bounds.extents.y);
        }

        while (elapsed < forceDuration)
        {
            if (enemy == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / forceDuration;
            
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

    private IEnumerator PullEnemy(Transform enemy, Vector2 center)
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
            // 미니언 공격이므로 isPlayerApplied = false로 취약 부여
            status.ApplyVulnerability(false);
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        Vector2 targetPos = center + (startPos - center).normalized * 0.5f;

        while (elapsed < forceDuration)
        {
            if (enemy == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / forceDuration;
            enemy.position = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }
        if (enemy != null) enemy.position = targetPos;
    }
}
