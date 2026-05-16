using UnityEngine;

[CreateAssetMenu(fileName = "DebuffData", menuName = "Necromancer/Registry/DebuffData")]
public class DebuffDataSO : ScriptableObject
{
    public Sprite poisonIcon;
    public Sprite chillIcon;
    public Sprite executeIcon;
    public Sprite bloodPopIcon;
    public Sprite agingIcon;
    public Sprite corrodedIcon;
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
            // Frozen이나 Stunned는 필요 시 추가
            _ => null
        };
    }
}
