// =====================================================================
// CinemachineScreenPixelSnap
// ---------------------------------------------------------------------
// [역할]
//   Cinemachine이 계산한 카메라의 최종 위치를 "화면 픽셀" 격자에 맞춰 반올림합니다.
//
// [왜 필요한가]
//   해상도가 큰 모니터에서 카메라가 픽셀 경계가 아닌 위치(예: y = 1.7267)에 있으면
//   타일 경계가 화면 픽셀 중간에 걸려 타일 사이에 1픽셀 틈(선)이 보입니다.
//   카메라 위치를 화면 픽셀 단위로 맞추면 타일 경계가 항상 픽셀 경계에 떨어져 틈이 사라집니다.
//
// [Upscale Render Texture 방식과의 차이]
//   Pixel Perfect Camera의 Grid Snapping = Upscale Render Texture 로도 틈은 없어지지만,
//   그 방식은 아트 픽셀(1/32 유닛) 단위로만 움직여 큰 화면에서 움직임이 끊겨 보입니다.
//   이 스크립트는 화면 픽셀(1 / (PPU x 배율) 유닛) 단위로 맞추므로 움직임이 부드럽습니다.
//
// [사용법]
//   - 타일맵이 있는 씬의 CinemachineCamera 오브젝트에 붙입니다.
//     (Add Component > Cinemachine > Extensions > Screen Pixel Snap)
//   - 현재 VillageScene, BattleScene 에 적용되어 있습니다.
//   - Main Camera의 Pixel Perfect Camera는 Grid Snapping = None 이어야 합니다.
//   - pixelPerfectCamera 칸을 비워두면 Main Camera에서 자동으로 찾습니다.
//
// [함께 맞춰둔 설정] (끊김/떨림 방지)
//   - Main Camera > Cinemachine Brain > Update Method = LateUpdate
//   - Player Melee 프리팹 > Rigidbody2D > Interpolate = Interpolate
// =====================================================================

using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 카메라 최종 위치를 "화면 픽셀" 격자에 맞춥니다.
/// Pixel Perfect Camera(Grid Snapping = None)와 함께 쓰면
/// 타일 경계가 항상 화면 픽셀 경계에 떨어져 타일 틈이 생기지 않고,
/// 움직임은 화면 픽셀 단위(아트 픽셀보다 훨씬 촘촘)로 부드럽게 유지됩니다.
/// CinemachineCamera에 붙이고, 다른 확장(Impulse 등)보다 아래에 두세요.
/// </summary>
[AddComponentMenu("Cinemachine/Extensions/Screen Pixel Snap")]
[ExecuteAlways]
[SaveDuringPlay]
public class CinemachineScreenPixelSnap : CinemachineExtension
{
    [Tooltip("비워두면 Main Camera의 Pixel Perfect Camera를 자동으로 찾습니다.")]
    public PixelPerfectCamera pixelPerfectCamera;

    Camera _cam;

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
        ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (!Resolve()) return;

        int ratio = Mathf.Max(1, pixelPerfectCamera.pixelRatio);
        float unitsPerScreenPixel = 1f / (pixelPerfectCamera.assetsPPU * ratio);

        // 화면 왼쪽/아래 가장자리가 픽셀 경계에 오도록 맞춤 (화면 크기가 홀수여도 안전)
        float halfW = _cam.pixelWidth * 0.5f * unitsPerScreenPixel;
        float halfH = _cam.pixelHeight * 0.5f * unitsPerScreenPixel;

        Vector3 p = state.GetFinalPosition();
        float left = Mathf.Round((p.x - halfW) / unitsPerScreenPixel) * unitsPerScreenPixel;
        float bottom = Mathf.Round((p.y - halfH) / unitsPerScreenPixel) * unitsPerScreenPixel;
        Vector3 snapped = new Vector3(left + halfW, bottom + halfH, p.z);

        state.PositionCorrection += snapped - p;
    }

    bool Resolve()
    {
        if (pixelPerfectCamera == null)
        {
            var main = Camera.main;
            if (main != null) pixelPerfectCamera = main.GetComponent<PixelPerfectCamera>();
        }
        if (pixelPerfectCamera == null) return false;
        if (_cam == null) _cam = pixelPerfectCamera.GetComponent<Camera>();
        return _cam != null && pixelPerfectCamera.assetsPPU > 0;
    }
}
