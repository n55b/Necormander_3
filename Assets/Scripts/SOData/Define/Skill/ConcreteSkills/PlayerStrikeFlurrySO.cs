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
        
        player.StartSkillCasting(FlurryRoutine(player));
    }

    private IEnumerator FlurryRoutine(PlayerController player)
    {
        for (int i = 0; i < hitCount; i++)
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
                Vector2 attackCenter = startPos;
                BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
                box.transform.localScale = new Vector3(hitDistance, hitWidth, 1f);
                
                float finalDamage = player.Stat.ATK * damageMultiplier;
                DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, $"Flurry {i+1}!");
                
                System.Action<CharacterHealth> onHit = (health) => {
                    var stat = health.GetComponent<CharacterStat>();
                    if (stat != null && stat.Status != null)
                    {
                        // 마지막 타격에만 격파 호출할 수도 있지만, 타격마다 호출해도 스택 없으면 무시됨
                        if (i == hitCount - 1)
                        {
                            Debug.Log("<color=red>[Physical]</color> 총난타 적중! (호출: Consume Strike)");
                        }
                        stat.Status.ConsumeVulnerability(SkillKeyword.Strike, player.gameObject, true);
                    }
                };

                box.Init(info, LayerMask.GetMask("Enemy"), 0.05f, 0f, true, onHit);
            }

            yield return new WaitForSeconds(timeBetweenHits);
        }
    }
}
