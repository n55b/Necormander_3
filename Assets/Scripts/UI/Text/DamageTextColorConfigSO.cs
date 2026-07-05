using UnityEngine;

/// <summary>
/// 데미지 팝업 텍스트의 색상을 인스펙터에서 공용으로 관리하기 위한 설정입니다.
/// 하나의 에셋을 여러 캐릭터(Enemy, Ally 등)의 FloatingTextSpawner가 공유해서 참조합니다.
/// </summary>
[CreateAssetMenu(fileName = "DamageTextColorConfig", menuName = "Necromancer/UI/DamageTextColorConfig")]
public class DamageTextColorConfigSO : ScriptableObject
{
    [Header("데미지 타입별 색상")]
    [Tooltip("일반 물리 공격 데미지 (평타 등 대부분의 기본 공격)")]
    public Color physicalColor = Color.white;
    [Tooltip("방어력을 무시하는 고정 피해. 비폭/출혈/상처/부식/골절 폭발 데미지, 취약 소모(기절/강타) 데미지, 슈퍼아머 파괴 후 벽 충돌 데미지 등에 쓰입니다")]
    public Color fixedColor = Color.cyan;
    [Tooltip("비폭(BloodPop) 디버프 스택이 다른 속성 디버프에 의해 터질 때(폭발 데미지)의 색상")]
    public Color bloodPopColor = Color.yellow;
    [Tooltip("출혈(Bleed) 디버프가 터질 때(즉발 데미지) 및 이후 매초 도트 데미지의 색상")]
    public Color bleedColor = Color.red;
    [Tooltip("상처(Wound) 디버프가 터질 때(즉발 데미지)의 색상")]
    public Color woundColor = new Color(1f, 0.5f, 0f);
    [Tooltip("부식(Corrosion) 디버프가 터질 때(즉발 데미지)의 색상")]
    public Color corrosionColor = Color.green;
    [Tooltip("골절(Fracture) 디버프가 터질 때(즉발 데미지)의 색상. 이동속도 감소 디버프도 함께 부여됩니다")]
    public Color fractureColor = new Color(0.5f, 0f, 0.5f);

    [Header("특수 팝업 색상 (팝업 문자열로 강제 지정되는 경우)")]
    [Tooltip("쉴드가 데미지를 대신 막아냈을 때(팝업 문자열이 'Shield'인 경우) 표시되는 색상")]
    public Color shieldColor = Color.grey;
    [Tooltip("처형기(즉사/확정 처치기 등)로 마무리했을 때(팝업 문자열이 'Execution'인 경우) 표시되는 색상")]
    public Color executionColor = Color.yellow;
    [Tooltip("공격이 빗나갔을 때(회피, 팝업 문자열이 'MISS'인 경우) 표시되는 색상")]
    public Color missColor = Color.gray;

    [Header("기타")]
    [Tooltip("아군(Army 레이어)이 피해를 입었을 때 데미지 타입과 무관하게 적용되는 색상")]
    public Color allyHitColor = Color.red;
    [Tooltip("회복(힐)을 받았을 때 '+숫자' 형태로 표시되는 색상")]
    public Color healColor = Color.green;

    [Header("상태 알림 텍스트")]
    [Tooltip("아래 취약 소모 텍스트에 해당하지 않는, 그 외 상태 알림 텍스트의 기본 색상")]
    public Color statusTextColor = Color.gray;

    [Tooltip("'기절!'/'격파!'/'강타!' 등 취약 소모 시 뜨는 텍스트 색상 (셋 다 취약을 소모한다는 공통점으로 하나의 색을 공유합니다)")]
    public Color vulnerabilityPopColor = Color.gray;

    public Color GetStatusPopColor(string statusName)
    {
        if (string.IsNullOrEmpty(statusName)) return statusTextColor;

        if (statusName.Contains("기절") || statusName.Contains("격파") || statusName.Contains("강타"))
            return vulnerabilityPopColor;

        return statusTextColor;
    }

    public Color GetDamageColor(DamageType type)
    {
        switch (type)
        {
            case DamageType.Physical: return physicalColor;
            case DamageType.Fixed: return fixedColor;
            case DamageType.BloodPop: return bloodPopColor;
            case DamageType.Bleed: return bleedColor;
            case DamageType.Wound: return woundColor;
            case DamageType.Corrosion: return corrosionColor;
            case DamageType.Fracture: return fractureColor;
            default: return physicalColor;
        }
    }
}
