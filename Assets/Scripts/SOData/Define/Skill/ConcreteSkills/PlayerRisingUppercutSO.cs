using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 라이징 어퍼컷: 전방의 적에게 어퍼컷을 날려 기본 공격력의 150% 피해를 주고 띄웁니다.
// (현재 시스템엔 별도의 '띄움' 상태가 없어 취약 1스택 부여 + 연출용 점프 모션으로 대체합니다.)
[CreateAssetMenu(fileName = "PlayerRisingUppercut", menuName = "Necromancer/Skills/Player/Physical/RisingUppercut")]
public class PlayerRisingUppercutSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float hitDistance = 2.2f;
    public float hitWidth = 1.6f;
    public float damageMultiplier = 1.5f; // 기본 공격력의 150%
    public float launchHeight = 0.6f;     // 연출용 띄움 높이
    public float launchDuration = 0.35f;  // 연출용 띄움 지속 시간

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        if (hitBoxPrefab == null) return;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 attackCenter = startPos + dir * (hitDistance * 0.5f);
        BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
        box.transform.localScale = new Vector3(hitDistance, hitWidth, 1f);

        float finalDamage = player.Stat.ATK * damageMultiplier;
        DamageInfo info = new DamageInfo(finalDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Rising Uppercut!");

        System.Action<CharacterHealth> onHit = (health) =>
        {
            var stat = health.GetComponent<CharacterStat>();
            if (stat == null) stat = health.GetComponentInParent<CharacterStat>();
            if (stat == null) stat = health.GetComponentInChildren<CharacterStat>();
            if (stat == null || stat.Status == null) return;

            if (stat.Status.HasSuperArmor)
            {
                stat.Status.DamageSuperArmor(30f);
                return;
            }

            // 부여(띄움) -> 취약 1스택
            stat.Status.ApplyVulnerability(true);

            player.StartCoroutine(LaunchVisual(stat.transform.root));
        };

        box.Init(info, LayerMask.GetMask("Enemy"), 0.15f, 0f, true, onHit);
    }

    // 실제 상태이상은 아니고, 살짝 떠올랐다가 내려오는 연출용 코루틴입니다.
    private IEnumerator LaunchVisual(Transform enemy)
    {
        if (enemy == null) yield break;
        var sr = enemy.GetComponentInChildren<SpriteRenderer>();
        if (sr == null) yield break;

        Transform visualTarget = sr.transform;
        Vector3 originalLocalPos = visualTarget.localPosition;
        float elapsed = 0f;

        while (elapsed < launchDuration)
        {
            if (enemy == null || visualTarget == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / launchDuration;
            float yOffset = Mathf.Sin(t * Mathf.PI) * launchHeight;
            visualTarget.localPosition = originalLocalPos + new Vector3(0f, yOffset, 0f);
            yield return null;
        }
        if (visualTarget != null) visualTarget.localPosition = originalLocalPos;
    }
}
