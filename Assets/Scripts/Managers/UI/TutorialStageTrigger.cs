using UnityEngine;

/// <summary>
/// 튜토리얼 던전에서 플레이어가 이 지점(방 순서)에 도달하면
/// TutorialQuestPanelController에 해당 단계 문구를 띄우는 범용 트리거입니다.
/// 실제 RoomType/IRoomEvent 시스템과는 무관하게, 씬에 배치한 위치와 순서로만 동작합니다.
/// 7개 방(스폰~내려가는방) 전부 이 컴포넌트 하나로 처리할 수 있습니다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TutorialStageTrigger : MonoBehaviour
{
    [Tooltip("이 지점에 도달했을 때 띄울 튜토리얼 단계입니다.")]
    [SerializeField] private TutorialQuestPanelController.TutorialStage stage;

    [Tooltip("체크하면 한 번만 발동합니다. (플레이어가 되돌아와도 다시 안 뜸)")]
    [SerializeField] private bool triggerOnce = true;

    [Tooltip("체크하면 문구를 띄우는 대신 패널을 숨깁니다. 마지막 '내려가는방' 트리거에 체크하면 튜토리얼을 종료하며 패널을 닫는 용도로 쓸 수 있습니다.")]
    [SerializeField] private bool hideInstead = false;

    private bool _fired = false;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (triggerOnce && _fired) return;
        if (!other.CompareTag("Player")) return;

        _fired = true;

        if (hideInstead)
        {
            TutorialQuestPanelController.Instance?.Hide();
        }
        else
        {
            TutorialQuestPanelController.Instance?.ShowStage(stage);
        }
    }
}
