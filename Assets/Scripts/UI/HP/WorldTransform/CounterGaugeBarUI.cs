using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 머리 위, 체력바 바로 옆/위에 얹히는 카운터 게이지(파훼 게이지) 바.
/// WorldHPBar 와 동일한 배치 원칙(회전 고정, 부모 Flip 스케일 보정)을 따른다.
/// BossCounterGauge 가 열려 있을 때만 나타나고, 닫히면 자동으로 숨는다.
/// </summary>
public class CounterGaugeBarUI : MonoBehaviour
{
    [SerializeField] private GameObject root;      // 켜고 끌 패널(비어 있으면 Image 를 켜고 끈다)
    [SerializeField] private Image fillImage;       // Filled / Horizontal
    [SerializeField] private BossCounterGauge gauge; // 비워두면 자동 탐색

    [Tooltip("Image 에 스프라이트가 안 꽂혀 있을 때 대신 쓸 Resources 경로. Filled 타입은 스프라이트가 없으면 아무것도 안 그려진다.")]
    [SerializeField] private string fallbackSpriteResourcePath = "Sprites/UI/Generated/UI_HpbarWhite";

    private void Awake()
    {
        // [버그 수정 — 카운터 게이지 바가 절대 안 뜨던 문제 ①: 자동 탐색이 구조적으로 실패]
        // 보스 프리팹의 계층은
        //     루트 ├ CharacterStatStuff (CharacterHealth + BossCounterGauge)
        //          └ Canvas → CounterGaugeImage (이 스크립트)
        // 라서 게이지와 이 바는 '형제 가지'에 있다. GetComponentInParent 는
        // CounterGaugeImage → Canvas → 루트 만 훑으므로 BossCounterGauge 에 영원히 닿지 못했다.
        // 루트에서 아래로 훑는 경로를 폴백으로 더한다(BoneMasterController 가 게이지를 찾는 방식과 동일).
        if (gauge == null) gauge = GetComponentInParent<BossCounterGauge>();
        if (gauge == null) gauge = transform.root.GetComponentInChildren<BossCounterGauge>(true);

        if (fillImage == null) fillImage = GetComponent<Image>();

        // [버그 수정 ③] 프리팹의 Image 는 m_Type: 1(Filled) 인데 m_Sprite 가 비어 있다.
        // Filled 는 스프라이트가 없으면 fillAmount 를 아무리 넣어도 화면에 아무것도 안 그린다.
        if (fillImage != null && fillImage.sprite == null && !string.IsNullOrEmpty(fallbackSpriteResourcePath))
        {
            var fallback = Resources.Load<Sprite>(fallbackSpriteResourcePath);
            if (fallback != null) fillImage.sprite = fallback;
            else Debug.LogWarning($"[CounterGaugeBarUI] 폴백 스프라이트를 찾지 못했습니다: Resources/{fallbackSpriteResourcePath}");
        }
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

    /// <summary>
    /// [버그 수정 — 카운터 게이지 바가 절대 안 뜨던 문제 ②: 한 번 숨으면 되살아날 수 없었다]
    /// root 가 비어 있으면 예전엔 <b>자기 자신</b>을 SetActive(false) 했다. 그러면 OnDisable 이
    /// OnGaugeChanged 구독을 끊고, 비활성 오브젝트는 이벤트도 Update 도 받지 못하므로
    /// 게이지가 다시 열려도 Refresh 를 들을 수 없다 — 첫 Refresh 한 번으로 영구히 죽는 구조였다.
    /// 전용 root 패널이 지정된 경우에만 오브젝트를 껐다 켜고, 아니면 Image 만 껐다 켠다
    /// (컴포넌트는 계속 살아 있어 구독이 유지된다).
    /// </summary>
    private void Refresh()
    {
        bool show = gauge != null && gauge.IsOpen;

        if (root != null)
        {
            if (root.activeSelf != show) root.SetActive(show);
        }
        else if (fillImage != null)
        {
            if (fillImage.enabled != show) fillImage.enabled = show;
        }

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
