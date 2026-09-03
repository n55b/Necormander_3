using UnityEngine;

/// <summary>DEMO_BUILD로 만든 실행 파일은 매 실행마다 모든 진행 데이터를 초기화한다.</summary>
public static class DemoDataReset
{
#if DEMO_BUILD && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetOnLaunch()
    {
        SaveSystem.DeleteSave();
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[DemoDataReset] 데모 실행 시작 — 저장 데이터와 PlayerPrefs를 초기화했습니다.");
    }
#endif
}
