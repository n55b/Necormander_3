using UnityEngine;

/// <summary>
/// 릴리스 빌드에서 Debug.Log / LogWarning 출력을 끈다. 에러와 예외는 버그 리포트에 필요하므로 Player.log에 남긴다.
/// 에디터와 Development Build에서는 아무것도 하지 않는다.
/// </summary>
public static class ReleaseLogFilter
{
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Apply()
    {
        Debug.unityLogger.filterLogType = LogType.Error;
    }
#endif
}
