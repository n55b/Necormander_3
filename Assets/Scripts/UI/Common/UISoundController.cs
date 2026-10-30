using UnityEngine;

/// <summary>
/// UIEventBus 의 창 열림/닫힘 신호를 받아 'UI 소리 설정'(Resources/UI/UISoundSettings.asset)대로 소리를 낸다.
/// 소리 값은 Tools > 사운드 > 사운드 테스트 툴 → [UI 소리] 탭에서 고친다.
///
/// 핵심 규칙:
///   SetReady()가 호출되기 전(게임 로딩 중)에는 모든 사운드를 차단합니다.
///   GameManager에서 플레이어 스폰 완료 후 UISoundController.SetReady() 호출 필요.
/// </summary>
public class UISoundController : MonoBehaviour
{
    public static UISoundController Instance;

    // 로딩 중 차단 플래그 — GameManager.SetReady() 전까지 false
    private bool _ready;

    /// <summary>GameManager에서 플레이어 스폰 완료 후 호출합니다.</summary>
    public static void SetReady()
    {
        if (Instance != null) Instance._ready = true;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        _ready = false; // 로딩 완료 전까지 차단
    }

    private void OnEnable()
    {
        UIEventBus.OnUIOpen  += OnUIOpen;
        UIEventBus.OnUIClose += OnUIClose;
    }

    private void OnDisable()
    {
        UIEventBus.OnUIOpen  -= OnUIOpen;
        UIEventBus.OnUIClose -= OnUIClose;
    }

    private void OnUIOpen(string uiName)  => Play(uiName, true);
    private void OnUIClose(string uiName) => Play(uiName, false);

    private void Play(string uiName, bool open)
    {
        if (!_ready || SoundManager.Instance == null) return;
        var settings = UISoundSettings.Active;
        if (settings != null) settings.PlayWindow(uiName, open);
    }
}
