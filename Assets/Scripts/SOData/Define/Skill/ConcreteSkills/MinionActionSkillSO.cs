using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum MinionActionType
{
    // [26/07/17] 상태이상 부여 타입(ApplyStun/Strike/Smash/StunExtension/ApplyCorrosion)은 삭제됐다.
    // 스킬은 상태이상을 걸지 않는다 — 부여 수단은 유물/아이템 전용이다.
    // 값 순서는 그대로 두었다(0/1/2). 에셋이 인덱스로 직렬화돼 있어서 바꾸면 데이터가 어긋난다.
    DamageOnly,             // 데미지만 입힘
    DamageAndPush,          // 데미지 + 밀치기
    DamageAndPull,          // 데미지 + 당기기
}

[CreateAssetMenu(fileName = "MinionActionSkill", menuName = "Necromancer/Skills/Minion/ActionSkill")]
public class MinionActionSkillSO : MinionSkillSO
{
    public MinionActionType actionType;

    [Header("속성/상태이상")]
    [Tooltip("이 스킬 타격의 속성. 마법이면 플레이어의 마법 피해 증폭을 탄다.")]
    public DamageType element = DamageType.Physical;

    [Tooltip("타격 시 부여할 상태이상. None 이면 안 검(지속은 기본값). 예: 네크 부채꼴 = Freeze.")]
    public StatusType onHitStatus = StatusType.None;

    [Header("판정")]
    public bool useHitBox = false;
    public BaseHitBox hitBoxPrefab;
    [Tooltip("원형 판정 반지름(유닛). hitBoxSize 가 0,0 일 때 이 값으로 균등 스케일(예: MeleeDoll 원형).")]
    public float hitRadius = 1.5f;
    [Tooltip("박스 판정 크기(유닛). x=가로, y=세로. 둘 다 > 0 이면 hitRadius 대신 이 비율로 비균등 스케일 " +
             "(예: 사다리꼴/부채꼴 = 납작한 박스). 0,0 이면 hitRadius 원형을 쓴다.")]
    public Vector2 hitBoxSize = Vector2.zero;
    public float damageMultiplier = 1.2f;
    public float forceAmount = 4f; // 넉백/끌어당김 힘
    public float forceDuration = 0.2f;

    [Header("다단히트 (useHitBox 일 때만)")]
    [Tooltip("몇 번 때릴지. 1 이면 단타.")]
    public int hitCount = 1;

    // 타격 구간은 이제 damageState(태그) 나 hitEvent(Aseprite 셀 이벤트)가 정한다.
    // 예전엔 hitDuration(초) -> hitEndRatio(비율) 였는데, 둘 다 그림과 따로 노는 숫자라
    // 애니를 다시 타이밍할 때마다 손으로 맞춰줘야 했다. SkillSO 의 damageState/hitEvent 참조.

    public override bool Execute(Transform user, MinionDataSO data, List<Transform> validTargets)
    {
        var caster = user.GetComponent<MinionSkillCaster>();
        if (caster == null) return false; // 코루틴을 돌릴 주체가 없으면 시전 불가
        if (data == null) data = caster.Data;
        if (data == null) return false;

        Vector2 playerPos = user.position;
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            playerPos = GameManager.Instance.PLAYERCONTROLLER.transform.position;
        }

        // 1. 적절한 타겟 찾기 (플레이어 기준 가장 가까운 대상)
        Transform closestTarget = null;
        float minDist = float.MaxValue;

        if (validTargets != null && validTargets.Count > 0)
        {
            foreach (var vt in validTargets)
            {
                if (vt == null) continue;
                var health = vt.GetComponentInChildren<CharacterHealth>();
                if (health == null) health = vt.GetComponentInParent<CharacterHealth>();
                if (health != null && health.IsDead) continue;

                float dist = Vector2.Distance(playerPos, vt.position);
                if (dist < minDist) { minDist = dist; closestTarget = vt; }
            }
        }

