using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 투척 시스템의 중앙 컨트롤러 및 데이터 저장소입니다.
/// </summary>
public class ThrowController : MonoBehaviour
{
    [Header("Throw Settings")]
    [SerializeField] private int maxHoldCount = 2;
    public int BaseMaxHoldCount => maxHoldCount;

    // [추가] 외부 접근용
    public int HeldObjectsCount => _heldObjects.Count;

    // 시너지 등에 의해 확장된 최대 집기 수 반환
    public int MaxHoldCount 
    {
        get
        {
            int bonus = 0;
            if (InventoryManager.Instance != null)
            {
                int count = InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.BigHand);
                if (count >= 5)
                    bonus += 3;
                else if (count >= 3)
                    bonus += 1;
            }
            return maxHoldCount + bonus;
        }
    }

    [Header("References")]
    [SerializeField] private Transform holdPoint;
    [SerializeField] public ThrowCluster clusterPrefab; 
    [SerializeField] private TrajectoryPredictor trajectoryPredictor;
    
    [Header("Input & UI Settings")]
    [SerializeField] private SelectionWheelUI selectionWheel;
    [SerializeField] private float dragThreshold = 50f;
    [SerializeField] private List<CommandData> directionMapping;

    // 데이터 게터 (서브 컴포넌트용)
    public Transform HoldPoint => holdPoint;
    public SelectionWheelUI SelectionWheel => (selectionWheel != null) ? selectionWheel : SelectionWheelUI.Instance; // [수정] 싱글톤 우선 활용
    
    // [수정] 모디파이어가 반영된 최종 ChargeTime 계산 (직구 도달 기준 시간)
    public float ChargeTime 
    {
        get
        {
            var pc = GameManager.Instance.PLAYERCONTROLLER;
            float time = pc.ThrowChargeTime + pc.bonusThrowChargeTime;
            return Mathf.Max(0.1f, time); // 최소 0.1초 보장
        }
    }

    // [추가] 오버차지 허용 시간을 포함한 최대 차지 가능 시간
    public float MaxChargeTime
    {
        get
        {
            var pc = GameManager.Instance.PLAYERCONTROLLER;
            return ChargeTime + pc.overchargeTimeLimit;
        }
    }

    public float DragThreshold => dragThreshold;
    public List<CommandData> DirectionMapping => directionMapping;

    [Header("Modular Components")]
    [SerializeField] private ThrowInputHandler _input;
    [SerializeField] private ThrowPhysics _physics;
    [SerializeField] private ThrowStrategy _strategy;

    public ThrowInputHandler InputHandler => _input;
    public ThrowPhysics Physics => _physics;
    public ThrowStrategy Strategy => _strategy;
    public TrajectoryPredictor TrajectoryPredictor => trajectoryPredictor;

    // 상태 관리
    private List<IThrowable> _heldObjects = new List<IThrowable>();
    private ThrowCluster _activeCluster;
    public List<IThrowable> HeldObjects => _heldObjects;
    public ThrowCluster ActiveCluster => _activeCluster;
    public float CurrentChargeRatio => _input != null ? _input.ChargeRatio : 0f;
    public bool IsCharging => _input != null && _input.IsCharging; // [추가]

    public event System.Action<ThrowRecipe> OnRecipeCreated; // [추가] 보석/시너지 핸들러들이 레시피를 수정할 수 있는 후킹 포인트

    // 호환성 래핑
    public TargetingMode GetCurrentTargetingMode() => _strategy.GetCurrentTargetingMode(_heldObjects);
    public Team GetExpectedTargetTeam() => _strategy.GetExpectedTargetTeam(_heldObjects);
    public Vector2 GetClampedTargetPos(Vector2 origin, Vector2 targetPos) => _physics.GetClampedTargetPos(origin, targetPos, _activeCluster);
    public GameObject FindSmartTarget(Vector2 searchPos, Team targetTeam) => _strategy.FindSmartTarget(searchPos, targetTeam);

    public Vector2 CurrentMouseWorldPos
    {
        get
        {
            Vector2 screenPos = Pointer.current.position.ReadValue();
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            mousePos.z = 0f;
            return (Vector2)mousePos;
        }
    }

    public void InvokeRecipeCreated(ThrowRecipe recipe)
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            recipe.modifiers.gemPowerMultiplier += pc.bonusThrowEffectMultiplier;
            if (recipe.info.isDirect)
            {
                recipe.modifiers.gemPowerMultiplier += pc.chargeEfficiencyMultiplier;
            }
        }
        
        OnRecipeCreated?.Invoke(recipe);
    }

    private void Awake()
    {
        _input = GetComponent<ThrowInputHandler>();
        if (_input == null) _input = gameObject.AddComponent<ThrowInputHandler>();
        _input.Init(this);

        _physics = GetComponent<ThrowPhysics>();
        if (_physics == null) _physics = gameObject.AddComponent<ThrowPhysics>();
        _physics.Init(this);

        _strategy = GetComponent<ThrowStrategy>();
        if (_strategy == null) _strategy = gameObject.AddComponent<ThrowStrategy>();
        _strategy.Init(this);

        if (trajectoryPredictor == null) trajectoryPredictor = GetComponentInChildren<TrajectoryPredictor>();
        if (trajectoryPredictor != null) trajectoryPredictor.Init(this);

        // [추가] Magician 슬롯을 Object 줍기(None) 모드로 자동 교체
        if (directionMapping != null)
        {
            for (int i = 0; i < directionMapping.Count; i++)
            {
                if (directionMapping[i] == CommandData.SkeletonMagician)
                {
                    directionMapping[i] = CommandData.None;
                }
            }
        }
    }

    private void Start()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnGemTreeUpdated += RefreshThrowInfo;
        }
        RefreshThrowInfo();
    }

    private void OnDestroy()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnGemTreeUpdated -= RefreshThrowInfo;
        }
    }

    private void RefreshThrowInfo()
    {
        Panel_ThrowInformation.Instance?.Refresh(MaxHoldCount, _heldObjects);
    }

    private void LateUpdate()
    {
        if (_activeCluster != null && _activeCluster.transform.parent == holdPoint)
        {
            _physics.UpdateHoldPosition(_activeCluster, (Vector2)transform.position);
        }
    }

    public void OnRightClickStarted() => _input.OnRightClickStarted();
    public void OnRightClickCanceled() => _input.OnRightClickCanceled();
    public void OnThrow(InputAction.CallbackContext context) => _input.OnThrow(context);

    public void TryPickUpWithMouse()
    {
        GameObject hovered = GameManager.Instance.mouseManager.HoverObject;
        if (hovered != null && hovered.TryGetComponent(out IThrowable throwable))
        {
            var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
            if (stamina != null && !stamina.CanThrow(_heldObjects.Count + 1, throwable.MinionType))
            {
                stamina.TriggerInsufficientFeedback();
                return;
            }
            // [추가] 이미 던져져서 날아가고 있는 유닛(FlyingObject 레이어)은 다시 잡을 수 없도록 방지
            int flyingLayer = LayerMask.NameToLayer("FlyingObject");
            if (hovered.layer == flyingLayer && !_heldObjects.Contains(throwable)) return;

            // [융합 방지] 융합체는 집을 수 없음
            if (hovered.TryGetComponent<FusionMinionController>(out var fusion) && fusion.IsFused) return;

            if (throwable is AllyController ally && !_strategy.CanPickUpType(ally.MinionType, _heldObjects, MaxHoldCount)) return;
            float dist = Vector2.Distance(transform.position, hovered.transform.position);
            if (dist > GameManager.Instance.PLAYERCONTROLLER.THROWRANGE) return;

            if (InventoryManager.Instance != null)
            {
                bool handled = false;
                foreach (var ability in InventoryManager.Instance.ActiveAbilities)
                {
                    if (ability != null && ability.OnTryPickUp(throwable, _heldObjects))
                    {
                        handled = true;
                        break;
                    }
                }
                if (handled)
                {
                    UpdateClusterAfterPickUp();
                    return;
                }
            }

            if (!_heldObjects.Contains(throwable))
            {
                PerformPickUp(throwable, hovered);
                if (trajectoryPredictor != null) trajectoryPredictor.ShowGuide();
            }
        }
    }

    public bool TryAutoPickUpNearbyThrowable()
    {
        // 1. 이미 들고 있는 사물이 있다면 성공으로 간주
        if (_heldObjects.Count > 0) return true;

        // 2. 주변 탐색
        float radius = GameManager.Instance.PLAYERCONTROLLER.THROWRANGE;
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, radius);
        IThrowable bestTarget = null;
        GameObject bestObj = null;
        float minDist = float.MaxValue;
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");

        foreach (var col in colls)
        {
            // 날아가고 있는 객체 제외
            if (col.gameObject.layer == flyingLayer) continue;

            if (col.TryGetComponent<IThrowable>(out var throwable))
            {
                // 융합체 등 집을 수 없는 조건 확인
                if (throwable is MonoBehaviour mb && mb.TryGetComponent<FusionMinionController>(out var fusion) && fusion.IsFused) continue;

                float dist = Vector2.Distance(transform.position, col.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestTarget = throwable;
                    bestObj = col.gameObject;
                }
            }
        }

        // 3. 찾았다면 즉시 집기 처리
        if (bestTarget != null)
        {
            PerformPickUp(bestTarget, bestObj);
            return true;
        }

        // 찾지 못했다면 투척 실패
        return false;
    }

    public void TryPickUpByType(CommandData targetType)
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null && !stamina.CanThrow(_heldObjects.Count + 1, targetType))
        {
            stamina.TriggerInsufficientFeedback();
            return;
        }

        if (!_strategy.CanPickUpType(targetType, _heldObjects, MaxHoldCount)) return;
        float radius = GameManager.Instance.PLAYERCONTROLLER.THROWRANGE;
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, radius);
        IThrowable bestTarget = null;
        float minDist = float.MaxValue;

        int flyingLayer = LayerMask.NameToLayer("FlyingObject");

        foreach (var col in colls)
        {
            // [수정] 이미 날아가고 있는 유닛(FlyingObject 레이어)은 제외
            if (col.gameObject.layer == flyingLayer && !_heldObjects.Contains(col.GetComponent<IThrowable>())) continue;

            if (col.TryGetComponent<IThrowable>(out var throwable) && throwable.MinionType == targetType && !_heldObjects.Contains(throwable))
            {
                // [융합 방지] 융합체는 집을 수 없음
                if (throwable is MonoBehaviour mb && mb.TryGetComponent<FusionMinionController>(out var fusion) && fusion.IsFused) continue;

                float d = Vector2.Distance(transform.position, col.transform.position);
                if (d < minDist) { minDist = d; bestTarget = throwable; }
            }
        }

        if (bestTarget != null)
        {
            if (InventoryManager.Instance != null)
            {
                bool handled = false;
                foreach (var ability in InventoryManager.Instance.ActiveAbilities)
                {
                    if (ability != null && ability.OnTryPickUp(bestTarget, _heldObjects))
                    {
                        handled = true;
                        break;
                    }
                }
                if (handled)
                {
                    UpdateClusterAfterPickUp();
                    return;
                }
            }

            PerformPickUp(bestTarget, bestTarget.transform.gameObject);
            if (trajectoryPredictor != null) trajectoryPredictor.ShowGuide();
        }
    }

    private void UpdateClusterAfterPickUp()
    {
        ThrowCluster cluster = GetActiveClusterOrCreate();
        cluster.Setup(_heldObjects);
        if (trajectoryPredictor != null) trajectoryPredictor.ShowGuide();
        RefreshThrowInfo();
    }

    private void PerformPickUp(IThrowable throwable, GameObject obj)
    {
        if (throwable == null || obj == null || (throwable is MonoBehaviour mb && (mb == null || !mb.gameObject.activeInHierarchy))) return;

        _heldObjects.Add(throwable);
        throwable.OnPickedUp();
        ThrowCluster cluster = GetActiveClusterOrCreate();
        throwable.transform.SetParent(cluster.transform);
        cluster.Setup(_heldObjects);

        // [추가] 카메라 조준 상태 활성화
        if (CameraTargetController.Instance != null) CameraTargetController.Instance.SetAiming(true);

        RefreshThrowInfo();
    }

    private ThrowCluster GetActiveClusterOrCreate()
    {
        if (_activeCluster == null)
        {
            _activeCluster = Instantiate(clusterPrefab, holdPoint.position, Quaternion.identity, holdPoint);
            _activeCluster.transform.localPosition = Vector3.zero;
        }
        return _activeCluster;
    }

    // [NEW LOGIC] 미니언 픽업 없이 즉발(Instant) 데미지 클러스터를 쏘는 새로운 투척 로직
    public void FireDamageCluster(bool isDirect)
    {
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.ConsumeStamina(1); // 기본 기력 소모량 1명 분량 적용 (또는 0으로 처리해도 됨)
        
        GameManager.Instance.PLAYERCONTROLLER.RecordCombatAction();

        Vector2 startPos = (Vector2)holdPoint.position;
        Vector2 mousePos = CurrentMouseWorldPos;
        float ratio = 1.0f; // 즉발이므로 풀 차징으로 간주
        
        // 방금 집은 사물 리스트를 넘겨서 레시피 생성
        ThrowRecipe recipe = _strategy.CreateRecipe(mousePos, ratio, _heldObjects);
        
        // 강제로 직구/곡선 모드 오버라이드
        recipe.info.isDirect = isDirect;
        recipe.info.targetingMode = TargetingMode.Area; // 일단 기본적으로 범위 타격으로 간주 (원한다면 Target으로 수정 가능)
        
        // PickUp에서 이미 만들어진 클러스터 사용
        ThrowCluster cluster = GetActiveClusterOrCreate();
        cluster.SetRecipe(recipe);

        float speed = isDirect ? 35f : 12f; // 직구는 35로 더 빠르게, 곡선은 12로 여유있게
        float jumpH = isDirect ? 0.1f : 3.5f; // 곡선은 확연히 붕 뜨게 (높이 3.5)

        Vector2 finalPos = _physics.GetClampedTargetPos(startPos, mousePos, cluster);
        float dist = Vector2.Distance(startPos, finalPos);
        float duration = dist / speed;

        if (isDirect)
        {
            Vector2 dir = (finalPos - startPos).normalized;
            if (dir == Vector2.zero) dir = (mousePos - startPos).normalized;
            if (dir == Vector2.zero) dir = Vector2.right;
            duration = 5.0f; // 직구는 멀리 날아가게 둠
            finalPos = startPos + dir * (speed * duration);
        }
        else
        {
            // 곡선 던지기 시 거리에 따른 데미지/효과 보너스 계산 (ThrowStrategy와 동일 로직 적용)
            if (InventoryManager.Instance != null)
            {
                float flightTimeBonus = InventoryManager.Instance.GetAggregatedGemBonus(CommandData.None, StatType.ParabolicFlightTimeMultiplier);
                if (GameManager.Instance.PLAYERCONTROLLER.TryGetComponent<PlayerUniqueEffectManager>(out var uem))
                {
                    flightTimeBonus += uem.JustThrowItSpeedBonus;
                    uem.OnParabolicThrow();
                }
                duration *= (1f - Mathf.Clamp(flightTimeBonus, 0f, 0.9f));
            }
        }

        float maxHeight = isDirect ? jumpH : Mathf.Min(jumpH, dist * 0.5f);

        // 어빌리티 Hook
        if (InventoryManager.Instance != null)
        {
            foreach (var ability in InventoryManager.Instance.ActiveAbilities)
            {
                if (ability != null && ability.IsApplicable(isDirect, recipe.info.targetingMode))
                {
                    ability.OnThrowLaunch(this, recipe, startPos, finalPos, duration, maxHeight, isDirect, ratio);
                }
            }
        }

        cluster.Launch(startPos, finalPos, duration, maxHeight, isDirect, ratio);
        
        // 날아간 클러스터는 내 손을 떠났으므로 참조 해제 및 리스트 비우기
        _activeCluster = null;
        _heldObjects.Clear();
        RefreshThrowInfo();
    }

    public void ThrowAll()
    {
        _heldObjects.RemoveAll(item => item == null || (item is MonoBehaviour mb && mb == null));
        if (_heldObjects.Count == 0) return;

        // [추가] 카메라 조준 상태 비활성화
        if (CameraTargetController.Instance != null) CameraTargetController.Instance.SetAiming(false);

        // 스태미나 차감
        var stamina = GameManager.Instance.PLAYERCONTROLLER.STAMINA;
        if (stamina != null) stamina.ConsumeStamina(_heldObjects.Count);
        
        GameManager.Instance.PLAYERCONTROLLER.RecordCombatAction(); // 투척 시 전투 상태 갱신

        float ratio = _input.ChargeRatio;
        Vector2 startPos = (Vector2)_activeCluster.transform.position;
        Vector2 mousePos = CurrentMouseWorldPos;
        
        ThrowRecipe recipe = _strategy.CreateRecipe(mousePos, ratio, _heldObjects);

        if (_activeCluster != null)
        {
            _activeCluster.Setup(_heldObjects); 
            _activeCluster.SetRecipe(recipe);

            if (recipe.info.targetingMode == TargetingMode.Self)
            {
                GameManager.Instance.throwImpactManager.ProcessThrowImpact(recipe, startPos, (mousePos - startPos).normalized);
                recipe.info.isImmediateApplied = true;
            }

            float speed, jumpH, straightH;
            if (_heldObjects.Count > 0)
            {
                IThrowable first = _heldObjects[0];
                speed = (ratio >= 0.98f) ? first.FullChargeSpeed : Mathf.Lerp(first.MinSpeed, first.MaxSpeed, ratio);
                jumpH = first.JumpHeight;
                straightH = first.StraightHeight;
            }
            else
            {
                speed = (ratio >= 0.98f) ? 30f : Mathf.Lerp(5f, 20f, ratio);
                jumpH = 1.5f;
                straightH = 0.1f;
            }

            Vector2 finalPos = _physics.GetClampedTargetPos(startPos, recipe.info.impactPoint, _activeCluster);
            bool isDirect = ratio >= 0.98f;
            float dist = Vector2.Distance(startPos, finalPos);
            float duration = dist / speed;

            if (!isDirect && InventoryManager.Instance != null)
            {
                float flightTimeBonus = InventoryManager.Instance.GetAggregatedGemBonus(CommandData.None, StatType.ParabolicFlightTimeMultiplier);
                
                // [일단 던지고 보자] 버프 스택 적용 (PlayerUniqueEffectManager에서 받아옴)
                if (GameManager.Instance.PLAYERCONTROLLER.TryGetComponent<PlayerUniqueEffectManager>(out var uem))
                {
                    flightTimeBonus += uem.JustThrowItSpeedBonus;
                }

                // 20% 증가 시 => duration * 0.8 (최대 90% 감소로 제한)
                duration *= (1f - Mathf.Clamp(flightTimeBonus, 0f, 0.9f));
            }

            // [이벤트 버스] 투척 시작 시 비행 시간 등 파라미터 조절용 확장 공간 (추후 연동 시 duration 조절 가능)
            // ref float durationMult 파라미터를 통해 ThrowStrategy에서 세팅된 값을 받아오거나, ThrowStart 이벤트로 제어 가능

            if (isDirect)
            {
                Vector2 dir = (finalPos - startPos).normalized;
                if (dir == Vector2.zero) dir = (mousePos - startPos).normalized;
                if (dir == Vector2.zero) dir = Vector2.right;
                duration = 5.0f;
                finalPos = startPos + dir * (speed * duration);
            }

            float maxHeight = Mathf.Min(Mathf.Lerp(jumpH, straightH, ratio), dist * 0.5f);
            if (isDirect) maxHeight = straightH;

            if (InventoryManager.Instance != null)
            {
                foreach (var ability in InventoryManager.Instance.ActiveAbilities)
                {
                    if (ability != null && ability.IsApplicable(isDirect, recipe.info.targetingMode))
                    {
                        ability.OnThrowLaunch(this, recipe, startPos, finalPos, duration, maxHeight, isDirect, ratio);
                    }
                }
            }

            if (!isDirect)
            {
                if (GameManager.Instance.PLAYERCONTROLLER.TryGetComponent<PlayerUniqueEffectManager>(out var uem))
                {
                    uem.OnParabolicThrow();
                }
            }

            _activeCluster.Launch(startPos, finalPos, duration, maxHeight, isDirect, ratio);
            _activeCluster = null;
        }

        // [신속한 재배치] 3명 이상 투척 시 소환수들에게 5초간 이동속도 50% 증가 버프
        if (_heldObjects.Count >= 3 && InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.SwiftRelocation))
        {
            foreach (var t in _heldObjects)
            {
                if (t is MonoBehaviour mb && mb.TryGetComponent<CharacterStatus>(out var status))
                {
                    status.ApplySpeedBuff("SwiftRelocation", 0.5f, 5.0f);
                }
            }
        }

        _heldObjects.Clear();
        _input.ResetCharging();
        RefreshThrowInfo();
    }

    public void DropAll()
    {
        if (_heldObjects.Count == 0 && _activeCluster == null) return;

        // [추가] 카메라 조준 상태 비활성화
        if (CameraTargetController.Instance != null) CameraTargetController.Instance.SetAiming(false);
        
        Vector3 dropPos = transform.position + (Vector3)Random.insideUnitCircle * 0.5f;

        foreach (var t in _heldObjects)
        {
            if (t != null && (t is MonoBehaviour mb && mb != null))
            {
                mb.transform.SetParent(null);
                mb.transform.position = dropPos;

                // [추가] 물리 상태 강제 복구 (다시 주울 수 있게 함)
                if (mb.TryGetComponent<Rigidbody2D>(out var rb)) rb.simulated = true;
                if (mb.TryGetComponent<Collider2D>(out var col)) col.enabled = true;

                t.OnLanded();
            }
        }        
        if (_activeCluster != null)
        {
            Destroy(_activeCluster.gameObject);
            _activeCluster = null;
        }

        _heldObjects.Clear();
        if (_input != null) _input.ResetCharging();
        if (trajectoryPredictor != null) trajectoryPredictor.HideGuide();
        RefreshThrowInfo();
    }

    public void ForceClear()
    {
        if (_input != null) _input.ResetCharging();
        DropAll();

        // [추가] 카메라 조준 상태 비활성화
        if (CameraTargetController.Instance != null) CameraTargetController.Instance.SetAiming(false);

        ThrowCluster[] activeClusters = Object.FindObjectsByType<ThrowCluster>(FindObjectsSortMode.None);
        foreach (var cluster in activeClusters)
        {
            if (cluster != null) Destroy(cluster.gameObject);
        }

        if (trajectoryPredictor != null) trajectoryPredictor.HideGuide();
        RefreshThrowInfo();
    }

    private void OnDrawGizmosSelected() { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, 2.0f); }
}
