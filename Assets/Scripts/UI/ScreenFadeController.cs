using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 전체를 덮는 검은색 페이드 연출을 제공하는 싱글턴 컨트롤러입니다.
/// 텔레포트, 씬 전환 등 다른 곳에서도 재사용할 수 있습니다.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFadeController : MonoBehaviour
{
    private static ScreenFadeController _instance;

    /// <summary>없으면 스스로 만들어서 돌려준다. (프리팹이 BattleScene에만 배치돼 있어서
    /// 마을 등 다른 씬에선 null이라 화면 페이드가 조용히 안 되던 문제를 없앤다.)</summary>
    public static ScreenFadeController Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying) CreateOverlay();
            return _instance;
        }
    }

    /// <summary>검은 커튼 그 자체. Fader가 이걸 직접 페이드시킨다.</summary>
    public CanvasGroup Group => canvasGroup;

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image fadeImage;

    private void Awake()
    {
        // 주의: 여기서 프로퍼티 Instance를 쓰면 getter가 또 하나를 만들어버린다. 반드시 _instance를 직접 본다.
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>화면 전체를 덮는 검은 커튼을 코드로 생성. 씬에 배치본이 있으면 그쪽이 먼저 Awake해서 여긴 안 탄다.</summary>
    private static void CreateOverlay()
    {
        GameObject go = new GameObject("ScreenFadeCanvas(Auto)");

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999; // 항상 최상단

        GameObject imgObj = new GameObject("FadeImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imgObj.transform.SetParent(go.transform, false);
        RectTransform rt = imgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero; // 화면 꽉 채우기

        Image img = imgObj.GetComponent<Image>();
        img.color = Color.black;

        var ctrl = go.AddComponent<ScreenFadeController>(); // Awake가 여기서 돌며 _instance를 잡는다
        ctrl.fadeImage = img;
    }

    /// <summary>커튼 색 바꾸기 (흰 페이드/붉은 페이드용).</summary>
    public void SetColor(Color color)
    {
        if (fadeImage != null) fadeImage.color = color;
    }

    /// <summary>
    /// 화면을 어둡게(alpha 1) 했다가 다시 밝게(alpha 0) 되돌립니다.
    /// 완전히 어두워진 시점(암전 상태)에서 onBlackout 콜백을 실행합니다 (예: 텔레포트 위치 이동).
    /// </summary>
    public void FadeOutIn(float fadeOutDuration, float holdDuration, float fadeInDuration, Action onBlackout)
    {
        StartCoroutine(FadeOutInRoutine(fadeOutDuration, holdDuration, fadeInDuration, onBlackout));
    }

    private IEnumerator FadeOutInRoutine(float fadeOutDuration, float holdDuration, float fadeInDuration, Action onBlackout)
    {
        yield return StartCoroutine(FadeTo(1f, fadeOutDuration));

        onBlackout?.Invoke();

        if (holdDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(holdDuration);
        }

        yield return StartCoroutine(FadeTo(0f, fadeInDuration));
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        if (duration <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
            yield break;
        }

        while (elapsed < duration)
        {
            // 텔레포트 중 시간 정지(Time.timeScale = 0) 상태에서도 페이드가 진행되도록 unscaledDeltaTime 사용
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }
}
