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

    private void LateUpdate()
    {
        if (transform.parent == null) return;

        // 부모의 scale.x가 음수(반전)라면, 자식인 나도 음수로 만들어 월드 기준 정방향(양수) 유지
        Vector3 newScale = transform.localScale;
        newScale.x = Mathf.Abs(newScale.x) * (transform.parent.localScale.x < 0 ? -1 : 1);
        transform.localScale = newScale;
    }

    public void HPBarUpdate()
    {
        hpBar.fillAmount = stats.CURHP / stats.MAXHP;
    }
}
