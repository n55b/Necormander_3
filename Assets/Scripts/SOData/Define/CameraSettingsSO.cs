using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 게임플레이 씬 카메라가 공유하는 설정.
/// 에셋 위치: Assets/Resources/CameraSettings.asset (Resources 이름 "CameraSettings")
///
/// 이 에셋 값을 바꾸면 열려 있는 모든 씬 카메라에 반영된다 (에디터/플레이 모두).
/// 씬이 로드될 때도 자동 적용되므로 씬마다 카메라를 따로 설정할 필요가 없다.
/// 적용 로직: CameraSettingsApplier (런타임) / CameraSettingsApplierEditor (에디터)
///
/// 월드와 UI는 PixelArtUpscaleRig의 640×360 프레임에서 함께 그린다.
/// </summary>
[CreateAssetMenu(fileName = "CameraSettings", menuName = "Settings/Camera Settings")]
public class CameraSettingsSO : ScriptableObject
{
    public const string ResourcesName = "CameraSettings";

    [Header("Lens (CinemachineCamera)")]
    [Tooltip("화면에 보이는 세로 절반 크기(월드 유닛). 아무 값이나 사용 가능.")]
    [Min(0.01f)] public float orthographicSize = 5.625f;

    [Header("Pixel Art")]
    [Tooltip("아트 원본의 PPU. 이 값에 맞는 렌즈 크기는 Sunyunie Panel에서 적용할 수 있습니다.")]
    [Min(1)] public int assetsPPU = 32;
    [Tooltip("빌드에 포함할 정수 확대 셰이더 (Hidden/PixelArt/IntegerUpscale).")]
    public Shader upscaleShader;

    [Header("벡터 이펙트")]
    public Shader vectorShapeShader;
    [Range(1, 3)] public int vectorIndicatorStrokePixels = 1;
    [Range(1, 3)] public int vectorTelegraphStrokePixels = 1;
    public Color vectorIndicatorStroke = new Color32(50, 60, 57, 255);
    [Min(4)] public int playerLocatorPixels = 30;
    [Min(4)] public int enemyLocatorPixels = 30;
    [Min(4)] public int offscreenArrowPixels = 18;
    [Min(0)] public int offscreenArrowInsetPixels = 22;

    [Header("연속 명중 줌")]
    [Range(0, 18)] public int comboZoomPixelsPerHit = 2;
    [Range(0, 40)] public int comboZoomMaxPixels = 6;
    [Min(.05f)] public float comboZoomHalfLife = .85f;

    [Header("표시 이동 단위")]
    public PixelMotionStep playerMotion = PixelMotionStep.Continuous;
    public PixelMotionStep cameraMotion = PixelMotionStep.Continuous;
    [Header("월드 분위기")]
    [Range(0, .65f)] public float edgeMistOpacity = .34f;
    [Range(0, 48)] public int edgeMistWidth = 44;
    [Range(0, 2)] public float edgeMistSpeed = .65f;
    [Range(0, 1)] public float mistStrength = .26f;
    [Range(0, 2)] public float bloomStrength = .94f;
    [Range(0f, 1f)] public float playerPoseStrength = .36f;
    [Range(0f, 1f)] public float enemyPoseStrength = .8f;
    [Range(0f, 1f)] public float singleEnemyContactRadius = .42f;
    [Range(0f, .5f)] public float contactKnockbackBonus = .15f;
    [Range(0f, 5f)] public float enemyRecoilPixels = 3;
    [Range(0f, 1.5f)] public float hudMotionStrength = 1;
    [Range(0f, .5f)] public float grazeDuration = .28f;
    [Range(.05f, 1f)] public float grazeTimeScale = .38f;
    [Range(.03f, .35f)] public float grazeMargin = .27f;
    [Range(.3f, 3f)] public float grazeCooldown = 1.4f;
    [Range(0f, 1.2f)] public float finalBlowDuration = .72f;
    [Range(.05f, 1f)] public float finalBlowTimeScale = .15f;
    [Range(.65f, 1.6f)] public float occlusionMistSize = 1;
    [Range(0, 1)] public float groundShadowStrength = .60f;
    [Range(-180, 180)] public float groundShadowAngle = -32f;
    [Range(.1f, 1.2f)] public float groundShadowLength = .56f;
    [Range(0, 3)] public int canopyWindPixels = 2;
    public PixelMotionStep enemyMotion = PixelMotionStep.Continuous;
    public PixelMotionStep summonMotion = PixelMotionStep.Continuous;

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

public enum PixelMotionStep
{
    [InspectorName("소수점 이동")] Continuous = 0,
    [InspectorName("¼ 픽셀")] Quarter = 4,
    [InspectorName("½ 픽셀")] Half = 2,
    [InspectorName("1 픽셀")] Whole = 1
}
