using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 본 마스터 보스 전용 컨트롤러.
/// - 부위(투구/견갑/흉갑) 파괴에 따른 받는피해 증가 + 페이즈2 진입을 관리한다.
/// - 카운터 게이지(BossCounterGauge), 머리 위 상태 텍스트(EliteBossPatternLabel)를 배선한다.
/// - 뼈 투기장(타원형 가시 경계)은 보스를 따라다니지 않고, 보스가 스폰된 RoomInstance 중심에 고정되며
///   방의 실제 가로/세로 비율(roomSize)에 맞춰 타원으로 그려진다.
/// - Charger Elite와 동일하게 상시 슈퍼아머를 부여해 플레이어 평타에 경직/넉백되지 않는다.
///
/// [버그 수정] NavMeshAgent 위치는 항상 WarpTo()로 옮긴다(직접 대입 시 에이전트 내부 상태와 어긋나
/// 나중에 "튕기는" 문제가 있었다). WarpTo()는 이제 대상 지점이 NavMesh 위가 아니면(예: 장식 바위 등
/// 걷기 불가능한 지형 근처) 가장 가까운 유효한 지점으로 보정해서 워프한다 — 안 그러면 Warp가 조용히
/// 실패하면서 보스가 그 자리에 "낑겨서" 아무것도 못 하는 것처럼 멈추는 문제가 있었다.
///
/// [버그 수정 — 페이즈 전환 시 이전 패턴이 안 끊기던 문제] 흉갑 파괴 즉시 StopAllCoroutines()로
/// 강제 정리한 뒤 페이즈2 전환을 시작한다.
///
/// [개선] 페이즈2 진입 시 체력이 새 최대치까지 서서히 차오르는 연출을 추가했다.
///
/// [버그 수정 — 흰 사각형] 아레나 테스트 모드에서 실제로 이어지는 방이 없는 "문 자리" 마커가
/// 기본 흰색 Square 스프라이트로 노출되는 문제를 막는다.
/// </summary>
public class BoneMasterController : EnemyController
{
    [Header("부위 파괴 설정")]
    [SerializeField] private float[] partBreakHpRatios = { 0.8f, 0.6f, 0.4f };
    [SerializeField] private float perPartIncomingDamageBonus = 0.15f;
    [SerializeField] private float baseArmorReduction = 0.2f;

    [SerializeField] private float helmetBreakRangeBonus = 0.15f;
    [SerializeField] private float pauldronBreakCastSpeedBonus = 0.15f;
    [SerializeField] private float chestBreakMoveSpeedBonus = 0.15f;
    [SerializeField] private float partBreakTextDuration = 1.5f;

    [Header("슈퍼아머")]
    public float superArmorGauge = 999999f;

    [Header("페이즈 전환")]
    public EnemyMinionDataSO phase2Data;
    [SerializeField] private float phase2HealFillDuration = 1f;

    [Header("UI 참조")]
    [SerializeField] private EliteBossPatternLabel patternLabel;
    [SerializeField] private Vector3 patternLabelOffset = new Vector3(0f, 1.6f, 0f);

    [Header("그로기(경직) 시각 피드백")]
    [SerializeField] private Color groggyFlashColor = new Color(0.4f, 0.85f, 1f, 1f);

    [Header("뼈 투기장 (방 경계 타원형, 링 판정)")]
    [Tooltip("방 크기 대비 타원 비율(1에 가까울수록 방을 거의 꽉 채움)")]
    [Range(0.5f, 1f)]
    [SerializeField] private float thornRingPhase1MarginRatio = 0.95f;
    [Tooltip("페이즈2에서 좁아지는 비율(페이즈1 크기 대비)")]
    [Range(0.3f, 1f)]
    [SerializeField] private float thornRingPhase2ShrinkRatio = 0.95f;
    [SerializeField] private Vector2 thornRingFallbackSize = new Vector2(16f, 16f);
    [SerializeField] private Color thornRingColor = new Color(1f, 0.05f, 0.05f, 1f);
    [SerializeField] private int thornRingSortingOrder = 5000;
    [SerializeField] private float thornRingBandRatio = 0.12f;

    public int PartsDestroyed { get; private set; } = 0;
    public int CurrentPhase { get; private set; } = 1;
    public bool IsGroggy { get; private set; } = false;

    public BossCounterGauge CounterGauge { get; private set; }
    public CharacterHealth Health => Stats != null ? Stats.Health : null;

    public float AttackRangeBonus { get; private set; } = 0f;
    public float PatternCastSpeedBonus { get; private set; } = 0f;

