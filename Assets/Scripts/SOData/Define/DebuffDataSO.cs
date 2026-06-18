using UnityEngine;

[CreateAssetMenu(fileName = "DebuffData", menuName = "Necromancer/Registry/DebuffData")]
public class DebuffDataSO : ScriptableObject
{
    public Sprite vulnerabilityIcon;
    public Sprite bloodPopIcon;
    public Sprite bleedIcon;
    public Sprite woundIcon;
    public Sprite corrosionIcon;
    public Sprite fractureIcon;
    
    public Sprite stunnedIcon;

    // 타입에 맞는 스프라이트를 반환하는 함수 (스택형)
    public Sprite GetSprite(DebuffStackType type)
    {
        Sprite sprite = type switch
        {
            DebuffStackType.Vulnerability => vulnerabilityIcon,
            DebuffStackType.BloodPop => bloodPopIcon,
            DebuffStackType.Bleed => bleedIcon,
            DebuffStackType.Wound => woundIcon,
            DebuffStackType.Corrosion => corrosionIcon,
            DebuffStackType.Fracture => fractureIcon,
            _ => null
        };

        if (sprite == null)
        {
            Debug.LogWarning($"<color=red>[DebuffData]</color> Sprite not found for StackType: {type}. (Enum index mismatch?)");
        }
        return sprite;
    }

    // 타입에 맞는 스프라이트를 반환하는 함수 (상태형)
    public Sprite GetSprite(DebuffBoolType type)
    {
        return type switch
        {
            DebuffBoolType.Stunned => stunnedIcon,
            DebuffBoolType.Bleeding => bleedIcon,
            DebuffBoolType.Wounded => woundIcon,
            DebuffBoolType.Corroded => corrosionIcon,
            DebuffBoolType.Fractured => fractureIcon,
            _ => null
        };
    }
}
