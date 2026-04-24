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

    [SerializeField] private TrajectoryPredictor trajectoryPredictor; // 드래그 앤 드롭으로 할당
    public TrajectoryPredictor TrajectoryPredictor { get { return trajectoryPredictor; }}

    // --- 가이드용 프로퍼티 추가 ---
    public Transform HoldPoint => holdPoint;
    public float CurrentChargeRatio => Mathf.Min(_chargeTimer / chargeTime, 1.0f);

    private List<IThrowable> _heldObjects = new List<IThrowable>();
    private float _chargeTimer;
    private bool _isCharging;

    // 현재 마우스 월드 좌표를 매 프레임 계산하여 제공
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

    // --- 벽 감지 및 목표 지점 보정 공용 메서드 ---
    public Vector2 GetClampedTargetPos(Vector2 origin, Vector2 targetPos)
    {
        int wallLayer = LayerMask.GetMask("Wall", "Obstacle");
        Vector2 direction = targetPos - origin;
        float distance = direction.magnitude;

        RaycastHit2D hit = Physics2D.Raycast(origin, direction.normalized, distance, wallLayer);
        if (hit.collider != null)
        {
            // 벽에 부딪혔다면, 벽보다 약간 앞 지점을 목표로 설정
            return hit.point - (direction.normalized * 0.1f);
        }
        return targetPos;
    }

    private void Update()
    {
        if (_isCharging)
        {
            _chargeTimer = Mathf.Min(_chargeTimer + Time.deltaTime, chargeTime);
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

        if (throwable is MonoBehaviour mb)
        {
            mb.transform.SetParent(holdPoint);
            // 쌓기 위치 계산: (Count - 1)을 사용해 0부터 차곡차곡 쌓임
            mb.transform.localPosition = new Vector3(0, (_heldObjects.Count - 1) * 0.5f, 0);
            mb.transform.localRotation = Quaternion.identity;
        }

        Debug.Log($"Picked up: {obj.name}. Total: {_heldObjects.Count}");
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

        Vector2 targetPos = GetClampedTargetPos((Vector2)transform.position, CurrentMouseWorldPos);

        // --- 1. 조합(Combination) 분석 ---
        AnalyzeCombination(targetPos, chargeRatio);

        // --- 2. 모든 객체 던지기 ---
        foreach (var throwable in _heldObjects)
        {
            if (throwable == null) continue;

            if (throwable is MonoBehaviour mb)
            {
                mb.transform.SetParent(null);
            }
            throwable.OnThrown(targetPos, chargeRatio);
        }

        _heldObjects.Clear();
        _chargeTimer = 0f; // [수정] 던지기가 끝난 후 타이머를 확실히 리셋
    }

    private void AnalyzeCombination(Vector2 targetPos, float chargeRatio)
    {
        if (_heldObjects.Count < 2) return;

        // IThrowable을 AllyController로 캐스팅 시도
        AllyController first = _heldObjects[0] as AllyController;
        AllyController second = _heldObjects[1] as AllyController;

        if (first == null || second == null) return;

        // 1. DataManager를 통해 핵심 조합이 있는지 확인
        var dataManager = GameManager.Instance.dataManager;
        ThrowCombinationSO combo = dataManager.GetCombination(first.MinionType, second.MinionType);

        if (combo != null)
        {
            Debug.Log($"<color=orange>[Combination Found]</color> {combo.combinationName}!");

            // 2. 서포터 분석 (3번째 유닛부터)
            List<AllyController> supporters = new List<AllyController>();
            for (int i = 2; i < _heldObjects.Count; i++)
            {
                if (_heldObjects[i] is AllyController supporter)
                {
                    if (combo.IsValidSupporter(supporter.MinionType))
                    {
                        supporters.Add(supporter);
                        // 서포터는 자신의 효과를 억제함
                        supporter.SetCombination(combo, false, null);
                    }
                }
            }

            // 3. 핵심 유닛들 설정
            // 0번 유닛이 효과를 직접 터뜨리는 'Lead'가 됨
            first.SetCombination(combo, true, supporters);
            // 1번 유닛은 조합원이므로 자신의 효과를 억제함
            second.SetCombination(combo, false, null);
        }
        else
        {
            Debug.Log("<color=white>[Combination]</color> No valid combination found for this pair.");
        }
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
