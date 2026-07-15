using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerWoundPunch", menuName = "Necromancer/Skills/Player/Physical/WoundPunch")]
public class PlayerWoundPunchSO : PlayerSkillSO
{
    [Header("Skill Settings")]
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 3f;
    public float hitWidth = 2f;
    public float damageMultiplier = 1.5f; // 기본 공격력의 150%

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null || hitBoxPrefab == null) return;

        PlaySkillSound();
        ShakeCamera();

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        
        BaseHitBox box = Instantiate(hitBoxPrefab, startPos, Quaternion.Euler(0, 0, angle));
        box.transform.localScale = new Vector3(hitDistance, hitWidth, 1f);
        
        float finalDamage = player.Stat.ATK * damageMultiplier;
        DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Wound Punch");
        
        System.Action<CharacterHealth> onHit = (health) => {
            var stat = health.GetComponent<CharacterStat>();
            if (stat != null && stat.Status != null && !stat.IsDead)
            {
                stat.Status.ApplyElementalDebuff(DebuffStackType.Wound, 1, player.gameObject);
            }
        };

        box.Init(info, Layers.EnemyMask, 0.1f, 0f, true, onHit);
    }
}
