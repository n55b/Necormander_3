using UnityEngine;

public class RoomAudioTrigger : MonoBehaviour
{
    public AudioClip roomBGM; // 이 방에서 나올 음악

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) // 플레이어가 들어오면
        {
            SoundManager.Instance.ChangeBGM(roomBGM);
        }
    }
}