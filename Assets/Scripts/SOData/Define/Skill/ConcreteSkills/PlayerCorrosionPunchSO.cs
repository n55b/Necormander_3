using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerCorrosionPunch", menuName = "Necromancer/Skills/Player/Physical/CorrosionPunch")]
public class PlayerCorrosionPunchSO : PlayerSkillSO
{
    [Header("Skill Settings")]
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 3.5f;
    public float hitWidth = 2f;
    public float damageMultiplier = 1.8f; // 기본 공격력의 180%
    public float chargeTime = 1f;

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) {
            Debug.LogError("ExecuteSkill: player is null!");
            return;
        }
        if (hitBoxPrefab == null) {
            Debug.LogError("ExecuteSkill: hitBoxPrefab is null! (The prefab is not properly linked in the Inspector)");
            return;
        }

        player.StartSkillCasting(ChargeAndPunch(player));
    }

    private IEnumerator ChargeAndPunch(PlayerController player)
    {
        // 충전 이펙트나 애니메이션이 있다면 여기서 재생
        // 일단 1초 대기
        yield return new WaitForSeconds(chargeTime);

        if (player == null) yield break;

        PlaySkillSound();
        ShakeCamera();

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        
        BaseHitBox box = Instantiate(hitBoxPrefab, startPos, Quaternion.Euler(0, 0, angle));
        box.transform.localScale = new Vector3(hitDistance, hitWidth, 1f);
        
        float finalDamage = GetBaseDamage(player.Stat) * damageMultiplier;
        DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, false, 1f, false, "Corrosion Punch");
        

        box.Init(info, Layers.EnemyMask, 0.15f, 0f, true);
    }
}
