using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 머리 위, 체력바 바로 옆/위에 얹히는 카운터 게이지(파훼 게이지) 바.
/// WorldHPBar 와 동일한 배치 원칙(회전 고정, 부모 Flip 스케일 보정)을 따른다.
/// BossCounterGauge 가 열려 있을 때만 나타나고, 닫히면 자동으로 숨는다.
/// </summary>
public class CounterGaugeBarUI : MonoBehaviour
{
    [SerializeField] private GameObject root;      // 켜고 끌 패널(비어 있으면 이 오브젝트 자신을 씀)
    [SerializeField] private Image fillImage;       // Filled / Horizontal
    [SerializeField] private BossCounterGauge gauge; // 비워두면 부모에서 자동 탐색

        private void Awake()
    {
        if (gauge == null) gauge = GetComponentInParent<BossCounterGauge>();
        if (fillImage == null) fillImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        if (gauge != null) gauge.OnGaugeChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (gauge != null) gauge.OnGaugeChanged -= Refresh;
    }

    private void Refresh()
    {
        bool show = gauge != null && gauge.IsOpen;
        GameObject target = root != null ? root : gameObject;
        if (target.activeSelf != show) target.SetActive(show);
        if (fillImage != null && gauge != null) fillImage.fillAmount = gauge.Ratio;
    }

    private void LateUpdate()
    {
        // WorldHPBar 와 동일: 부모가 돌아가도 항상 수평 유지
        transform.rotation = Quaternion.identity;

        if (transform.parent != null)
        {
            Vector3 localScale = transform.localScale;
            float parentGlobalX = transform.parent.lossyScale.x;
            localScale.x = Mathf.Abs(localScale.x) * (parentGlobalX < 0 ? -1 : 1);
            transform.localScale = localScale;
        }
    }
}
