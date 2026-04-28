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
            // 1단계 (기본 유닛)
            return _selectedNum switch
            {
                1 => CommandData.SkeletonWarrior,
                2 => CommandData.SkeletonArcher,
                3 => CommandData.SkeletonShieldbearer,
                4 => CommandData.SkeletonPriest,
                _ => CommandData.SkeletonWarrior
            };
        }
        else
        {
            // 2단계 (확장 유닛)
            return _selectedNum switch
            {
                1 => CommandData.SkeletonMagician,
                2 => CommandData.SkeletonSpearman,
                3 => CommandData.SkeletonBomber,
                4 => CommandData.SkeletonThief,
                _ => CommandData.SkeletonMagician
            };
        }
    }

    private string GetMinionName(int num, int step)
    {
        if (step == 1)
        {
            return num switch { 1 => "전사", 2 => "궁수", 3 => "방패병", 4 => "사제", _ => "" };
        }
        else
        {
            return num switch { 1 => "마법사", 2 => "창병", 3 => "폭탄병", 4 => "도적", _ => "" };
        }
    }

    // 소환 위치 찾기 함수 (플레이어 주변부터 점진적으로 빈 공간 탐색)
    public List<Vector2> GetSummonPositions2D(int count, float radius)
    {
        List<Vector2> resultPositions = new List<Vector2>();
        Vector2 playerPos = (Vector2)transform.position;

        // 1. 점진적 거리 확장 (가까운 곳부터 탐색)
        // 0.5m 간격으로 거리를 늘려가며 빈 자리를 찾습니다.
        float distanceStep = 0.5f;
        int angleStep = 30; // 30도 간격으로 주변 탐사

        for (float currentDist = 0.5f; currentDist <= radius; currentDist += distanceStep)
        {
            // 각 거리에 대해 360도 방향을 확인
            for (int angle = 0; angle < 360; angle += angleStep)
            {
                if (resultPositions.Count >= count) break;

                // 해당 각도와 거리의 목표 지점 계산
                float rad = angle * Mathf.Deg2Rad;
                Vector2 targetPos = playerPos + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * currentDist;

                // 2. NavMesh 상에서 플레이어로부터 해당 지점까지 벽이 없는지 체크 (가장 중요)
                // NavMesh.Raycast는 벽에 부딪히면 true를 반환하며 hit.position에 충돌 지점을 담습니다.
                if (!NavMesh.Raycast(playerPos, targetPos, out NavMeshHit hit, NavMesh.AllAreas))
                {
                    // 벽에 걸리지 않았다면, 해당 지점이 실제로 서 있을 수 있는 곳인지 확인
                    if (NavMesh.SamplePosition(targetPos, out NavMeshHit navHit, 0.5f, NavMesh.AllAreas))
                    {
                        Vector2 sampledPos = navHit.position;

                        // 3. 소환수들끼리 너무 겹치지 않게 거리 체크
                        if (!IsTooClose(sampledPos, resultPositions, 0.4f))
                        {
                            resultPositions.Add(sampledPos);
                        }
                    }
                }
            }

            if (resultPositions.Count >= count) break;
        }

        // 4. 만약 점진적 탐색으로도 자리를 다 채우지 못했다면 (좁은 공간 등), 
        // 남은 마릿수만큼 플레이어 위치 주변에 강제로라도 배치 시도 (최후의 수단)
        int attempts = 0;
        while (resultPositions.Count < count && attempts < 20)
        {
            attempts++;
            Vector2 randomPos = playerPos + (Random.insideUnitCircle * 0.5f);
            if (NavMesh.SamplePosition(randomPos, out NavMeshHit navHit, 1.0f, NavMesh.AllAreas))
            {
                resultPositions.Add(navHit.position);
            }
        }
        
        // 만약 정말 자리가 없다면, 최소한 플레이어 위치라도 반환
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
