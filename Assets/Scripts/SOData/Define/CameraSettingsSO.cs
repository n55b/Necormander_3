using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 게임플레이 씬 카메라가 공유하는 설정.
/// 에셋 위치: Assets/SOData/Camera/Resources/CameraSettings.asset (Resources 이름 "CameraSettings")
///
/// 이 에셋 값을 바꾸면 열려 있는 모든 씬 카메라에 반영된다 (에디터/플레이 모두).
/// 씬이 로드될 때도 자동 적용되므로 씬마다 카메라를 따로 설정할 필요가 없다.
/// 적용 로직: CameraSettingsApplier (런타임) / CameraSettingsApplierEditor (에디터)
///
/// 렌더링: PixelArtUpscaleRig (저해상도 렌더 + 서브픽셀 보정 + sharp bilinear 업스케일).
///   → Orthographic Size 를 아무 값이나 써도 픽셀 두께 균일 · 타일 틈 없음 · 이동 부드러움.
/// 적용 대상: CinemachineBrain 이 붙은 카메라가 있는 씬.
/// </summary>
[CreateAssetMenu(fileName = "CameraSettings", menuName = "Settings/Camera Settings")]
public class CameraSettingsSO : ScriptableObject
{
    public const string ResourcesName = "CameraSettings";

    [Header("Lens (CinemachineCamera)")]
    [Tooltip("화면에 보이는 세로 절반 크기(월드 유닛). 아무 값이나 사용 가능.")]
    [Min(0.01f)] public float orthographicSize = 7f;

    [Header("Pixel Art")]
    [Tooltip("스프라이트/타일의 Pixels Per Unit. 저해상도 렌더 텍스처 1픽셀 = 아트 1픽셀.")]
    [Min(1)] public int assetsPPU = 32;
    [Tooltip("업스케일 셰이더 (Hidden/PixelArt/SharpUpscale). 빌드에 포함되도록 여기서 참조한다.")]
    public Shader upscaleShader;

    [Header("제외")]
    [Tooltip("여기 적은 씬 이름에는 적용하지 않는다.")]
    public List<string> excludedScenes = new List<string>();

    /// <summary>값이 바뀌었을 때 (인스펙터 수정 등) 발생.</summary>
    public static event Action Changed;

    static CameraSettingsSO _instance;
    public static CameraSettingsSO Instance
    {
        get
        {
            if (_instance == null) _instance = Resources.Load<CameraSettingsSO>(ResourcesName);
            return _instance;
        }
    }

    void OnEnable()
    {
        if (_instance == null) _instance = this;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // OnValidate 안에서 다른 오브젝트를 바로 수정하면 경고가 나므로 한 틱 미룬다.
        UnityEditor.EditorApplication.delayCall -= RaiseChanged;
        UnityEditor.EditorApplication.delayCall += RaiseChanged;
    }
#endif

    static void RaiseChanged() => Changed?.Invoke();
}