    private float _baseMoveSpeedCached = -1f;
    private SpriteRenderer[] _bodyRenderers;
    private Color[] _bodyOriginalColors;

    private ThornArenaHazard _thornRing;
    private Vector2 _thornRingPhase1Size = Vector2.zero;
    private Coroutine _stateTextClearRoutine;
    private Coroutine _groggyFlashRoutine;
    private float _lastControllerDiagTime = -100f;
    private NavMeshAgent _navAgent;
    private RoomInstance _cachedRoom;

    protected override void Start()
    {
        base.Start();

        _navAgent = GetComponent<NavMeshAgent>();

        CounterGauge = GetComponentInChildren<BossCounterGauge>();
        if (CounterGauge == null)
        {
            Debug.LogWarning($"[BoneMaster] {gameObject.name}: BossCounterGauge 컴포넌트가 없습니다.");
        }

        if (patternLabel == null) patternLabel = GetComponentInChildren<EliteBossPatternLabel>();
        if (patternLabel == null) patternLabel = CreatePatternLabel();

        _bodyRenderers = GetComponentsInChildren<SpriteRenderer>();
        _bodyOriginalColors = new Color[_bodyRenderers.Length];
        for (int i = 0; i < _bodyRenderers.Length; i++) _bodyOriginalColors[i] = _bodyRenderers[i].color;

        if (Stats != null) _baseMoveSpeedCached = Stats.BaseMoveSpeed;

        if (Stats != null && Stats.Status != null)
        {
            Stats.Status.ApplySuperArmor(superArmorGauge);
        }

        if (Health != null) Health.UpdateHPBar += CheckPartBreak;

        SetStateText("추격 중...");
        DamageEventBus.OnBeforeDamageCalculated += HandleIncomingDamageAmp;

        SetupThornArenaRing();
        HideDanglingDoorPlaceholders();
    }

