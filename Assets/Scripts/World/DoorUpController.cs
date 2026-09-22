using UnityEngine;

public class DoorUpController : MonoBehaviour
{
    [SerializeField] private GameObject openDoor;
    [SerializeField] private GameObject closeDoor;

    [Tooltip("문이 벽 방향에 맞춰 회전돼도(MapGenerator가 Door Up을 90/180/-90도 돌려서 재사용) " +
             "회전하지 않고 항상 똑바로 서 있을 자식 오브젝트 이름. 위치는 벽 방향을 따라가고 회전만 고정된다.")]
    [SerializeField] private string[] keepUprightChildNames = { "DoorDownObject" };

    private void Awake()
    {
        KeepChildrenUpright();
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
        closeDoor.SetActive(false);
        openDoor.SetActive(true);
        KeepChildrenUpright();
    }

    public void CloseDoor()
    {
        closeDoor.SetActive(true);
        openDoor.SetActive(false);
    }
}
