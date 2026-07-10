using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 스테이지 1 엘리트 몬스터(차저) AI 패턴입니다.
///
/// 전투 흐름:
/// 1) 방 입장
/// 2) 기본 공격을 8초간 반복
/// 3) 8초가 지나면 모든 행동을 강제로 인터럽트하고, 패턴 1/2/3 중 1개를 무작위로 발동
/// 4) 패턴 종료 후 다시 기본 공격을 8초간 반복
/// 5) 다음 차례에는 "아직 쓰지 않은 나머지 2개" 중 1개를 무작위로 발동
/// 6) 기본 공격 8초 반복 -> 7) 마지막 남은 1개 패턴을 발동 -> 8) 기본 공격 8초 반복
/// 9) 이후 다시 3개 중 1개를 무작위로 뽑는 사이클로 복귀 (풀 리필)
///
/// [중요] Unity 엔진의 BaseEntity.Update()는 IsAttacking == true인 동안(공격 windup~후딜레이)에는
/// CanExecuteAI()가 false를 반환해 브레인의 Execute()를 아예 호출하지 않습니다. 그래서 8초 판정은
/// Time.deltaTime 누적이 아니라, "기본 공격 상태로 돌아온 절대 시각(Time.time)"을 기록해두고 그로부터
/// 8초가 지났는지를 비교하는 방식으로 계산합니다. Execute()가 얼마나 뜸하게 불리든 상관없이 정확합니다.
///
/// - 플레이어의 기본 공격에는 슈퍼아머로 경직/넉백되지 않습니다.
/// - 현재 사용 중인 공격/패턴 이름을 보스 머리 위에 한글로 표시합니다.
/// (기획서: 스테이지 1 엘리트 몬스터 기획, 26/07/09 최신 수정안 기준)
/// </summary>
[CreateAssetMenu(fileName = "EliteChargerAIPattern", menuName = "Necromancer/AI/EliteChargerPattern")]
public class EliteChargerAIPatternSO : BossAIPatternSO
{
    // ==============================================================
    // 기본 공격 3종 설정
    // ==============================================================
    [Header("기본 공격 - 프리팹 (비워두면 기본 원형/부채꼴 히트박스로 대체)")]
    [Tooltip("단순 찍기: 전방 원형 범위")] public GameObject stabHitboxPrefab;
    [Tooltip("부채꼴 범위 공격: 전방 넓은 부채꼴 (보스 중심에서 플레이어 방향으로 회전만 적용됩니다)")] public GameObject fanHitboxPrefab;
    [Tooltip("휩쓸기 공격: 보스 주변 원형 범위")] public GameObject sweepHitboxPrefab;

    [Header("기본 공격 - 타이밍/범위")]
    public float stabWindup = 1.0f;
    public float fanWindup = 1.0f;
    [Tooltip("20% 빨라진 값 (기존 1.5초 -> 1.2초)")]
    public float sweepWindup = 1.2f;
    [Tooltip("모든 기본 공격 후 공통으로 부여되는 후딜레이")]
    public float basicAttackPostDelay = 1.0f;
    public float stabRadius = 4.4f;
    public float fanRadius = 7.2f;
    public float sweepRadius = 6.0f;
    [Tooltip("부채꼴 프리팹의 '앞쪽(뾰족한 방향)' 로컬 회전 보정값(도). 프리팹의 기본 방향과 실제 조준 방향이 어긋날 때 이 값만 조정하면 됩니다. (예: 위쪽이 앞이면 -90, 아래쪽이 앞이면 90)")]
    public float fanRotationOffset = 90f;

    // ==============================================================
    // 기둥 설정
    // ==============================================================
    [Header("기둥 설정")]
    [Tooltip("비워두면 코드에서 임시 원기둥 형태로 생성합니다.")]
    public GameObject pillarPrefab;
    public int pillarCount = 4;
    [Tooltip("방을 찾지 못했을 때(fallback) 보스 기준 기둥 배치 거리")]
    public float pillarSpawnDistance = 5.5f;
    public int pillarMaxHP = 4;
    [Tooltip("방 벽에서 기둥까지 남겨둘 최소 여백(절대값). 값이 클수록 기둥이 벽에서 멀어집니다.")]
    public float pillarWallMargin = 3f;
    [Range(0.1f, 1f)]
    [Tooltip("벽 여백을 제외한 남은 절반 크기 중 기둥이 실제로 밀려나는 비율. 1에 가까울수록 벽에 붙고, 작을수록 중앙 쪽으로 모입니다.")]
    public float pillarInwardFactor = 0.65f;

    // ==============================================================
    // 슈퍼아머 / 패턴 이름 표시
    // ==============================================================
    [Header("슈퍼아머")]
    [Tooltip("플레이어의 기본 공격으로는 경직/넉백되지 않도록 부여할 슈퍼아머 게이지 (사실상 무제한)")]
    public float superArmorGauge = 999999f;

    [Header("패턴 이름 표시 (보스 머리 위 한글 라벨)")]
    public bool showPatternLabel = true;
    public TMP_FontAsset patternLabelFont;
    public Vector3 patternLabelOffset = new Vector3(0f, 1.3f, 0f);
    public string label_Stab = "단순 찍기";
    public string label_Fan = "부채꼴 공격";
    public string label_Sweep = "휩쓸기";
    public string label_Pattern1Windup = "기둥과 돌진 준비";
    public string label_Pattern1 = "돌진!";
    public string label_Pattern2 = "안 팎 도넛";
    public string label_Pattern3Windup = "바닥 충격파 준비";
    public string label_Pattern3 = "바닥 충격파";

    // ==============================================================
    // 특수 패턴 공통 설정
    // ==============================================================
    [Header("특수 패턴 공통 설정 (기본 공격 8초 -> 패턴 1회 -> 기본 공격 8초 -> ...)")]
    [Tooltip("기본 공격 상태로 돌아온 뒤 이 시간(초)이 지나면, 하던 행동을 강제로 중단하고 패턴을 발동합니다.")]
    public float specialPatternInterval = 8f;
    [Tooltip("특수 패턴 장판(경고) 프리팹. 비워두면 원형 히트박스로 대체합니다.")]
    public GameObject fieldTelegraphPrefab;

