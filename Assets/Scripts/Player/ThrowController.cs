using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어의 던지기 입력을 처리하고 실행합니다.
/// </summary>
public class ThrowController : MonoBehaviour
{
    [Header("Throw Settings")]
    [SerializeField] private float chargeTime = 1.0f; // 최대 충전에 걸리는 시간
    [SerializeField] private int maxHoldCount = 5; // 최대 집기 개수

    [Header("References")]
    [SerializeField] private Transform holdPoint; // 집어들었을 때 유닛이 위치할 곳
    [SerializeField] private ThrowCluster clusterPrefab; // [추가] 인스펙터에서 할당하거나 동적으로 생성
    private ThrowCluster _activeCluster;

    [SerializeField] private TrajectoryPredictor trajectoryPredictor; // 드래그 앤 드롭으로 할당
    public TrajectoryPredictor TrajectoryPredictor { get { return trajectoryPredictor; }}

    // --- 가이드용 프로퍼티 추가 ---
    public Transform HoldPoint => holdPoint;
    public float CurrentChargeRatio => Mathf.Min(_chargeTimer / chargeTime, 1.0f);
    public ThrowCluster ActiveCluster => _activeCluster;

    private List<IThrowable> _heldObjects = new List<IThrowable>();
    private float _chargeTimer;
    private bool _isCharging;

    [Header("Radial Menu (Ping System)")]
    [SerializeField] private SelectionWheelUI selectionWheel;
    [SerializeField] private float dragThreshold = 50f;
    [SerializeField] private List<CommandData> directionMapping = new List<CommandData>
    {
        CommandData.SkeletonWarrior,      // 12시
        CommandData.SkeletonShieldbearer, // 1시 30분
        CommandData.SkeletonArcher,       // 3시
        CommandData.SkeletonPriest,       // 4시 30분
        CommandData.SkeletonBomber,       // 6시
        CommandData.SkeletonSpearman,     // 7시 30분
        CommandData.SkeletonMagician,     // 9시
        CommandData.SkeletonThief         // 10시 30분
    };

    private Vector2 _rightClickStartPos;
    private bool _isWheelActive;

    // 현재 마우스 스크린 좌표
    private Vector2 CurrentMouseScreenPos => Pointer.current.position.ReadValue();

