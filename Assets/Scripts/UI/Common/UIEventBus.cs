using System;

/// <summary>
/// UI 열림/닫힘 이벤트 버스.
/// Toggle() 방식 UI에서 UIEventBus.NotifyOpen/Close 한 줄만 추가하면
/// UISoundController가 자동으로 구독해서 사운드를 재생합니다.
/// </summary>
public static class UIEventBus
{
    /// <summary>UI가 열릴 때 발생. string = UI 식별 이름 (예: "GemTree", "Reward")</summary>
    public static event Action<string> OnUIOpen;

    /// <summary>UI가 닫힐 때 발생.</summary>
    public static event Action<string> OnUIClose;

    public static void NotifyOpen(string uiName)  => OnUIOpen?.Invoke(uiName);
    public static void NotifyClose(string uiName) => OnUIClose?.Invoke(uiName);
}
