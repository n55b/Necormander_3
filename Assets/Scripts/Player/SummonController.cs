using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;

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
        int maxAttempts = count * 20; // NavMesh 체크를 위해 시도 횟수를 조금 더 늘림

        Vector2 playerPos = (Vector2)transform.position;
        NavMeshPath path = new NavMeshPath();

        while (resultPositions.Count < count && attempts < maxAttempts)
        {
            attempts++;

            // 1. 플레이어 주변 반지름 내의 임의의 지점 선정
            Vector2 randomPos = playerPos + (Random.insideUnitCircle * radius);
            
            // 2. NavMesh 상의 유효한 지점인지 확인 (SamplePosition)
            // 인접한 NavMesh 지점을 찾습니다. (최대 1.0f 거리 내)
            if (NavMesh.SamplePosition(randomPos, out NavMeshHit navHit, 1.0f, NavMesh.AllAreas))
            {
                Vector2 sampledPos = navHit.position;

                // 3. 플레이어 위치와 샘플링된 지점 사이의 직선 경로(Linecast) 체크 (벽 뚫기 1차 방어)
                RaycastHit2D wallHit = Physics2D.Linecast(playerPos, sampledPos, obstacleLayer);
                
                if (wallHit.collider == null)
                {
                    // [핵심 로직] 4. 플레이어와 소환 지점 사이의 NavMesh 경로가 유효한지 체크 (벽 뚫기 2차 방어)
                    // 만약 벽 너머 다른 방이라면 NavMesh 경로가 '직선'이 아닐 것이고,
                    // 경로의 상태가 Complete이 아니거나 거리가 너무 멀어질 것입니다.
                    if (NavMesh.CalculatePath(playerPos, sampledPos, NavMesh.AllAreas, path))
                    {
                        // 경로가 완성되었고(Complete), 너무 멀리 돌아가는 경로가 아니라면 승인
                        if (path.status == NavMeshPathStatus.PathComplete)
                        {
                            // 5. 소환수들끼리 너무 겹치지 않게 거리 체크
                            if (!IsTooClose(sampledPos, resultPositions, 0.5f))
                            {
                                resultPositions.Add(sampledPos);
                            }
                        }
                    }
                }
            }
        }
        
        // 만약 자리가 너무 없다면, 최소한 플레이어 위치라도 반환
        if (resultPositions.Count == 0)
        {
            resultPositions.Add(playerPos);
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