        if (closestTarget == null)
        {
            // 칠 대상이 없으면 소환수 강제 돌진을 예방하고 동작을 완전히 차단한다.
            // false 를 돌려 호출자가 쿨타임을 먹이지 않게 한다 (허공에 눌러 6~8초를 날리는 것 방지).
            return false;
        }

        // 2. 텔레포트 및 넉백 방향 계산 (플레이어 기준)
        Vector2 dirFromPlayer = ((Vector2)closestTarget.position - playerPos).normalized;
        if (dirFromPlayer == Vector2.zero) dirFromPlayer = Vector2.right;

        Vector2 teleportPos;
        if (actionType == MinionActionType.DamageAndPull)
        {
            // 당기기의 경우 타겟 등 뒤로 이동
            teleportPos = (Vector2)closestTarget.position + dirFromPlayer * 0.5f;
        }
        else
        {
            // 나머지는 플레이어와 타겟 사이로 이동
            teleportPos = (Vector2)closestTarget.position - dirFromPlayer * 0.5f;
        }

        // 대시와 동일 판정: 미니언이 벽/낭떠러지를 뚫고 타겟으로 순간이동하지 않도록 목적지 제동.
        Vector2 teleStart = user.position;
        Vector2 teleTo = teleportPos - teleStart;
        float teleDist = teleTo.magnitude;
        if (teleDist > 0.001f)
            teleportPos = SkillCombatUtil.GetSafeDestination(teleStart, teleTo / teleDist, teleDist);

        user.position = teleportPos;

        PlaySkillSound();
        ShakeCamera();

        Vector2 lookDir = ((Vector2)closestTarget.position - (Vector2)user.position).normalized;
        bool faceRight = lookDir.x > 0f;

        // 시전 시간 = skillAnimDuration. 애니메이션 전체가 여기 정확히 맞춰 스케일된다.
        float animDuration = skillAnimDuration > 0f ? skillAnimDuration : 1f;

        // 이펙트 오버레이(예: DashDoll 의 Skill_Attack_Effect)는 이제 PlaySequenced 가 effectState 로 직접
        // 겹쳐 재생한다 — 타격 이벤트가 이펙트 클립에 박힌 경우 그쪽 애니메이터에 relay 를 붙여야 하기 때문.

        Debug.Log($"<color=cyan>[Minion Skill]</color> 미니언이 '{skillName}' 스킬을 사용했습니다! (대상: {closestTarget.name})");

        // 언제 때릴지는 그림이 정한다 — damageState 태그가 재생되는 동안, 혹은 Aseprite 에 심어둔
        // event:OnHitEvent 프레임에. 초로 박지 않으므로 시전 속도가 바뀌어도 알아서 따라온다.
        float eventWindow = Mathf.Max(0.05f, animDuration * Mathf.Clamp01(hitWindowRatio));

        // 피해 정보 — 미니언은 자기 스탯이 없어 플레이어의 ATK 를 빌린다. 여기에 소환수 고유 배율을 곱한다.
        var playerStat = GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null
            ? GameManager.Instance.PLAYERCONTROLLER.Stat
            : null;
        float finalDamage = (playerStat != null ? playerStat.ATK : 0f) * damageMultiplier;
        var info = new DamageInfo(finalDamage, element, caster.gameObject, 1f,
            !string.IsNullOrEmpty(skillName) ? skillName : $"Action {actionType}", category: DamageCategory.Skill,
            applyStatus: onHitStatus == StatusType.None ? (StatusType?)null : onHitStatus);

