using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SummonController : MonoBehaviour
{
    public LayerMask obstacleLayer; // 벽이나 장애물 레이어 설정
    
    // 현재 눌려 있는 소환 키와 조합 키 상태
    private bool _isTabPressed = false;
    private int _pressedNumKey = -1; // -1: 안 눌림, 1-4: 숫자 키

    public bool IsSummoningMode => _pressedNumKey != -1;

    public void OnTab(InputAction.CallbackContext context)
    {
        if (context.performed) _isTabPressed = true;
        else if (context.canceled) _isTabPressed = false;
    }

    public void OnNumKey(int num, InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            _pressedNumKey = num;
            Debug.Log($"<color=cyan>[SummonController]</color> 소환 모드 활성화: {num}번 키 누름");
        }
        else if (context.canceled)
        {
            if (_pressedNumKey == num)
            {
                _pressedNumKey = -1;
                Debug.Log($"<color=yellow>[SummonController]</color> 소환 모드 해제");
            }
        }
    }

    // 현재 선택된 미니언 타입 반환
    public CommandData GetCurrentSelectedType()
    {
        // 소환 모드가 아니면 기본적으로 Warrior 반환 (실제로는 IsSummoningMode 체크 후 사용됨)
        if (_pressedNumKey == -1) return CommandData.SkeletonWarrior;

        if (!_isTabPressed)
        {
            // Tab 안 눌렸을 때 (1-4)
            return _pressedNumKey switch
            {
                1 => CommandData.SkeletonWarrior,
                2 => CommandData.SkeletonShieldbearer,
                3 => CommandData.SkeletonArcher,
                4 => CommandData.SkeletonPriest,
                _ => CommandData.SkeletonWarrior
            };
        }
        else
        {
            // Tab 눌렸을 때 (Tab + 1-4)
            return _pressedNumKey switch
            {
                1 => CommandData.SkeletonBomber,
                2 => CommandData.SkeletonSpearman,
                3 => CommandData.SkeletonMagician,
                4 => CommandData.SkeletonThief,
                _ => CommandData.SkeletonBomber
            };
        }
    }

    // 소환 위치 찾기 함수
    public List<Vector2> GetSummonPositions2D(int count, float radius)
    {
        List<Vector2> resultPositions = new List<Vector2>();
        int attempts = 0;
        int maxAttempts = count * 10;

        while (resultPositions.Count < count && attempts < maxAttempts)
        {
            attempts++;

            Vector2 randomPos = (Vector2)transform.position + (Random.insideUnitCircle * radius);
            Collider2D hit = Physics2D.OverlapCircle(randomPos, 0.2f, obstacleLayer);

            if (hit == null)
            {
                if (!IsTooClose(randomPos, resultPositions, 0.5f))
                {
                    resultPositions.Add(randomPos);
                }
            }
        }
        return resultPositions;
    }

    private bool IsTooClose(Vector2 pos, List<Vector2> list, float minDistance)
    {
        foreach (Vector2 p in list)
        {
            if (Vector2.Distance(pos, p) < minDistance) return true;
        }
        return false;
    }
}