    // --- 패턴 1: 기둥과 돌진 ---
    [Header("패턴 1 - 기둥과 돌진")]
    public float chargeWindup = 3f;
    [Tooltip("돌진 속도 배율 (보스 이동속도 대비). 값이 클수록 돌진이 훨씬 빨라집니다.")]
    public float chargeSpeedMultiplier = 9f;
    [Tooltip("돌진 판정 반경 (일반 차저보다 3배 이상 넓게)")]
    public float chargeHitRadius = 1.5f;
    [Tooltip("돌진 전조(바닥 경고 직사각형)의 기본 길이(대상이 없을 때). 평소에는 플레이어 발밑까지 이어지도록 자동 계산됩니다.")]
    public float chargeTelegraphLength = 30f;
    [Tooltip("전조 길이를 플레이어 위치보다 얼마나 더 길게 그릴지(발밑을 확실히 덮도록)")]
    public float chargeTelegraphOvershoot = 2.5f;
    public Color chargeTelegraphColor = new Color(1f, 0f, 0f, 0.35f);
    [Tooltip("기둥에 박았을 때 보스 기절 시간")]
    public float pillarChargeStunDuration = 5f;
    [Tooltip("기둥이 없거나 벽에 유도되었을 때 보스 기절 시간")]
    public float wallChargeStunDuration = 1.5f;

    // --- 패턴 2: 안 팎 도넛 ---
    [Header("패턴 2 - 안 팎 도넛 (엘리트 몹 위치 중심, 방 크기에 비례해서 자동 조정됩니다)")]
    [Range(0.1f, 1.2f)] public float donutInnerSmallRatio = 0.38f;
    [Range(0.1f, 1.2f)] public float donutOuterSmallRatio = 0.62f;
    [Range(0.1f, 1.2f)] public float donutInnerLargeRatio = 0.75f;
    [Tooltip("2회차 '팎' 페이즈의 안전지대 반경 비율. fieldMax에 너무 가까우면 위험지대(고리) 폭이 얇아져 범위가 작아 보이므로 여유를 둡니다.")]
    [Range(0.1f, 1.2f)] public float donutOuterLargeRatio = 0.92f;
    [Tooltip("판정에 사용할 필드 최대 반경의 배율 (방 반경 기준)")]
    public float donutFieldMaxRatio = 1.2f;
    [Tooltip("방을 찾지 못했을 때(fallback) 사용할 절대 반경 4종 (작은 안/팎, 큰 안/팎)")]
    public float donutFallbackInnerSmall = 4.5f;
    public float donutFallbackOuterSmall = 7f;
    public float donutFallbackInnerLarge = 8.5f;
    public float donutFallbackOuterLarge = 10.5f;
    [Tooltip("장판 표시 이후 실제 피해가 들어가기까지의 시간")]
    public float donutExplodeDelay = 1.5f;
    public float donutStunDuration = 3f;
    [Tooltip("안전지대(초록) 색상")]
    public Color donutSafeColor = new Color(0.25f, 1f, 0.35f, 0.35f);
    [Tooltip("위험지대(빨강) 색상")]
    public Color donutDangerColor = new Color(1f, 0f, 0f, 0.4f);

    // --- 패턴 3: 바닥 충격파 ---
    [Header("패턴 3 - 바닥 충격파 (파동이 방 끝까지 퍼져나갑니다, 대쉬로 회피 가능)")]
    [Tooltip("애니메이션이 없는 것을 보완하기 위한 사전 예비동작 시간. 이 시간 동안 보스 발밑에 경고 원이 서서히 채워지며, 아직 피해는 없습니다.")]
    public float slamPreCastDelay = 0.9f;
    [Tooltip("보스 근처 확정 피해 반경 (꼼수 방지, 대쉬 무적/기둥 뒤 숨기로 회피됩니다)")]
    public float slamMeleeRadius = 2f;
    public int slamWaveCount = 2;
    [Tooltip("파동이 중심에서 최대 반경까지 도달하는 데 걸리는 시간 (클수록 파동이 느려져 회피하기 쉬워집니다)")]
    public float slamWaveExpandTime = 1.4f;
    [Tooltip("충격파 회차 사이의 간격")]
    public float slamWaveInterval = 0.8f;
    [Tooltip("방을 찾지 못했을 때(fallback) 사용할 파동 최대 반경")]
    public float slamWaveFallbackMaxRadius = 10f;
    [Tooltip("파동 고리의 실제 피해 판정 두께 (이 두께만큼 스쳐 지나가는 순간에만 피해 판정 - 얇을수록 회피가 쉬워집니다)")]
    public float slamRingThickness = 0.9f;
    public float slamWaveDamage = 18f;
    [Tooltip("기둥 뒤에 숨어서 충격파를 회피했을 때, 그 기둥이 대신 입는 내구도 피해 (1회당)")]
    public int slamPillarDamagePerWave = 2;

    // ==============================================================
    // 런타임 상태 (ScriptableObject 공유 인스턴스 기준 - 다른 보스 패턴들과 동일한 구조)
    // ==============================================================
    private bool _isBusy = false;
    private bool _pillarsSpawned = false;
    private bool _superArmorApplied = false;
    private List<EliteMonsterPillar> _pillars = new List<EliteMonsterPillar>();
    private List<int> _specialPool = new List<int>();
    private int _lastBasicAttack = -1;
    private EliteBossPatternLabel _label;
    private Coroutine _basicAttackCoroutine;

    // 마지막으로 유효했던(0벡터가 아니었던) 조준 방향입니다. 목표와 완전히 겹치는 등 방향 계산이
    // 불가능한 순간에도, 임의의 고정 방향(예: 오른쪽) 대신 이 값을 사용해 "플레이어 반대 방향으로
    // 공격이 나가는" 것처럼 보이는 문제를 방지합니다.
    private Vector2 _lastAimDir = Vector2.down;

