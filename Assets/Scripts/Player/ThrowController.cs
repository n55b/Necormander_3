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
    [SerializeField] private ThrowCluster clusterPrefab;
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

    private void Update()
    {
        if (_activeCluster != null) _physics.UpdateHoldPosition(_activeCluster, (Vector2)transform.position);
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
            float dist = Vector2.Distance(transform.position, hovered.transform.position);
            if (dist <= GameManager.Instance.PLAYERCONTROLLER.THROWRANGE && !_heldObjects.Contains(throwable))
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
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, radius);
        AllyController bestTarget = null;
        float minDist = float.MaxValue;
        foreach (var col in colls)
        {
            if (col.TryGetComponent<AllyController>(out var ally) && ally.MinionType == targetType && !_heldObjects.Contains(ally))
            {
                float d = Vector2.Distance(transform.position, ally.transform.position);
                if (d < minDist) { minDist = d; bestTarget = ally; }
            }
        }
        if (bestTarget != null)
        {
            PerformPickUp(bestTarget, bestTarget.gameObject);
            if (trajectoryPredictor != null) trajectoryPredictor.ShowGuide();
        }
    }

    private void PerformPickUp(IThrowable throwable, GameObject obj)
    {
        _heldObjects.Add(throwable);
        throwable.OnPickedUp();
        ThrowCluster cluster = GetActiveClusterOrCreate();
        if (throwable is MonoBehaviour mb) mb.transform.SetParent(cluster.transform);
        List<AllyController> allyList = new List<AllyController>();
        foreach (var item in _heldObjects) if (item is AllyController ally) allyList.Add(ally);
        cluster.Setup(allyList);
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
        ThrowRecipe recipe = _strategy.CreateRecipe(mousePos, ratio, _heldObjects);

        if (_activeCluster != null)
        {
            List<AllyController> allyList = new List<AllyController>();
            foreach (var item in _heldObjects) if (item is AllyController ally) allyList.Add(ally);
            if (allyList.Count > 0)
            {
                _activeCluster.Setup(allyList);
                _activeCluster.SetRecipe(recipe);
                if (recipe.targetingMode == TargetingMode.Self)
                {
                    GameManager.Instance.throwImpactManager.ProcessThrowImpact(recipe, startPos, (mousePos - startPos).normalized);
                    recipe.isImmediateApplied = true;
                }
                AllyController first = allyList[0];
                float speed = (ratio >= 0.98f) ? first.FullChargeSpeed : Mathf.Lerp(first.MinSpeed, first.MaxSpeed, ratio);
                Vector2 finalPos = _physics.GetClampedTargetPos(startPos, recipe.impactPoint, _activeCluster);
                float dist = Vector2.Distance(startPos, finalPos);
                _activeCluster.Launch(startPos, finalPos, dist / speed, Mathf.Min(Mathf.Lerp(first.JumpHeight, first.StraightHeight, ratio), dist * 0.5f), ratio >= 0.98f, ratio);
                _activeCluster = null;
            }
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
