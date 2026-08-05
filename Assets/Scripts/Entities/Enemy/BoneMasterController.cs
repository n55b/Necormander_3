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
    [Tooltip("페이즈2 시작 시 방 중앙으로 이동 후 무적 상태로 대기하는 시간(초). 이 동안 어떤 패턴도 시전하지 않는다.")]
    [SerializeField] private float phase2InvincibleDuration = 2f;

    [Header("UI 참조")]
    [SerializeField] private EliteBossPatternLabel patternLabel;
    [SerializeField] private Vector3 patternLabelOffset = new Vector3(0f, 1.6f, 0f);

    [Header("그로기(경직) 시각 피드백")]
    [SerializeField] private Color groggyFlashColor = new Color(0.4f, 0.85f, 1f, 1f);

    [Header("뼈 투기장 (방 경계 타원형, 링 판정)")]
    [Tooltip("방 크기 대비 타원 비율(1에 가까울수록 방을 거의 꽉 채움)")]
    [Range(0.5f, 1f)]
    [SerializeField] private float thornRingPhase1MarginRatio = 0.95f;
    [Tooltip("페이즈2 투기장 크기 비율(페이즈1 크기 대비). 1보다 크면 페이즈1보다 더 넓어진다.")]
    [Range(0.3f, 1f)]
    [SerializeField] private float thornRingPhase2ShrinkRatio = 0.85f; // [수정] 0.99는 페이즈1과 거의 차이가 없어(1%) "안 줄어드는 것처럼" 보이는 문제가 있었다. 경계 자체는 눈에 띄게 줄어들되(15%), 안쪽 이동 공간은 아래 thornRingPhase2BandRatio로 별도 보정한다.
    [SerializeField] private Vector2 thornRingFallbackSize = new Vector2(16f, 16f);
    [SerializeField] private Color thornRingColor = new Color(1f, 0.05f, 0.05f, 1f);
    [SerializeField] private int thornRingSortingOrder = 5000;
    [SerializeField] private float thornRingBandRatio = 0.12f;
    [Tooltip("페이즈2 전용 가시 띠 두께(바깥 반지름 대비). 페이즈1보다 얇게 잡아서, 경계는 줄어들어도 실제 이동 가능한 안쪽 공간은 덜 좁아지게 한다.")]
    [Range(0.02f, 0.3f)]
    [SerializeField] private float thornRingPhase2BandRatio = 0.06f;
    [Header("뼈 투기장 바깥 차단 영역")]
    [SerializeField] private float voidBarrierMargin = 60f;
    [SerializeField] private Color voidBarrierColor = Color.black;
    [SerializeField] private int voidBarrierSortingOrder = 4990;


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
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (Health != null) Health.UpdateHPBar -= CheckPartBreak;
        DamageEventBus.OnBeforeDamageCalculated -= HandleIncomingDamageAmp;
        if (_thornRing != null) Destroy(_thornRing.gameObject);
        // 예고 중에 보스가 죽으면 패턴 코루틴이 통째로 끊겨서, 그 코루틴이 만든 텔레그래프를
        // 지우는 Destroy 가 실행되지 않는다. 텔레그래프는 보스의 자식이 아니라 월드 루트
        // 오브젝트라 보스와 같이 사라지지도 않으므로 여기서 치운다.
        CleanupDanglingTelegraphs();
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

                // CancelAttack() 을 먼저 부른다. StopAllCoroutines() 만 쓰면 공격 루틴이 끝에서
                // 되돌리는 IsAttacking 이 true 로 굳고, BaseEntity.CanExecuteAI 가 그걸 보고 막아서
                // 페이즈2 브레인의 Execute() 가 영영 호출되지 않는다(= 보스가 통째로 얼어붙는다).
                // IsAttacking 을 false 로 되돌리는 곳은 공격 루틴 끝과 CancelAttack() 둘뿐인데,
                // 흉갑 파괴는 플레이어 타격 순간에 터지므로 공격 도중일 확률이 높다.
                CancelAttack();
                StopAllCoroutines();
                // [버그 수정 — 전환 순간 잔여물] StopAllCoroutines()로 죽는 코루틴은 자기가 만든
                // 오브젝트(텔레그래프)를 못 지우고, 열어둔 카운터 게이지도 못 닫는다. 여기서 대신 정리한다.
                CleanupDanglingTelegraphs();
                CounterGauge?.CloseWindow();
                StartCoroutine(Phase2TransitionRoutine());
                break;
        }
    }

