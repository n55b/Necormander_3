using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MinionSpear_StunThrust", menuName = "Necromancer/Skills/Minion/C_Spear_StunThrust")]
public class SpearMinionSkillSO : MinionSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float thrustDistance = 4f;
    public float hitWidth = 1.5f;
    public float baseDamage = 25f;
    public float stunTime = 2f;

public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        
        List<AllyController> spearmen = new List<AllyController>();
        var allyManager = player.GetComponent<AllyManager>();
        if (allyManager != null)
        {
            spearmen = allyManager.GetAliveAllies(CommandData.SkeletonSpearman);
            foreach (var minion in spearmen)
            {
                minion.EnterSkillState();
                minion.ExitSkillState(); 
            }
        }

        if (spearmen.Count == 0)
        {
            Debug.Log("<color=gray>[Minion Skill C]</color> 소환된 창병이 없습니다.");
            return;
        }

        bool hasInvokedKeyword = false;


        foreach (var s in spearmen)
        {
            if (s == null || s.Stats.Health.IsDead) continue;

            Vector2 sPos = s.transform.position;
            Transform closestTarget = null;
            float minDist = float.MaxValue;
            
            if (validTargets != null && validTargets.Count > 0)
            {
                foreach (var vt in validTargets)
                {
                    if (vt == null) continue;
                    var health = vt.GetComponent<CharacterHealth>();
                    if (health != null && health.IsDead) continue;
                    float dist = Vector2.Distance(sPos, vt.position);
                    if (dist < minDist) { minDist = dist; closestTarget = vt; }
                }
            }

            Vector2 targetPos = closestTarget != null ? (Vector2)closestTarget.position : (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dir = (targetPos - sPos).normalized;
            if (dir == Vector2.zero) dir = Vector2.right;

            Vector2 attackCenter = targetPos;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            if (hitBoxPrefab != null)
            {
                BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
                box.transform.localScale = new Vector3(thrustDistance, hitWidth, 1f);
                DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, s.gameObject, false, 1f, false, "Spear Thrust!");
                System.Action<CharacterHealth> onThrustHit = (health) => {
                    var stat = health.GetComponent<CharacterStat>();
                    if (stat != null && stat.Status != null)
                    {
                        stat.Status.ApplyStatusEffect(SkillKeyword.Stun, s.gameObject, false);
                    }

                    if (!hasInvokedKeyword)
                    {
                        hasInvokedKeyword = true;
                        Debug.Log($"<color=magenta>[Minion Skill C]</color> 창병 찌르기 적중! 기절 및 터트림 발동 (미니언 시전)");
                    }
                };
                
                box.Init(info, LayerMask.GetMask("Enemy"), 0.3f, 0f, true, onThrustHit);
            }
        }
    }
}
