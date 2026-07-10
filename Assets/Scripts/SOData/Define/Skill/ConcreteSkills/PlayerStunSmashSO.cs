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
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        
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
            
            float finalDamage = player.Stat.ATK * damageMultiplier;
            DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Kkong!");
            
            System.Action<CharacterHealth> onHit = (health) => {
                var stat = health.GetComponent<CharacterStat>();
                if (stat != null && stat.Status != null)
                {
                    Debug.Log("<color=red>[Physical]</color> 꽁! 적중! (호출: Consume Stun)");
                    stat.Status.ConsumeVulnerability(SkillKeyword.Stun, player.gameObject, true);
                }
            };

            box.Init(info, Layers.EnemyMask, 0.2f, 0f, true, onHit);
        }
    }
}
