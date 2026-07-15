using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 전체를 덮는 '검은 커튼' 그 자체. 하는 일은 딱 하나 — "내가 그 커튼이다".
///
/// 페이드는 전혀 하지 않는다. 그건 오직 Fader의 일이다.
/// 역할이 이렇게 갈린다:
///   ScreenCurtain = 정체성과 수명 — 나는 그 커튼이고, 씬을 넘어 산다
///   Fader         = 행동 — 나는 뭔가를 페이드시킬 줄 안다
/// Fader는 아무 오브젝트에나 붙는 범용 컴포넌트라 싱글턴이 될 수 없어서 둘로 나뉜다.
///
/// 구체적으로 넷을 한다:
///   1. 표식 — Fader가 GetComponent&lt;ScreenCurtain&gt;()으로 "얘가 커튼이구나" 알아채고 화면 전체 모드로 잡는다
///   2. 씬을 넘어 살아남기 — 씬 전환 페이드는 로드 '중간'에 걸쳐 있어서, 안 살아남으면 페이드 인이 끊긴다
///   3. 중복 정리 — 다음 씬에도 커튼이 있으면 새 배치본이 물러난다(안 그러면 커튼이 두 겹)
///   4. 커튼 넘겨주기 — Group(Fader가 알파를 만지는 대상), SetColor(신호별 색)
///
/// [배치] 화면 페이드가 필요한 씬마다 ScreenFadeCanvas 프리팹을 하나씩 넣어둔다.
/// 없는 씬에선 화면 페이드가 그냥 안 나온다(대신 이동/로드는 정상 수행 — 검은 화면에 갇히지 않는다).
///
/// [주의] 이 오브젝트를 인스펙터 슬롯에 드래그해서 참조하지 마라.
/// DontDestroyOnLoad로 씬을 넘어 살아남기 때문에, 다음 씬에 있는 배치본은 Awake에서 스스로를 지운다.
/// 즉 어느 배치본이 살아남을지는 어느 씬에서 시작했냐에 따라 달라지고, 드래그해둔 참조는 파괴돼서
/// MissingReference로 죽는다. 반드시 Fader.FullScreenFader 로 '살아있는 놈'을 찾아 써라.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ScreenCurtain : MonoBehaviour
{
    private static ScreenCurtain _instance;

    /// <summary>지금 살아있는 커튼. 이 씬에 커튼을 안 넣어뒀으면 null이다.</summary>
    public static ScreenCurtain Instance => _instance;

    /// <summary>검은 커튼 그 자체. Fader가 이걸 직접 페이드시킨다.</summary>
    public CanvasGroup Group => canvasGroup;

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image fadeImage;

    private void Awake()
    {
        // 씬마다 하나씩 배치돼 있으므로, 씬을 넘어오면 먼저 살아남은 놈이 이미 있다. 그럼 새 배치본은 물러난다.
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject); // 씬 전환 페이드는 로드 '중간'에 걸쳐 있어서, 안 살아남으면 페이드 인이 끊긴다

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>커튼 색 바꾸기 (흰 페이드/붉은 페이드용). Fader가 신호별 색에 맞춰 불러준다.</summary>
    public void SetColor(Color color)
    {
        if (fadeImage != null) fadeImage.color = color;
    }
}
