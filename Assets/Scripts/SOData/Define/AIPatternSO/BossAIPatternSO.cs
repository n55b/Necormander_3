using UnityEngine;

/// <summary>
/// 모든 보스 AI 패턴의 기반이 되는 클래스입니다.
/// 페이즈 전환 등 보스 공통 로직을 관리합니다.
///
/// BaseAIPatternSO 를 상속한다 — AIPatternSO 의 훅(UpdateTargeting/UpdateStateTransitions/OnIdle/
/// OnFollow/OnAttack)은 전부 빈 몸통이라, 여기서 AIPatternSO 를 직접 상속하면 보스는 Execute() 를
/// 통째로 override 하고 NavMesh 추격·공격 루틴을 각자 복붙하는 것 말고는 선택지가 없다(차저/워리어/
/// 아처/서머너가 실제로 그렇다). BaseAIPatternSO 를 끼워 두면 새 보스는 필요한 훅만 골라 덮어쓸 수
/// 있고, Execute() 를 override 하는 기존 보스들은 이 훅이 아예 호출되지 않으므로 동작이 바뀌지 않는다.
/// </summary>
public abstract class BossAIPatternSO : BaseAIPatternSO
{
    [Header("Boss Phase Settings")]
    public float phase2Threshold = 0.5f; // 페이즈 2 전환 체력 비율
    protected int currentPhase = 1;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        currentPhase = 1;
    }

    protected void UpdatePhase(BaseEntity entity)
    {
        if (entity.Stats == null || entity.Stats.Health == null) return;

        float hpRatio = entity.Stats.Health.CurHP / entity.Stats.Health.MaxHP;
        if (currentPhase == 1 && hpRatio <= phase2Threshold)
        {
            currentPhase = 2;
            OnPhaseChanged(entity, 2);
        }
    }

    protected virtual void OnPhaseChanged(BaseEntity entity, int newPhase)
    {
        Debug.Log($"<color=red>[Boss]</color> Phase Changed to <b>{newPhase}</b>!");
    }

    protected RoomInstance GetCurrentRoom(BaseEntity entity)
    {
        foreach (var room in FindObjectsByType<RoomInstance>(FindObjectsSortMode.None))
        {
            Bounds bounds = new Bounds((Vector2)room.transform.position + room.centerOffset, new Vector3(room.roomSize.x, room.roomSize.y, 100f));
            if (bounds.Contains(entity.transform.position))
            {
                return room;
            }
        }
        return null;
    }

    protected Vector2 GetTacticalPosition(BaseEntity entity, Transform target)
    {
        RoomInstance room = GetCurrentRoom(entity);
        if (room == null) return entity.transform.position;

        Vector2 roomCenter = (Vector2)room.transform.position + room.centerOffset;
        Vector2 extents = new Vector2(Mathf.Max(0, room.roomSize.x / 2f - 2f), Mathf.Max(0, room.roomSize.y / 2f - 2f)); // 벽에서 2만큼 띄움

        Vector2 bestPos = entity.transform.position;
        float maxDist = -1f;

        // 방 안의 랜덤한 위치 5개를 뽑아서 그 중 플레이어와 가장 먼 위치를 선택
        for (int i = 0; i < 5; i++)
        {
            float randX = Random.Range(-extents.x, extents.x);
            float randY = Random.Range(-extents.y, extents.y);
            Vector2 candidate = roomCenter + new Vector2(randX, randY);

            float distToPlayer = Vector2.Distance(candidate, entity.Target.position);
            if (distToPlayer > maxDist)
            {
                maxDist = distToPlayer;
                bestPos = candidate;
            }
        }
        
        // 갈 수 있는 경로인지 확인(NavMesh)
        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(bestPos, out hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
        {
            return hit.position;
        }

        return bestPos;
    }

    // ==============================================================
    // 애니메이션 재생 헬퍼 (해태/본 마스터 공용)
    // ==============================================================
    // [26/08/26] 원래 EliteChargerAIPatternSO 안에 private static 으로 있었다.
    // 본 마스터가 페이즈마다 브레인 SO 를 따로 쓰는 구조라(BoneMasterAIPatternSO /
    // BoneMasterPhase2AIPatternSO) 그대로 두면 같은 구현이 세 벌이 된다. 세 브레인이
    // 전부 BossAIPatternSO 를 상속하므로 여기로 올려서 한 벌만 남긴다.
    // 스테이트 '이름'은 보스마다 다르니 각 SO 의 animState_* 필드로 남겨둔다.
    /// <summary>
    /// 스테이트를 처음부터 재생한다. duration 을 주고 matchSpeed 가 켜져 있으면 클립 길이가
    /// 그 시간에 정확히 맞도록 Animator.speed 를 조절한다(BaseAIPatternSO.AttackRoutine 과 동일한 공식).
    ///
    /// stateName 이 비어 있으면 공용 "Attack" 으로 폴백한다 — 전용 아트가 없는 보스도 죽지 않게.
    /// speed 는 전역 상태라 공격이 끝나면 반드시 1 로 되돌려야 한다(BasicAttackRoutine / RunSpecialPattern 말미).
    /// </summary>
    /// <param name="anchorOverride">
    /// 배속을 맞출 기준점을 클립 안에서 직접 지정한다(초). 0 이면 평소대로 타격 프레임 -> 클립 끝 순으로 찾는다.
    /// 한 클립이 '예비동작 + 본동작' 을 통째로 담고 있어서 앞부분만 늘려야 할 때 쓴다
    /// (본 마스터의 Pattern_Dash: 1~3프레임이 충전, 4프레임이 질주).
    /// </param>
    /// <param name="startNormalized">클립의 이 지점(0~1)부터 재생한다. 위와 짝을 이룬다.</param>
    public static void PlayState(BaseEntity entity, string stateName, float duration = 0f, bool matchSpeed = false,
                                 float anchorOverride = 0f, float startNormalized = 0f)
    {
        var anim = entity != null ? entity.Animator : null;
        if (anim == null || anim.runtimeAnimatorController == null) return;

        // [죽은 뒤 재생 금지] 돌진 계열은 windup 이 끝난 '뒤'에 질주 스테이트를 트는데(0.8초 / 조준 3초),
        // 그 사이에 죽으면 이 호출이 사망 처리 이후에 도착한다. 이 패턴은 entity.ActiveAttackCoroutine 을
        // 등록하지 않아서 BaseEntity.CancelAttack 이 루틴을 멈추지 못하고, MonsterDeathHandler 가
        // 컴포넌트를 꺼도 이미 돌던 코루틴은 계속 흐른다(MonsterDeathHandler.cs 주석 참조).
        // 막지 않으면 방금 튼 "Die" 를 루프 클립(Dash_Attack)이 덮어써서 시체가 제자리 질주한다.
        var health = entity.Stats != null ? entity.Stats.Health : null;
        if (health != null && health.IsDead) return;

        if (string.IsNullOrWhiteSpace(stateName)) stateName = "Attack";

        float speed = 1f;
        if (matchSpeed && duration > 0.0001f)
        {
            // [26/08/18] 기준점을 클립 끝에서 <b>타격 프레임(OnHitEvent)</b> 으로 옮겼다.
            // 클립 끝을 windup 에 맞추면, 타격 프레임은 클립의 중간(42~54%)이라 항상 windup 의
            // 절반쯤에 지나가 버린다 — 해태가 이미 내려찍고 원래 자세로 돌아온 뒤에야 데미지가
            // 들어왔다(측정값 ①-0.52s ③-0.59s 슬램-0.60s). 타격 프레임을 duration 끝에 맞추면
            // 예비동작이 windup 전체를 쓰고 내려찍는 순간과 판정이 정확히 겹친다.
            // 데미지 타이밍·텔레그래프는 하나도 안 건드린다 — 바뀌는 건 재생 속도뿐이다.
            float anchor = anchorOverride > 0.0001f ? anchorOverride : StateHitEventTime(anim, stateName);
            // 이벤트가 없는 클립(Dash_Ready/Dash_Attack 등)은 예전대로 클립 끝을 기준으로.
            if (anchor <= 0.0001f) anchor = StateClipLength(anim, stateName);
            // 1프레임짜리 홀드 포즈(Dash_Ready/Stun)는 늘려봐야 정지 화면이라 배속을 건드리지 않는다.
            if (anchor > 0.0001f) speed = Mathf.Clamp(anchor / duration, 0.05f, 20f);
        }

        anim.speed = speed;
        anim.Play(stateName, -1, Mathf.Clamp01(startNormalized));
    }

    /// <summary>
    /// 스테이트에 물린 클립의 길이(초). aseprite 임포터가 태그 이름 그대로 클립을 만들고
    /// 컨트롤러도 같은 이름의 스테이트를 쓰므로 이름 매칭으로 찾는다
    /// (MinionSkillCaster.ClipLength 와 동일한 방식). 못 찾으면 0.
    /// </summary>
    public static float StateClipLength(Animator anim, string stateName)
    {
        var rac = anim.runtimeAnimatorController;
        if (rac == null) return 0f;
        foreach (var c in rac.animationClips)
            if (c != null && c.name == stateName) return c.length;
        return 0f;
    }

    /// <summary>
    /// 이 스테이트 클립에 박힌 첫 <c>OnHitEvent</c> 가 클립 시작 몇 초 뒤인가. 없으면 0.
    ///
    /// aseprite 셀 user data 의 <c>event:OnHitEvent</c> 가 임포트 때 AnimationEvent 로 들어온다.
    /// (콜론을 두 번 찍으면 함수 이름이 ":OnHitEvent" 가 되어 조용히 아무 데도 안 붙는다 — 실제로
    ///  그 상태로 한 번 들어왔었다. Report 메뉴가 이름을 그대로 찍어주니 의심되면 거기서 확인할 것.)
    ///
    /// 클립 이름 == 스테이트 이름 규칙에 기대는 건 StateClipLength 와 같다. 이름이 어긋나면
    /// (Attack/Follow/Die 처럼) 조용히 0 이 나오고 예전 동작(클립 끝 기준)으로 떨어진다.
    /// </summary>
    /// <summary>
    /// 이 스테이트 클립의 <b>마지막</b> OnHitEvent 시각(초). 없으면 0.
    ///
    /// 한 클립에 동작이 둘 들어 있고(회전 베기 + 내려찍기 / 2연격) 코드가 그 둘을 따로 굴릴 때,
    /// 뒷동작을 '늦게 틀어서' 판정과 겹치게 하려면 뒤쪽 타격 프레임의 시각이 필요하다.
    /// 배속을 건드리지 않으므로 뒷동작이 느려지지 않는다.
    /// </summary>
    public static float StateLastHitEventTime(Animator anim, string stateName)
    {
        if (anim == null) return 0f;
        var rac = anim.runtimeAnimatorController;
        if (rac == null) return 0f;
        foreach (var c in rac.animationClips)
        {
            if (c == null || c.name != stateName) continue;
            float best = 0f;
            foreach (var e in c.events)
                if (e.functionName == "OnHitEvent" && e.time > best) best = e.time;
            return best;
        }
        return 0f;
    }

    public static float StateHitEventTime(Animator anim, string stateName)
    {
        var rac = anim.runtimeAnimatorController;
        if (rac == null) return 0f;
        foreach (var c in rac.animationClips)
        {
            if (c == null || c.name != stateName) continue;
            float best = 0f;
            foreach (var e in c.events)
                if (e.functionName == "OnHitEvent" && (best <= 0.0001f || e.time < best)) best = e.time;
            return best;
        }
        return 0f;
    }
}
