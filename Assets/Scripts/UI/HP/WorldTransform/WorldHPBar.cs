using UnityEngine;
using UnityEngine.UI;

public class WorldHPBar : MonoBehaviour
{
    [SerializeField] CharacterStat stats;
    [SerializeField] private Image hpBar;

    private void Start()
    {
        stats.Health.UpdateHPBar += HPBarUpdate;
    }

    private void OnDisable()
    {
        stats.Health.UpdateHPBar -= HPBarUpdate;
    }

    public void HPBarUpdate()
    {
        hpBar.fillAmount = stats.CURHP / stats.MAXHP;
    }
}
