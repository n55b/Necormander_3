using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerTetsuzanko", menuName = "Necromancer/Skills/Player/Physical/Tetsuzanko")]
public class PlayerTetsuzankoSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float dashDistance = 4f;
    public float dashDuration = 0.2f;
    public float hitWidth = 2f;
    public float damageMultiplier = 0.8f; // 기본 공격력의 80%
    public float knockbackForce = 5f;
    public float knockbackDuration = 0.2f;
    [Tooltip("벽에 처박혔을 때 추가 피해(ATK x 이 값). 예전 '취약 부여' 대체. 0 이면 없음.")]
    public float wallSlamMultiplier = 0.8f;
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);
        player.StartSkillCasting(DashRoutine(player));
    }

    private IEnumerator DashRoutine(PlayerController player)
    {
        float hitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        if (hitDelay > 0f) yield return new WaitForSeconds(hitDelay);

        if (player == null) yield break;

        PlaySkillSound();
        ShakeCamera();

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        // 대시와 동일 판정: 벽/낭떠러지를 넘지 않도록 돌진 목적지를 제동한다. (히트박스는 아래에서 full dashDistance 유지 → 주먹은 벽까지 닿음)
        Vector2 targetPos = SkillCombatUtil.GetSafeDestination(startPos, dir, dashDistance);

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float finalDamage = ResolveDamage(player.Stat, damageMultiplier);

        // [변경] 기존엔 시전 순간 1프레임짜리 OverlapBox 를 '플레이어 중심'으로 깔아(전방 도달거리 = dashDistance/2 뿐)
        // 판정이 빡빡했다. 이제 돌진 복도 전체(길이 = dashDistance)를 덮는 히트박스를 깔아, 돌진하는 동안 그 안에
        // 있거나 들어온 적을 모두 타격한다. (히트박스는 벽 클램프와 무관하게 full dashDistance → 주먹은 벽까지 닿음)
        if (hitBoxPrefab != null)
        {
            // Center 프리팹은 피벗이 중앙이라, 시작점에서 dashDistance/2 앞에 스폰하면 [시작점 ~ 착지점] 구간을 정확히 덮는다.
            Vector2 boxCenter = startPos + dir * (dashDistance * 0.5f);
            BaseHitBox box = Instantiate(hitBoxPrefab, boxCenter, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(dashDistance, hitWidth, 1f);
            box.hitEffectAngle = angle;

            DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, 1f, "Tetsuzanko!");

            // 적중한 적을 돌진 방향으로 넉백(PushEnemy 가 취약 1스택도 부여) — 루트 기준 1회만, 코루틴은 히트박스보다
            // 오래 사는 player 에서 돌린다(히트박스가 먼저 파괴돼도 넉백이 끊기지 않도록).
            var pushedRoots = new HashSet<Transform>();
            System.Action<CharacterHealth> onHit = (health) =>
            {
                if (health == null) return;
                Transform root = SkillCombatUtil.ResolveEntityTransform(health);
                if (root == null || !pushedRoots.Add(root)) return;
                if (player != null)
                    player.StartCoroutine(SkillCombatUtil.PushEnemy(root, dir, knockbackForce, knockbackDuration,
                        onWallHit: () =>
                        {
                            // 벽에 처박히면 추가 피해(취약 대체). 스킬 갈래로 태그.
                            if (root == null || wallSlamMultiplier <= 0f) return;
                            var h = root.GetComponentInChildren<CharacterHealth>() ?? root.GetComponentInParent<CharacterHealth>();
                            if (h != null && !h.IsDead)
                                h.GetDamage(new DamageInfo(ResolveDamage(player.Stat, wallSlamMultiplier),
                                    ResolveDamageType(), player.gameObject, 1f, "벽꽝!", category: DamageCategory.Skill));
                        }));
            };

            // 돌진 시간 동안 판정 유지(짧은 최소창 보장) → 스쳐 지나가는 적도 놓치지 않는다.
            box.Init(info, Layers.EnemyMask, Mathf.Max(dashDuration, 0.15f), 0f, true, onHit);
        }

        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            if (player == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / dashDuration;
            player.transform.position = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }
        if (player != null) player.transform.position = targetPos;
    }
}