    protected override void Update()
    {
        base.Update();

        if (Time.time - _lastControllerDiagTime > 2f)
        {
            _lastControllerDiagTime = Time.time;
            string brainType = Brain != null ? Brain.GetType().Name : "NULL";
            string targetName = Target != null ? Target.name : "NULL";
            bool onMesh = _navAgent != null && _navAgent.isOnNavMesh;
            Debug.Log($"<color=magenta>[BoneMaster-CTRL-Diag]</color> enabled={enabled} CurrentState={CurrentState} IsAttacking={IsAttacking} Target={targetName} Brain={brainType} Phase={CurrentPhase} IsGroggy={IsGroggy} OnNavMesh={onMesh} Pos={transform.position}");
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (Health != null) Health.UpdateHPBar -= CheckPartBreak;
        DamageEventBus.OnBeforeDamageCalculated -= HandleIncomingDamageAmp;
        if (_thornRing != null) Destroy(_thornRing.gameObject);
    }

    private void HandleIncomingDamageAmp(CharacterHealth target, ref DamageInfo info)
    {
        if (target != Health) return;
        if (info.amount <= 0f) return;

        float multiplier = (1f - baseArmorReduction) + perPartIncomingDamageBonus * PartsDestroyed;
        info.amount *= Mathf.Max(0f, multiplier);
    }

    private void CheckPartBreak()
    {
        if (Stats == null || Health == null) return;
        if (PartsDestroyed >= partBreakHpRatios.Length) return;

        float ratio = Health.MaxHP > 0f ? Health.CurHP / Health.MaxHP : 1f;
        float nextThreshold = partBreakHpRatios[PartsDestroyed];

        if (ratio <= nextThreshold)
        {
            BreakNextPart();
        }
    }

    private void BreakNextPart()
    {
        int partIndex = PartsDestroyed;
        PartsDestroyed++;

        switch (partIndex)
        {
            case 0:
                AttackRangeBonus += helmetBreakRangeBonus;
                SetStateTextTemporary("투구 파괴!", Color.red, partBreakTextDuration);
                Debug.Log("<color=orange>[BoneMaster]</color> 투구 파괴! 받는피해 +15%, 공격범위 +15%");
                break;
            case 1:
                PatternCastSpeedBonus += pauldronBreakCastSpeedBonus;
                SetStateTextTemporary("견갑 파괴!", Color.red, partBreakTextDuration);
                Debug.Log("<color=orange>[BoneMaster]</color> 견갑 파괴! 받는피해 +15%(누적 30%), 패턴 시전속도 +15%");
                break;
            case 2:
                if (Stats != null && _baseMoveSpeedCached > 0f)
                {
                    Stats.SetBaseMoveSpeed(_baseMoveSpeedCached * (1f + chestBreakMoveSpeedBonus));
                }
                SetStateTextTemporary("흉갑 파괴! 페이즈 2 돌입!", Color.red, partBreakTextDuration);
                Debug.Log("<color=red>[BoneMaster]</color> 흉갑 파괴! 받는피해 +15%(누적 45%), 이동속도 +15%. 페이즈2 진입.");

                StopAllCoroutines();
                StartCoroutine(Phase2TransitionRoutine());
                break;
        }
    }

    private IEnumerator Phase2TransitionRoutine()
    {
        IsGroggy = false;
        HardStopMovement();
        if (Health != null) Health.Invincible = true;

        yield return new WaitForSeconds(1.5f);

        CurrentPhase = 2;
        ShrinkThornArenaRing();

        if (phase2Data != null)
        {
            Stats.InitializeStats(phase2Data);
            _baseMoveSpeedCached = Stats.BaseMoveSpeed;

            if (Stats.Status != null) Stats.Status.ApplySuperArmor(superArmorGauge);

            if (phase2Data.aiPattern != null)
            {
                var newAi = ScriptableObject.Instantiate(phase2Data.aiPattern);
                var oldBrain = Brain;
                newAi.Init(this);
                SetRuntimeBrain(newAi);
                if (oldBrain != null) Destroy(oldBrain);
            }

            if (Health != null)
            {
                float newMax = Health.MaxHP;
                Health.SetHP(0f);
                yield return StartCoroutine(AnimateHealthFillUp(newMax, phase2HealFillDuration));
            }
        }
        else
        {
            Debug.LogWarning("[BoneMaster] phase2Data가 비어 있어 페이즈2 스탯/AI 전환을 건너뜁니다.");
        }

        if (Health != null) Health.Invincible = false;
        Debug.Log("<color=red>[BoneMaster]</color> 페이즈 2 전투 시작!");
    }

    private IEnumerator AnimateHealthFillUp(float targetMax, float duration)
    {
        if (Health == null || duration <= 0f)
        {
            Health?.SetHP(targetMax);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Health.SetHP(Mathf.Lerp(0f, targetMax, t / duration));
            yield return null;
        }
        Health.SetHP(targetMax);
    }

    private void SetRuntimeBrain(AIPatternSO brain)
    {
        _runtimeBrain = brain;
    }

    public void ApplyGroggy(float duration)
    {
        if (Stats != null && Stats.Status != null)
        {
            IsGroggy = true;
            Stats.Status.ApplyFixedStun(duration);

            if (_groggyFlashRoutine != null) StopCoroutine(_groggyFlashRoutine);
            _groggyFlashRoutine = StartCoroutine(GroggyFlashRoutine(duration));

            StartCoroutine(ClearGroggyFlagAfter(duration));
        }
    }

    private IEnumerator GroggyFlashRoutine(float duration)
    {
        SetVisualFlash(groggyFlashColor);
        yield return new WaitForSeconds(duration);
        ClearVisualFlash();
        _groggyFlashRoutine = null;
    }

    private IEnumerator ClearGroggyFlagAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        IsGroggy = false;
    }

    public void SetStateText(string text, Color? color = null)
    {
        if (_stateTextClearRoutine != null)
        {
            StopCoroutine(_stateTextClearRoutine);
            _stateTextClearRoutine = null;
        }
        patternLabel?.SetText(text, color);
    }

    public void SetStateTextTemporary(string text, Color color, float duration)
    {
        patternLabel?.SetText(text, color);
        if (_stateTextClearRoutine != null) StopCoroutine(_stateTextClearRoutine);
        _stateTextClearRoutine = StartCoroutine(ClearStateTextAfter(duration));
    }

