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
    [SerializeField] CharacterState stat;
    [SerializeField] GameObject TrackingCollider;

    [Header("플레이어 상태")]
    [SerializeField] PlayerStates P_State = PlayerStates.Idle;

    [Header("이동 오브젝트")]
    [SerializeField] private GameObject _targetPosition;
    [SerializeField, ReadOnly] private bool Move = false;

    [Header("소환수 목록")]
    [SerializeField] private List<AllyController> allys;

    private void FixedUpdate()
    {
        if(Move)
        {
            OnMove();
        }

        if(P_State == PlayerStates.Battle)
        {
            bool yet = false;
            foreach(var ally in allys)
            {
                if(ally.IsBattle)
                {
                    yet = true;
                    break;
                }
            }

            if(!yet)
            {
                ChangeState(PlayerStates.Idle);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 입력 관련 처리 if문
        if(collision.gameObject == _targetPosition.gameObject)
        {
            Move = false;
        }
    }

    // 플레이어 움직임 관리 함수
    private void OnMove()
    {
        float speed = stat.MOVESPEED;
        float dist = Vector3.Distance(transform.position, _targetPosition.transform.position);
        
        if((Vector2)this.transform.position != (Vector2)_targetPosition.transform.position)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                (Vector3)_targetPosition.transform.position,
                stat.MOVESPEED * Time.deltaTime
            );
        }

        return;
    }

    // 플레이어 움직임 입력 받는 함수
    public void OnRightClick(InputAction.CallbackContext context)
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 screenPosWithDepth = new Vector3(
            mousePos.x, 
            mousePos.y, 
            Math.Abs(Camera.main.transform.position.z));
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(screenPosWithDepth);

        _targetPosition.transform.position = worldPos;

        Move = true;
    }

    // 플레이어 전투 or 평소 상태 변경 함수
    public void ChangeState(PlayerStates _state)
    {
        if(P_State == _state) return;

        P_State = _state;

        if(P_State == PlayerStates.Battle)
        {
            TrackingCollider.gameObject.SetActive(false);
            foreach(var ally in allys)
            {
                ally.SetBattleState(true);
            }
        }
        else if(P_State == PlayerStates.Idle)
        {
            TrackingCollider.gameObject.SetActive(true);
        }
    }
}
