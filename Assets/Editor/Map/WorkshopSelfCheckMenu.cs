using UnityEditor;

// Tools/Level Workshop/자동 점검: enters play mode in the room workshop and runs the self check there.
public static class WorkshopSelfCheckMenu
{
    [MenuItem("Tools/Level Workshop/자동 점검 (Self Check)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("자동 점검", "플레이 모드를 끄고, 컴파일이 끝난 뒤 실행해 주세요.", "확인");
            return;
        }
        SessionState.SetBool("WorkshopSelfCheck.Pending", true);
        if (!EditorApplication.ExecuteMenuItem("Tools/Level Workshop/Open in Play Mode"))
            SessionState.SetBool("WorkshopSelfCheck.Pending", false);
    }
}
