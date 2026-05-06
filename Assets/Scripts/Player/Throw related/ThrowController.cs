using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 투척 시스템의 중앙 컨트롤러 및 데이터 저장소입니다.
/// </summary>
public class ThrowController : MonoBehaviour
{
    [Header("Throw Settings")]
    [SerializeField] private int maxHoldCount = 5;
    public int MaxHoldCount => maxHoldCount;

    [Header("References")]
    [SerializeField] private Transform holdPoint;
    [SerializeField] public ThrowCluster clusterPrefab; // [수정] 외부(능력)에서 접근 가능하도록 public으로 변경
    [SerializeField] private TrajectoryPredictor trajectoryPredictor;
    
    [Header("Input & UI Settings")]
    [SerializeField] private SelectionWheelUI selectionWheel;
    [SerializeField] private float chargeTime = 1.0f;
    [SerializeField] private float dragThreshold = 50f;
    [SerializeField] private List<CommandData> directionMapping;

    // 데이터 게터 (서브 컴포넌트용)
    public Transform HoldPoint => holdPoint;
    public SelectionWheelUI SelectionWheel => selectionWheel;
    public float ChargeTime => chargeTime;
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

    private void Awake()
    {
        // 컴포넌트 초기화 및 연결
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
            if (throwable is AllyController ally && !_strategy.CanPickUpType(ally.MinionType, _heldObjects, maxHoldCount)) return;
            
            // [수정] 거리 체크를 능력 훅보다 먼저 수행
            float dist = Vector2.Distance(transform.position, hovered.transform.position);
            if (dist > GameManager.Instance.PLAYERCONTROLLER.THROWRANGE) return;

            // [추가] 던지기 능력 Hook (OnTryPickUp)
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

    public void TryPickUpByType(CommandData targetType)
    {
        if (!_strategy.CanPickUpType(targetType, _heldObjects, maxHoldCount)) return;
        float radius = GameManager.Instance.PLAYERCONTROLLER.THROWRANGE;
        
        // [수정] 이미 Physics2D.OverlapCircleAll로 범위 안의 애들만 가져오므로 거리는 OK
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, radius);
        IThrowable bestTarget = null;
        float minDist = float.MaxValue;
        foreach (var col in colls)
        {
            if (col.TryGetComponent<IThrowable>(out var throwable) && throwable.MinionType == targetType && !_heldObjects.Contains(throwable))
            {
                float d = Vector2.Distance(transform.position, col.transform.position);
                if (d < minDist) { minDist = d; bestTarget = throwable; }
            }
        }

        if (bestTarget != null)
        {
            // [추가] 던지기 능력 Hook (OnTryPickUp)
            if (InventoryManager.Instance != null)
            {
                bool handled = false;
                foreach (var ability in InventoryManager.Instance.ActiveAbilities)
                {
                    // [수정] 여기서도 사거리 체크는 이미 되어있으므로 훅만 호출
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
    }

    private void PerformPickUp(IThrowable throwable, GameObject obj)
    {
        _heldObjects.Add(throwable);
        throwable.OnPickedUp();
        ThrowCluster cluster = GetActiveClusterOrCreate();
        throwable.transform.SetParent(cluster.transform);
        cluster.Setup(_heldObjects);
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

    public void ThrowAll()
    {
        _heldObjects.RemoveAll(item => item == null || (item is MonoBehaviour mb && mb == null));
        if (_heldObjects.Count == 0) return;

        float ratio = _input.ChargeRatio;
        Vector2 startPos = (Vector2)_activeCluster.transform.position;
        Vector2 mousePos = CurrentMouseWorldPos;
        
        // 1. 레시피 생성 및 능력 Hook (ModifyRecipe)
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

            // 속도 및 궤적 수치 결정 (유닛이 있으면 첫 번째 유닛 기준, 없으면 기본값)
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
                // Phantom이나 일반 물체일 경우의 기본 발사 물리량
                speed = (ratio >= 0.98f) ? 30f : Mathf.Lerp(5f, 20f, ratio);
                jumpH = 1.5f;
                straightH = 0.1f;
            }

            Vector2 finalPos = _physics.GetClampedTargetPos(startPos, recipe.info.impactPoint, _activeCluster);
            bool isDirect = ratio >= 0.98f;
            float dist = Vector2.Distance(startPos, finalPos);
            float duration = dist / speed;

            // [수정] 직구인 경우: 마우스 위치를 넘어서 5초 동안 계속 날아가도록 설정
            if (isDirect)
            {
                Vector2 dir = (finalPos - startPos).normalized;
                if (dir == Vector2.zero) dir = (mousePos - startPos).normalized;
                if (dir == Vector2.zero) dir = Vector2.right;

                duration = 5.0f; // 최대 비행 시간 5초
                finalPos = startPos + dir * (speed * duration); // 5초 동안 이동할 거리
            }

            float maxHeight = Mathf.Min(Mathf.Lerp(jumpH, straightH, ratio), dist * 0.5f);
            if (isDirect) maxHeight = straightH; // 직구는 고정 높이 유지

            // [능력 Hook] OnThrowLaunch
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

            _activeCluster.Launch(startPos, finalPos, duration, maxHeight, isDirect, ratio);
            _activeCluster = null;
        }

        _heldObjects.Clear();
        _input.ResetCharging();
    }

    public void DropAll()
    {
        if (_heldObjects.Count == 0) return;
        foreach (var t in _heldObjects) if (t != null) { if (t is MonoBehaviour mb) mb.transform.SetParent(null); t.OnLanded(); }
        _heldObjects.Clear();
        _input.ResetCharging();
    }

    private void OnDrawGizmosSelected() { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, 2.0f); }
}
