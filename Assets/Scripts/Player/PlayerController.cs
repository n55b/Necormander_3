using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerStates
{
    Idle,
    Battle
}

public class PlayerController : MonoBehaviour
{
    [Header("플레이어 스탯")]
    [SerializeField] CharacterStat stat;
    [Header("감지 영역")]
    [SerializeField] GameObject TrackingCollider;
    [Header("아군 유닛 관련 매니저")]
    [SerializeField] AllyManager allyManager;
    [Header("소환 컨트롤러")]
    [SerializeField] SummonController sumController;
    [SerializeField] private int summonNum;
    [SerializeField] private float summonRange;

    [Header("플레이어 상태")]
    [SerializeField] PlayerStates P_State = PlayerStates.Idle;

    [Header("이동 변수")]
    [SerializeField] Vector3 MoveDirection = Vector3.zero;
    [SerializeField] Vector2 moveInput = Vector2.zero;

    private void Update()
    {
        MoveDirection = moveInput;
    }

    private void FixedUpdate()
    {
        transform.position += MoveDirection * stat.MOVESPEED * Time.deltaTime;

        if(P_State == PlayerStates.Battle)
        {
            bool check = allyManager.CheckAllyState();
            if(!check)
                ChangeState(PlayerStates.Idle);
        }
    }

    // 플레이어 움직임 관리 함수
    public void OnMove(InputAction.CallbackContext context)
    {
        if(context.performed || context.canceled)
        {
            moveInput = context.ReadValue<Vector2>();
        }

        return;
    }

    // 아군 유닛 소환 함수
    public void RightClick(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        GameObject obj = GameManager.Instance.dataManager.SummonAlly(sumController.COMMNADS);

        // obj가 null 이면 소환 실패
        if(ReferenceEquals(obj, null))
        {
            sumController.ResetCommands();
            return;
        }

        List<Vector2> pos = sumController.GetSummonPositions2D(summonNum, summonRange);

        // SpawnAlly에서 포지션도 지정해줌
        for(int i = 0; i < summonNum; i++)
        {
            if(pos[i] != null)
                allyManager.SpawnAlly(obj, pos[i]);
            else
                allyManager.SpawnAlly(obj, pos[pos.Count]);
        }

        sumController.ResetCommands();
    }

    // 플레이어 전투 or 평소 상태 변경 함수
    public void ChangeState(PlayerStates _state)
    {
        if(P_State == _state) return;

        P_State = _state;

        if(P_State == PlayerStates.Battle)
        {
            TrackingCollider.gameObject.SetActive(false);
            allyManager.SetBattleState(true);
        }
        else if(P_State == PlayerStates.Idle)
        {
            TrackingCollider.gameObject.SetActive(true);
        }
    }
}
