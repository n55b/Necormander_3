using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MinionShield_LeapStrike", menuName = "Necromancer/Skills/Minion/B_Shield_LeapStrike")]
public class ShieldMinionSkillSO : MinionSkillSO
{
    public BaseHitBox hitBoxPrefab;
    [Header("도약 설정")]
    public float jumpDuration = 0.5f;
    public float jumpHeight = 3f;
    public float baseDamage = 30f;
    public float hitRadius = 2.5f;

public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        Debug.Log($"<color=magenta>[Minion Skill B]</color> 방패병 미니언 도약 타격 발동! (반응: {reactKeyword} / 호출: Strike)");

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;

        Vector2 targetPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        List<AllyController> shieldMinions = new List<AllyController>();
        var allyManager = player.GetComponent<AllyManager>();
        if (allyManager != null)
        {
            shieldMinions = allyManager.GetAliveAllies(CommandData.SkeletonShieldbearer);
            foreach (var minion in shieldMinions)
            {
                minion.EnterSkillState();
            }
        }

        if (shieldMinions.Count > 0)
        {
            player.StartCoroutine(MinionJumpRoutine(player, validTargets, shieldMinions));
        }
        else
        {
            Debug.Log("<color=gray>[Minion Skill B]</color> 소환된 방패병이 없어 스킬이 취소되었습니다.");
        }
    }

    private IEnumerator MinionJumpRoutine(PlayerController player, List<Transform> validTargets, List<AllyController> minions)
    {
        float elapsed = 0f;
        List<Vector2> mStartPos = new List<Vector2>();
        List<Vector2> mEndPos = new List<Vector2>();
        List<Transform> mTargetTransforms = new List<Transform>();
        List<Vector2> mTargetPosFallback = new List<Vector2>();

        for (int i = 0; i < minions.Count; i++)
        {
            Vector2 mPos = minions[i].transform.position;
            mStartPos.Add(mPos);

            Transform closestTarget = null;
            float minDist = float.MaxValue;
            if (validTargets != null && validTargets.Count > 0)
            {
                foreach (var vt in validTargets)
                {
                    if (vt == null) continue;
                    var health = vt.GetComponent<CharacterHealth>();
                    if (health != null && health.IsDead) continue; // 죽은 타겟 제외

                    float dist = Vector2.Distance(mPos, vt.position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestTarget = vt;
                    }
                }
            }

            Vector2 targetPos = closestTarget != null ? (Vector2)closestTarget.position : (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
            
            // 타겟이 이동할 수 있으므로 Transform과 Fallback 좌표를 모두 저장
            mTargetTransforms.Add(closestTarget);
            mTargetPosFallback.Add(targetPos);
            
            // 약간의 흩뿌림 오프셋 (모두 똑같은 위치에 떨어지지 않도록)
            float radius = 1.5f;
            float angle = i * (360f / minions.Count) * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            
            mEndPos.Add(targetPos + offset);
        }

        while (elapsed < jumpDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / jumpDuration;
            float height = 4f * jumpHeight * t * (1f - t);

            for (int i = 0; i < minions.Count; i++)
            {
                var m = minions[i];
                if (m != null && !m.Stats.Health.IsDead)
                {
                    Vector2 mCur = Vector2.Lerp(mStartPos[i], mEndPos[i], t);
                    m.transform.position = new Vector3(mCur.x, mCur.y + height, 0f);
                }
            }

            yield return null;
        }

        bool hasInvokedKeyword = false;
        System.Action<CharacterHealth> onLeapHit = (health) => {
            if (!hasInvokedKeyword)
            {
                hasInvokedKeyword = true;
                Debug.Log("<color=magenta>[Minion Skill B]</color> 타격 성공! (호출: Strike)");
            }
            GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.Strike, health.transform);
        };

        for (int i = 0; i < minions.Count; i++)
        {
            var m = minions[i];
            if (m != null && !m.Stats.Health.IsDead)
            {
                m.transform.position = mEndPos[i];
                m.ExitSkillState();
                
                if (hitBoxPrefab != null)
                {
                    // 타겟이 살아있다면 현재 위치, 아니면 점프 전 좌표를 사용하여 정확히 정중앙에 소환
                    Vector2 finalTargetPos = mTargetTransforms[i] != null ? (Vector2)mTargetTransforms[i].position : mTargetPosFallback[i];
                    BaseHitBox box = Instantiate(hitBoxPrefab, finalTargetPos, Quaternion.identity);
                    box.transform.localScale = new Vector3(hitRadius * 2f, hitRadius * 2f, 1f);
                    
                    DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, m.gameObject, false, 1f, false, "Minion Leap!");
                    box.Init(info, LayerMask.GetMask("Enemy"), 0.3f, 0f, true, onLeapHit);
                }
            }
        }

        if (GameManager.Instance != null && GameManager.Instance.cameraManager != null)
        {
            GameManager.Instance.cameraManager.HitShakeCamera();
        }
    }
}
