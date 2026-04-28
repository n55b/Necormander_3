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

        // [수정] 클러스터가 손에 있을 때만 위치 동기화
        if (_activeCluster != null && _activeCluster.transform.parent == holdPoint)
        {
            _activeCluster.transform.localPosition = Vector3.zero;
        }
    }

    public void OnRightClickStarted()
    {
        _rightClickStartPos = CurrentMouseScreenPos;
        _isWheelActive = true;
        
        if (selectionWheel != null)
        {
            selectionWheel.Show(_rightClickStartPos, directionMapping);
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
                TryPickUpByType(targetType);
                return;
            }
        }

        // 2. 짧은 클릭이거나 선택된 방향이 없다면 기존 마우스 줍기 실행
        TryPickUpWithMouse();
    }

    private void TryPickUpByType(CommandData targetType)
    {
        if (_heldObjects.Count >= maxHoldCount) return;

        // [추가] 중복 타입 체크: 이미 같은 타입을 들고 있다면 줍기 불가
        foreach (var held in _heldObjects)
        {
            if (held is AllyController heldAlly && heldAlly.MinionType == targetType)
            {
                Debug.LogWarning($"Already holding {targetType}!");
                return;
            }
        }

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
        // 1. 최대치 체크
        if (_heldObjects.Count >= maxHoldCount)
        {
            Debug.LogWarning("Already holding max units!");
            return;
        }

        IThrowable targetThrowable = null;
        GameObject targetObj = null;

        // 2. MouseManager의 HoverObject 확인
        GameObject hovered = GameManager.Instance.mouseManager.HoverObject;

        if (hovered != null)
        {
            // 마우스 아래 객체가 IThrowable인지 확인
            if (hovered.TryGetComponent(out IThrowable throwable))
            {
                // [추가] 중복 타입 체크
                if (throwable is AllyController ally)
                {
                    foreach (var held in _heldObjects)
                    {
                        if (held is AllyController heldAlly && heldAlly.MinionType == ally.MinionType)
                        {
                            Debug.LogWarning($"Already holding {ally.MinionType}!");
                            return;
                        }
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

    private ThrowRecipe CreateRecipe(Vector2 targetPos, float chargeRatio)
    {
        ThrowRecipe recipe = new ThrowRecipe();
        recipe.impactPoint = targetPos;
        recipe.chargeRatio = chargeRatio;

        if (_heldObjects.Count == 0) return recipe;

        // 0. 타겟팅 모드 및 마스터 배율 사전 결정 (줍는 순서 무관)
        AllyController leadUnit = null;
        
        // 우선순위 1: 궁수 (Area)
        foreach (var obj in _heldObjects)
        {
            if (obj is AllyController ally && ally.MinionType == CommandData.SkeletonArcher)
            {
                leadUnit = ally;
                recipe.targetingMode = TargetingMode.Area;
                break;
            }
        }

        // 우선순위 2: 전사 (Target) - 궁수가 없을 때만
        if (leadUnit == null)
        {
            foreach (var obj in _heldObjects)
            {
                if (obj is AllyController ally && ally.MinionType == CommandData.SkeletonWarrior)
                {
                    leadUnit = ally;
                    recipe.targetingMode = TargetingMode.Target;
                    break;
                }
            }
        }

        // 우선순위 3: 전사/궁수가 모두 없으면 첫 번째 유닛 기준 Self 모드
        if (leadUnit == null)
        {
            leadUnit = _heldObjects[0] as AllyController;
            recipe.targetingMode = TargetingMode.Self;
        }

        // 마스터 배율 설정
        if (leadUnit != null) recipe.masterMultiplier = leadUnit.MinionData.effectMultiplier;

        // 1. 성질 분석 (Job to Effect Mapping)
        for (int i = 0; i < _heldObjects.Count; i++)
        {
            if (_heldObjects[i] is AllyController ally)
            {
                CommandData type = ally.MinionType;

                // 효과 합산 및 기본 수치 수집
                switch (type)
                {
                    case CommandData.SkeletonWarrior:
                        if (recipe.targetingMode != TargetingMode.Area)
                        {
                            recipe.impactDamage += ally.MinionData.baseEffectValue * ally.MinionData.effectMultiplier;
                        }
                        break;
                    case CommandData.SkeletonArcher: 
                        recipe.baseAreaRadius = ally.MinionData.baseAreaRadius;
                        if (recipe.targetingMode == TargetingMode.Area)
                        {
                            recipe.impactDamage += ally.MinionData.baseEffectValue * ally.MinionData.effectMultiplier;
                        }
                        break;
                    case CommandData.SkeletonPriest: 
                        recipe.hasCC = true; 
                        recipe.ccBaseValue += ally.MinionData.baseEffectValue;
                        break;
                    case CommandData.SkeletonShieldbearer: 
                        recipe.hasShield = true; 
                        recipe.shieldBaseValue += ally.MinionData.baseEffectValue;
                        break;
                    case CommandData.SkeletonSpearman: 
                        recipe.hasFormation = true; 
                        recipe.formationBaseValue += ally.MinionData.baseEffectValue;
                        break;
                    case CommandData.SkeletonMagician: 
                        recipe.magicianCount += Mathf.FloorToInt(ally.MinionData.baseEffectValue); 
                        break;
                }
            }
        }

        // 2. 타겟 팀 결정: 방패병이 섞여 있으면 무조건 아군, 아니면 적군
        recipe.targetTeam = recipe.hasShield ? Team.Ally : Team.Enemy;

        // 3. 지능적 타겟팅 (Target 모드일 때)
        if (recipe.targetingMode == TargetingMode.Target)
        {
            recipe.finalTarget = FindSmartTarget(targetPos, recipe.targetTeam);
            
            // [강제 타겟팅] 타겟 모드인데 대상이 없다면 바닥에 던지지 않고 타겟 위치로 강제 고정
            if (recipe.finalTarget != null)
            {
                recipe.impactPoint = recipe.finalTarget.transform.position;
            }
        }

        return recipe;
    }

    private GameObject FindSmartTarget(Vector2 searchPos, Team targetTeam)
    {
        float searchRadius = 5.0f; // 마우스 주변 우선 탐색 범위
        LayerMask mask = (targetTeam == Team.Enemy) ? LayerMask.GetMask("Enemy") : LayerMask.GetMask("Army", "Player");
        
        // 1. 먼저 마우스 주변에서 탐색
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

        // 2. 마우스 주변에 없다면, 맵 전체에서 플레이어와 가장 가까운 타겟 탐색 (강제 추적)
        if (bestTarget == null)
        {
            GameObject[] allPotentialTargets = (targetTeam == Team.Enemy) 
                ? GameObject.FindGameObjectsWithTag("Enemy") 
                : GameObject.FindGameObjectsWithTag("Army"); // Player 태그는 별도로 처리 필요할 수 있음

            float minGlobalDist = float.MaxValue;
            foreach (var target in allPotentialTargets)
            {
                float dist = Vector2.Distance(transform.position, target.transform.position);
                if (dist < minGlobalDist)
                {
                    minGlobalDist = dist;
                    bestTarget = target;
                }
            }
        }

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
        Vector2 startPos = (Vector2)holdPoint.position;
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

    // 동일 종류 여러 명을 던졌을 때의 효과 (기본 효과 강화 등)
    private void HandleSameTypeEffect(CommandData type, int count, Vector2 targetPos, float chargeRatio)
    {
        Debug.Log($"<color=cyan>[Synergy]</color> Same Type Throw: <b>{type} x{count}</b> (Charge: {chargeRatio:F2})");

        // 타입별 고유 시너지 효과 로직 (여기에 구체적인 버프나 범위 공격 로직 추가)
        switch (type)
        {
            case CommandData.SkeletonWarrior:
                Debug.Log("Warrior Synergy: 충격파 범위 및 공격력 증가");
                break;
            case CommandData.SkeletonShieldbearer:
                Debug.Log("Shieldbearer Synergy: 착지 지점에 방어 구역 생성");
                break;
            case CommandData.SkeletonArcher:
                Debug.Log("Archer Synergy: 추가 화살 세례 발사");
                break;
            case CommandData.SkeletonPriest:
                Debug.Log("Priest Synergy: 주변 아군 광역 치유");
                break;
            case CommandData.SkeletonBomber:
                Debug.Log("Bomber Synergy: 연쇄 폭발 범위 및 위력 강화");
                break;
            case CommandData.SkeletonSpearman:
                Debug.Log("Spearman Synergy: 직선상 모든 적 관통 및 넉백");
                break;
            case CommandData.SkeletonMagician:
                Debug.Log("Magician Synergy: 마법 폭발 및 속성 상태이상 부여");
                break;
            case CommandData.SkeletonThief:
                Debug.Log("Thief Synergy: 착지 후 즉시 은신 및 치명타 공격");
                break;
        }
    }

    // 서로 다른 종류가 섞였을 때의 효과 (새로운 조합 효과 등)
    private void HandleMixedTypeEffect(List<AllyController> allies, Vector2 targetPos, float chargeRatio)
    {
        Debug.Log($"<color=orange>[Synergy]</color> Mixed Type Throw: <b>{allies.Count} units</b> (Charge: {chargeRatio:F2})");

        // 조합 분석 로직 (예: Warrior + Priest = 팔라딘 효과 등)
        // 현재는 간단하게 구성 유닛 종류만 출력
        Dictionary<CommandData, int> counts = new Dictionary<CommandData, int>();
        foreach (var ally in allies)
        {
            if (!counts.ContainsKey(ally.MinionType)) counts[ally.MinionType] = 0;
            counts[ally.MinionType]++;
        }

        string composition = "";
        foreach (var pair in counts) composition += $"{pair.Key} x{pair.Value}, ";
        Debug.Log($"Composition: {composition.TrimEnd(',', ' ')}");
    }

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
