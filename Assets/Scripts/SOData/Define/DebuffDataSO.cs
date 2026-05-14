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

    // 타입에 맞는 스프라이트를 반환하는 함수
    public Sprite GetSprite(DebuffStackType type)
    {
        return type switch
        {
            DebuffStackType.Poison => poisonIcon,
            DebuffStackType.Chill => chillIcon,
            DebuffStackType.Execute => executeIcon,
            DebuffStackType.BloodPop => bloodPopIcon,
            DebuffStackType.Aging => agingIcon,
            DebuffStackType.Corroded => corrodedIcon,
            _ => null
        };
    }
}