private IEnumerator Phase2TransitionRoutine()
    {
        // [버그 수정 — 전환 연출 중 이전 페이즈 AI가 새 패턴을 뽑아버리는 문제] CurrentState를 Skill로
        // 강제 고정해서 AIPatternSO.Execute()가 매 프레임 즉시 return하게 만든다(패턴 코루틴이 시작될
        // 때와 동일한 방식). 안 그러면 이 코루틴이 WaitForSeconds로 대기하는 동안에도 아직 안 바뀐
        // 페이즈1 브레인이 계속 매 프레임 돌면서 돌진 등 새 패턴을 뽑아, 중앙 연출 도중 보스가 딴 데로
        // 움직여버리는 문제가 실제로 있었다. 페이즈2 브레인으로 교체되고 체력 차오르는 연출이 끝날
        // 때까지 계속 Skill 상태를 유지해서, 그 사이엔 어떤 브레인도 패턴을 시전할 수 없게 한다.
        CurrentState = AIState.Skill;

        IsGroggy = false;
        ClearVisualFlash();
        HardStopMovement();
        if (Health != null) Health.Invincible = true;

        // [추가] 페이즈2 시작 연출: 방(맵) 정중앙으로 즉시 이동시킨다. 흉갑이 깨진 위치가 투기장 벽
        // 근처 등 애매한 곳일 수 있어서, 페이즈2는 항상 중앙에서 시작하도록 고정한다.
        RoomInstance room = FindContainingRoom();
        if (room != null)
        {
            Vector3 center = (Vector3)((Vector2)room.transform.position + room.centerOffset);
            WarpTo(center);
        }

        yield return new WaitForSeconds(phase2InvincibleDuration);

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
                newAi.Init(this); // Init()이 CurrentState를 Idle로 되돌리므로, 바로 아래에서 다시 Skill로 잠근다.
                SetRuntimeBrain(newAi);
                if (oldBrain != null) Destroy(oldBrain);
            }

            CurrentState = AIState.Skill; // 체력이 다 차오를 때까지는 새 브레인도 아직 움직이지 않게 계속 잠가둔다.

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
        CurrentState = AIState.Idle; // 연출 종료 — 이제부터 정상적으로 AI 판단 재개
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
        // [버그 수정 — 뼈 투기장을 뚫는 문제] Warp()는 물리 충돌을 거치지 않는 순간이동이라, 개별
        // 패턴이 벽 체크를 깜빡하면 그대로 경계를 뚫고 나갈 수 있었다(예: 견갑 찌르기의 재조준+대시엔
        // 벽 체크가 아예 없었음). 모든 이동이 최종적으로 이 함수를 거치므로, 여기서 한 번에
        // 뼈 투기장 바깥 경계 안쪽으로 clamp해서 원천 차단한다.
        if (_thornRing != null)
        {
            pos = _thornRing.ClampInsideArena(pos);
        }

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
        _thornRing.SetupVoidBarrier(voidBarrierMargin, voidBarrierColor, voidBarrierSortingOrder);
        Debug.Log($"<color=cyan>[BoneMaster]</color> 뼈 투기장 생성 완료: {ringObj.name} at {ringObj.transform.position}, size={size}");
    }

private void ShrinkThornArenaRing()
    {
        if (_thornRing == null || _thornRingPhase1Size == Vector2.zero) return;
        Vector2 newSize = _thornRingPhase1Size * thornRingPhase2ShrinkRatio;
        // [수정] 페이즈2는 가시 띠를 더 얇게(thornRingPhase2BandRatio) 그려서, 경계 자체는 눈에 띄게
        // 줄어들어도 실제 이동 가능한 안쪽 공간은 덜 좁아지게 보정한다.
        _thornRing.SetupAsRing(newSize, thornRingColor, thornRingSortingOrder, thornRingPhase2BandRatio, this);
        _thornRing.SetupVoidBarrier(voidBarrierMargin, voidBarrierColor, voidBarrierSortingOrder);
    }

    /// <summary>
    /// [버그 수정 — 페이즈 전환 시 텔레그래프 잔여물] StopAllCoroutines()로 패턴 코루틴을 강제 종료하면
    /// 그 코루틴이 만든 텔레그래프(BoneMasterTelegraphUtil.SpawnXXX)를 지우는 Object.Destroy() 코드가
    /// 실행되지 못하고 그대로 씬에 남는다(예: 견갑 찌르기 3타 도중 흉갑이 깨지면 노란 텔레그래프가 영원히
    /// 남음). BoneMasterTelegraphUtil이 만드는 모든 텔레그래프는 "BoneMaster_Telegraph_" 접두사를 쓰므로,
    /// 페이즈 전환 시작 시점에 이름으로 찾아 전부 정리한다.
    /// </summary>
    private void CleanupDanglingTelegraphs()
    {
        var allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            if (!t.name.StartsWith("BoneMaster_Telegraph_")) continue;
            Destroy(t.gameObject);
            count++;
        }
        if (count > 0)
        {
            Debug.Log($"<color=cyan>[BoneMaster]</color> 중단된 패턴이 남긴 텔레그래프 {count}개 정리함.");
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
