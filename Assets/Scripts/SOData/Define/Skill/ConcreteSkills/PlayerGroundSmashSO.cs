using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerGroundSmash", menuName = "Necromancer/Skills/Player/Physical/GroundSmash")]
public class PlayerGroundSmashSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float gatherRadius = 4f; // 2칸 정도
    public float gatherDuration = 0.2f;
    public float damageMultiplier = 0f; // 기본 데미지는 없음 (기획서: 바닥 부수기 = 끌어당김 + 취약 1 부여)
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.StartSkillCasting(SmashRoutine(player));
    }

    private IEnumerator SmashRoutine(PlayerController player)
    {
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
            
            float finalDamage = player.Stat.ATK * damageMultiplier;
            DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Ground Smash!");
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
            // [추가] 실제로 당겨지므로 당기기(Pull)에 묶여있는 취약 부여 작동!
            status.ApplyVulnerability(true);
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        // 플레이어에게서 약간 떨어진 위치까지만 당기기
        Vector2 targetPos = center + (startPos - center).normalized * 0.5f;

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