    private IEnumerator ClearStateTextAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        patternLabel?.Clear();
        _stateTextClearRoutine = null;
    }

    public void SetVisualFlash(Color color)
    {
        if (_bodyRenderers == null) return;
        foreach (var sr in _bodyRenderers)
        {
            if (sr == null) continue;
            sr.color = color;
        }
    }

    public void ClearVisualFlash()
    {
        if (_bodyRenderers == null) return;
        for (int i = 0; i < _bodyRenderers.Length; i++)
        {
            if (_bodyRenderers[i] == null) continue;
            _bodyRenderers[i].color = _bodyOriginalColors[i];
        }
    }

    public void HardStopMovement()
    {
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        if (_navAgent != null && _navAgent.isOnNavMesh)
        {
            _navAgent.velocity = Vector3.zero;
        }
    }

    /// <summary>NavMeshAgent가 있으면 Warp()로, 없으면 그냥 Transform으로 위치를 옮긴다.
    /// 목표 지점이 NavMesh 위가 아니면(장식 바위 등 걷기 불가능한 지형 근처) 가장 가까운 유효한
    /// 지점으로 보정해서 워프한다 — 안 그러면 Warp가 조용히 실패하면서 보스가 그 자리에 멈춰버린다.</summary>
    public void WarpTo(Vector3 pos)
    {
        if (_navAgent != null)
        {
            Vector3 target = pos;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                target = hit.position;
            }
            else
            {
                Debug.LogWarning($"[BoneMaster] WarpTo({pos})가 NavMesh 근처(3유닛 이내)에서 유효한 지점을 못 찾았습니다. 원래 좌표로 그대로 이동합니다.");
            }
            _navAgent.Warp(target);
        }
        else
        {
            transform.position = pos;
        }
    }

    public float GetChargeDistance(Vector2 origin, Vector2 dir)
    {
        return _thornRing != null ? _thornRing.GetDistanceToInnerEdge(origin, dir) : -1f;
    }

    public const string ThornWallTag = "BoneSpikeWall";

    private EliteBossPatternLabel CreatePatternLabel()
    {
        GameObject labelObj = new GameObject("PatternLabel");
        labelObj.transform.SetParent(transform, false);
        labelObj.transform.localPosition = patternLabelOffset;

        Vector3 lossy = transform.lossyScale;
        float invX = lossy.x != 0f ? 1f / lossy.x : 1f;
        float invY = lossy.y != 0f ? 1f / lossy.y : 1f;
        labelObj.transform.localScale = new Vector3(invX, invY, 1f);

        return labelObj.AddComponent<EliteBossPatternLabel>();
    }

    private void SetupThornArenaRing()
    {
        RoomInstance room = FindContainingRoom();
        Vector3 center;
        Vector2 size;

        if (room != null)
        {
            center = (Vector3)((Vector2)room.transform.position + room.centerOffset);
            size = new Vector2(room.roomSize.x, room.roomSize.y) * thornRingPhase1MarginRatio;
            Debug.Log($"<color=cyan>[BoneMaster]</color> 뼈 투기장: 방 발견 (center={center}, roomSize={room.roomSize}, size={size})");
        }
        else
        {
            center = transform.position;
            size = thornRingFallbackSize;
            Debug.LogWarning($"[BoneMaster] 보스가 속해 있는 RoomInstance를 찾지 못해, 보스 위치({center}) 기준 기본 크기({size})로 뼈 투기장을 배치합니다.");
        }

        _thornRingPhase1Size = size;

        GameObject ringObj = new GameObject("ThornArenaRingHazard");
        ringObj.transform.position = center;
        _thornRing = ringObj.AddComponent<ThornArenaHazard>();
        _thornRing.SetupAsRing(size, thornRingColor, thornRingSortingOrder, thornRingBandRatio, this);
        Debug.Log($"<color=cyan>[BoneMaster]</color> 뼈 투기장 생성 완료: {ringObj.name} at {ringObj.transform.position}, size={size}");
    }

    private void ShrinkThornArenaRing()
    {
        if (_thornRing == null || _thornRingPhase1Size == Vector2.zero) return;
        Vector2 newSize = _thornRingPhase1Size * thornRingPhase2ShrinkRatio;
        _thornRing.SetupAsRing(newSize, thornRingColor, thornRingSortingOrder, thornRingBandRatio, this);
    }

    private void HideDanglingDoorPlaceholders()
    {
        var allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (var t in allTransforms)
        {
            if (!t.name.StartsWith("Door_")) continue;
            if (!t.name.Contains("Room_")) continue;

            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null && sr.enabled)
            {
                sr.enabled = false;
                Debug.Log($"<color=cyan>[BoneMaster]</color> 막다른 문 마커 시각 표시 숨김: {t.name}");
            }
        }
    }

    private RoomInstance FindContainingRoom()
    {
        if (_cachedRoom != null) return _cachedRoom;

        foreach (var room in FindObjectsByType<RoomInstance>(FindObjectsSortMode.None))
        {
            Bounds bounds = new Bounds(
                (Vector2)room.transform.position + room.centerOffset,
                new Vector3(room.roomSize.x, room.roomSize.y, 100f));
            if (bounds.Contains(transform.position))
            {
                _cachedRoom = room;
                return room;
            }
        }
        return null;
    }
}
