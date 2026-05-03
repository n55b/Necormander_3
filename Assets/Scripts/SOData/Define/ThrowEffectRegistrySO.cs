using UnityEngine;

/// <summary>
/// 투척 카테고리별 시각 효과(VFX) 및 장판 프리팹을 관리하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowEffectRegistry", menuName = "Necromancer/Registry/ThrowEffectRegistry")]
public class ThrowEffectRegistrySO : ScriptableObject
{
    [Header("CC (사제/슬로우)")]
    public GameObject ccAreaPrefab;      // 광역 장판
    public GameObject ccAttachVFX;       // 대상 부착형 (Target/Self 공용)

    [Header("Shield (방패병/보호막)")]
    public GameObject shieldAreaPrefab;  // 광역 장판
    public GameObject shieldAttachVFX;   // 대상 부착형 (Target/Self 공용)

    [Header("Formation (창병/넉백)")]
    public GameObject formationAreaVFX;  // 충격 지점 (Area/Target/Self 모두 장판형으로 사용)

    [Header("Basic Impact (기본 데미지 장판)")]
    public GameObject basicAreaVFX;      // 아무 효과 없이 데미지만 주는 장판 (궁수 등)
}
