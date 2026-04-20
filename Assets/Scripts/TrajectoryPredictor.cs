using UnityEngine;

namespace Necromancer.Player
{
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

            // 1. 필요한 데이터 가져오기 (ThrowController와 동일한 방식)
            Vector2 startPos = _throwController.HoldPoint.position; // 시작점 (holdPoint 사용)
            Vector2 targetPos = _throwController.CurrentMouseWorldPos; // 마우스 위치 (ThrowController에 프로퍼티 추가 필요)
            float chargeRatio = _throwController.CurrentChargeRatio; // 현재 차징 비율 (ThrowController에 프로퍼티 추가 필요)

            DrawTrajectory(startPos, targetPos, chargeRatio);
        }

        private void DrawTrajectory(Vector2 startPos, Vector2 targetPos, float chargeRatio)
        {
            // 2. 물리 및 기하 데이터 계산 (ThrowableUnit.cs의 OnThrown 로직 복사)
            Vector2 diff = targetPos - startPos;
            float distance = diff.magnitude;
            
            // 거리가 너무 가까우면 그리지 않음
            if (distance < 0.1f)
            {
                _lineRenderer.positionCount = 0;
                return;
            }

            float speed;
            float duration;
            float maxHeight;

            if (chargeRatio >= 1.0f)
            {
                speed = fullChargeSpeed;
                duration = distance / speed; // 실제 ThrowableUnit 코드의 2.0f는 가이드용으로 부적합할 수 있으므로 거리/속도로 계산
                maxHeight = straightHeight;
            }
            else
            {
                speed = Mathf.Lerp(minSpeed, maxSpeed, chargeRatio);
                duration = distance / speed;

                float targetHeight = Mathf.Lerp(jumpHeight, straightHeight, chargeRatio);
                maxHeight = Mathf.Min(targetHeight, distance * 0.5f);
            }

            // 3. LineRenderer 점 위치 계산
            _lineRenderer.positionCount = numPoints;
            Vector3[] points = new Vector3[numPoints];

            for (int i = 0; i < numPoints; i++)
            {
                // 비율 t (0.0 ~ 1.0)
                float t = i / (float)(numPoints - 1);
                
                // 3a. 직선 방향 위치 (x, z 평면)
                Vector2 currentPos = Vector2.Lerp(startPos, targetPos, t);

                // 3b. 포물선 높이 계산 (ArcMovement와 동일한 공식을 사용해야 함)
                // 여기에 ArcMovement가 사용하는 실제 공식을 넣어야 합니다.
                // 아래는 가장 일반적인 포물선 공식 예시입니다.
                float height = 4 * maxHeight * t * (1 - t);

                // 3c. 최종 위치 (2D 게임이므로 y값에 높이를 더함)
                points[i] = new Vector3(currentPos.x, currentPos.y + height, 0f);
            }

            // 4. LineRenderer에 점 할당
            _lineRenderer.SetPositions(points);
        }
    }
}