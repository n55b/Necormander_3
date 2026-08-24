using UnityEngine;

public class DoorUpController : MonoBehaviour
{
    [SerializeField] private GameObject openDoor;
    [SerializeField] private GameObject closeDoor;
    
    public void OpenDoor()
    {
        closeDoor.SetActive(false);
        openDoor.SetActive(true);
    }

    public void CloseDoor()
    {
        closeDoor.SetActive(true);
        openDoor.SetActive(false);
    }
}
