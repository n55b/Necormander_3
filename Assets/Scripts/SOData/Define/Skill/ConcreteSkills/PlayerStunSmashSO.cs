using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerStunSmash", menuName = "Necromancer/Skills/Player/Physical/StunSmash")]
public class PlayerStunSmashSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 3f;
    public float hitWidth = 2f;
    public float damageMultiplier = 1.8f; // 기본 공격력의 180%

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);

        player.StartCoroutine(HitRoutine(player));
    }

    private IEnumerator HitRoutine(PlayerController player)
    {
        // HandSkill 클립 길이 * hitTimingRatio 시점까지 기다렸다가 실제 타격 판정 (사운드/카메라 흔들림도 이 시점에 맞춤)
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

            float finalDamage = GetBaseDamage(player.Stat) * damageMultiplier;
            DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, false, 1f, false, "Kkong!");


            box.Init(info, Layers.EnemyMask, 0.2f, 0f, true);
        }
    }
}
