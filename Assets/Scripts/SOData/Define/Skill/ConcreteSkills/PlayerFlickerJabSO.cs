using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 플리커 잽: 전방의 리치가 긴 빠른 잽. 기본 공격력의 100% 피해 + 밀쳐냄.
// 이동 속도 스탯이 높을수록 피해량이 최대 50%까지 증가 (이동속도 6에서 최대치).
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
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);
        if (hitBoxPrefab == null) return;

        player.StartCoroutine(HitRoutine(player));
    }

    private IEnumerator HitRoutine(PlayerController player)
    {
        float hitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        if (hitDelay > 0f) yield return new WaitForSeconds(hitDelay);

        if (player == null) yield break;

        PlaySkillSound();
        ShakeCamera();

        // 이동 속도 스탯 기반 피해 보너스. 예전엔 rb.linearVelocity(순간 물리 속도)를 읽어서
        // 서서 쓰면 0, 움직이며 쓰면 최대치로 결과가 들쭉날쭉했다. 이제 이속 스탯을 그대로 본다.
        float moveSpeed = player.Stat != null ? player.Stat.MOVESPEED : 0f;

        float speedRatio = Mathf.Clamp01(moveSpeed / Mathf.Max(0.01f, speedForMaxBonus));
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

        float finalDamage = ResolveDamage(player.Stat, finalMultiplier);
        DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, 1f, "Flicker Jab!");

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
                player.StartCoroutine(SkillCombatUtil.PushEnemy(rootObj, dir, knockbackForce, knockbackDuration));
            }
        };

        box.Init(info, Layers.EnemyMask, 0.15f, 0f, true, onHit);
    }
}
