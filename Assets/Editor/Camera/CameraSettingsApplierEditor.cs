using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 에디터에서 CameraSettingsSO 를 열린 씬 카메라에 자동 반영한다.
///  - 에디터 시작 / 스크립트 리로드 / 씬 열기 시 적용
///  - CameraSettings 에셋 값을 바꾸면 즉시 적용
/// 씬 파일에는 따로 저장하지 않아도 된다 (열 때마다, 플레이할 때마다 다시 적용됨).
/// </summary>
[InitializeOnLoad]
static class CameraSettingsApplierEditor
{
    static CameraSettingsApplierEditor()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        CameraSettingsSO.Changed -= ApplyAll;
        CameraSettingsSO.Changed += ApplyAll;
        EditorApplication.delayCall += ApplyAll;
    }

    static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        => CameraSettingsApplier.ApplyToScene(scene);

    static void ApplyAll()
    {
        CameraSettingsApplier.ApplyToAllLoadedScenes();
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    [MenuItem("Tools/Camera/Select Camera Settings")]
    static void SelectSettings()
    {
        var s = CameraSettingsSO.Instance;
        if (s == null)
        {
            Debug.LogWarning($"[CameraSettings] Resources/{CameraSettingsSO.ResourcesName}.asset 을 찾을 수 없습니다.");
            return;
        }
        Selection.activeObject = s;
        EditorGUIUtility.PingObject(s);
    }

    [MenuItem("Tools/Camera/Apply Camera Settings To Open Scenes")]
    static void ApplyMenu() => ApplyAll();
}
