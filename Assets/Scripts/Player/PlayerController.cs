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
    [SerializeField] GameObject TrackingCollider;
    [SerializeField] AllyManager allyManager;

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
