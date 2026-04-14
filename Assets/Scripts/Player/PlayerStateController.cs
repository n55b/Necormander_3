using UnityEngine;

public class PlayerStateController : MonoBehaviour
{
    [SerializeField] private PlayerController playerCtr;
    [SerializeField] private LayerMask layer;               // 주변에 오면 공격하게 되는 대상 레이어

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if ((layer.value & (1 << collision.gameObject.layer)) != 0)
        {
            playerCtr.ChangeState(PlayerStates.Battle); // 플레이어의 상태 Battle로 변경
            Debug.Log("공격 대상 발견!");
        }
    }
}
