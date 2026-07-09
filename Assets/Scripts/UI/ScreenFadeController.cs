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
    public static ScreenFadeController Instance { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image fadeImage;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
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
