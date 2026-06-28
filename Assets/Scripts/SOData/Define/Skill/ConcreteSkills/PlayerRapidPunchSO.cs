using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerRapidPunch", menuName = "Necromancer/Skills/Player/Physical/RapidPunch")]
public class PlayerRapidPunchSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 3f;
    public float hitWidth = 2f;
    public float damageMultiplier = 1.1f; // 기본 공격력의 110%
    public int punchCount = 3;
    public float timeBetweenPunches = 0.15f;
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        
        player.StartSkillCasting(RapidPunchRoutine(player));
    }

    private IEnumerator RapidPunchRoutine(PlayerController player)
    {
        for (int i = 0; i < punchCount; i++)
        {
            PlaySkillSound();
            ShakeCamera();

            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 startPos = player.transform.position;
            Vector2 dir = (mousePos - startPos).normalized;
            if (dir == Vector2.zero) dir = Vector2.right;

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            if (hitBoxPrefab != null)
            {
                Vector2 attackCenter = startPos + dir * (hitDistance * 0.5f);
                BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
                box.transform.localScale = new Vector3(hitDistance, hitWidth, 1f);
                
                float finalDamage = player.Stat.ATK * damageMultiplier;
                // isBasicAttack = true 로 설정
                DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, true, $"Rapid Punch {i+1}!");
                
                System.Action<CharacterHealth> onHit = (health) => {
                    Debug.Log($"<color=yellow>[Physical]</color> 둥둥타 {i+1}타 적중! (기본 공격 판정)");
                };

                box.Init(info, LayerMask.GetMask("Enemy"), 0.1f, 0f, true, onHit);
            }

            yield return new WaitForSeconds(timeBetweenPunches);
        }
    }
}
