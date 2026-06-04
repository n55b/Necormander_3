using UnityEngine;
using UnityEngine.UI;

public class WorldHPBar : MonoBehaviour
{
    [SerializeField] CharacterStat stats;
    [SerializeField] private Image hpBar;

    private void OnEnable()
    {
        if (stats != null)
        {
            if (stats.Health == null)
            {
                stats.Setup();
            }
            if (stats.Health != null)
            {
                stats.Health.UpdateHPBar += HPBarUpdate;
                HPBarUpdate(); // 활성화 시 즉시 갱신
            }
        }
    }

    private void OnDisable()
    {
        if (stats != null && stats.Health != null)
        {
            stats.Health.UpdateHPBar -= HPBarUpdate;
        }
    }

    private void LateUpdate()
    {
        // 1. 회전 고정: 부모가 돌아가더라도 체력바는 항상 수평을 유지
        transform.rotation = Quaternion.identity;

        // 2. 스케일 보정: 계층 구조 어디선가 Flip이 발생해 전역 스케일이 뒤집혔다면 로컬 스케일을 반전시켜 상쇄
        if (transform.parent != null)
        {
            Vector3 localScale = transform.localScale;
            // 부모의 전역 스케일 방향을 내 로컬 스케일에 반영하여 최종 월드 스케일이 항상 양수가 되게 함
            float parentGlobalX = transform.parent.lossyScale.x;
            localScale.x = Mathf.Abs(localScale.x) * (parentGlobalX < 0 ? -1 : 1);
            transform.localScale = localScale;
        }
    }

    public void HPBarUpdate()
    {
        hpBar.fillAmount = stats.CURHP / stats.MAXHP;
    }
}
