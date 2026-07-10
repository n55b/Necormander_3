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
                player.StartCoroutine(SkillCombatUtil.PushEnemy(rootObj, dir, knockbackForce, knockbackDuration));
            }
        };

        box.Init(info, Layers.EnemyMask, 0.15f, 0f, true, onHit);
    }
}
