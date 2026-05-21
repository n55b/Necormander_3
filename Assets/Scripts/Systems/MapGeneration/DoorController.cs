using UnityEngine;

/// <summary>
/// 개별 문의 상태(열림/닫힘)와 비주얼을 관리하는 컨트롤러입니다.
/// </summary>
public class DoorController : MonoBehaviour
{
    [SerializeField] private GameObject doorVisual; // 실제 문 스프라이트 및 애니메이터가 있는 오브젝트
    [SerializeField] private Collider2D doorCollider;

    private void Awake()
    {
        // 초기화 로직이 필요한 경우 여기에 작성 (SetOpen 강제 호출 제거)
    }

    public void SetOpen(bool isOpen)
    {
        if (doorVisual != null) doorVisual.SetActive(!isOpen);
        if (doorCollider != null) doorCollider.enabled = !isOpen;
        
        // TODO: 향후 Animator.SetBool("isOpen", isOpen) 등으로 애니메이션 처리 가능
    }
}
