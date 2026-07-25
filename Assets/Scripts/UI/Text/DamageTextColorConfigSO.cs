using UnityEngine;

/// <summary>
/// 데미지 팝업 텍스트의 색상을 인스펙터에서 공용으로 관리하기 위한 설정입니다.
/// 하나의 에셋을 여러 캐릭터(Enemy, Ally 등)의 FloatingTextSpawner가 공유해서 참조합니다.
/// </summary>
[CreateAssetMenu(fileName = "DamageTextColorConfig", menuName = "Necromancer/UI/DamageTextColorConfig")]
public class DamageTextColorConfigSO : ScriptableObject
{
    [Header("데미지 타입별 색상")]
    [Tooltip("물리 공격 데미지 (평타 등 대부분의 기본 공격). ATK 기반")]
    public Color physicalColor = Color.white;
    [Tooltip("마법 공격 데미지. MAGIC 기반. 적은 마법사 계열이 이 색으로 뜬다")]
    public Color magicColor = new Color(0.6f, 0.4f, 1f);
    [Tooltip("어느 상태이상에도 속하지 않는 고정 피해. 방어력을 무시합니다(쉴드는 못 뚫습니다)")]
    public Color fixedColor = Color.cyan;
    [Tooltip("빙결이 깨질 때 터지는 고정 피해의 색상")]
    public Color freezeColor = new Color(0.4f, 0.85f, 1f);
    [Tooltip("중독 틱(초당) 고정 피해의 색상")]
    public Color poisonColor = Color.green;
    [Tooltip("비폭(BloodPop) 10스택이 터질 때의 폭발 데미지 색상")]
    public Color bloodPopColor = Color.yellow;
    [Tooltip("출혈(Bleed) 상태에서 피격 시 추가로 들어가는 고정 피해의 색상")]
    public Color bleedColor = Color.red;

    [Header("특수 팝업 색상 (팝업 문자열로 강제 지정되는 경우)")]
    [Tooltip("쉴드가 데미지를 대신 막아냈을 때(팝업 문자열이 'Shield'인 경우) 표시되는 색상")]
    public Color shieldColor = Color.grey;
    [Tooltip("공격이 빗나갔을 때(회피, 팝업 문자열이 'MISS'인 경우) 표시되는 색상")]
    public Color missColor = Color.gray;

    [Header("기타")]
    [Tooltip("아군(Army 레이어)이 피해를 입었을 때 데미지 타입과 무관하게 적용되는 색상")]
    public Color allyHitColor = Color.red;
    [Tooltip("회복(힐)을 받았을 때 '+숫자' 형태로 표시되는 색상")]
    public Color healColor = Color.green;

    [Header("상태 알림 텍스트")]
    [Tooltip("아래 강조 텍스트에 해당하지 않는, 그 외 상태 알림 텍스트의 기본 색상")]
    public Color statusTextColor = Color.gray;

    [Tooltip("'기절!' 같은 강조할 상태이상 텍스트의 색상")]
    [UnityEngine.Serialization.FormerlySerializedAs("vulnerabilityPopColor")]
    public Color statusPopColor = Color.gray;
    [Tooltip("'빙결'과 관련된 상태이상 텍스트의 색상")]
    public Color freezeTextColor = Color.blue;

    [Tooltip("강조 상태이상 텍스트의 팝업 크기 배율. 1보다 크게 하면 더 눈에 띄게 표시됩니다")]
    [UnityEngine.Serialization.FormerlySerializedAs("vulnerabilityPopScale")]
    public float statusPopScale = 1.4f;

    /// <summary>크게 띄울 상태이상 팝업인지 여부.</summary>
    public bool IsStatusPop(string statusName)
    {
        return !string.IsNullOrEmpty(statusName) && statusName.Contains("기절");
    }

    public Color GetStatusPopColor(string statusName)
    {
        if (string.IsNullOrEmpty(statusName)) return statusTextColor;
        return IsStatusPop(statusName) ? statusPopColor : statusTextColor;
    }

    public Color GetDamageColor(DamageType type)
    {
        switch (type)
        {
            case DamageType.Physical: return physicalColor;
            case DamageType.Magic: return magicColor;
            case DamageType.Fixed: return fixedColor;
            case DamageType.Freeze: return freezeColor;
            case DamageType.Poison: return poisonColor;
            case DamageType.BloodPop: return bloodPopColor;
            case DamageType.Bleed: return bleedColor;
            default: return physicalColor;
        }
    }
}
