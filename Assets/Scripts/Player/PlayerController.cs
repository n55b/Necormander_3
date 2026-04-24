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
    [SerializeField] float throwRange;
    public float THROWRANGE {get { return throwRange; }}
    [Header("감지 영역")]
    [SerializeField] GameObject TrackingCollider;
    [Header("아군 유닛 관련 매니저")]
    [SerializeField] AllyManager allyManager;
    [Header("소환 컨트롤러")]
    [SerializeField] SummonController sumController;
    public SummonController SUMCONTROLLER {get{ return sumController;}}
    [Header("던지기 컨트롤러")]
    [SerializeField] private ThrowController throwController;
    [SerializeField] private int summonNum;
    [SerializeField] private float summonRange;

    [Header("플레이어 상태")]
    [SerializeField] PlayerStates P_State = PlayerStates.Idle;

    [Header("이동 변수")]
    [SerializeField] Vector3 MoveDirection = Vector3.zero;
    [SerializeField] Vector2 moveInput = Vector2.zero;

    private void Awake()
    {
        // 동일 오브젝트에서 ThrowController를 자동으로 찾아 할당
        if (throwController == null)
        {
            throwController = GetComponent<ThrowController>();
        }
    }

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

    // 아군 유닛 소환 혹은 줍기 관리 함수 (우클릭)
    public void OnRightClick(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            Debug.Log($"<color=white>[PlayerController]</color> 우클릭 입력됨! 소환 모드 상태: {sumController.IsSummoningMode}");
            
            // 1. 소환 모드(숫자 키가 눌려 있음)라면 소환 수행
            if (sumController.IsSummoningMode)
            {
                CommandData selectedType = sumController.GetCurrentSelectedType();
                MinionDataSO data = GameManager.Instance.dataManager.GetMinionData(selectedType);

                if (ReferenceEquals(data, null)) 
                {
                    sumController.ResetSummonMode();
                    return;
                }

                // 소환시 필요 재화 계산
                if (data.cost == 0)
                {
                    Debug.Log("cost가 0이므로 오류가 생김");
                    return;
                }
                int finalSummonCount = GameManager.Instance.dataManager.CalculateBonepoint(summonNum, data.cost);

                List<Vector2> pos = sumController.GetSummonPositions2D(finalSummonCount, summonRange);

                for (int i = 0; i < finalSummonCount; i++)
                {
                    Vector2 spawnPos = (i < pos.Count) ? pos[i] : (Vector2)transform.position;
                    allyManager.SpawnAlly(data, spawnPos);
                }

                // 소환 완료 후 모드 리셋
                sumController.ResetSummonMode();
            }
            // 2. 소환 모드가 아니라면 주변 미니언 줍기
            else
            {
                Debug.Log("<color=white>[PlayerController]</color> 줍기 모드 실행");
                if (throwController != null)
                {
                    throwController.TryPickUpWithMouse();
                }
            }
        }
    }

    // 아군 유닛 던지기 관리 함수 (좌클릭)
    public void OnThrow(InputAction.CallbackContext context)
    {
        if (throwController != null)
        {
            throwController.OnThrow(context);
        }
    }

    // --- Input System 전용 메서드들 (Inspector에서 연결 필요) ---
    public void OnTab(InputAction.CallbackContext context) => sumController.OnTab(context);
    public void OnNum1(InputAction.CallbackContext context) => sumController.OnNumKey(1, context);
    public void OnNum2(InputAction.CallbackContext context) => sumController.OnNumKey(2, context);
    public void OnNum3(InputAction.CallbackContext context) => sumController.OnNumKey(3, context);
    public void OnNum4(InputAction.CallbackContext context) => sumController.OnNumKey(4, context);

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
