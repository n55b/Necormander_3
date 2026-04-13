using UnityEngine;
using UnityEngine.InputSystem;
using Necromancer.Interfaces;

namespace Necromancer.Player
{
    /// <summary>
    /// 플레이어의 던지기 입력을 처리하고 실행합니다.
    /// </summary>
    public class ThrowController : MonoBehaviour
    {
        [Header("Throw Settings")]
        [SerializeField] private float chargeTime = 1.0f; // 최대 충전에 걸리는 시간

        [Header("References")]
        [SerializeField] private Transform holdPoint; // 집어들었을 때 유닛이 위치할 곳

        private IThrowable _currentHeldObject;
        private float _chargeTimer;
        private bool _isCharging;

        // Input System 관련 (Unity Input System 패키지 사용 가정)
        // 실제 인스펙터에서 Input Action과 연결하거나 코드로 바인딩 가능

        private void Update()
        {
            // 테스트를 위해 직접 마우스 입력 체크 추가
            if (Mouse.current.leftButton.wasPressedThisFrame) OnClickStart();
            if (Mouse.current.leftButton.wasReleasedThisFrame) OnClickEnd();

            if (_isCharging)
            {
                _chargeTimer = Mathf.Min(_chargeTimer + Time.deltaTime, chargeTime);
            }
        }

        // 마우스 좌클릭 시 호출 (Unity Input System 이벤트 또는 직접 입력 체크)
        public void OnClickStart()
        {
            // 1. 만약 들고 있는 게 없다면 주변에서 줍기
            if (_currentHeldObject == null)
            {
                TryPickUp();
            }
            else
            {
                // 2. 들고 있다면 차징 시작
                _isCharging = true;
                _chargeTimer = 0f;
            }
        }

        public void OnClickEnd()
        {
            if (_isCharging && _currentHeldObject != null)
            {
                Throw();
            }
            _isCharging = false;
        }

        private void TryPickUp()
        {
            // 주변의 Throwable 객체 탐색 (범위를 2.0으로 상향)
            Collider2D[] colliders = UnityEngine.Physics2D.OverlapCircleAll(transform.position, 2.0f);
            foreach (var col in colliders)
            {
                // 자기 자신 제외 (플레이어에게 콜라이더가 있는 경우 대비)
                if (col.gameObject == gameObject) continue;

                IThrowable throwable = col.GetComponent<IThrowable>();
                if (throwable != null)
                {
                    _currentHeldObject = throwable;
                    _currentHeldObject.OnPickedUp();
                    
                    // 들고 있는 상태의 비주얼 처리
                    col.transform.SetParent(holdPoint);
                    col.transform.localPosition = Vector3.zero;
                    col.transform.localRotation = Quaternion.identity;
                    break;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            // 에디터에서 줍기 범위를 보여줌
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 2.0f);
        }

        private void Throw()
        {
            float chargeRatio = _chargeTimer / chargeTime;

            // 마우스 월드 좌표 계산
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            mousePos.z = 0f;
            Vector2 targetPos = (Vector2)mousePos;

            // 오브젝트 던지기
            if (_currentHeldObject != null)
            {
                if (_currentHeldObject is MonoBehaviour mb)
                {
                    mb.transform.SetParent(null);
                }

                _currentHeldObject.OnThrown(targetPos, chargeRatio);
                _currentHeldObject = null;
            }
        }
    }
}
