using UnityEngine;
using System.Collections;

[CreateAssetMenu(fileName = "PlayerMainDeal", menuName = "Necromancer/Skills/Player/A_MainDeal_Strike")]
public class PlayerMainDealSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float damageRadius = 2f;
    public float firstHitDamage = 10f;
    public float secondHitDamage = 25f;
    public float hitDelay = 0.2f;
    
    public override void ExecuteSkill(Transform user, Transform target = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.StartCoroutine(AttackRoutine(player));
    }

    private IEnumerator AttackRoutine(PlayerController player)
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = (mousePos - (Vector2)player.transform.position).normalized;
        Vector2 attackCenter = (Vector2)player.transform.position + dir * 1.5f;

        Debug.Log("<color=cyan>[Player Skill A]</color> 1타 타격!");
        
        bool hasInvokedKeyword = false;
        System.Action<CharacterHealth> onStrikeSuccess = (health) => {
            if (!hasInvokedKeyword) {
                hasInvokedKeyword = true;
                Debug.Log("<color=cyan>[Player Skill A]</color> 1타 적중! (호출: Strike)");
                GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.Strike);
            }
        };

        SpawnHitBox(player.gameObject, attackCenter, damageRadius, firstHitDamage, onStrikeSuccess);

        yield return new WaitForSeconds(hitDelay);

        Debug.Log("<color=cyan>[Player Skill A]</color> 2타 추가 공격!");
        Vector2 secondAttackCenter = (Vector2)player.transform.position + dir * 2.0f;
        SpawnHitBox(player.gameObject, secondAttackCenter, damageRadius * 1.5f, secondHitDamage, null);
    }

    private void SpawnHitBox(GameObject attacker, Vector2 center, float radius, float damage, System.Action<CharacterHealth> onHit)
    {
        if (hitBoxPrefab == null) return;

        BaseHitBox box = Instantiate(hitBoxPrefab, center, Quaternion.identity);
        box.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
        
        DamageInfo info = new DamageInfo(damage, DamageType.Physical, attacker, false, 1f, false, "PlayerStrike", false, true, 2f);
        box.Init(info, LayerMask.GetMask("Enemy"), 0.2f, 0f, true, onHit);
    }
}
