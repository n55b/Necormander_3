using UnityEngine;
using UnityEngine.UI;

public class ImageThrown : MonoBehaviour
{
    [SerializeField] private Image image;
    [SerializeField] private Image backgroundImage;

    private Color defaultBackgroundColor = Color.white;
    private Color defaultIconColor = Color.white;

    private void Awake()
    {
        if (image == null)
        {
            image = GetComponentInChildren<Image>();
        }

        if (backgroundImage == null)
        {
            Image rootImage = GetComponent<Image>();
            if (rootImage != null && rootImage != image)
            {
                backgroundImage = rootImage;
            }
        }

        if (backgroundImage != null)
        {
            defaultBackgroundColor = backgroundImage.color;
        }

        if (image != null)
        {
            defaultIconColor = image.color;
        }
    }

    public void Init(Sprite sprite)
    {
        if (image == null) return;

        image.sprite = sprite;
        image.enabled = sprite != null;
        image.color = defaultIconColor;

        if (backgroundImage != null)
        {
            backgroundImage.color = defaultBackgroundColor;
        }
    }

    public void Remove()
    {
        if (image == null) return;
        image.sprite = null;
        image.enabled = false;
        image.color = defaultIconColor;
        ResetBackgroundColor();
    }

    public void SetBackgroundColor(Color color)
    {
        if (backgroundImage != null)
        {
            backgroundImage.color = color;
        }
        else if (image != null)
        {
            image.color = color;
        }
    }

    public void ResetBackgroundColor()
    {
        if (backgroundImage != null)
        {
            backgroundImage.color = defaultBackgroundColor;
        }
        else if (image != null)
        {
            image.color = defaultIconColor;
        }
    }
}
