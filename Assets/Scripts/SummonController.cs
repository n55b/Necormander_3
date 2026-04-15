using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SummonController : MonoBehaviour
{
    [SerializeField] private List<CommandData> commands = new List<CommandData>();
    public LayerMask obstacleLayer; // 벽이나 장애물 레이어 설정
    public bool isCommand = false;

    public List<CommandData> COMMNADS { get {return commands;} }

    public void ResetCommands()
    {
        commands.Clear();
        isCommand = false;
    }

    // 커맨드 입력 받는 함수
    public void PushCommand(InputAction.CallbackContext context)
    {
        if(!context.performed) return;

        switch(context.action.name)
        {
            case "SummonButton1":   // 버튼 1 클릭
            commands.Add(CommandData.SkeletonWarrior);
            break;

            case "SummonButton2":   // 버튼 2 클릭
            commands.Add(CommandData.SkeletonArcher);
            break;

            case "SummonButton3":   // 버튼 3 클릭
            commands.Add(CommandData.SkeletonShieldbearer);
            break;

            case "SummonButton4":   // 버튼 4 클릭
            commands.Add(CommandData.SkeletonPriest);
            break;
        }

        isCommand = true;
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

            // 1. 2D 원형 랜덤 좌표 (Vector2라 바로 사용 가능)
            Vector2 randomPos = (Vector2)transform.position + (Random.insideUnitCircle * radius);

            // 2. 해당 위치에 벽(장애물)이 있는지 확인
            // 0.2f는 소환될 아군의 최소 반지름 정도라고 생각하시면 됩니다.
            Collider2D hit = Physics2D.OverlapCircle(randomPos, 0.2f, obstacleLayer);

            if (hit == null) // 장애물이 없을 때만 추가
            {
                // 3. 아군끼리 겹치지 않게 거리 체크
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
