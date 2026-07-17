using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 도약: 바라보는 방향으로 3칸 거리를 도약합니다.
// 도약 중 적에게 닿거나, 최대 사거리에서 착지하면 기본 공격력의 100% 피해 + 1칸 범위 밀쳐냄.
[CreateAssetMenu(fileName = "PlayerLeap", menuName = "Necromancer/Skills/Player/Physical/Leap")]
public class PlayerLeapSO : PlayerSkillSO
{
    public float leapDistance = 3f;     // 3칸
    public float leapDuration = 0.25f;
    public float impactRadius = 1f;     // 1칸 범위
    public float damageMultiplier = 1.0f; // 기본 공격력의 100%
    public float knockbackForce = 4f;
    public float knockbackDuration = 0.2f;
    public float midFlightCheckRadius = 0.5f; // 도약 중 적과 닿았는지 체크할 반경

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);

        PlaySkillSound();
        ShakeCamera();
        player.StartSkillCasting(LeapRoutine(player));
    }

    private IEnumerator LeapRoutine(PlayerController player)
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        // 대시와 동일 판정: 도약 착지점이 벽/낭떠러지를 넘지 않도록 제동한다.
        Vector2 fullTargetPos = SkillCombatUtil.GetSafeDestination(startPos, dir, leapDistance);
        int enemyMask = Layers.EnemyMask;

        float elapsed = 0f;
        Vector2 landedPos = fullTargetPos;
        bool hitSomething = false;

        while (elapsed < leapDuration)
        {
            if (player == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / leapDuration;
            Vector2 currentPos = Vector2.Lerp(startPos, fullTargetPos, t);
            player.transform.position = currentPos;

            // 도약 중 적과 닿았는지 확인
            Collider2D[] hits = Physics2D.OverlapCircleAll(currentPos, midFlightCheckRadius, enemyMask);
            if (hits.Length > 0)
            {
                landedPos = currentPos;
                hitSomething = true;
                break;
            }

            yield return null;
        }

        if (!hitSomething)
        {
            player.transform.position = fullTargetPos;
            landedPos = fullTargetPos;
        }

        // 착지 / 충돌 지점에서 폭발형 임팩트
        Collider2D[] impactHits = Physics2D.OverlapCircleAll(landedPos, impactRadius, enemyMask);
        float finalDamage = GetBaseDamage(player.Stat) * damageMultiplier;
        List<Transform> pushedRoots = new List<Transform>();

        foreach (var col in impactHits)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health == null) health = col.GetComponentInParent<CharacterHealth>();
            if (health == null || health.IsDead) continue;

            DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, false, 1f, false, "Leap Impact!");
            health.GetDamage(info);

            var stat = health.GetComponent<CharacterStat>();
            if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
            if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();
            if (stat == null) continue;

            Transform rootObj = stat.transform.root;
            if (!pushedRoots.Contains(rootObj))
            {
                pushedRoots.Add(rootObj);
                Vector2 pushDir = ((Vector2)rootObj.position - landedPos).normalized;
                if (pushDir == Vector2.zero) pushDir = dir;
                player.StartCoroutine(SkillCombatUtil.PushEnemy(rootObj, pushDir, knockbackForce, knockbackDuration));
            }
        }
    }
}
