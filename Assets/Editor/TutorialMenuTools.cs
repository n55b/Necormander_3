using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 완료 기록(PlayerPrefs)을 손으로 만지는 메뉴.
///
/// <b>'첫 플레이인가'는 오직 이 기록 하나가 정한다</b> — 타이틀의 '새 게임' 버튼이 이걸 보고
/// 튜토리얼로 갈지 마을로 갈지 고른다.
///
/// 반복해서 튜토리얼을 돌려볼 거면 StartScene 의 MainMenuManager 에 있는 Debug Force Tutorial 을
/// 켜는 쪽이 편하다. 여기 메뉴는 기록 자체를 만지는 것이라, '두 번째 새 게임은 정말 마을로 가는가'
/// 처럼 기록의 동작 자체를 확인할 때 쓴다.
/// </summary>
public static class TutorialMenuTools
{
    [MenuItem("Tools/Tutorial/튜토리얼 다시 보기 (완료 기록 삭제)")]
    private static void ResetTutorial()
    {
        TutorialFlow.ResetProgress();
        Report();
    }

    [MenuItem("Tools/Tutorial/튜토리얼 건너뛰기 (봤음으로 표시)")]
    private static void CompleteTutorial()
    {
        TutorialFlow.Complete();
        Report();
    }

    private static void Report()
    {
        Debug.Log(TutorialFlow.Completed
            ? "<color=yellow>[Tutorial]</color> 완료 기록 <b>있음</b> — 새 게임을 누르면 마을로 바로 간다."
            : "<color=cyan>[Tutorial]</color> 완료 기록 <b>없음</b> — 새 게임을 누르면 튜토리얼부터 시작한다.");
    }
}
