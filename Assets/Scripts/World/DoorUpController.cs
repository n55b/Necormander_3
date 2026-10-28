using UnityEngine;

public class DoorUpController : MonoBehaviour
{
    [SerializeField] private GameObject openDoor;
    [SerializeField] private GameObject closeDoor;
    [SerializeField] private PortalDoorVisual portalVisual;

    [Tooltip("문이 벽 방향에 맞춰 회전돼도(MapGenerator가 Door Up을 90/180/-90도 돌려서 재사용) " +
             "회전하지 않고 항상 똑바로 서 있을 자식 오브젝트 이름. 위치는 벽 방향을 따라가고 회전만 고정된다.")]
    [SerializeField] private string[] keepUprightChildNames = { "DoorDownObject" };

    private void Awake()
    {
        KeepChildrenUpright();
        // 맵 생성 직후 연결된 문은 열려 있다. 전투가 시작되면 CloseDoor가 장벽을 켠다.
        if (portalVisual != null)
        {
            closeDoor.SetActive(false);
            openDoor.SetActive(true);
            portalVisual.SetOpen(true);
        }
    }

    private void KeepChildrenUpright()
    {
        if (keepUprightChildNames == null) return;
        foreach (var childName in keepUprightChildNames)
        {
            if (string.IsNullOrEmpty(childName)) continue;
            Transform child = transform.Find(childName);
            if (child != null) child.rotation = Quaternion.identity;
        }
    }
    
    public void OpenDoor()
    {
        if(closeDoor.activeSelf && PolishFeedback.Player!=null && (PolishFeedback.Player.transform.position-transform.position).sqrMagnitude<225f)
            PolishFeedback.Play(FeedbackCue.DoorOpen,transform.position,Vector2.up);
        closeDoor.SetActive(false);
        openDoor.SetActive(true);
        KeepChildrenUpright();
        if (portalVisual != null) portalVisual.SetOpen(true);
    }

    public void CloseDoor()
    {
        closeDoor.SetActive(true);
        openDoor.SetActive(false);
        if (portalVisual != null) portalVisual.SetOpen(false);
    }
}