    // 기본 공격 상태로 (다시) 돌아온 절대 시각(Time.time). 이 시각으로부터 specialPatternInterval초가
    // 지나면 다음 패턴을 발동합니다. Execute()가 매 프레임 불리지 않아도(공격 중엔 아예 안 불림) 정확합니다.
    private float _basicPhaseStartTime;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _isBusy = false;
        _pillarsSpawned = false;
        _superArmorApplied = false;
        _pillars.Clear();
        _specialPool.Clear();
        _lastBasicAttack = -1;
        _label = null;
        _basicAttackCoroutine = null;
        _lastAimDir = Vector2.down;
        _basicPhaseStartTime = Time.time;
    }

    public override void Execute(BaseEntity entity)
    {
        if (entity.CurrentState == AIState.Thrown || entity.CurrentState == AIState.Caught) return;

        if (entity.Target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) entity.Target = player.transform;
            if (entity.Target == null)
            {
                entity.UpdateAnimation(AIState.Idle);
                return;
            }
        }

        // 슈퍼아머: 플레이어의 기본 공격에 경직/넉백되지 않도록 최초 1회 부여합니다.
        if (!_superArmorApplied && entity.Stats != null && entity.Stats.Status != null)
        {
            entity.Stats.Status.ApplySuperArmor(superArmorGauge);
            _superArmorApplied = true;
        }

        if (!_pillarsSpawned)
        {
            SpawnPillars(entity);
            _pillarsSpawned = true;
        }

        if (showPatternLabel && _label == null)
        {
            _label = CreatePatternLabel(entity);
        }

        // 참고: 엔진(BaseEntity.Update -> CanExecuteAI)이 IsAttacking == true인 동안에는 이 함수 자체를
        // 호출하지 않으므로, 아래 인터럽트 분기는 사실상 "공격과 공격 사이의 짧은 순간"에만 유효합니다.
        // 그래도 8초 판정 자체는 Time.time 절대시각 비교라 정확합니다.
        if (!_isBusy)
        {
            entity.LookAtTarget(entity.Target);

            if (Time.time - _basicPhaseStartTime >= specialPatternInterval)
            {
                // 기본 공격 도중이라도(가능한 타이밍이라면) 강제로 중단하고 즉시 패턴을 발동합니다.
                if (entity.IsAttacking)
                {
                    if (_basicAttackCoroutine != null)
                    {
                        entity.StopCoroutine(_basicAttackCoroutine);
                        _basicAttackCoroutine = null;
                    }
                    entity.IsAttacking = false;
                    ClearLabel();
                }

                int pattern = DrawFromSpecialPool();
                entity.StartCoroutine(RunSpecialPattern(entity, pattern));
                return;
            }

            if (!entity.IsAttacking)
            {
                entity.AtkTimer += Time.deltaTime;

                float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
                var agent = entity.GetComponent<NavMeshAgent>();

                if (dist <= entity.Stats.ATKRANGE && entity.AtkTimer >= entity.Stats.ATKSPD)
                {
                    entity.CurrentState = AIState.Attack;
                    StopNavAgent(entity);
                    entity.AtkTimer = 0f;
                    _basicAttackCoroutine = entity.StartCoroutine(BasicAttackRoutine(entity));
                }
                else
                {
                    entity.CurrentState = AIState.Follow;
                    if (agent != null && agent.isActiveAndEnabled)
                    {
                        agent.isStopped = false;
                        agent.speed = entity.Stats.MOVESPEED;
                        agent.SetDestination(entity.Target.position);
                    }
                }
            }
        }

        entity.UpdateAnimation(entity.CurrentState);
    }

    /// <summary>
    /// 대상 방향을 계산합니다. 방향을 구할 수 없는 경우(대상이 없거나 완전히 겹쳐 0벡터가 되는 경우)
    /// 임의의 고정 방향 대신 마지막으로 유효했던 방향을 반환하여, 공격이 엉뚱한(반대) 방향으로
    /// 나가는 것처럼 보이는 문제를 방지합니다.
    /// </summary>
    private Vector2 GetAimDir(BaseEntity entity)
    {
        if (entity.Target != null)
        {
            Vector2 raw = (Vector2)entity.Target.position - (Vector2)entity.transform.position;
            if (raw.sqrMagnitude > 0.0001f)
            {
                Vector2 dir = raw.normalized;
                _lastAimDir = dir;
                return dir;
            }
        }
        return _lastAimDir;
    }

    /// <summary>
    /// [단순화] 부채꼴 히트박스를 항상 보스 중심에 위치시키고, 현재 조준 방향(GetAimDir)을 향해
    /// 회전만 시킵니다. 예전에는 프리팹의 로컬 꼭짓점 좌표를 역산해 꼭짓점을 보스 위치에 정확히
    /// 맞추려 했는데, 프리팹의 실제 "앞쪽" 방향에 대한 가정이 틀려서 오히려 반대 방향으로 나가는
    /// 문제가 있었습니다. 정확한 꼭짓점 정렬보다 확실하게 플레이어를 향하는 것이 더 중요하므로,
    /// 보스 중심 고정 + 회전만 적용하는 단순한 방식으로 바꿨습니다. 시전(윈드업) 도중 매 프레임
    /// 호출해 마지막 순간까지 플레이어를 계속 조준합니다.
    /// </summary>
    private void AimFanHitbox(GameObject hitboxObj, BaseEntity entity)
    {
        Vector2 dir = GetAimDir(entity);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        hitboxObj.transform.position = entity.transform.position;
        hitboxObj.transform.rotation = Quaternion.Euler(0f, 0f, angle + fanRotationOffset);
    }

    // ==============================================================
    // 방 정보 헬퍼
    // ==============================================================
    private struct RoomMetrics
    {
        public bool found;
        public Vector2 center;
        public float halfX;
        public float halfY;
        public Bounds bounds;
    }

    private RoomMetrics GetRoomMetrics(BaseEntity entity)
    {
        RoomInstance room = GetCurrentRoom(entity);
        RoomMetrics m = new RoomMetrics();
        if (room == null)
        {
            m.found = false;
            return m;
        }

        m.found = true;
        m.center = (Vector2)room.transform.position + room.centerOffset;
        m.halfX = room.roomSize.x / 2f;
        m.halfY = room.roomSize.y / 2f;
        m.bounds = new Bounds(m.center, new Vector3(room.roomSize.x - 0.5f, room.roomSize.y - 0.5f, 10f));
        return m;
    }

    /// <summary>
    /// origin이 bounds 내부에 있다고 가정하고, dir 방향으로 bounds를 빠져나가는 지점까지의 거리를 계산합니다.
    /// (UnityEngine.Bounds.IntersectRay는 origin이 내부에 있으면 0을 반환하므로 직접 슬랩(slab) 방식으로 계산합니다.)
    /// </summary>
    private float GetBoundsExitDistance(Bounds bounds, Vector2 origin, Vector2 dir)
    {
        float t = float.MaxValue;

        if (Mathf.Abs(dir.x) > 0.0001f)
        {
            float tx = dir.x > 0f ? (bounds.max.x - origin.x) / dir.x : (bounds.min.x - origin.x) / dir.x;
            if (tx > 0f) t = Mathf.Min(t, tx);
        }
        if (Mathf.Abs(dir.y) > 0.0001f)
        {
            float ty = dir.y > 0f ? (bounds.max.y - origin.y) / dir.y : (bounds.min.y - origin.y) / dir.y;
            if (ty > 0f) t = Mathf.Min(t, ty);
        }

        return t == float.MaxValue ? 0f : t;
    }

    // ==============================================================
    // 기둥 소환
    // ==============================================================
    private void SpawnPillars(BaseEntity entity)
    {
        RoomMetrics room = GetRoomMetrics(entity);
        Vector2 origin = entity.transform.position;
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        for (int i = 0; i < pillarCount && i < dirs.Length; i++)
        {
            Vector2 targetPos;
            if (room.found)
            {
                float halfX = Mathf.Max(1f, room.halfX - pillarWallMargin);
                float halfY = Mathf.Max(1f, room.halfY - pillarWallMargin);
                float marginX = halfX * pillarInwardFactor;
                float marginY = halfY * pillarInwardFactor;

                targetPos = room.center + new Vector2(dirs[i].x * marginX, dirs[i].y * marginY);
            }
            else
            {
                targetPos = origin + dirs[i] * pillarSpawnDistance;
            }

            if (NavMesh.SamplePosition(targetPos, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
            {
                targetPos = navHit.position;
            }

            GameObject pillarObj;
            if (pillarPrefab != null)
            {
                pillarObj = GameObject.Instantiate(pillarPrefab, targetPos, Quaternion.identity);
            }
            else
            {
                pillarObj = CreateFallbackPillar(targetPos);
            }

            EliteMonsterPillar pillar = pillarObj.GetComponent<EliteMonsterPillar>();
            if (pillar == null) pillar = pillarObj.AddComponent<EliteMonsterPillar>();
            pillar.SetMaxHP(pillarMaxHP);
            pillar.Owner = entity.gameObject;

            _pillars.Add(pillar);
        }
    }

    private GameObject CreateFallbackPillar(Vector2 pos)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        obj.name = "EliteMonster_Pillar";
        obj.transform.position = pos;
        obj.transform.localScale = new Vector3(1f, 1.4f, 1f);

        var col = obj.AddComponent<CircleCollider2D>();
        col.radius = 0.55f;

        // "Obstacle" 레이어는 이 프로젝트에 존재하지 않으므로, 실제 존재하며 Player/Enemy와
        // 충돌하도록 설정되어 있는 "Object" 레이어를 사용합니다.
        int objectLayer = LayerMask.NameToLayer("Object");
        if (objectLayer >= 0) obj.layer = objectLayer;

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(0.55f, 0.4f, 0.25f);

        return obj;
    }

    // ==============================================================
    // 패턴 이름 라벨
    // ==============================================================
    private EliteBossPatternLabel CreatePatternLabel(BaseEntity entity)
    {
        GameObject labelObj = new GameObject("PatternLabel");
        labelObj.transform.SetParent(entity.transform, false);
        labelObj.transform.localPosition = patternLabelOffset;

        Vector3 lossy = entity.transform.lossyScale;
        float invX = lossy.x != 0f ? 1f / lossy.x : 1f;
        float invY = lossy.y != 0f ? 1f / lossy.y : 1f;
        labelObj.transform.localScale = new Vector3(invX, invY, 1f);

        EliteBossPatternLabel label = labelObj.AddComponent<EliteBossPatternLabel>();
        label.SetFont(patternLabelFont);
        return label;
    }

    private void ShowLabel(string text)
    {
        if (showPatternLabel && _label != null) _label.SetText(text);
    }

    private void ClearLabel()
    {
        if (_label != null) _label.Clear();
    }

    // ==============================================================
    // 기본 공격 3종
    // ==============================================================
    private int PickBasicAttack()
    {
        int next;
        do { next = Random.Range(0, 3); } while (next == _lastBasicAttack);
        _lastBasicAttack = next;
        return next;
    }

    private IEnumerator BasicAttackRoutine(BaseEntity entity)
    {
        entity.IsAttacking = true;
        entity.HasFiredHitEvent = false;
        entity.HasFiredAttackEndEvent = false;

        if (entity.Animator != null && entity.Animator.runtimeAnimatorController != null)
        {
            entity.Animator.speed = 1f;
            entity.Animator.Play("Attack", -1, 0f);
        }

        int atkIndex = PickBasicAttack(); // 0: 단순찍기, 1: 부채꼴, 2: 휩쓸기

        float windup = atkIndex == 0 ? stabWindup : atkIndex == 1 ? fanWindup : sweepWindup;
        float radius = atkIndex == 0 ? stabRadius : atkIndex == 1 ? fanRadius : sweepRadius;
        GameObject prefab = atkIndex == 0 ? stabHitboxPrefab : atkIndex == 1 ? fanHitboxPrefab : sweepHitboxPrefab;
        string labelText = atkIndex == 0 ? label_Stab : atkIndex == 1 ? label_Fan : label_Sweep;

        ShowLabel(labelText);

        Vector2 dir = GetAimDir(entity);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        GameObject hitboxObj;
        bool isFan = (atkIndex == 1);

        if (isFan)
        {
            // 부채꼴(삼각형) 공격: 보스 중심에 스폰하고, 회전은 AimFanHitbox()가 담당합니다.
            // 이 함수는 아래 윈드업 대기 루프에서 매 프레임 다시 호출되어, 시전 도중에도
            // 플레이어를 계속 조준하도록 합니다.
            hitboxObj = fanHitboxPrefab != null
                ? GameObject.Instantiate(fanHitboxPrefab, entity.transform.position, Quaternion.identity)
                : CreateFallbackCircle(entity.transform.position, 0.5f, new Color(1f, 0f, 0f, 0.35f));

            hitboxObj.transform.localScale = Vector3.one * radius;
            AimFanHitbox(hitboxObj, entity);
        }
        else
        {
            // 휩쓸기는 보스 자신을 중심으로, 단순 찍기는 전방으로 살짝 띄워서 스폰합니다.
            Vector2 spawnPos = atkIndex == 2
                ? (Vector2)entity.transform.position
                : (Vector2)entity.transform.position + dir * 0.8f;

            hitboxObj = prefab != null
                ? GameObject.Instantiate(prefab, spawnPos, Quaternion.Euler(0, 0, angle))
                : CreateFallbackCircle(spawnPos, 0.5f, new Color(1f, 0f, 0f, 0.35f));

            hitboxObj.transform.localScale = Vector3.one * radius;
        }

        BaseHitBox hb = hitboxObj.GetComponent<BaseHitBox>();
        if (hb == null) hb = hitboxObj.AddComponent<BaseHitBox>();

        DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, false, 1f, true);
        hb.Init(info, entity.opponentLayer, 0.25f, windup, entity.team == Team.Ally);

        float t = 0f;
        while (t < windup)
        {
            t += Time.deltaTime;

            // 부채꼴 공격은 시전(윈드업) 도중에도 매 프레임 다시 조준해, 마지막 순간까지
            // 플레이어를 따라갑니다.
            if (isFan && hitboxObj != null)
            {
                AimFanHitbox(hitboxObj, entity);
            }

            yield return null;
        }

        entity.HasFiredHitEvent = true;

        // 모든 기본 공격 후 1초 후딜레이 (연속 즉시 시전 방지)
        yield return new WaitForSeconds(basicAttackPostDelay);

        if (entity.Animator != null) entity.Animator.speed = 1f;
        entity.IsAttacking = false;
        _basicAttackCoroutine = null;
        entity.ResetAnimationState();
        ClearLabel();
    }

    // ==============================================================
    // 특수 패턴 풀: 3개(0,1,2)를 전부 한 번씩 쓸 때까지 같은 패턴이 다시 나오지 않습니다.
    // 풀이 비면(3개 다 사용) 다시 3개로 리필합니다.
    // ==============================================================
    private int DrawFromSpecialPool()
    {
        if (_specialPool.Count == 0)
        {
            _specialPool = new List<int> { 0, 1, 2 };
        }

        int idx = Random.Range(0, _specialPool.Count);
        int picked = _specialPool[idx];
        _specialPool.RemoveAt(idx);
        return picked;
    }

    private IEnumerator RunSpecialPattern(BaseEntity entity, int pattern)
    {
        _isBusy = true;
        entity.IsAttacking = true;
        entity.CurrentState = AIState.Attack;

        switch (pattern)
        {
            case 0:
                yield return Pattern1_PillarCharge(entity);
                break;
            case 1:
                yield return Pattern2_Donut(entity);
                break;
            default:
                yield return Pattern3_GroundSlam(entity);
                break;
        }

        ClearLabel();
        entity.IsAttacking = false;
        entity.ResetAnimationState();
        _isBusy = false;

        // 이제부터 다시 기본 공격 페이즈이므로, 8초 카운트를 여기서부터 새로 시작합니다.
        _basicPhaseStartTime = Time.time;
    }

    // --- 패턴 1: 기둥과 돌진 ---
    private IEnumerator Pattern1_PillarCharge(BaseEntity entity)
    {
        ShowLabel(label_Pattern1Windup);

        RoomMetrics room = GetRoomMetrics(entity);

        float t = 0f;
        Vector2 chargeDir = GetAimDir(entity);
        GameObject telegraph = null;

        // 3초 조준: 플레이어 방향을 실시간으로 주시하며, 바닥에 돌진 경로를 빨간 직사각형으로 표시합니다.
        // 전조는 항상 "플레이어 발밑"까지 확실히 이어지도록 대상과의 거리 기준으로 길이를 계산합니다.
        while (t < chargeWindup)
        {
            t += Time.deltaTime;
            if (entity.Target != null)
            {
                chargeDir = GetAimDir(entity);
                entity.LookAtTarget(entity.Target);
            }

            float length = chargeTelegraphLength;
            if (entity.Target != null)
            {
                length = Vector2.Distance(entity.transform.position, entity.Target.position) + chargeTelegraphOvershoot;
            }
            if (room.found)
            {
                float exitDist = GetBoundsExitDistance(room.bounds, entity.transform.position, chargeDir);
                if (exitDist > 0f) length = Mathf.Min(length, exitDist);
            }
            length = Mathf.Min(length, chargeTelegraphLength);

            if (telegraph == null)
            {
                telegraph = CreateFallbackRect(chargeTelegraphColor);
            }
            UpdateChargeTelegraph(telegraph, entity.transform.position, chargeDir, length, chargeHitRadius * 2f);

            yield return null;
        }

        if (telegraph != null) GameObject.Destroy(telegraph);

        ShowLabel(label_Pattern1);

        var agent = entity.GetComponent<NavMeshAgent>();
        bool wasAgentEnabled = agent != null && agent.enabled;
        if (wasAgentEnabled)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;
        }

        // 방 경계를 벗어나 맵 밖으로 튀어나가는 것을 막기 위한 안전장치 (벽 콜라이더가 없거나
        // 누락된 경우에도 방 범위 안에서 강제로 돌진을 멈춥니다).
        Bounds? roomBounds = room.found ? (Bounds?)room.bounds : null;

        var rb = entity.GetComponent<Rigidbody2D>();
        float chargeSpeed = entity.Stats.MOVESPEED * chargeSpeedMultiplier;
        float maxDuration = 3f;
        float elapsed = 0f;

        LayerMask playerMask = LayerMask.GetMask("Player", "Player_Dash");
        LayerMask wallMask = LayerMask.GetMask("Wall", "Object");
        LayerMask hitMask = playerMask | wallMask;

        bool hitSomething = false;
        EliteMonsterPillar hitPillar = null;

        while (elapsed < maxDuration)
        {
            elapsed += Time.deltaTime;
            if (rb != null) rb.linearVelocity = chargeDir * chargeSpeed;

            float checkDist = chargeSpeed * Time.deltaTime + 0.2f;

            // 기둥/벽이 플레이어보다 먼저 걸리도록, "Object"(기둥) + "Wall" 레이어를 우선 검사합니다.
            // 기둥 뒤에 숨은 플레이어가 기둥보다 먼저 맞는 일이 없도록 플레이어 판정은 별도로 나중에 검사합니다.
            RaycastHit2D obstacleHit = Physics2D.CircleCast(entity.transform.position, chargeHitRadius, chargeDir, checkDist, wallMask);

            bool outOfBounds = roomBounds.HasValue && !roomBounds.Value.Contains(entity.transform.position);

            if (obstacleHit.collider != null || outOfBounds)
            {
                hitSomething = true;
                if (obstacleHit.collider != null)
                {
                    hitPillar = obstacleHit.collider.GetComponentInParent<EliteMonsterPillar>();
                }

                if (rb != null) rb.linearVelocity = -chargeDir * 3f;
                entity.transform.position = (Vector2)entity.transform.position - chargeDir * 0.15f;
                break;
            }

            // 기둥/벽에 막히지 않았을 때만 플레이어 직격 여부를 검사합니다.
            RaycastHit2D playerHit = Physics2D.CircleCast(entity.transform.position, chargeHitRadius, chargeDir, checkDist, playerMask);
            if (playerHit.collider != null)
            {
                hitSomething = true;

                CharacterHealth pHealth = playerHit.collider.GetComponentInChildren<CharacterHealth>();
                if (pHealth == null) pHealth = playerHit.collider.GetComponentInParent<CharacterHealth>();
                if (pHealth != null && !pHealth.Invincible)
                {
                    pHealth.GetDamage(new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));
                }

                if (rb != null) rb.linearVelocity = -chargeDir * 3f;
                entity.transform.position = (Vector2)entity.transform.position - chargeDir * 0.15f;
                break;
            }

            yield return null;
        }

        if (rb != null) rb.linearVelocity = Vector2.zero;

        if (wasAgentEnabled && agent != null)
        {
            if (NavMesh.SamplePosition(entity.transform.position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                entity.transform.position = navHit.position;
            }
            agent.enabled = true;
            agent.isStopped = false;
        }

        if (hitPillar != null && hitPillar.IsAlive)
        {
            // 기둥에 명중: 체력과 무관하게 즉시 붕괴 (기획서대로 이 순간에는 무피해) + 보스 5초 기절.
            // 안전지대 폭발 피해는 2초 뒤(EliteMonsterPillar.CollapseRoutine)에만 발생합니다.
            hitPillar.CollapseInstantly();
            if (entity.Stats != null && entity.Stats.Status != null)
            {
                entity.Stats.Status.SetDebuffBool(DebuffBoolType.Stunned, pillarChargeStunDuration);
            }
        }
        else if (hitSomething)
        {
            // 벽 혹은 기둥이 없는 상태에서의 충돌: 짧은 기절만 부여
            if (entity.Stats != null && entity.Stats.Status != null)
            {
                entity.Stats.Status.SetDebuffBool(DebuffBoolType.Stunned, wallChargeStunDuration);
            }
        }
    }

    /// <summary>
    /// 돌진 경로를 나타내는 빨간 직사각형 전조를 생성/갱신합니다. (보스 위치 기준 전방으로 length만큼)
    /// </summary>
    private void UpdateChargeTelegraph(GameObject telegraph, Vector2 originPos, Vector2 dir, float length, float width)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 mid = originPos + dir * (length * 0.5f);
        telegraph.transform.position = mid;
        telegraph.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        telegraph.transform.localScale = new Vector3(length, width, 1f);
    }

    // --- 패턴 2: 안 팎 도넛 ---
    private IEnumerator Pattern2_Donut(BaseEntity entity)
    {
        ShowLabel(label_Pattern2);

        StopNavAgent(entity);

        RoomMetrics room = GetRoomMetrics(entity);

        // 판정 중심은 항상 "엘리트 몹 자신의 현재 위치"입니다. (맵/방 중앙이 아님)
        Vector2 center = entity.transform.position;

        float innerSmall, outerSmall, innerLarge, outerLarge, fieldMax;
        if (room.found)
        {
            // 방이 원형이 아니므로, 짧은 쪽 절반 크기를 기준으로 원형 범위 크기만 산정합니다.
            // (판정 "중심"은 여전히 보스 위치이며, 이 값은 크기 스케일링에만 사용됩니다.)
            float roomRadius = Mathf.Min(room.halfX, room.halfY);
            innerSmall = roomRadius * donutInnerSmallRatio;
            outerSmall = roomRadius * donutOuterSmallRatio;
            innerLarge = roomRadius * donutInnerLargeRatio;
            outerLarge = roomRadius * donutOuterLargeRatio;
            fieldMax = roomRadius * donutFieldMaxRatio;
        }
        else
        {
            innerSmall = donutFallbackInnerSmall;
            outerSmall = donutFallbackOuterSmall;
            innerLarge = donutFallbackInnerLarge;
            outerLarge = donutFallbackOuterLarge;
            fieldMax = donutFallbackOuterLarge * 1.25f;
        }

        // 안->팎->안->팎 또는 팎->안->팎->안 중 랜덤
        bool startsIn = Random.value > 0.5f;

        float[] innerRadii = { innerSmall, innerLarge };
        float[] outerRadii = { outerSmall, outerLarge };

        for (int i = 0; i < 4; i++)
        {
            bool isInPhase = (i % 2 == 0) ? startsIn : !startsIn;
            int sizeIndex = i / 2; // 0: 1회차(작은 범위), 1: 2회차(더 넓은 범위)
            float radius = isInPhase ? innerRadii[sizeIndex] : outerRadii[sizeIndex];

            GameObject telegraph = SpawnDonutTelegraph(center, radius, isInPhase, fieldMax);

            yield return new WaitForSeconds(donutExplodeDelay);

            LayerMask targetLayer = LayerMask.GetMask("Player", "Army", "Ally");
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, fieldMax, targetLayer);
            foreach (var hit in hits)
            {
                float d = Vector2.Distance(center, hit.transform.position);
                bool inDanger = isInPhase ? (d <= radius) : (d > radius && d <= fieldMax);
                if (!inDanger) continue;

                CharacterStat stat = hit.GetComponentInParent<CharacterStat>();
                if (stat == null) stat = hit.GetComponentInChildren<CharacterStat>();
                if (stat != null && stat.Health != null && !stat.Health.IsDead)
                {
                    stat.Health.GetDamage(new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));
                }
            }

            if (telegraph != null) GameObject.Destroy(telegraph);
        }

        if (entity.Stats != null && entity.Stats.Status != null)
        {
            entity.Stats.Status.SetDebuffBool(DebuffBoolType.Stunned, donutStunDuration);
        }
    }

    /// <summary>
    /// 안/팎 도넛 전조를 위험지대(빨강)와 안전지대(초록) 두 겹으로 표시합니다.
    /// isInPhase == true: 중심(radius 이내)이 위험, 그 바깥이 안전.
    /// isInPhase == false: 중심(radius 이내)이 안전, 그 바깥이 위험.
    /// </summary>
    private GameObject SpawnDonutTelegraph(Vector2 center, float radius, bool isInPhase, float fieldMax)
    {
        GameObject container = new GameObject("Elite_Telegraph_Donut");
        container.transform.position = center;

        GameObject outer = new GameObject("Outer");
        outer.transform.SetParent(container.transform, false);
        SpriteRenderer outerSr = outer.AddComponent<SpriteRenderer>();
        outerSr.sprite = GetOrCreateCircleSprite();
        outerSr.sortingOrder = 9;
        outer.transform.localScale = Vector3.one * (fieldMax * 2f);

        GameObject inner = new GameObject("Inner");
        inner.transform.SetParent(container.transform, false);
        SpriteRenderer innerSr = inner.AddComponent<SpriteRenderer>();
        innerSr.sprite = GetOrCreateCircleSprite();
        innerSr.sortingOrder = 10;
        inner.transform.localScale = Vector3.one * (radius * 2f);

        if (isInPhase)
        {
            outerSr.color = donutSafeColor;
            innerSr.color = donutDangerColor;
        }
        else
        {
            outerSr.color = donutDangerColor;
            innerSr.color = donutSafeColor;
        }

        return container;
    }

    // --- 패턴 3: 바닥 충격파 (보스를 중심으로 퍼져나가는 얇은 고리형 파동, 대쉬로 회피 가능) ---
    private IEnumerator Pattern3_GroundSlam(BaseEntity entity)
    {
        ShowLabel(label_Pattern3Windup);
        StopNavAgent(entity);

        Vector2 preCenter = entity.transform.position;

        // 애니메이션이 없는 것을 보완하는 사전 예비동작: 발밑에 경고 원이 서서히 채워집니다. (이 동안은 무피해)
        GameObject warmup = CreateFallbackCircle(preCenter, 0.4f, new Color(1f, 0.4f, 0f, 0.15f));
        float wt = 0f;
        while (wt < slamPreCastDelay)
        {
            wt += Time.deltaTime;
            float scale = Mathf.Lerp(0.4f, slamMeleeRadius * 2f, wt / slamPreCastDelay);
            if (warmup != null)
            {
                warmup.transform.position = entity.transform.position;
                warmup.transform.localScale = Vector3.one * scale;
                var wsr = warmup.GetComponent<SpriteRenderer>();
                if (wsr != null) wsr.color = new Color(1f, 0.4f, 0f, Mathf.Lerp(0.15f, 0.4f, wt / slamPreCastDelay));
            }
            yield return null;
        }
        if (warmup != null) GameObject.Destroy(warmup);

        ShowLabel(label_Pattern3);
        Vector2 center = entity.transform.position;

        RoomMetrics room = GetRoomMetrics(entity);
        float maxRadius = room.found ? Mathf.Max(room.halfX, room.halfY) * 1.15f : slamWaveFallbackMaxRadius;

        // 보스 근처 밀착 시 확정 피해 (꼼수 방지) - 대쉬 무적, 혹은 기둥 뒤에 숨었으면 회피됩니다.
        LayerMask playerLayers = LayerMask.GetMask("Player", "Player_Dash");
        Collider2D[] meleeHits = Physics2D.OverlapCircleAll(center, slamMeleeRadius, playerLayers);
        foreach (var hit in meleeHits)
        {
            CharacterHealth pHealth = hit.GetComponentInChildren<CharacterHealth>();
            if (pHealth == null) pHealth = hit.GetComponentInParent<CharacterHealth>();
            if (pHealth == null || pHealth.IsDead || pHealth.Invincible) continue;

            EliteMonsterPillar shelterPillar = FindShelteringPillar(hit.transform.position);
            if (shelterPillar != null)
            {
                shelterPillar.DamagePattern(slamPillarDamagePerWave);
                continue;
            }

            pHealth.GetDamage(new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));
        }

        for (int wave = 0; wave < slamWaveCount; wave++)
        {
            yield return RunShockwaveRing(entity, center, maxRadius);

            if (wave < slamWaveCount - 1)
            {
                yield return new WaitForSeconds(slamWaveInterval);
            }
        }
    }

    /// <summary>
    /// 보스 중심에서 maxRadius까지 퍼져나가는 얇은 고리형 충격파 1회를 재생합니다.
    /// 고리가 실제로 지나가는 순간에만 피해 판정을 하며, 그 순간 대쉬 무적 상태면 회피됩니다.
    /// 기둥 뒤에 숨어서 회피한 경우, 플레이어는 무피해 대신 그 기둥이 내구도 피해를 입습니다.
    /// </summary>
    private IEnumerator RunShockwaveRing(BaseEntity entity, Vector2 center, float maxRadius)
    {
        GameObject ring = CreateFallbackRing(new Color(1f, 0.35f, 0f, 0.5f));
        ring.transform.position = center;

        HashSet<GameObject> alreadyChecked = new HashSet<GameObject>();
        LayerMask targetLayer = LayerMask.GetMask("Player", "Player_Dash");

        float t = 0f;
        while (t < slamWaveExpandTime)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / slamWaveExpandTime);
            float currentRadius = Mathf.Lerp(0f, maxRadius, progress);

            float diameter = currentRadius * 2f;
            ring.transform.localScale = new Vector3(diameter, diameter, 1f);

            Collider2D[] hits = Physics2D.OverlapCircleAll(center, currentRadius + slamRingThickness, targetLayer);
            foreach (var hit in hits)
            {
                if (alreadyChecked.Contains(hit.gameObject)) continue;

                float d = Vector2.Distance(center, hit.transform.position);
                bool inRing = d >= currentRadius - slamRingThickness && d <= currentRadius + slamRingThickness;
                if (!inRing) continue;

                alreadyChecked.Add(hit.gameObject);

                CharacterHealth pHealth = hit.GetComponentInChildren<CharacterHealth>();
                if (pHealth == null) pHealth = hit.GetComponentInParent<CharacterHealth>();
                if (pHealth == null || pHealth.IsDead || pHealth.Invincible) continue; // 대쉬(무적)로 회피 가능

                EliteMonsterPillar shelterPillar = FindShelteringPillar(hit.transform.position);
                if (shelterPillar != null)
                {
                    // 기둥 뒤에 숨었다면 플레이어는 무피해, 대신 기둥이 내구도 피해를 입음
                    shelterPillar.DamagePattern(slamPillarDamagePerWave);
                    continue;
                }

                pHealth.GetDamage(new DamageInfo(slamWaveDamage, DamageType.Physical, entity.gameObject));
            }

            yield return null;
        }

        if (ring != null) GameObject.Destroy(ring);
    }

    private EliteMonsterPillar FindShelteringPillar(Vector2 worldPos)
    {
        for (int i = 0; i < _pillars.Count; i++)
        {
            EliteMonsterPillar p = _pillars[i];
            if (p != null && p.IsAlive && p.IsSheltering(worldPos)) return p;
        }
        return null;
    }

    // ==============================================================
    // 유틸리티: 장판/히트박스 생성 (프리팹이 없을 때 대체용)
    // ==============================================================
    private GameObject CreateFallbackCircle(Vector2 pos, float baseScale, Color color)
    {
        GameObject obj = new GameObject("Elite_Telegraph_Circle");
        obj.transform.position = pos;
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateCircleSprite();
        sr.color = color;
        sr.sortingOrder = 10;
        obj.transform.localScale = Vector3.one * baseScale;
        return obj;
    }

    private GameObject CreateFallbackRect(Color color)
    {
        GameObject obj = new GameObject("Elite_Telegraph_Rect");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateSquareSprite();
        sr.color = color;
        sr.sortingOrder = 10;
        return obj;
    }

    private GameObject CreateFallbackRing(Color color)
    {
        GameObject obj = new GameObject("Elite_Telegraph_Ring");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateRingSprite();
        sr.color = color;
        sr.sortingOrder = 10;
        return obj;
    }

    private static Sprite _cachedCircleSprite;
    private static Sprite GetOrCreateCircleSprite()
    {
        if (_cachedCircleSprite != null) return _cachedCircleSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float r = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                tex.SetPixel(x, y, dist <= r ? Color.white : new Color(1, 1, 1, 0));
            }
        }
        tex.Apply();

        _cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedCircleSprite;
    }

    private static Sprite _cachedSquareSprite;
    private static Sprite GetOrCreateSquareSprite()
    {
        if (_cachedSquareSprite != null) return _cachedSquareSprite;

        int size = 8;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, Color.white);
            }
        }
        tex.Apply();

        _cachedSquareSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedSquareSprite;
    }

    private static Sprite _cachedRingSprite;
    private static Sprite GetOrCreateRingSprite()
    {
        if (_cachedRingSprite != null) return _cachedRingSprite;

        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerR = size / 2f;
        // 반경 대비 12%만 고리 두께로 사용 (실제 판정 두께와 시각적으로 더 잘 맞도록)
        float innerR = outerR * 0.88f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                bool inRing = dist <= outerR && dist >= innerR;
                tex.SetPixel(x, y, inRing ? Color.white : new Color(1, 1, 1, 0));
            }
        }
        tex.Apply();

        _cachedRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedRingSprite;
    }
}
