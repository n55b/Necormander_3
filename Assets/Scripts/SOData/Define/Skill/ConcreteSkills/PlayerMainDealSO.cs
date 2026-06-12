using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerMainDeal", menuName = "Necromancer/Skills/Player/A_MainDeal_Strike")]
public class PlayerMainDealSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float damageRadius = 2f;
    public float firstHitDamage = 10f;
    public float secondHitDamage = 25f;
    public float hitDelay = 0.2f;
    public float maxRange = 5f;
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.StartCoroutine(AttackRoutine(player));
    }

    private IEnumerator AttackRoutine(PlayerController player)
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 offset = mousePos - (Vector2)player.transform.position;
        Vector2 dir = offset.normalized;
        
        // 최대 범위 제한
        Vector2 attackCenter = (Vector2)player.transform.position + Vector2.ClampMagnitude(offset, maxRange);

        Debug.Log("<color=cyan>[Player Skill A]</color> 1타 타격!");
        
        bool hasInvokedKeyword = false;
        System.Action<CharacterHealth> onStrikeSuccess = (health) => {
            if (!hasInvokedKeyword) {
                hasInvokedKeyword = true;
                Debug.Log("<color=cyan>[Player Skill A]</color> 1타 적중! (호출: Strike)");
            }
            // 적중한 개별 타겟들을 큐에 등록하기 위해 항상 호출 (내부적으로 타겟 누적)
            GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.Strike, health.transform);
        };

        SpawnHitBox(player.gameObject, attackCenter, dir, damageRadius, firstHitDamage, onStrikeSuccess);

        yield return new WaitForSeconds(hitDelay);

        Debug.Log("<color=cyan>[Player Skill A]</color> 2타 추가 공격!");
        Vector2 secondAttackCenter = attackCenter + dir * 1.5f; // 2타는 1타 위치에서 약간 더 앞쪽
        SpawnHitBox(player.gameObject, secondAttackCenter, dir, damageRadius * 1.5f, secondHitDamage, null);
    }

    private void SpawnHitBox(GameObject attacker, Vector2 center, Vector2 dir, float radius, float damage, System.Action<CharacterHealth> onHit)
    {
        if (hitBoxPrefab == null) return;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        BaseHitBox box = Instantiate(hitBoxPrefab, center, Quaternion.Euler(0, 0, angle));
        box.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
        
        DamageInfo info = new DamageInfo(damage, DamageType.Physical, attacker, false, 1f, false, "PlayerStrike", false, true, 2f);
        box.Init(info, LayerMask.GetMask("Enemy"), 0.2f, 0f, true, onHit);
    }
}
