using System.Collections.Generic;
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
        [SerializeField] private int maxHoldCount = 5; // 최대 집기 개수

        [Header("References")]
        [SerializeField] private Transform holdPoint; // 집어들었을 때 유닛이 위치할 곳

        private List<IThrowable> _heldObjects = new List<IThrowable>();
        private float _chargeTimer;
        private bool _isCharging;

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
            _chargeTimer = 0f;
        }

        private void OnThrowCanceled()
        {
            if (_isCharging && _heldObjects.Count > 0)
            {
                ThrowAll();
            }
            _isCharging = false;
        }
        // 외부(PlayerController)에서 호출할 줍기 함수 (가장 가까운 한 마리만)
        public void TryPickUpMultiple()
        {
            // 이미 최대치면 더 줍지 않음
            if (_heldObjects.Count >= maxHoldCount)
            {
                Debug.LogWarning("Already holding max units!");
                return;
            }

            // 주변의 Throwable 객체 탐색
            float pickUpRadius = 3.0f;
            Collider2D[] colliders = UnityEngine.Physics2D.OverlapCircleAll(transform.position, pickUpRadius);

            IThrowable nearestThrowable = null;
            float minDistance = float.MaxValue;
            GameObject nearestObj = null;

            foreach (var col in colliders)
            {
                if (col.gameObject == gameObject) continue;

                IThrowable throwable = col.GetComponent<IThrowable>();
                
                // 이미 들고 있는 유닛이 아니고, IThrowable을 구현하고 있다면
                if (throwable != null && !_heldObjects.Contains(throwable))
                {
                    float distance = Vector2.Distance(transform.position, col.transform.position);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearestThrowable = throwable;
                        nearestObj = col.gameObject;
                    }
                }
            }

            // 가장 가까운 한 마리만 집기
            if (nearestThrowable != null)
            {
                _heldObjects.Add(nearestThrowable);
                nearestThrowable.OnPickedUp();

                Debug.Log($"Picked up nearest unit: {nearestObj.name}. Total: {_heldObjects.Count}");

                if (nearestThrowable is MonoBehaviour mb)
                {
                    mb.transform.SetParent(holdPoint);
                    // 쌓기 효과 유지
                    mb.transform.localPosition = new Vector3(0, (_heldObjects.Count - 1) * 0.5f, 0);
                    mb.transform.localRotation = Quaternion.identity;
                }
            }
            else
            {
                Debug.LogWarning("No units with IThrowable found nearby.");
            }
        }

        private void ThrowAll()
        {
            // 리스트에서 이미 파괴된 객체 제거
            _heldObjects.RemoveAll(item => item == null || (item is MonoBehaviour mb && mb == null));

            if (_heldObjects.Count == 0)
            {
                _isCharging = false;
                return;
            }

            float chargeRatio = _chargeTimer / chargeTime;

            Vector2 screenPos = Pointer.current.position.ReadValue();
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            mousePos.z = 0f;
            Vector2 targetPos = (Vector2)mousePos;

            // --- 구성 분석 ---
            AnalyzeAndHandleEffects(targetPos, chargeRatio);

            // 모든 객체 던지기
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
        }

        private void AnalyzeAndHandleEffects(Vector2 targetPos, float chargeRatio)
        {
            // 리스트에서 이미 파괴된 객체 제거
            _heldObjects.RemoveAll(item => item == null || (item is MonoBehaviour mb && mb == null));
            
            if (_heldObjects.Count == 0) return;

            // AllyController 리스트 추출
            List<AllyController> allies = new List<AllyController>();
            foreach (var item in _heldObjects)
            {
                if (item is AllyController ally && ally != null) allies.Add(ally);
            }

            if (allies.Count == 0) return;

            // 1. 모든 타입이 같은지 확인
            CommandData firstType = allies[0].MinionType;
            bool isAllSameType = true;
            foreach (var ally in allies)
            {
                if (ally.MinionType != firstType)
                {
                    isAllSameType = false;
                    break;
                }
            }

            if (isAllSameType)
            {
                HandleSameTypeEffect(firstType, allies.Count, targetPos, chargeRatio);
            }
            else
            {
                HandleMixedTypeEffect(allies, targetPos, chargeRatio);
            }
        }

        // 동일 종류 여러 명을 던졌을 때의 효과 (기본 효과 강화 등)
        private void HandleSameTypeEffect(CommandData type, int count, Vector2 targetPos, float chargeRatio)
        {
            Debug.Log($"Same Type Throw: {type} x{count}");
            // TODO: 타입별 강화 효과 로직 (현재 비워둠)
        }

        // 서로 다른 종류가 섞였을 때의 효과 (새로운 조합 효과 등)
        private void HandleMixedTypeEffect(List<AllyController> allies, Vector2 targetPos, float chargeRatio)
        {
            Debug.Log("Mixed Type Throw: " + allies.Count + " units");
            // TODO: 조합 효과 로직 (현재 비워둠)
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
}
