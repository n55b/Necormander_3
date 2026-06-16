using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MinionWarrior_CorrosionSlash", menuName = "Necromancer/Skills/Minion/A_Warrior_CorrosionSlash")]
public class WarriorMinionSkillSO : MinionSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float hitRadius = 2.0f;
    public float baseDamage = 20f;
    public float corrosionTime = 3f;

public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        
        List<AllyController> warriors = new List<AllyController>();
        var allyManager = player.GetComponent<AllyManager>();
        if (allyManager != null)
        {
            warriors = allyManager.GetAliveAllies(CommandData.SkeletonWarrior);
            foreach (var minion in warriors)
            {
                minion.EnterSkillState();
                minion.ExitSkillState(); 
            }
        }

        if (warriors.Count == 0)
        {
            Debug.Log("<color=gray>[Minion Skill A]</color> 소환된 전사가 없습니다.");
            return;
        }

        bool hasInvokedKeyword = false;
        System.Action<CharacterHealth> onSlashHit = (health) => {
            var stat = health.GetComponent<CharacterStat>();
            if (stat != null && stat.Status != null)
                stat.Status.SetDebuffBool(DebuffBoolType.Corroded, corrosionTime);

            if (!hasInvokedKeyword)
            {
                hasInvokedKeyword = true;
                Debug.Log($"<color=magenta>[Minion Skill A]</color> 전사 참격 적중! 부식 적용 (호출: Corrosion)");
            }
            GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.Corrosion, health.transform);
        };

        foreach (var w in warriors)
        {
            if (w == null || w.Stats.Health.IsDead) continue;

            Vector2 wPos = w.transform.position;
            Transform closestTarget = null;
            float minDist = float.MaxValue;
            
            if (validTargets != null && validTargets.Count > 0)
            {
                foreach (var vt in validTargets)
                {
                    if (vt == null) continue;
                    var health = vt.GetComponent<CharacterHealth>();
                    if (health != null && health.IsDead) continue;
                    float dist = Vector2.Distance(wPos, vt.position);
                    if (dist < minDist) { minDist = dist; closestTarget = vt; }
                }
            }

            Vector2 targetPos = closestTarget != null ? (Vector2)closestTarget.position : (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dir = (targetPos - wPos).normalized;
            if (dir == Vector2.zero) dir = Vector2.right;

            Vector2 attackCenter = targetPos;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            if (hitBoxPrefab != null)
            {
                BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
                box.transform.localScale = new Vector3(hitRadius * 2f, hitRadius * 2f, 1f);
                DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, w.gameObject, false, 1f, false, "Corrosion Slash!");
                box.Init(info, LayerMask.GetMask("Enemy"), 0.3f, 0f, true, onSlashHit);
            }
        }
    }
}
