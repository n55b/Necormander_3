using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// CameraSettingsSO 값을 씬 카메라에 적용한다 (Cinemachine 렌즈 크기 + 플레이 중 PixelArtUpscaleRig). 씬에 컴포넌트를 붙일 필요 없음.
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

    /// <returns>적용한 컴포넌트가 있으면 true</returns>
    /// <returns>적용한 컴포넌트가 있으면 true</returns>
    /// <returns>적용한 카메라가 있으면 true</returns>
    public static bool ApplyToScene(Scene scene)
    {
        var s = CameraSettingsSO.Instance;
        if (s == null || !scene.IsValid() || !scene.isLoaded) return false;
        if (s.excludedScenes.Contains(scene.name)) return false;

        var roots = scene.GetRootGameObjects();

        // CinemachineBrain 이 붙은 카메라가 있는 씬(= 게임플레이 카메라 구성)만 대상으로 한다.
        bool hasBrain = false;
        foreach (var root in roots)
        {
            foreach (var brain in root.GetComponentsInChildren<CinemachineBrain>(true))
            {
                hasBrain = true;

                // 업스케일 리그는 플레이 중에만 붙인다 (에디터 씬 파일을 건드리지 않도록).
                if (!Application.isPlaying) continue;
                var rig = brain.GetComponent<PixelArtUpscaleRig>();
                if (rig == null)
                {
                    rig = brain.gameObject.AddComponent<PixelArtUpscaleRig>();
                    rig.enabled = false;
                }
                rig.Configure(s.assetsPPU, s.upscaleShader);
                rig.enabled = true;
            }
        }
        if (!hasBrain) return false;

        foreach (var root in roots)
        {
            foreach (var vcam in root.GetComponentsInChildren<CinemachineCamera>(true))
            {
                var lens = vcam.Lens;
                lens.OrthographicSize = s.orthographicSize;
                vcam.Lens = lens;
            }
        }
        return true;
    }
}
