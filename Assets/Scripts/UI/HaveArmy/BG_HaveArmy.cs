using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BG_HaveArmy : MonoBehaviour
{
    [SerializeField] Image image;
    [SerializeField] TextMeshProUGUI text;

    public void Init(Sprite _image, int _num)
    {
        image.sprite = _image;
        text.text = _num.ToString();
    }
}