    // 현재 마우스 월드 좌표를 매 프레임 계산하여 제공
    public Vector2 CurrentMouseWorldPos
    {
        get
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(new Vector3(CurrentMouseScreenPos.x, CurrentMouseScreenPos.y, 0f));
            mousePos.z = 0f;
            return (Vector2)mousePos;
        }
    }

    private void Awake()
    {
        // 동일 오브젝트에서 ThrowController를 자동으로 찾아 할당
        if (trajectoryPredictor == null)
        {
            trajectoryPredictor = GetComponentInChildren<TrajectoryPredictor>();
        }
    }

    /// <summary>
    /// 특정 타입의 유닛을 현재 집을 수 있는지 확인합니다.
    /// </summary>
    public bool CanPickUpType(CommandData targetType)
    {
        // 1. 최대치 체크
        if (_heldObjects.Count >= maxHoldCount) return false;

        // 2. 마법사(Magician) 제한: 첫 번째 유닛으로 집을 수 없음
        if (targetType == CommandData.SkeletonMagician && _heldObjects.Count == 0)
        {
            return false;
        }

        // 3. 궁수(Archer) vs 전사(Warrior) 혼합 제한
        bool hasWarrior = false;
        bool hasArcher = false;

        foreach (var held in _heldObjects)
        {
            if (held is AllyController ally)
            {
                if (ally.MinionType == CommandData.SkeletonWarrior) hasWarrior = true;
                else if (ally.MinionType == CommandData.SkeletonArcher) hasArcher = true;
                
                // 중복 타입 체크 (이미 로직에 있으나 안전을 위해 포함)
                if (ally.MinionType == targetType) return false;
            }
        }

        if (targetType == CommandData.SkeletonWarrior && hasArcher) return false;
        if (targetType == CommandData.SkeletonArcher && hasWarrior) return false;

        return true;
    }

    /// <summary>
    /// 현재 손에 든 클러스터를 반환하거나, 없으면 새로 생성합니다.
    /// </summary>
    private ThrowCluster GetActiveClusterOrCreate()
    {
        if (_activeCluster == null)
        {
            if (clusterPrefab != null)
            {
                _activeCluster = Instantiate(clusterPrefab, holdPoint.position, Quaternion.identity, holdPoint);
            }
            else
            {
                GameObject clusterObj = new GameObject("ThrowCluster_Instance");
                _activeCluster = clusterObj.AddComponent<ThrowCluster>();
                _activeCluster.transform.SetParent(holdPoint);
            }
            _activeCluster.transform.localPosition = Vector3.zero;
        }
        return _activeCluster;
    }

    // --- 벽 감지 및 목표 지점 보정 공용 메서드 ---
    public Vector2 GetClampedTargetPos(Vector2 origin, Vector2 targetPos)
    {
        int wallLayer = LayerMask.GetMask("Wall", "Obstacle");
        Vector2 direction = targetPos - origin;
        float distance = direction.magnitude;

        if (distance < 0.01f) return targetPos;

        // [개선] 클러스터의 반지름을 고려하여 CircleCast 수행
        float radius = (_activeCluster != null) ? _activeCluster.GetCurrentRadius() : 0.35f;
        RaycastHit2D hit = Physics2D.CircleCast(origin, radius, direction.normalized, distance, wallLayer);
        
        if (hit.collider != null)
        {
            // 원의 중심이 있어야 할 정확한 위치를 반환
            return hit.centroid;
        }
        return targetPos;
    }

    private void Update()
    {
        if (_isCharging)
        {
            _chargeTimer = Mathf.Min(_chargeTimer + Time.deltaTime, chargeTime);
        }

        if (_isWheelActive && selectionWheel != null)
        {
            selectionWheel.UpdateHighlight(CurrentMouseScreenPos);
        }

        // [수정] 클러스터 위치를 벽으로부터 보호하며 동기화
        UpdateHoldPosition();
    }

    /// <summary>
    /// [최종 보정] 클러스터가 금지 구역(Wall, BackGround + 1.0 범위)에 절대 진입할 수 없도록 강제 보정합니다.
    /// 주변의 모든 벽을 감지하고 반복 보정을 통해 '가장 가까운 공터'를 완벽히 찾아냅니다.
    /// </summary>
    private void UpdateHoldPosition()
    {
        if (_activeCluster == null || _activeCluster.transform.parent != holdPoint) return;

        // 1. 기준 좌표 및 레이어 설정
        Vector2 playerPos = (Vector2)transform.position;
        Vector2 idealWorldPos = (Vector2)holdPoint.position;
        int forbiddenLayers = LayerMask.GetMask("Wall", "Obstacle", "BackGround");
        
        float safetyDistance = 0.75f; // [수정] 벽 표면에서 유지할 최소 안전 거리 (0.5 -> 0.75)
        float clusterRadius = _activeCluster.GetCurrentRadius();
        float totalThreshold = clusterRadius + safetyDistance;

        Vector2 currentSafePos = idealWorldPos;

        // 2. Recursive Multi-Wall Solver: 최대 10회 반복하여 모든 겹침을 순차적으로 해결
        for (int i = 0; i < 10; i++)
        {
            // 현재 위치에서 겹치는 '모든' 금지 구역 콜라이더를 가져옴
            Collider2D[] hits = Physics2D.OverlapCircleAll(currentSafePos, totalThreshold, forbiddenLayers);
            if (hits.Length == 0) break; // 더 이상 겹치는 곳이 없으면 탈출 성공

            Vector2 combinedEscape = Vector2.zero;

            foreach (var hit in hits)
            {
                Vector2 closest = hit.ClosestPoint(currentSafePos);
                bool isInside = hit.OverlapPoint(currentSafePos);
                
                Vector2 dir;
                float depth;

                if (isInside)
                {
                    // 벽 내부 침범 시: '벽 중심'이 아닌 '플레이어 방향'으로 탈출 (점프 방지 및 안전 보장)
                    dir = (playerPos - currentSafePos).normalized;
                    if (dir == Vector2.zero) dir = Vector2.up;
                    depth = totalThreshold; // 내부일 때는 임계치만큼 크게 밀어냄
                }
                else
                {
                    // 벽 외부 근접 시: 벽 표면에서 바깥쪽 최단 거리 방향으로 밀어냄
                    dir = (currentSafePos - closest).normalized;
                    // 방향을 못 잡으면 플레이어 쪽으로 당김
                    if (dir.sqrMagnitude < 0.001f) dir = (playerPos - currentSafePos).normalized;
                    if (dir == Vector2.zero) dir = Vector2.up;
                    
                    depth = totalThreshold - Vector2.Distance(currentSafePos, closest);
                }

                if (depth > 0) combinedEscape += dir * depth;
            }

            // 계산된 모든 탈출 벡터의 합을 적용하여 위치 보정
            if (combinedEscape.sqrMagnitude > 0.0001f)
            {
                currentSafePos += combinedEscape;
            }
            else break;
        }

        // 3. 적용: 확정된 안전 지대로 즉각적이고 부드럽게 이동
        _activeCluster.transform.position = Vector3.Lerp(_activeCluster.transform.position, (Vector3)currentSafePos, Time.deltaTime * 100f);
    }

    public void OnRightClickStarted()
    {
        _rightClickStartPos = CurrentMouseScreenPos;
        _isWheelActive = true;
        
        if (selectionWheel != null)
        {
            // 각 타입별 집기 가능 여부 리스트 생성
            List<bool> availability = new List<bool>();
            foreach (var type in directionMapping)
            {
                availability.Add(CanPickUpType(type));
            }

            selectionWheel.Show(_rightClickStartPos, directionMapping, availability);
        }
    }

    public void OnRightClickCanceled()
    {
        if (!_isWheelActive) return;
        _isWheelActive = false;

        float dragDist = Vector2.Distance(_rightClickStartPos, CurrentMouseScreenPos);
        
        if (selectionWheel != null)
        {
            int selectedIndex = selectionWheel.GetSelectedIndex();
            selectionWheel.Hide();

            // 1. 드래그가 충분하고 선택된 직업이 있다면 해당 타입 잡기
            if (dragDist >= dragThreshold && selectedIndex != -1)
            {
                CommandData targetType = directionMapping[selectedIndex];
                // [수정] 집기 가능 여부 최종 확인
                if (CanPickUpType(targetType))
                {
                    TryPickUpByType(targetType);
                }
                else
                {
                    Debug.LogWarning($"Cannot pick up {targetType} due to restrictions.");
                }
                return;
            }
        }

        // 2. 짧은 클릭이거나 선택된 방향이 없다면 기존 마우스 줍기 실행
        TryPickUpWithMouse();
    }

    private void TryPickUpByType(CommandData targetType)
    {
        // [수정] CanPickUpType은 이미 OnRightClickCanceled에서 체크했지만, 
        // 메서드 자체의 안전성을 위해 내부에서도 한 번 더 체크 (최대치 등)
        if (!CanPickUpType(targetType)) return;

        float pickUpRadius = GameManager.Instance.PLAYERCONTROLLER.THROWRANGE;
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, pickUpRadius);

        AllyController bestTarget = null;
        float minTargetDist = float.MaxValue;

        foreach (var col in colls)
        {
            if (col.TryGetComponent<AllyController>(out var ally))
            {
                // 이미 들고 있는 유닛 제외 및 타입 일치 확인
                if (ally.MinionType == targetType && !_heldObjects.Contains(ally))
                {
                    float dist = Vector2.Distance(transform.position, ally.transform.position);
                    if (dist < minTargetDist)
                    {
                        minTargetDist = dist;
                        bestTarget = ally;
                    }
                }
            }
        }

        if (bestTarget != null)
        {
            PerformPickUp(bestTarget, bestTarget.gameObject);
            if (trajectoryPredictor != null) trajectoryPredictor.ShowGuide();
        }
    }

    public void OnThrow(InputAction.CallbackContext context)
    {
        // 던지기는 오직 들고 있는 유닛이 있을 때만 시작됨
        if (_heldObjects.Count == 0) return;

        if (context.started)
        {
            OnThrowStarted();
        }
        else if (context.canceled)
        {
            OnThrowCanceled();
        }
    }

    private void OnThrowStarted()
    {
        // 들고 있다면 차징 시작
        _isCharging = true;
        _chargeTimer = 0f; // 차징 시작 시 타이머 초기화
    }

    private void OnThrowCanceled()
    {
        // --- 가이드 숨기기 ----
        if (trajectoryPredictor != null)
        {
            trajectoryPredictor.HideGuide();
        }

        if (_isCharging && _heldObjects.Count > 0)
        {
            ThrowAll();
        }
        _isCharging = false;
    }

    // 외부(PlayerController)에서 호출할 줍기 함수
    public void TryPickUpWithMouse()
    {
        IThrowable targetThrowable = null;
        GameObject targetObj = null;

        // 2. MouseManager의 HoverObject 확인
        GameObject hovered = GameManager.Instance.mouseManager.HoverObject;

        if (hovered != null)
        {
            // 마우스 아래 객체가 IThrowable인지 확인
            if (hovered.TryGetComponent(out IThrowable throwable))
            {
                // [수정] CanPickUpType을 사용하여 모든 제한 사항 일괄 체크
                if (throwable is AllyController ally)
                {
                    if (!CanPickUpType(ally.MinionType))
                    {
                        Debug.LogWarning($"Cannot pick up {ally.MinionType} with mouse due to restrictions.");
                        return;
                    }
                }

                // 거리 체크
                float dist = Vector2.Distance(transform.position, hovered.transform.position);
                float pickUpRadius = GameManager.Instance.PLAYERCONTROLLER.THROWRANGE;

                if (dist <= pickUpRadius)
                {
                    targetThrowable = throwable;
                    targetObj = hovered;
                }
            }
        }

        // 4. 최종 픽업 처리
        if (targetThrowable != null && !_heldObjects.Contains(targetThrowable))
        {
            PerformPickUp(targetThrowable, targetObj);
            // 가이드 활성화
            trajectoryPredictor.ShowGuide();
        }
    }

    // 헬퍼 메서드: 실제 집는 동작 (코드 중복 방지)
    private void PerformPickUp(IThrowable throwable, GameObject obj)
    {
        _heldObjects.Add(throwable);
        throwable.OnPickedUp();

        ThrowCluster cluster = GetActiveClusterOrCreate();

        if (throwable is MonoBehaviour mb)
        {
            mb.transform.SetParent(cluster.transform);
        }

        Debug.Log($"Picked up: {obj.name}. Total: {_heldObjects.Count}");

        // 유닛 목록 갱신 및 클러스터 설정 업데이트
        List<AllyController> allyList = new List<AllyController>();
        foreach(var item in _heldObjects) if(item is AllyController ally) allyList.Add(ally);
        cluster.Setup(allyList);
    }

    public TargetingMode GetCurrentTargetingMode()
    {
        if (_heldObjects.Count == 0) return TargetingMode.Self;

        // 우선순위 1: 궁수 (Area)
        foreach (var obj in _heldObjects)
        {
            if (obj is AllyController ally && ally.MinionType == CommandData.SkeletonArcher)
                return TargetingMode.Area;
        }

        // 우선순위 2: 전사 (Target)
        foreach (var obj in _heldObjects)
        {
            if (obj is AllyController ally && ally.MinionType == CommandData.SkeletonWarrior)
                return TargetingMode.Target;
        }

        return TargetingMode.Self;
    }

    public Team GetExpectedTargetTeam()
    {
        if (_heldObjects.Count == 0) return Team.Enemy;

        bool hasWarrior = false;
        bool hasShield = false;
        bool hasMagician = false;
        bool hasOthers = false;

        foreach (var obj in _heldObjects)
        {
            if (obj is AllyController ally)
            {
                CommandData type = ally.MinionType;
                
                if (type == CommandData.SkeletonWarrior) hasWarrior = true;
                else if (type == CommandData.SkeletonShieldbearer) hasShield = true;
                else if (type == CommandData.SkeletonMagician) hasMagician = true;
                else hasOthers = true; // 궁수, 사제, 창병 등 다른 유닛
            }
        }

        // [확장된 조건] 
        // 1. [전사 + 방패병]만 있을 때
        // 2. [전사 + 방패병 + 법사]만 있을 때
        // 이 두 경우에만 아군 타겟
        bool isSupportCombo = hasWarrior && hasShield && !hasOthers;
        
        if (isSupportCombo)
        {
            return Team.Ally;
        }

        // 그 외 모든 상황은 적군 타겟
        return Team.Enemy;
    }

    private ThrowRecipe CreateRecipe(Vector2 targetPos, float chargeRatio)
    {
        ThrowRecipe recipe = new ThrowRecipe();
        recipe.impactPoint = targetPos;
        recipe.chargeRatio = chargeRatio;

        // [수정] DataManager의 Min/Max 범위를 사용하여 차징 비율에 따른 선형 배율 계산
        float minMult = GameManager.Instance.dataManager.MIN_THROW_CHARGE_MULTIPLIER;
        float maxMult = GameManager.Instance.dataManager.MAX_THROW_CHARGE_MULTIPLIER;
        recipe.chargeMultiplier = Mathf.Lerp(minMult, maxMult, chargeRatio);

        if (_heldObjects.Count == 0) return recipe;

        // 0. 타겟팅 모드 결정
        recipe.targetingMode = GetCurrentTargetingMode();
        recipe.targetTeam = GetExpectedTargetTeam();

        // 1. 주력 유닛(전사/궁수)의 배율을 모드 배율로 설정
        AllyController leadUnit = null;
        if (recipe.targetingMode == TargetingMode.Area)
        {
            foreach(var obj in _heldObjects) if(obj is AllyController a && a.MinionType == CommandData.SkeletonArcher) { leadUnit = a; break; }
        }
        else if (recipe.targetingMode == TargetingMode.Target)
        {
            foreach(var obj in _heldObjects) if(obj is AllyController a && a.MinionType == CommandData.SkeletonWarrior) { leadUnit = a; break; }
        }

        // 주력 유닛이 있으면 그 배율을 사용, 없으면(Self) 1.0 사용
        recipe.modeMultiplier = (leadUnit != null) ? leadUnit.MinionData.effectMultiplier : 1.0f;

        // 2. 성질 분석 및 순수 기본값 합산
        for (int i = 0; i < _heldObjects.Count; i++)
        {
            if (_heldObjects[i] is AllyController ally)
            {
                CommandData type = ally.MinionType;
                float baseVal = ally.MinionData.baseEffectValue;

                switch (type)
                {
                    case CommandData.SkeletonWarrior:
                        // 데미지는 전사가 있을 때, 그리고 광역 모드가 아닐 때만 합산
                        if (recipe.targetingMode != TargetingMode.Area)
                            recipe.impactDamage += baseVal;
                        break;
                    case CommandData.SkeletonArcher: 
                        recipe.baseAreaRadius = ally.MinionData.baseAreaRadius;
                        // 데미지는 궁수가 있을 때, 그리고 광역 모드일 때만 합산
                        if (recipe.targetingMode == TargetingMode.Area)
                            recipe.impactDamage += baseVal;
                        break;
                    case CommandData.SkeletonPriest: 
                        recipe.hasCC = true; 
                        recipe.ccBaseValue += baseVal;
                        break;
                    case CommandData.SkeletonShieldbearer: 
                        recipe.hasShield = true; 
                        recipe.shieldBaseValue += baseVal;
                        break;
                    case CommandData.SkeletonSpearman: 
                        recipe.hasFormation = true; 
                        recipe.formationBaseValue += baseVal;
                        break;
                    case CommandData.SkeletonMagician: 
                        recipe.magicianCount += Mathf.FloorToInt(baseVal); 
                        break;
                }
            }
        }

        // 3. 지능적 타겟팅 (Target 모드일 때만, 풀차지가 아닐 때만 수행)
        if (recipe.targetingMode == TargetingMode.Target && chargeRatio < 0.98f)
        {
            recipe.finalTarget = FindSmartTarget(targetPos, recipe.targetTeam);
            if (recipe.finalTarget != null)
            {
                recipe.impactPoint = recipe.finalTarget.transform.position;
            }
        }

        return recipe;
    }

    public GameObject FindSmartTarget(Vector2 searchPos, Team targetTeam)
    {
        float searchRadius = 5.0f; // 마우스 주변 우선 탐색 범위
        LayerMask mask = (targetTeam == Team.Enemy) ? LayerMask.GetMask("Enemy") : LayerMask.GetMask("Army", "Player");
        
        // 오직 마우스 주변에서만 탐색 수행
        Collider2D[] colls = Physics2D.OverlapCircleAll(searchPos, searchRadius, mask);
        GameObject bestTarget = null;
        float minTargetDist = float.MaxValue;

        foreach (var col in colls)
        {
            float dist = Vector2.Distance(searchPos, col.transform.position);
            if (dist < minTargetDist)
            {
                minTargetDist = dist;
                bestTarget = col.gameObject;
            }
        }

        // 전역 탐색(2단계) 제거: 마우스 근처에 없으면 null 반환 -> 스냅 없음 -> 추격 없음
        return bestTarget;
    }

    private void ThrowAll()
    {
        _heldObjects.RemoveAll(item => item == null || (item is MonoBehaviour mb && mb == null));

        if (_heldObjects.Count == 0)
        {
            _isCharging = false;
            return;
        }

        float chargeRatio = _chargeTimer / chargeTime;
        // [수정] 보정된 클러스터 위치를 투척 시작점으로 정확히 사용
        Vector2 startPos = (_activeCluster != null) ? (Vector2)_activeCluster.transform.position : (Vector2)holdPoint.position;
        Vector2 mouseWorldPos = CurrentMouseWorldPos;

        // 새로운 레시피 기반 시스템 적용
        ThrowRecipe recipe = CreateRecipe(mouseWorldPos, chargeRatio);

        if (_activeCluster != null)
        {
            List<AllyController> allyList = new List<AllyController>();
            foreach (var item in _heldObjects) if (item is AllyController ally) allyList.Add(ally);

            if (allyList.Count > 0)
            {
                _activeCluster.Setup(allyList);
                _activeCluster.SetRecipe(recipe); // 클러스터에 레시피 전달
                
                // [리팩토링] 'Self' 모드인 경우 던지는 즉시 플레이어에게 효과 적용
                if (recipe.targetingMode == TargetingMode.Self)
                {
                    Vector2 selfTravelDir = (mouseWorldPos - startPos).normalized;
                    GameManager.Instance.dataManager.ProcessThrowImpact(recipe, startPos, selfTravelDir);
                    recipe.isImmediateApplied = true;
                }

                AllyController first = allyList[0];
                float speed = (chargeRatio >= 0.98f) ? first.FullChargeSpeed : Mathf.Lerp(first.MinSpeed, first.MaxSpeed, chargeRatio);
                
                // 타겟 모드일 때 최종 목표 지점 사용
                Vector2 finalTargetPos = GetClampedTargetPos(startPos, recipe.impactPoint);
                float distance = Vector2.Distance(startPos, finalTargetPos);
                float duration = distance / speed;
                float maxHeight = Mathf.Min(Mathf.Lerp(first.JumpHeight, first.StraightHeight, chargeRatio), distance * 0.5f);

                bool isDirect = chargeRatio >= 0.98f;
                _activeCluster.Launch(startPos, finalTargetPos, duration, maxHeight, isDirect, chargeRatio);
                
                _activeCluster = null;
            }
        }

        _heldObjects.Clear();
        _chargeTimer = 0f; 
    }

    // PlayerDashRoutine 코루틴 제거됨 (CharacterStat.ApplyKnockback으로 통합)

    // 플레이어가 맞았을 때 모든 미니언을 강제로 떨어뜨림
    public void DropAll()
    {
        if (_heldObjects.Count == 0) return;

        Debug.Log("<color=orange>[ThrowController]</color> Player hit! Dropping all units.");

        foreach (var throwable in _heldObjects)
        {
            if (throwable == null) continue;

            if (throwable is MonoBehaviour mb)
            {
                mb.transform.SetParent(null);
            }

            // OnLanded를 호출하여 상태를 복구시킴
            throwable.OnLanded();
        }

        _heldObjects.Clear();
        _isCharging = false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 2.0f);
    }
}
