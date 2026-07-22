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
        // [Fix] 총난타는 3연타(punchCount)를 timeBetweenPunches 간격으로 시전하므로, 손 애니메이션 클립 하나보다 시전 시간이 깁니다.
        // 전체 예상 시전 시간을 계산해 넘겨줘서, HandSkillAnimator가 스킬 도중에 꺼지지 않도록 합니다.
        float estimatedHitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        // 강화로 타수가 늘 수 있으므로 손 모션 유지 시간도 최종 타수 기준으로 잡는다.
        float totalHandHoldDuration = estimatedHitDelay + ResolveHitCount(punchCount) * timeBetweenPunches;
        player.PlayHandSkillAnim(handSkillAnimName, totalHandHoldDuration);
        
        player.StartSkillCasting(RapidPunchRoutine(player));
    }

    private IEnumerator RapidPunchRoutine(PlayerController player)
    {
        // 첫 타격까지만 HandSkill 모션 타이밍에 맞춤 (이후 타격은 timeBetweenPunches 간격 유지)
        float hitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        if (hitDelay > 0f) yield return new WaitForSeconds(hitDelay);

        // [강화 경로] 타수/데미지는 장비 강화레벨을 반영한 값으로 낸다(enhanceEffects 비면 원본과 동일).
        int totalPunches = ResolveHitCount(punchCount);
        for (int i = 0; i < totalPunches; i++)
        {
            if (player == null) yield break;

            // [Fix] 매 펀치마다 손 스킬 애니메이션을 다시 재생해, 클립이 끝난 뒤(마지막 프레임이 비어있을 수 있음) 정지된 채로 남지 않도록 합니다.
            player.PlayHandSkillAnim(handSkillAnimName);

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

                float finalDamage = ResolveDamage(player.Stat, damageMultiplier);
                // isBasicAttack = true 로 설정
                DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, 1f, $"Rapid Punch {i+1}!");

                System.Action<CharacterHealth> onHit = (health) => {
                    Debug.Log($"<color=yellow>[Physical]</color> 둥둥타 {i+1}타 적중! (기본 공격 판정)");
                };

                box.Init(info, Layers.EnemyMask, 0.1f, 0f, true, onHit);
            }

            yield return new WaitForSeconds(timeBetweenPunches);
        }
    }
}
