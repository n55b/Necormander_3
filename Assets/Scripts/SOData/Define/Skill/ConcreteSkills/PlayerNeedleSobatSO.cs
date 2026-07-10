using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 니들 소배트: 전방으로 주먹을 지르며, 닿은 적에게 기본 공격력의 120% 피해를 주고 밀쳐냅니다.
[CreateAssetMenu(fileName = "PlayerNeedleSobat", menuName = "Necromancer/Skills/Player/Physical/NeedleSobat")]
public class PlayerNeedleSobatSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 2.5f;
    public float hitWidth = 1.8f;
    public float damageMultiplier = 1.2f; // 기본 공격력의 120%
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

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        Vector2 attackCenter = startPos;
        BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
        box.transform.localScale = new Vector3(hitDistance, hitWidth, 1f);

        float finalDamage = player.Stat.ATK * damageMultiplier;
        DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Needle Sobat!");

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
