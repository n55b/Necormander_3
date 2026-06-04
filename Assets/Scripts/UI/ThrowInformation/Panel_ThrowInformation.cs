using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class Panel_ThrowInformation : MonoBehaviour
{
    public static Panel_ThrowInformation Instance { get; private set; }

    [Header("Thrown Object")]
    [SerializeField] private GameObject imageThrown;
    [SerializeField] private Color occupiedSlotColor = Color.red;
    [SerializeField] private Color emptySlotColor = Color.white;
    [SerializeField] private Color objectHeldSlotColor = Color.red;

    private List<ImageThrown> thrownImages = new List<ImageThrown>();
    private int maxSlots = 0;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnGemTreeUpdated += RefreshFromController;
        }
        RefreshFromController();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnGemTreeUpdated -= RefreshFromController;
        }
    }

    public void Init(int _num)
    {
        SetSlotCount(_num);
        RefreshFromController();
    }

    public void Refresh(int slotCount, List<IThrowable> heldObjects)
    {
        SetSlotCount(slotCount);

        bool hasObject = false;
        foreach (var held in heldObjects)
        {
            if (held != null && held.MinionType == CommandData.None)
            {
                hasObject = true;
                break;
            }
        }

        int activeCount = 0;
        foreach (var held in heldObjects)
        {
            if (held != null) activeCount++;
        }

        for (int i = 0; i < thrownImages.Count; i++)
        {
            ImageThrown slot = thrownImages[i];
            if (i < activeCount && heldObjects[i] != null)
            {
                Sprite icon = heldObjects[i].MinionData != null ? heldObjects[i].MinionData.minionIcon : null;
                slot.Init(icon);
                slot.SetBackgroundColor(hasObject ? objectHeldSlotColor : occupiedSlotColor);
            }
            else
            {
                slot.Remove();
                slot.ResetBackgroundColor();
            }

            if (hasObject)
            {
                slot.SetBackgroundColor(objectHeldSlotColor);
            }
        }
    }

    public void RefreshFromController()
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        ThrowController throwController = GameManager.Instance.PLAYERCONTROLLER.GetComponentInChildren<ThrowController>();
        if (throwController == null) return;
        Refresh(throwController.MaxHoldCount, throwController.HeldObjects);
    }

    private void SetSlotCount(int slotCount)
    {
        if (slotCount < 0) slotCount = 0;
        maxSlots = slotCount;

        if (imageThrown == null)
        {
            Debug.LogWarning("[Panel_ThrowInformation] imageThrown prefab is not assigned.");
            return;
        }

        while (thrownImages.Count < maxSlots)
        {
            GameObject obj = Instantiate(imageThrown, transform);
            ImageThrown imageThrownComponent = obj.GetComponent<ImageThrown>();
            if (imageThrownComponent != null)
            {
                thrownImages.Add(imageThrownComponent);
            }
            else
            {
                Destroy(obj);
                Debug.LogWarning("[Panel_ThrowInformation] imageThrown prefab does not contain ImageThrown component.");
                break;
            }
        }

        while (thrownImages.Count > maxSlots)
        {
            int lastIndex = thrownImages.Count - 1;
            ImageThrown imageThrownComponent = thrownImages[lastIndex];
            if (imageThrownComponent != null)
            {
                Destroy(imageThrownComponent.gameObject);
            }
            thrownImages.RemoveAt(lastIndex);
        }
    }
}
