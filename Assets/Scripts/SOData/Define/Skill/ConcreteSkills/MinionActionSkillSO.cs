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

    [Header("다단히트 (useHitBox 일 때만)")]
    [Tooltip("몇 번 때릴지. 1 이면 단타.")]
    public int hitCount = 1;
    [Tooltip("타격이 유지되는 시간(초). hitCount 를 이 시간 안에 균등 배분한다.")]
    public float hitDuration = 0.2f;

    public override bool Execute(Transform user, MinionDataSO data, List<Transform> validTargets)
    {
        var caster = user.GetComponent<MinionSkillCaster>();
        if (caster == null) return false; // 코루틴을 돌릴 주체가 없으면 시전 불가
        if (data == null) data = caster.Data;
        if (data == null) return false;

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

                float dist = Vector2.Distance(playerPos, vt.position);
                if (dist < minDist) { minDist = dist; closestTarget = vt; }
            }
        }

        if (closestTarget == null)
        {
            // 칠 대상이 없으면 소환수 강제 돌진을 예방하고 동작을 완전히 차단한다.
            // false 를 돌려 호출자가 쿨타임을 먹이지 않게 한다 (허공에 눌러 6~8초를 날리는 것 방지).
            return false;
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

        // 대시와 동일 판정: 미니언이 벽/낭떠러지를 뚫고 타겟으로 순간이동하지 않도록 목적지 제동.
        Vector2 teleStart = user.position;
        Vector2 teleTo = teleportPos - teleStart;
        float teleDist = teleTo.magnitude;
        if (teleDist > 0.001f)
            teleportPos = SkillCombatUtil.GetSafeDestination(teleStart, teleTo / teleDist, teleDist);

        user.position = teleportPos;

        PlaySkillSound();
        ShakeCamera();
        float animDuration = PlaySkillAnimVisual(user);

        // 타겟을 바라보도록 flipX 설정. 반드시 PlaySkillAnimVisual 뒤여야 한다 —
        // 시전자는 빈 GameObject 라서 비주얼이 자식으로 붙기 전에는 SpriteRenderer 가 없다.
        Vector2 lookDir = ((Vector2)closestTarget.position - (Vector2)user.position).normalized;
        var userSR = user.GetComponentInChildren<SpriteRenderer>();
        if (userSR != null)
        {
            if (lookDir.x > 0.01f) userSR.flipX = true; // 오른쪽 바라봄
            else if (lookDir.x < -0.01f) userSR.flipX = false; // 왼쪽 바라봄
        }

        Debug.Log($"<color=cyan>[Minion Skill]</color> 미니언이 '{skillName}' 스킬을 사용했습니다! (대상: {closestTarget.name})");

        float hitDelay = animDuration * hitTimingRatio;
        if (hitDelay > 0f)
        {
            caster.StartCoroutine(DelayedHit(hitDelay, caster, data, closestTarget, dirFromPlayer, teleportPos));
        }
        else
        {
            DoHitStop();
            DealHit(caster, data, closestTarget, dirFromPlayer, teleportPos);
        }

        return true;
    }

    private IEnumerator DelayedHit(float delay, MinionSkillCaster caster, MinionDataSO data, Transform closestTarget, Vector2 dirFromPlayer, Vector2 teleportPos)
    {
        yield return new WaitForSeconds(delay);
        if (caster == null) yield break; // 대기 중 시전자가 소멸했을 수 있다

        DoHitStop();
        DealHit(caster, data, closestTarget, dirFromPlayer, teleportPos);
    }

    private void DealHit(MinionSkillCaster caster, MinionDataSO data, Transform closestTarget, Vector2 dirFromPlayer, Vector2 teleportPos)
    {
        float finalDamage = data.attack * damageMultiplier;

        // 공격 실행
        if (useHitBox && hitBoxPrefab != null)
        {
            float angle = Mathf.Atan2(dirFromPlayer.y, dirFromPlayer.x) * Mathf.Rad2Deg;
            BaseHitBox box = Instantiate(hitBoxPrefab, caster.transform.position, Quaternion.identity, caster.transform); // 시전자 하위 자식으로 붙여 이동 동기화
            box.transform.localPosition = Vector3.zero; // 시전자 중심 정렬

            box.transform.localRotation = Quaternion.Euler(0, 0, angle);
            box.transform.localScale = new Vector3(hitRadius * 2f, hitRadius * 2f, 1f);

            DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, caster.gameObject, false, 1f, false, !string.IsNullOrEmpty(skillName) ? skillName : $"Action {actionType}");

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

                ApplyActionEffect(stat, stat.transform.root, caster, dirFromPlayer, teleportPos);
            };

            // 다단히트는 BaseHitBox 의 틱 기능으로 낸다. hitCount 를 hitDuration 안에 균등 배분.
            float boxDuration = Mathf.Max(0.05f, hitDuration);
            if (hitCount > 1)
            {
                box.isContinuousDamage = true;
                box.damageTickRate = boxDuration / hitCount;
            }
            else
            {
                box.isContinuousDamage = false;
            }

            box.Init(info, Layers.EnemyMask, boxDuration, 0f, true, onHit);
        }
        else
        {
            // 히트박스 없이 즉시 타격
            if (closestTarget == null) return;

            var health = closestTarget.GetComponentInChildren<CharacterHealth>();
            if (health == null) health = closestTarget.GetComponentInParent<CharacterHealth>();

            if (health != null && !health.IsDead)
            {
                DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, caster.gameObject, false, 1f, false, !string.IsNullOrEmpty(skillName) ? skillName : $"Action {actionType}");
                health.GetDamage(info);

                var stat = health.GetComponent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();

                if (stat != null)
                {
                    ApplyActionEffect(stat, stat.transform.root, caster, dirFromPlayer, teleportPos);
                }
            }
        }
    }

    private void ApplyActionEffect(CharacterStat stat, Transform targetTransform, MinionSkillCaster caster, Vector2 dirFromPlayer, Vector2 teleportPos)
    {
        switch (actionType)
        {
            case MinionActionType.DamageOnly:
                break;
            case MinionActionType.DamageAndPush:
                caster.StartCoroutine(PushEnemy(targetTransform, dirFromPlayer));
                break;
            case MinionActionType.DamageAndPull:
                caster.StartCoroutine(PushEnemy(targetTransform, -dirFromPlayer));
                break;
            case MinionActionType.ApplyStun:
                stat.Status.ApplyStatusEffect(SkillKeyword.Stun, caster.gameObject, false);
                break;
            case MinionActionType.ApplyStrike:
                stat.Status.ApplyStatusEffect(SkillKeyword.Strike, caster.gameObject, false);
                break;
            case MinionActionType.ApplySmash:
                stat.Status.ApplyStatusEffect(SkillKeyword.Smash, caster.gameObject, false);
                break;
            case MinionActionType.StunExtension:
                if (stat.Status.GetDebuffBool(DebuffBoolType.Stunned))
                {
                    stat.Status.SetDebuffBool(DebuffBoolType.Stunned, 1f); // 기절 시간 1초 추가
                }
                break;
            case MinionActionType.ApplyCorrosion:
                stat.Status.SetDebuffBool(DebuffBoolType.Corroded, 3f);
                stat.Status.ApplyDebuff(DebuffType.Corrosion, caster.gameObject, false);
                break;
        }
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
            // 미니언 공격이므로 isPlayerApplied = false로 취약 부여
            status.ApplyVulnerability(false);
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        Vector2 targetPos = startPos + pushDir * forceAmount;
        
        int obstacleMask = Layers.WallMask | Layers.UnsteppableMask;

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
