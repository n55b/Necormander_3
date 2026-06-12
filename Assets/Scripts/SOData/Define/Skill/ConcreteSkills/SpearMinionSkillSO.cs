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

    public override void ExecuteSkill(Transform user, Transform target = null)
    {
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
        System.Action<CharacterHealth> onThrustHit = (health) => {
            var stat = health.GetComponent<CharacterStat>();
            if (stat != null && stat.Status != null)
            {
                stat.Status.SetDebuffBool(DebuffBoolType.Stunned, stunTime);
            }

            if (!hasInvokedKeyword)
            {
                hasInvokedKeyword = true;
                Debug.Log($"<color=magenta>[Minion Skill C]</color> 창병 찌르기 적중! 기절 부여 (호출: StatusEffect)");
                GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.StatusEffect);
            }
        };

        foreach (var s in spearmen)
        {
            if (s == null || s.Stats.Health.IsDead) continue;

            Vector2 dir = (mousePos - (Vector2)s.transform.position).normalized;
            Vector2 attackCenter = (Vector2)s.transform.position + dir * (thrustDistance / 2f);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            if (hitBoxPrefab != null)
            {
                BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
                // 네모 반듯한 기본 프리팹을 길고 좁게(직사각형) 변형하여 사용
                box.transform.localScale = new Vector3(thrustDistance, hitWidth, 1f);
                
                DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, s.gameObject, false, 1f, false, "Spear Thrust!");
                box.Init(info, LayerMask.GetMask("Enemy"), 0.3f, 0f, true, onThrustHit);
            }
        }
    }
}
