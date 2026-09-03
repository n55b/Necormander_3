using UnityEngine;

/// <summary>
/// 튜토리얼을 한 번이라도 끝냈는가, 그리고 지금 로드된 씬이 튜토리얼인가.
///
/// 완료 여부는 런 세이브(SaveSystem)가 아니라 <b>PlayerPrefs</b> 에 남는다 — 우클릭 해금과 같은
/// 취급이다. '새 게임'이 세이브를 지워도 튜토리얼을 두 번 보지는 않는다.
/// </summary>
public static class TutorialFlow
{
    private const string Key = "TutorialCompleted";

    /// <summary>지금 짓는 맵이 튜토리얼인가. 씬을 넘어 살아남아야 해서 static 이다 —
    /// 튜토리얼 도중 죽어서 씬을 다시 로드해도 튜토리얼 맵으로 돌아와야 한다.</summary>
    public static bool IsRunning { get; set; }

    public static bool Completed => PlayerPrefs.GetInt(Key, 0) != 0;

    public static void Begin() => IsRunning = true;

    /// <summary>튜토리얼을 끝까지 봤다. 다음 '새 게임'부터는 바로 마을로 간다.</summary>
    public static void Complete()
    {
        IsRunning = false;
        PlayerPrefs.SetInt(Key, 1);
        PlayerPrefs.Save();
    }

    /// <summary>완료 기록을 지워 튜토리얼을 다시 보게 한다(테스트용).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRunningState()
    {
        // 도메인 리로드를 끈 채로 플레이하면 static 이 지난 플레이의 값을 그대로 들고 있다.
        // 그 상태로 BattleScene 을 직접 재생하면 난데없이 튜토리얼 맵이 지어진다.
        IsRunning = false;
    }

    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
        Debug.Log("<color=yellow>[TutorialFlow]</color> 튜토리얼 완료 기록을 지웠다 — 다음 새 게임에서 다시 나온다.");
    }
}
