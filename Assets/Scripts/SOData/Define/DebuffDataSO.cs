using UnityEngine;

[CreateAssetMenu(fileName = "DebuffData", menuName = "Necromancer/Registry/DebuffData")]
public class DebuffDataSO : ScriptableObject
{
    public Sprite poisonIcon;
    public Sprite chillIcon;
    public Sprite executeIcon;
    public Sprite bloodPopIcon;
    public Sprite agingIcon;
    public Sprite vulnerabilityIcon; // [추가] 취약
    public Sprite corrodedIcon;
    public Sprite fearedIcon; // [추가]
    public Sprite frozenIcon;
    public Sprite stunnedIcon;
    public Sprite senilityIcon;
    public Sprite bloodPopVulnerableIcon;
    public Sprite poisonHostIcon; // [추가]
    // ... 추가 가능

    // 타입에 맞는 스프라이트를 반환하는 함수 (스택형)
    public Sprite GetSprite(DebuffStackType type)
    {
        Sprite sprite = type switch
        {
            DebuffStackType.Poison => poisonIcon,
            DebuffStackType.Chill => chillIcon,
            DebuffStackType.Execute => executeIcon,
            DebuffStackType.BloodPop => bloodPopIcon,
            DebuffStackType.Aging => agingIcon,
            DebuffStackType.Vulnerability => vulnerabilityIcon, // [추가]
            _ => null
        };

        if (sprite == null)
        {
            Debug.LogWarning($"<color=red>[DebuffData]</color> Sprite not found for StackType: {type}. (Enum index mismatch?)");
        }
        return sprite;
    }

    // [추가] 타입에 맞는 스프라이트를 반환하는 함수 (상태형)
    public Sprite GetSprite(DebuffBoolType type)
    {
        return type switch
        {
            DebuffBoolType.Corroded => corrodedIcon,
            DebuffBoolType.Feared => fearedIcon, // [추가]
            DebuffBoolType.Frozen => frozenIcon,
            DebuffBoolType.Stunned => stunnedIcon,
            DebuffBoolType.Senility => senilityIcon,
            DebuffBoolType.BloodPopVulnerable => bloodPopVulnerableIcon,
            DebuffBoolType.PoisonHost => poisonHostIcon,
            _ => null
        };
    }
}
