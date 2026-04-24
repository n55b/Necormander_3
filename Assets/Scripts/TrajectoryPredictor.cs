using UnityEngine;

/// <summary>
/// 플레이어의 던지기 궤도를 예측하고 시각화합니다.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class TrajectoryPredictor : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField, Range(10, 100)] private int numPoints = 50; // 궤도를 구성할 점의 개수 (많을수록 부드러움)
    
    [Header("Throwable Settings (Match with ThrowableUnit)")]
    // ThrowableUnit의 필드값들과 동일하게 맞춥니다.
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float straightHeight = 0.1f;
    [SerializeField] private float minSpeed = 2f;
    [SerializeField] private float maxSpeed = 18f;
    [SerializeField] private float fullChargeSpeed = 25f;

    private LineRenderer _lineRenderer;
    private ThrowController _throwController; // 차징 상태 정보를 가져오기 위해 필요

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        // 같은 오브젝트에 있거나, Player 오브젝트에서 가져오도록 설정
        _throwController = GetComponentInParent<ThrowController>(); 

        // 시작할 때는 가이드를 끕니다.
        _lineRenderer.enabled = false;
    }

    // ThrowController에서 차징이 시작될 때 호출
    public void ShowGuide()
    {
        _lineRenderer.enabled = true;
    }

    // ThrowController에서 던지거나 취소될 때 호출
    public void HideGuide()
    {
        _lineRenderer.enabled = false;
    }

    // 매 프레임 마우스 위치와 차징 비율에 따라 궤도를 업데이트
    private void Update()
    {
        if (!_lineRenderer.enabled || _throwController == null) return;

        // 1. 필요한 데이터 가져오기 (ThrowController와 동일한 데이터 사용)
        Vector2 startPos = _throwController.HoldPoint.position; // 시작점: 플레이어가 유닛을 들고 있는 위치
        
        // --- 벽 감지 및 목표 지점 보정 ---
        // 마우스 월드 좌표를 가져와서 벽에 막히는지 체크하고 최종 착지 지점을 계산합니다.
        Vector2 mouseWorldPos = _throwController.CurrentMouseWorldPos;
        Vector2 targetPos = _throwController.GetClampedTargetPos(startPos, mouseWorldPos);
        
        // 현재 플레이어의 던지기 차징 진행도 (0.0 ~ 1.0)
        float chargeRatio = _throwController.CurrentChargeRatio;

        DrawTrajectory(startPos, targetPos, chargeRatio);
    }

    private void DrawTrajectory(Vector2 startPos, Vector2 targetPos, float chargeRatio)
    {
        // 2. 물리 및 기하 데이터 계산
        Vector2 diff = targetPos - startPos;
        float distance = diff.magnitude;
        
        // 거리가 너무 가까우면 궤도를 그리지 않아 불필요한 연산을 방지합니다.
        if (distance < 0.1f)
        {
            _lineRenderer.positionCount = 0;
            return;
        }

        // --- 물리 파라미터 결정 ---
        // 기본값으로 초기화 (유닛이 없을 경우를 대비한 궤도 예측기 자체 설정값)
        float s_min = minSpeed;
        float s_max = maxSpeed;
        float s_full = fullChargeSpeed;
        float h_jump = jumpHeight;
        float h_straight = straightHeight;

        // [핵심 로직] 현재 들고 있는 유닛이 있다면 해당 유닛의 고유 데이터를 우선적으로 사용합니다.
        // ThrowController의 HoldPoint 자식 중 첫 번째 유닛을 확인합니다.
        if (_throwController.HoldPoint.childCount > 0)
        {
            var firstChild = _throwController.HoldPoint.GetChild(0);
            if (firstChild.TryGetComponent<AllyController>(out var ally))
            {
                // AllyController에 정의된 public 프로퍼티를 통해 유닛별 고유 물리 수치를 가져옵니다.
                // 이를 통해 실제 던졌을 때와 예측 궤도가 완벽하게 일치하게 됩니다.
                s_min = ally.MinSpeed;
                s_max = ally.MaxSpeed;
                s_full = ally.FullChargeSpeed;
                h_jump = ally.JumpHeight;
                h_straight = ally.StraightHeight;
            }
        }

        float maxHeight;

        // 3. 차징 비율에 따른 실시간 물리 수치 계산 (AllyController.OnThrown 로직과 동기화)
        if (chargeRatio >= 1.0f)
        {
            // 풀 차징 상태: 직사 투척 (낮은 높이, 최대 속도)
            maxHeight = h_straight;
        }
        else
        {
            // 일반 차징 상태: 포물선 투척 (차징 정도에 따라 높이와 속도 보간)
            float targetHeight = Mathf.Lerp(h_jump, h_straight, chargeRatio);
            
            // 거리가 너무 가까울 때 높이가 비정상적으로 솟구치는 것을 방지하기 위해 거리의 절반으로 제한합니다.
            maxHeight = Mathf.Min(targetHeight, distance * 0.5f);
        }

        // 4. LineRenderer 점 위치 계산 (ArcMovement.cs의 포물선 공식과 동일한 공식 사용)
        _lineRenderer.positionCount = numPoints;
        Vector3[] points = new Vector3[numPoints];

        for (int i = 0; i < numPoints; i++)
        {
            // 전체 궤도에서의 진행 비율 t (0.0 ~ 1.0)
            float t = i / (float)(numPoints - 1);
            
            // 수평 평면상에서의 위치 (시작점에서 목표점까지 선형 보간)
            Vector2 currentPos = Vector2.Lerp(startPos, targetPos, t);

            // 수직 높이(포물선) 계산 
            // [공식] h = 4 * H * t * (1 - t)
            // 이 공식은 ArcMovement.cs에서 실시간 위치 계산에 사용하는 것과 동일합니다.
            float height = 4f * maxHeight * t * (1f - t);

            // 최종 좌표 결정 (2D 게임이므로 Y축에 높이 오프셋을 더함)
            points[i] = new Vector3(currentPos.x, currentPos.y + height, 0f);
        }

        // 5. LineRenderer에 최종 계산된 점들을 할당하여 시각화
        _lineRenderer.SetPositions(points);
    }
}