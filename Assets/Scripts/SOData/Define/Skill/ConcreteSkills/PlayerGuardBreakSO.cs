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
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);

        player.StartCoroutine(HitRoutine(player));
    }

    private IEnumerator HitRoutine(PlayerController player)
    {
        float hitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        if (hitDelay > 0f) yield return new WaitForSeconds(hitDelay);

        if (player == null) yield break;

        PlaySkillSound();
        ShakeCamera();

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

            float finalDamage = ResolveDamage(player.Stat, damageMultiplier);
            DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, 1f, "Guard Break!", category: DamageCategory.Skill);

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
                    player.StartCoroutine(SkillCombatUtil.PushEnemy(stat.transform.root, dir, knockbackForce, knockbackDuration));
                }
            };

            box.Init(info, Layers.EnemyMask, 0.2f, 0f, true, onHit);
        }
    }
}
