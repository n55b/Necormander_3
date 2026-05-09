using UnityEngine;
using UnityEngine.UI;

public class FloatingTextSpawner : MonoBehaviour
{
    private CharacterStat stats;

    [SerializeField] private Transform vec_float;

    private void Start()
    {
        stats = GetComponent<CharacterStat>();

    }

    private void OnDestory()
    {

    }

    private void ShowDamageText(int damage, string type, bool isCritical)
    {
        string text = damage.ToString();
        Color color = Color.grey;

        if (type == "Poison") color = Color.green;

        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        textObj.SetUp(text, color, vec_float, isCritical);
    }

    private void ShowStatusText(string statusName)
    {
        Color color = Color.cyan;
        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        textObj.SetUp(statusName, color, vec_float);
    }
}
