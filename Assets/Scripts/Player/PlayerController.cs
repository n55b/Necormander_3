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

    [Header("던지기 배율 설정")]
    [SerializeField] private float minThrowChargeMultiplier = 1.0f;
    [SerializeField] private float maxThrowChargeMultiplier = 2.0f;

    public float GetThrowChargeMultiplier(float ratio)
    {
        return Mathf.Lerp(minThrowChargeMultiplier, maxThrowChargeMultiplier, ratio);
    }

    public void IncreaseMaxChargeMultiplier(float amount)
    {
        maxThrowChargeMultiplier += amount;
        Debug.Log($"<color=yellow>[Growth]</color> 최대 투척 배율 증가! 현재: {maxThrowChargeMultiplier}");
    }

    [Header("이동 변수")]
    [SerializeField] Vector3 MoveDirection = Vector3.zero;
    [SerializeField] Vector2 moveInput = Vector2.zero;
    public Vector2 MoveInput => moveInput;

    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        if (throwController == null)
        {
            throwController = GetComponentInChildren<ThrowController>();
        }
    }

    private void Start()
    {
        if (stat != null)
        {
            stat.Setup();
        }
    }

    private void Update()
    {
        MoveDirection = moveInput;
    }

    private void FixedUpdate()
    {
        // [복구] 기존 이동 로직으로 원복하되, 넉백 중일 때는 물리 속도를 덮어쓰지 않도록 개선 가능
        // 만약 리지드바디의 속도가 넉백에 의해 아주 높다면 이동 처리를 스킵하거나 합산
        if (_rb != null && _rb.linearVelocity.sqrMagnitude < 200f) // 대략적인 임계값
        {
             transform.position += MoveDirection * stat.MOVESPEED * Time.deltaTime;
        }

        if(P_State == PlayerStates.Battle)
        {
            bool check = allyManager.CheckAllyState();
            if(!check)
                ChangeState(PlayerStates.Idle);
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if(context.performed || context.canceled)
        {
            moveInput = context.ReadValue<Vector2>();
        }
        return;
    }

    public void OnRightClick(InputAction.CallbackContext context)
    {
        if (sumController.IsSummoningMode)
        {
            if (context.performed)
            {
                Debug.Log($"<color=white>[PlayerController]</color> 우클릭 입력됨! 소환 수행");
                CommandData selectedType = sumController.GetCurrentSelectedType();
                MinionDataSO data = GameManager.Instance.dataManager.GetMinionData(selectedType);

                if (ReferenceEquals(data, null)) 
                {
                    sumController.ResetSummonMode();
                    return;
                }

                if (data.cost == 0)
                {
                    Debug.LogError($"<color=red>[PlayerController]</color> {data.minionName}의 소환 비용(Cost)이 0으로 설정되어 있습니다!");
                }
                
                int finalSummonCount = 1; 
                Debug.Log($"<color=white>[Summon Request]</color> Type: {selectedType}, Count: {finalSummonCount} (Resource check disabled)");

                List<Vector2> pos = sumController.GetSummonPositions2D(finalSummonCount, summonRange);

                for (int i = 0; i < finalSummonCount; i++)
                {
                    Vector2 spawnPos = (i < pos.Count) ? pos[i] : (Vector2)transform.position;
                    allyManager.SpawnAlly(data, spawnPos);
                }

                sumController.ResetSummonMode();
            }
        }
        else
        {
            if (throwController != null)
            {
                if (context.started)
                {
                    throwController.OnRightClickStarted();
                }
                else if (context.canceled)
                {
                    throwController.OnRightClickCanceled();
                }
            }
        }
    }

    public void OnThrow(InputAction.CallbackContext context)
    {
        if (throwController != null)
        {
            throwController.OnThrow(context);
        }
    }

    public void OnTab(InputAction.CallbackContext context) => sumController.OnTab(context);
    public void OnNum1(InputAction.CallbackContext context) => sumController.OnNumKey(1, context);
    public void OnNum2(InputAction.CallbackContext context) => sumController.OnNumKey(2, context);
    public void OnNum3(InputAction.CallbackContext context) => sumController.OnNumKey(3, context);
    public void OnNum4(InputAction.CallbackContext context) => sumController.OnNumKey(4, context);

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
