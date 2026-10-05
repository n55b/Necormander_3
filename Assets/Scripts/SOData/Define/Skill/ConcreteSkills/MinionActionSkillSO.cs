using UnityEngine;
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

    [Header("시전 위치")]
    [Tooltip("음수(-1)=기존 근접형: 타겟 근처로 순간이동, 히트박스는 미니언 위치, 조준=타겟.\n" +
             "0 이상=원거리형(부채꼴 등): 히트박스를 '플레이어 + 마우스조준 * 이 거리'에 깐다(마우스 기준, 미니언과 분리).")]
    public float hitBoxForwardOffset = -1f;

    [Tooltip("(원거리형 전용) 미니언을 히트박스 중심에서 '플레이어 쪽'으로 이만큼 뒤로 물린다(미니언↔히트박스 거리).\n" +
             "0=히트박스 자리에 그대로. 히트박스와 안 겹치게 바깥에서 시전하는 느낌을 줄 때 키운다.")]
    public float minionOffsetFromHitBox = 0f;

    [Header("다단히트 (useHitBox 일 때만)")]
    [Tooltip("몇 번 때릴지. 1 이면 단타.")]
    public int hitCount = 1;

    [Header("구역 분할 충격파 (박스 판정 전용)")]
    [Tooltip("1 이면 끔. 2 이상이면 hitBoxSize 직선 박스를 진행 방향으로 N 구역으로 나눠,\n" +
             "가까운 구역부터 zoneInterval 간격으로 차례로 터진다(충격파). 구역마다 1타 — 이때 hitCount 는 무시된다.\n" +
             "보통 원거리형(hitBoxForwardOffset = 박스 길이의 절반)으로 써서 미니언 앞에서 시작하게 한다.")]
    [Min(1)] public int zoneCount = 1;
    [Tooltip("구역과 구역 사이 시간차(초). 뒤 구역은 이 시간 동안 차오르는 텔레그래프를 보여준 뒤 터진다.")]
    [Min(0f)] public float zoneInterval = 0.12f;
    [Tooltip("구역 하나가 판정을 유지하는 시간(초).")]
    [Min(0.02f)] public float zoneActiveTime = 0.1f;

    private bool UsesZones => zoneCount > 1 && hitBoxSize.x > 0f && hitBoxSize.y > 0f;

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

        // 조준은 평타·마무리와 동일하게 마우스 기준. 원거리형(hitBoxForwardOffset>=0)에서만 쓴다.
        // 마우스가 플레이어 위면 타겟 방향으로 폴백.
        Vector2 aimDir = SkillCombatUtil.GetAimDir(playerPos);
        if (aimDir.sqrMagnitude < 0.0001f) aimDir = dirFromPlayer;

        bool rangedMode = hitBoxForwardOffset >= 0f;
        // 원거리형 히트박스 중심: '플레이어 + 마우스조준 * hitBoxForwardOffset' (미니언과 무관, 마우스에서 안 벗어남).
        Vector2 hitBoxCenter = playerPos + aimDir * hitBoxForwardOffset;

        Vector2 teleportPos;
        if (rangedMode)
        {
            // [원거리형] 히트박스는 hitBoxCenter 에 고정. 미니언(비주얼)만 거기서 '플레이어 쪽'으로 minionOffsetFromHitBox 만큼 뒤로.
            teleportPos = hitBoxCenter - aimDir * minionOffsetFromHitBox;
        }
        else if (actionType == MinionActionType.DamageAndPull)
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

        // 미니언 바라보는 방향 + 히트박스 방향. 원거리형이면 마우스 조준, 아니면 기존 타겟 방향.
        Vector2 skillDir = rangedMode ? aimDir : dirFromPlayer;
        bool faceRight = skillDir.x >= 0f;

        // 애니메이션은 미니언이 갖는다(MainMinionDataSO.skillAnim). 스킬은 로직만 갖고 연출은 여기서 읽는다.
        var mainData = data as MainMinionDataSO;
        var animSet = mainData != null ? mainData.skillAnim : null;

        // 시전 시간 = skillAnim.duration. 애니메이션 전체가 여기 정확히 맞춰 스케일된다.
        float animDuration = animSet != null ? animSet.ResolvedDuration : 1f;

        // 이펙트 오버레이(예: DashDoll 의 Skill_Attack_Effect)는 이제 PlaySequenced 가 effectState 로 직접
        // 겹쳐 재생한다 — 타격 이벤트가 이펙트 클립에 박힌 경우 그쪽 애니메이터에 relay 를 붙여야 하기 때문.

        Debug.Log($"<color=cyan>[Minion Skill]</color> 미니언이 '{skillName}' 스킬을 사용했습니다! (대상: {closestTarget.name})");

        // 언제 때릴지는 그림이 정한다 — damageState 태그가 재생되는 동안, 혹은 Aseprite 에 심어둔
        // event:OnHitEvent 프레임에. 초로 박지 않으므로 시전 속도가 바뀌어도 알아서 따라온다.
        float eventWindow = animSet != null ? animSet.EventHitWindow : Mathf.Max(0.05f, animDuration * 0.15f);

        // 피해 정보 — 미니언은 자기 스탯이 없어 플레이어의 ATK 를 빌린다. 여기에 소환수 고유 배율을 곱한다.
        var playerStat = GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null
            ? GameManager.Instance.PLAYERCONTROLLER.Stat
            : null;
        // [강화] 보상방 강화 단계만큼 스킬 피해 배율이 붙는다(MinionEnhance).
        float finalDamage = (playerStat != null ? playerStat.ATK : 0f) * damageMultiplier
                            * MinionEnhance.SkillDamageMult(mainData);
        var info = new DamageInfo(finalDamage, element, caster.gameObject, 1f,
            !string.IsNullOrEmpty(skillName) ? skillName : $"Action {actionType}", category: DamageCategory.Skill,
            applyStatus: onHitStatus == StatusType.None ? (StatusType?)null : onHitStatus);

        if (useHitBox && hitBoxPrefab != null && UsesZones)
        {
            Vector2 lineCenter = rangedMode ? hitBoxCenter : (Vector2)caster.transform.position;
            PlayZoneShockwave(caster, animSet, animDuration, eventWindow, faceRight, skillDir, lineCenter, info, dirFromPlayer);
        }
        else if (useHitBox && hitBoxPrefab != null)
        {
            // 히트박스를 미리 만들고 판정창이 열릴 때 Init 한다. 다단히트 규약은 finisher 와 동일:
            // OnAttackEnd 있으면 창에 hitCount 균등 배분, 없으면 OnHitEvent 마다 1타
            // (hitCount=타수 진실, 이벤트=타이밍, 이벤트가 모자라면 마지막 이벤트 뒤로 몰아치기).
            float angle = Mathf.Atan2(skillDir.y, skillDir.x) * Mathf.Rad2Deg;
            // 히트박스 위치: 원거리형이면 hitBoxCenter(플레이어+마우스 기준, 미니언과 무관), 아니면 시전한 미니언 위치.
            Vector2 boxPos = rangedMode ? hitBoxCenter : (Vector2)caster.transform.position;
            BaseHitBox box = Instantiate(hitBoxPrefab, (Vector3)boxPos, Quaternion.Euler(0f, 0f, angle), caster.transform);
            box.transform.position = (Vector3)boxPos;
            box.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            // 크기: hitBoxSize(가로*세로)가 둘 다 양수면 박스로 비균등 스케일(사다리꼴/부채꼴), 아니면 hitRadius 원형 균등.
            box.transform.localScale = (hitBoxSize.x > 0f && hitBoxSize.y > 0f)
                ? new Vector3(hitBoxSize.x, hitBoxSize.y, 1f)
                : new Vector3(hitRadius * 2f, hitRadius * 2f, 1f);

            var col = box.GetComponent<Collider2D>();
            if (col != null) col.enabled = false; // 판정창 열릴 때까지 꺼둔다

            // Init(팀 색 포함)은 OnHit 판정창에서 늦게 불린다 → 윈드업 동안엔 프리팹 기본색(빨강)이 뜬다.
            // 스폰 직후 직접 물들여 처음부터 아군 색이 나오게 한다.
            box.ApplyTeamColor(true);

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
                ApplyActionEffect(SkillCombatUtil.ResolveEntityTransform(health), caster, dirFromPlayer);
            };

            caster.PlaySequenced(
                animSet, animDuration, eventWindow, hitCount, faceRight,
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
                animSet, animDuration, eventWindow, hitCount, faceRight,
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
                    if (stat != null) ApplyActionEffect(SkillCombatUtil.ResolveEntityTransform(health), caster, dirFromPlayer);
                });
        }

        return true;
    }

    /// <summary>
    /// 구역 분할 충격파. hitBoxSize 직선 박스를 진행 방향으로 zoneCount 칸으로 나눠, 판정창이 열리면
    /// 0번(가장 가까운) 구역은 즉시, 이후 구역은 zoneInterval 간격으로 차오른 뒤 터진다.
    /// 구역 히트박스는 시전자(미니언)의 자식으로 두지 않는다 — 뒤 구역이 시전 애니보다 오래 살 수 있어서다.
    /// </summary>
    private void PlayZoneShockwave(MinionSkillCaster caster, MinionAnimSet animSet, float animDuration, float eventWindow,
        bool faceRight, Vector2 dir, Vector2 lineCenter, DamageInfo info, Vector2 pushDir)
    {
        int n = zoneCount;
        float zoneLen = hitBoxSize.x / n;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float safetyLifetime = animDuration + n * zoneInterval + zoneActiveTime + 1f;

        var zones = new BaseHitBox[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 pos = lineCenter + dir * (-hitBoxSize.x * 0.5f + zoneLen * (i + 0.5f));
            var z = Instantiate(hitBoxPrefab, (Vector3)pos, Quaternion.Euler(0f, 0f, angle));
            z.transform.localScale = new Vector3(zoneLen, hitBoxSize.y, 1f);
            var c = z.GetComponent<Collider2D>();
            if (c != null) c.enabled = false; // 판정창 열릴 때까지 꺼둔다
            z.ApplyTeamColor(true);
            z.isContinuousDamage = false;     // 구역당 1타
            z.SetManualHitOnly(false);
            Destroy(z.gameObject, safetyLifetime); // 시전이 끊겨 판정창이 안 열려도 남지 않게
            zones[i] = z;
        }

        System.Action<CharacterHealth> onHit = (health) =>
        {
            if (caster == null) return;
            ApplyActionEffect(SkillCombatUtil.ResolveEntityTransform(health), caster, pushDir);
        };

        bool opened = false;
        caster.PlaySequenced(
            animSet, animDuration, eventWindow, 1, faceRight,
            (window, useContinuous) =>
            {
                if (opened) return; // 이벤트가 여러 번 와도 충격파는 한 번만
                opened = true;
                DoHitStop();
                for (int i = 0; i < n; i++)
                {
                    var z = zones[i];
                    if (z == null) continue;
                    float delay = i * zoneInterval;
                    z.Init(info, Layers.EnemyMask, zoneActiveTime, delay, true, onHit);
                    if (delay <= 0f)
                    {
                        // 지연 없는 Init 은 콜라이더를 켜주지 않는다(지연 있으면 ForceActivate 가 켠다).
                        var c = z.GetComponent<Collider2D>();
                        if (c != null) c.enabled = true;
                    }
                }
            });
    }

    private void ApplyActionEffect(Transform targetTransform, MinionSkillCaster caster, Vector2 dirFromPlayer)
    {
        switch (actionType)
        {
            case MinionActionType.DamageOnly:
                break;
            case MinionActionType.DamageAndPush:
                caster.StartCoroutine(SkillCombatUtil.PushEnemy(targetTransform, dirFromPlayer, forceAmount, forceDuration));
                break;
            case MinionActionType.DamageAndPull:
                caster.StartCoroutine(SkillCombatUtil.PushEnemy(targetTransform, -dirFromPlayer, forceAmount, forceDuration));
                break;
        }
    }
}
