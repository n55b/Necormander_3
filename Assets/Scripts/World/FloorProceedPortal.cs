using UnityEngine;

public class FloorProceedPortal : MonoBehaviour, IInteractable
{
    public string InteractionPrompt => "Proceed to Next Floor";

    private void Awake()
    {
        var col = GetComponent<CircleCollider2D>();
        if (col == null)
        {
            col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = 0.5f; // 동적 생성 시 기본값 할당
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
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = new Color(0.6f, 0.1f, 0.9f, 0.8f); // Mystical Purple Portal
            sr.sortingOrder = 5; // Draw above grounds
        }
        Debug.Log("<color=purple>[FloorProceedPortal]</color> Initialized. Ready for interaction.");
    }

    public bool Interact(GameObject interactor)
    {
        Debug.Log("<color=green>[FloorProceedPortal]</color> Player interacted with portal. Moving to the next floor!");
        if (GameManager.Instance != null)
        {
            GameManager.Instance.GoToNextFloor();
        }
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }
    public void OnFocused(GameObject interactor){}
    public void OnLostFocus(GameObject interactor){}
}