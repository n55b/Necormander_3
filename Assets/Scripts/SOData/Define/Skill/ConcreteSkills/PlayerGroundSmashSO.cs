using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerGroundSmash", menuName = "Necromancer/Skills/Player/Physical/GroundSmash")]
public class PlayerGroundSmashSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float gatherRadius = 4f; // 2칸 정도
    public float gatherDuration = 0.2f;
    // [26/07/17] 원래 0f 였다 — 이 스킬의 페이로드가 '취약 1 부여'뿐이었기 때문이다.
    // 취약이 사라지면서 아무것도 안 하는 스킬이 돼버려 피해를 넣어줬다.
    // 끌어당김이 붙어 있으니 순수 딜링기보다는 낮게 잡는다.
    public float damageMultiplier = 1.0f;
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);
        player.StartSkillCasting(SmashRoutine(player));
    }

    private IEnumerator SmashRoutine(PlayerController player)
    {
        float hitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        if (hitDelay > 0f) yield return new WaitForSeconds(hitDelay);

        if (player == null) yield break;

        PlaySkillSound();
        ShakeCamera();

        Vector2 center = player.transform.position;
        float angle = 0f;

        bool hasInvokedKeyword = false;
        System.Action<CharacterHealth> onSmashHit = (health) => {
            if (!hasInvokedKeyword) {
                hasInvokedKeyword = true;
                Debug.Log("<color=cyan>[Physical]</color> 바닥 부수기 적중!");
            }
        };

        if (hitBoxPrefab != null)
        {
            BaseHitBox box = Instantiate(hitBoxPrefab, center, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(gatherRadius * 2f, gatherRadius * 2f, 1f);

            float finalDamage = GetBaseDamage(player.Stat) * damageMultiplier;
            DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, 1f, "Ground Smash!");
            box.Init(info, Layers.EnemyMask, 0.1f, 0f, true, onSmashHit);
        }

        // 당겨오기 처리
        Collider2D[] hitEnemies;
        bool isCircle = false;
        if (hitBoxPrefab != null)
        {
            var prefabCol = hitBoxPrefab.GetComponent<Collider2D>();
            if (prefabCol is CircleCollider2D) isCircle = true;
        }

        if (isCircle)
        {
            hitEnemies = Physics2D.OverlapCircleAll(center, gatherRadius, Layers.EnemyMask);
        }
        else
        {
            hitEnemies = Physics2D.OverlapBoxAll(center, new Vector2(gatherRadius * 2f, gatherRadius * 2f), angle, Layers.EnemyMask);
        }

        List<Coroutine> pullCoroutines = new List<Coroutine>();
        List<Transform> pulledRoots = new List<Transform>();

        foreach (var col in hitEnemies)
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
                    if (!pulledRoots.Contains(rootObj))
                    {
                        pulledRoots.Add(rootObj);
                        pullCoroutines.Add(player.StartCoroutine(PullEnemy(rootObj, center)));
                    }
                }
            }
        }

        if (pulledRoots.Count > 0) SkillCombatUtil.NotifyEnemyDisplaced(); // 끌기 성공 → 발동형 버프 트리거

        foreach (var c in pullCoroutines)
        {
            yield return c;
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
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        // 플레이어에게서 약간 떨어진 위치까지만 당기기 (벽/낭떠러지를 관통해 끌려오지 않도록 대시와 동일 판정으로 제동)
        Vector2 rawTarget = center + (startPos - center).normalized * 0.5f;
        Vector2 toTarget = rawTarget - startPos;
        float pullDist = toTarget.magnitude;
        Vector2 targetPos = pullDist > 0.001f
            ? SkillCombatUtil.GetSafeDestination(startPos, toTarget / pullDist, pullDist)
            : rawTarget;

        while (elapsed < gatherDuration)
        {
            if (enemy == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / gatherDuration;
            enemy.position = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }
        if (enemy != null) enemy.position = targetPos;
    }
}
