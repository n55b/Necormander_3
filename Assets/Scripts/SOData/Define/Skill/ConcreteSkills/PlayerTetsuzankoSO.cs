using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerTetsuzanko", menuName = "Necromancer/Skills/Player/Physical/Tetsuzanko")]
public class PlayerTetsuzankoSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float dashDistance = 4f;
    public float dashDuration = 0.2f;
    public float hitWidth = 2f;
    public float damageMultiplier = 0.8f; // 기본 공격력의 80%
    public float knockbackForce = 5f;
    public float knockbackDuration = 0.2f;
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.StartSkillCasting(DashRoutine(player));
    }

    private IEnumerator DashRoutine(PlayerController player)
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        Vector2 targetPos = startPos + dir * dashDistance;

        // 프리팹 대신 직접 OverlapBox 처리
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 attackCenter = startPos;
        Collider2D[] hits = Physics2D.OverlapBoxAll(attackCenter, new Vector2(dashDistance, hitWidth), angle, Layers.EnemyMask);
        
        float finalDamage = player.Stat.ATK * damageMultiplier;
        bool hasInvokedKeyword = false;
        
        List<Coroutine> pushCoroutines = new List<Coroutine>();
        List<Transform> pushedRoots = new List<Transform>();

        foreach (var col in hits)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health == null) health = col.GetComponentInParent<CharacterHealth>();

            if (health != null && !health.IsDead)
            {
                if (!hasInvokedKeyword) {
                    hasInvokedKeyword = true;
                    Debug.Log($"<color=cyan>[Physical]</color> '{skillName}' 적중! (호출: Vulnerability)");
                }
                
                DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Tetsuzanko!");
                health.GetDamage(info);

                var stat = health.GetComponent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
                if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();

                if (stat != null)
                {
                    Transform rootObj = stat.transform.root;
                    if (!pushedRoots.Contains(rootObj))
                    {
                        pushedRoots.Add(rootObj);
                        pushCoroutines.Add(player.StartCoroutine(SkillCombatUtil.PushEnemy(rootObj, dir, knockbackForce, knockbackDuration)));
                    }
                }
            }
        }

        // 플레이어 이동 처리
        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / dashDuration;
            // 뚫고 지나가는 것을 방지하려면 Rigidbody2D를 써야할 수 있지만 임시로 position 직접 수정
            player.transform.position = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }
        player.transform.position = targetPos;
    }
}
