using UnityEngine;

public class FloorProceedPortal : MonoBehaviour, IInteractable
{
    [SerializeField] private float triggerRadius = 1.2f;

    public string InteractionPrompt => "Proceed to Next Floor";

    private void Awake()
    {
        var col = GetComponent<CircleCollider2D>();
        if (col == null)
        {
            col = gameObject.AddComponent<CircleCollider2D>();
        }
        col.isTrigger = true;
        col.radius = triggerRadius;

        var rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        // 상호작용 레이어가 존재하면 설정, 없으면 Default 사용
        int interactableLayer = LayerMask.NameToLayer("Interactable");
        gameObject.layer = interactableLayer != -1 ? interactableLayer : LayerMask.NameToLayer("Default");
    }

    private void Start()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            sr.color = new Color(0.6f, 0.1f, 0.9f, 0.8f); // Mystical Purple Portal
            sr.sortingOrder = 5; // Draw above grounds
        }
        transform.localScale = new Vector3(1.5f, 1.5f, 1.0f);
        Debug.Log("<color=purple>[FloorProceedPortal]</color> Spawned and initialized. Ready for interaction.");
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
}
