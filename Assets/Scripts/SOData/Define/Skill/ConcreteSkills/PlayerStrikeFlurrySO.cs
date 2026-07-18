using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerStrikeFlurry", menuName = "Necromancer/Skills/Player/Physical/StrikeFlurry")]
public class PlayerStrikeFlurrySO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 3f;
    public float hitWidth = 2f;
    public float damageMultiplier = 0.3f; // 기본 공격력의 30%
    public int hitCount = 6;
    public float timeBetweenHits = 0.08f; // 매우 빠름
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);
        
        player.StartSkillCasting(FlurryRoutine(player));
    }

    private IEnumerator FlurryRoutine(PlayerController player)
    {
        float hitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        if (hitDelay > 0f) yield return new WaitForSeconds(hitDelay);

        for (int i = 0; i < hitCount; i++)
        {
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
                DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, false, 1f, false, $"Flurry {i+1}!");


                box.Init(info, Layers.EnemyMask, 0.05f, 0f, true);
            }

            yield return new WaitForSeconds(timeBetweenHits);
        }
    }
}