        if (useHitBox && hitBoxPrefab != null)
        {
            // 히트박스를 미리 만들고 판정창이 열릴 때 Init 한다. 다단히트 규약은 finisher 와 동일:
            // OnAttackEnd 있으면 창에 hitCount 균등 배분, 없으면 OnHitEvent 마다 1타
            // (hitCount=타수 진실, 이벤트=타이밍, 이벤트가 모자라면 마지막 이벤트 뒤로 몰아치기).
            float angle = Mathf.Atan2(dirFromPlayer.y, dirFromPlayer.x) * Mathf.Rad2Deg;
            BaseHitBox box = Instantiate(hitBoxPrefab, caster.transform.position, Quaternion.identity, caster.transform);
            box.transform.localPosition = Vector3.zero;
            box.transform.localRotation = Quaternion.Euler(0, 0, angle);
            // 크기: hitBoxSize(가로*세로)가 둘 다 양수면 박스로 비균등 스케일(사다리꼴/부채꼴), 아니면 hitRadius 원형 균등.
            box.transform.localScale = (hitBoxSize.x > 0f && hitBoxSize.y > 0f)
                ? new Vector3(hitBoxSize.x, hitBoxSize.y, 1f)
                : new Vector3(hitRadius * 2f, hitRadius * 2f, 1f);

            var col = box.GetComponent<Collider2D>();
            if (col != null) col.enabled = false; // 판정창 열릴 때까지 꺼둔다

            bool hasInvokedKeyword = false;
            System.Action<CharacterHealth> onHit = (health) =>
            {
                var stat = health.GetComponent<CharacterStat>()
                    ?? health.GetComponentInParent<CharacterStat>()
                    ?? health.GetComponentInChildren<CharacterStat>();
                if (stat == null) return;
                if (!hasInvokedKeyword)
                {
                    hasInvokedKeyword = true;
                    Debug.Log($"<color=yellow>[MinionAction]</color> {actionType} 발동! (미니언 시전)");
                }
                ApplyActionEffect(stat, stat.transform.root, caster, dirFromPlayer, teleportPos);
            };

            caster.PlaySequenced(
                skillAnimVisual, animSequence, damageState, hitEvent, effectState,
                animDuration, eventWindow, hitCount, faceRight,
                // 판정 열기. useContinuous=true 면 창 동안 hitCount 균등 틱, false(이벤트당)면 단발+펄스.
                (window, useContinuous) =>
                {
                    if (box == null) return;
                    DoHitStop();
                    float w = Mathf.Max(0.05f, window);
                    if (useContinuous && hitCount > 1)
                    {
                        box.isContinuousDamage = true;
                        box.damageTickRate = w / hitCount;
                    }
                    else
                    {
                        box.isContinuousDamage = false;
                    }
                    box.SetManualHitOnly(!useContinuous); // 이벤트당 모드면 펄스로만 타격(OnTriggerStay/sleep 비의존)
                    if (col != null) col.enabled = true;
                    box.Init(info, Layers.EnemyMask, w, 0f, true, onHit);
                },
                onHitPulse: () => { if (box != null) box.PulseDamageOverlapping(); },
                onAttackEnd: () => { if (col != null) col.enabled = false; });
        }
        else
        {
            // 히트박스 없는 즉시 타격: 판정창이 열리는 순간 1회.
            caster.PlaySequenced(
                skillAnimVisual, animSequence, damageState, hitEvent, effectState,
                animDuration, eventWindow, hitCount, faceRight,
                (window, useContinuous) =>
                {
                    if (caster == null || closestTarget == null) return;
                    DoHitStop();
                    var health = closestTarget.GetComponentInChildren<CharacterHealth>()
                        ?? closestTarget.GetComponentInParent<CharacterHealth>();
                    if (health == null || health.IsDead) return;
                    health.GetDamage(info);
                    var stat = health.GetComponent<CharacterStat>()
                        ?? health.GetComponentInParent<CharacterStat>()
                        ?? health.GetComponentInChildren<CharacterStat>();
                    if (stat != null) ApplyActionEffect(stat, stat.transform.root, caster, dirFromPlayer, teleportPos);
                });
        }

