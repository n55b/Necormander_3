using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SummonController : MonoBehaviour
{
    public LayerMask obstacleLayer; // 벽이나 장애물 레이어 설정
    
    // 현재 선택 상태 관리
    private int _selectionStep = 0; // 0: 대기, 1: 1단계(기본), 2: 2단계(확장)
    private int _selectedNum = -1;  // 선택된 숫자 (1-4)

    public bool IsSummoningMode => _selectionStep > 0;

    // 더 이상 Tab 키는 소환에 관여하지 않으므로 제거하거나 비워둠
    public void OnTab(InputAction.CallbackContext context) { }

    public void OnNumKey(int num, InputAction.CallbackContext context)
    {
        // 키가 실제로 눌린 시점(Value > 0)에만 로직 실행
        // performed가 키를 뗄 때도 호출될 수 있으므로 이를 방지함
        if (context.performed && context.ReadValueAsButton())
        {
            // 이미 해당 숫자가 선택된 상태에서 또 누르거나, 다른 숫자를 누르면 단계 진행
            if (_selectionStep == 0)
            {
                _selectionStep = 1;
                _selectedNum = num;
                Debug.Log($"<color=cyan>[SummonController]</color> 1단계 소환 모드: {GetMinionName(num, 1)} 선택");
            }
            else if (_selectionStep == 1)
            {
                _selectionStep = 2;
                _selectedNum = num;
                Debug.Log($"<color=cyan>[SummonController]</color> 2단계 소환 모드: {GetMinionName(num, 2)} 선택");
            }
            else // 2단계에서 또 누르면 다시 2단계 유지하며 종류만 변경 (사용자 요청: "두번째 누른 애만 종류에 영향")
            {
                _selectedNum = num;
                Debug.Log($"<color=cyan>[SummonController]</color> 2단계 종류 변경: {GetMinionName(num, 2)} 선택");
            }
        }
    }

    // 소환 모드 강제 종료 (소환 후 호출)
    public void ResetSummonMode()
    {
        _selectionStep = 0;
        _selectedNum = -1;
        Debug.Log($"<color=yellow>[SummonController]</color> 소환 모드 해제 (줍기 모드로 복귀)");
    }

    // 현재 선택된 미니언 타입 반환
    public CommandData GetCurrentSelectedType()
    {
        if (_selectionStep == 0) return CommandData.SkeletonWarrior;

        if (_selectionStep == 1)
        {
            // 1단계 (1-4)
            return _selectedNum switch
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
            // 2단계 (Combo: 1-4)
            return _selectedNum switch
            {
                1 => CommandData.SkeletonBomber,
                2 => CommandData.SkeletonSpearman,
                3 => CommandData.SkeletonMagician,
                4 => CommandData.SkeletonThief,
                _ => CommandData.SkeletonBomber
            };
        }
    }

    private string GetMinionName(int num, int step)
    {
        if (step == 1)
        {
            return num switch { 1 => "전사", 2 => "방패병", 3 => "궁수", 4 => "사제", _ => "" };
        }
        else
        {
            return num switch { 1 => "폭탄병", 2 => "창병", 3 => "마법사", 4 => "도적", _ => "" };
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
