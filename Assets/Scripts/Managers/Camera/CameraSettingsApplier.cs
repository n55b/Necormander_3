using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 공용 카메라 설정에서 플레이 씬의 Cinemachine 렌즈 크기를 적용한다.
///  - 플레이: 씬 로드 시 자동 적용 + 에셋 값 변경 시 즉시 재적용
///  - 에디터: CameraSettingsApplierEditor 가 씬 열기 / 에셋 변경 시 호출
/// </summary>
public static class CameraSettingsApplier
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        CameraSettingsSO.Changed -= ApplyToAllLoadedScenes;
        CameraSettingsSO.Changed += ApplyToAllLoadedScenes;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToScene(scene);

    public static void ApplyToAllLoadedScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            ApplyToScene(SceneManager.GetSceneAt(i));
    }

    /// <returns>적용한 카메라가 있으면 true</returns>
    public static bool ApplyToScene(Scene scene)
    {
        var s = CameraSettingsSO.Instance;
        if (s == null || !scene.IsValid() || !scene.isLoaded) return false;
        if (s.excludedScenes.Contains(scene.name)) return false;

        var roots = scene.GetRootGameObjects();

        bool applied = false;
        foreach (var root in roots)
        {
            foreach (var vcam in root.GetComponentsInChildren<CinemachineCamera>(true))
            {
                var lens = vcam.Lens;
                lens.OrthographicSize = s.orthographicSize;
                vcam.Lens = lens;
                applied = true;
            }
        }
        return applied;
    }
}
