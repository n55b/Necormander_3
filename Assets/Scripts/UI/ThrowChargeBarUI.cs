using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 발밑에서 투척 차징 상태를 시각적으로 보여주는 UI 클래스입니다.
/// </summary>
public class ThrowChargeBarUI : Singleton<ThrowChargeBarUI>
{

    [Header("UI References")]
    [SerializeField] private GameObject visualPanel;
    [SerializeField] private Image fillImage;

    [Header("Color Settings")]
    [SerializeField] private Color normalColor = new Color(0.2f, 0.6f, 1.0f);
    [SerializeField] private Color fullChargeColor = new Color(0.6f, 0.9f, 1.0f);

    protected override void OnAwake()
    {
        if (visualPanel != null) visualPanel.SetActive(false);
    }

    private void LateUpdate()
    {
        if (visualPanel != null && visualPanel.activeSelf)
        {
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

    public void SetVisible(bool show)
    {
        if (visualPanel != null)
        {
            visualPanel.SetActive(show);
            if (show) UpdateCharge(0); // 켜질 때 0으로 초기화
        }
    }

    public void UpdateCharge(float ratio)
    {
        if (fillImage != null)
        {
            fillImage.fillAmount = ratio;
            //fillImage.color = (ratio >= 0.98f) ? fullChargeColor : normalColor; 이새끼 있으면 색이 안보임; 일단 제거
        }
    }
}
