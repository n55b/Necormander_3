using UnityEngine;

public class FloorProceedPortal : MonoBehaviour, IInteractable
{
    public string InteractionPrompt => "Proceed to Next Floor";

    private void Awake()
    {
        // 프리팹의 BoxCollider2D도 재사용해야 감지되는 콜라이더에 따라 F 아이콘 높이가 바뀌지 않는다.
        var col = GetComponent<Collider2D>();
        if (col == null)
        {
            var circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = 0.5f; // 동적 생성 시 기본값 할당
            col = circle;
        }
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        // 상호작용 레이어가 존재하면 설정, 없으면 Default 사용
        int interactableLayer = Layers.Interactable;
        gameObject.layer = interactableLayer != -1 ? interactableLayer : Layers.Default;
    }

    private void Start()
    {
        Debug.Log("<color=purple>[FloorProceedPortal]</color> Initialized. Ready for interaction.");
    }

    public bool Interact(GameObject interactor)
    {
        Debug.Log("<color=green>[FloorProceedPortal]</color> Player interacted with portal. Moving to the next floor!");
        if (GameManager.Instance != null)
        {
            PolishFeedback.PlaySound(FeedbackCue.PortalArrive, transform.position);
            GameManager.Instance.GoToNextFloor();
        }
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }
    public void OnFocused(GameObject interactor){}
    public void OnLostFocus(GameObject interactor){}
}