        return true;
    }

    private void ApplyActionEffect(CharacterStat stat, Transform targetTransform, MinionSkillCaster caster, Vector2 dirFromPlayer, Vector2 teleportPos)
    {
        switch (actionType)
        {
            case MinionActionType.DamageOnly:
                break;
            case MinionActionType.DamageAndPush:
                caster.StartCoroutine(PushEnemy(targetTransform, dirFromPlayer));
                break;
            case MinionActionType.DamageAndPull:
                caster.StartCoroutine(PushEnemy(targetTransform, -dirFromPlayer));
                break;
        }
    }

    private IEnumerator PushEnemy(Transform enemy, Vector2 pushDir)
    {
        if (enemy == null) yield break;

        // [추가] 넉백 경직 및 돌진/공격 인터럽트 적용
        var entity = enemy.GetComponent<BaseEntity>() ?? enemy.GetComponentInChildren<BaseEntity>();
        if (entity != null)
        {
            entity.ApplyKnockback(Vector2.zero); // 수동 Lerp 이동을 타므로 물리 힘은 zero 전달해 충돌 예방
        }

        var status = enemy.GetComponentInChildren<CharacterStatus>();
        if (status == null) status = enemy.GetComponentInParent<CharacterStatus>();
        if (status != null)
        {
            if (status.HasSuperArmor)
            {
                status.DamageSuperArmor(30f);
                yield break;
            }
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        Vector2 targetPos = startPos + pushDir * forceAmount;
        
        int obstacleMask = Layers.WallMask | Layers.UnsteppableMask;

        // 몬스터 콜라이더 크기 구하기
        var enemyCol = enemy.GetComponent<Collider2D>();
        float checkRadius = 0.3f;
        if (enemyCol != null)
        {
            if (enemyCol is CircleCollider2D circle) checkRadius = circle.radius * enemy.localScale.x;
            else checkRadius = Mathf.Max(enemyCol.bounds.extents.x, enemyCol.bounds.extents.y);
        }

        while (elapsed < forceDuration)
        {
            if (enemy == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / forceDuration;
            
            Vector2 nextPos = Vector2.Lerp(startPos, targetPos, t);
            Vector2 moveDir = nextPos - (Vector2)enemy.position;
            float moveDist = moveDir.magnitude;
            
            if (moveDist > 0.001f)
            {
                // 충돌 반지름 마진을 1.0f로 원의 축소를 방지하고 온전한 크기 검출
                RaycastHit2D hit = Physics2D.CircleCast(enemy.position, checkRadius * 1.0f, moveDir.normalized, moveDist, obstacleMask);
                if (hit.collider != null)
                {
                    // hit.centroid 대신 충돌지점에서 벽 바깥 법선(normal) 방향으로 반지름+안전오차 만큼 떨어진 포지션 밀착 지정
                    enemy.position = hit.point + hit.normal * (checkRadius * 1.02f);
                    yield break;
                }
                else
                {
                    enemy.position = nextPos;
                }
            }
            yield return null;
        }
        if (enemy != null)
        {
            Vector2 moveDir = targetPos - (Vector2)enemy.position;
            float moveDist = moveDir.magnitude;
            if (moveDist > 0.001f)
            {
                RaycastHit2D hit = Physics2D.CircleCast(enemy.position, checkRadius * 0.9f, moveDir.normalized, moveDist, obstacleMask);
                if (hit.collider != null)
                {
                    enemy.position = hit.centroid;
                }
                else
                {
                    enemy.position = targetPos;
                }
            }
        }
    }

    private IEnumerator PullEnemy(Transform enemy, Vector2 center)
    {
        if (enemy == null) yield break;

        var status = enemy.GetComponentInChildren<CharacterStatus>();
        if (status == null) status = enemy.GetComponentInParent<CharacterStatus>();
        if (status != null)
        {
            if (status.HasSuperArmor)
            {
                status.DamageSuperArmor(30f);
                yield break;
            }
        }

        float elapsed = 0f;
        Vector2 startPos = enemy.position;
        Vector2 targetPos = center + (startPos - center).normalized * 0.5f;

        while (elapsed < forceDuration)
        {
            if (enemy == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / forceDuration;
            enemy.position = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }
        if (enemy != null) enemy.position = targetPos;
    }
}
