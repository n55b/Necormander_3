using UnityEngine;

/// <summary>
/// 플레이어의 던지기 궤도를 예측하고 시각화합니다.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class TrajectoryPredictor : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField, Range(10, 100)] private int numPoints = 50; // 궤도를 구성할 점의 개수
    [SerializeField] private Color normalColor = Color.white;    // 기본 궤도 색상
    [SerializeField] private Color fullChargeColor = Color.green; // 최대 차징 시 색상

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
        if (_throwController.HoldPoint.childCount > 0)
        {
            var firstChild = _throwController.HoldPoint.GetChild(0);
            if (firstChild.TryGetComponent<AllyController>(out var ally))
            {
                s_min = ally.MinSpeed;
                s_max = ally.MaxSpeed;
                s_full = ally.FullChargeSpeed;
                h_jump = ally.JumpHeight;
                h_straight = ally.StraightHeight;
            }
        }

        float maxHeight;
        bool isFullCharge = chargeRatio >= 0.98f;
        
        // 3. 차징 비율에 따른 실시간 물리 수치 계산 및 시각화
        if (isFullCharge)
        {
            // 풀 차징 상태: 완벽한 직선을 위해 점을 2개만 사용
            _lineRenderer.positionCount = 2;
            _lineRenderer.SetPosition(0, (Vector3)startPos);
            _lineRenderer.SetPosition(1, (Vector3)targetPos);
            maxHeight = h_straight; 
        }
        else
        {
            // 일반 차징 상태: 기존 포물선 계산 로직 유지
            float targetHeight = Mathf.Lerp(h_jump, h_straight, chargeRatio);
            maxHeight = Mathf.Min(targetHeight, distance * 0.5f);

            _lineRenderer.positionCount = numPoints;
            Vector3[] points = new Vector3[numPoints];

            for (int i = 0; i < numPoints; i++)
            {
                float t = i / (float)(numPoints - 1);
                Vector2 currentPos = Vector2.Lerp(startPos, targetPos, t);
                float height = 4f * maxHeight * t * (1f - t);
                points[i] = new Vector3(currentPos.x, currentPos.y + height, 0f);
            }
            _lineRenderer.SetPositions(points);
        }

        // 4. 차징 상태에 따른 색상 업데이트
        Color targetColor = isFullCharge ? fullChargeColor : normalColor;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(targetColor, 0.0f), new GradientColorKey(targetColor, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(targetColor.a, 0.0f), new GradientAlphaKey(targetColor.a, 1.0f) }
        );
        _lineRenderer.colorGradient = gradient;
    }
}
