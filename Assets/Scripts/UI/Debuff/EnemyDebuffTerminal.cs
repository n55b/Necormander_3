using System.Collections.Generic;
using UnityEngine;

public class EnemyDebuffTerminal : Base_DebuffUITerminal
{
    [SerializeField] DebuffDataSO debuffData; // 도감 하나 등록
    [SerializeField] Panel_Debuff debuffUI;

    public override void UpdateUI(DebuffStackType type, float value)
    {
        // 도감에서 이미지 찾아서 UI에 넘겨줌
        Sprite _sprite = debuffData.GetSprite(type);
        debuffUI.AddDebuff(type, _sprite, (int)value);
    }

    public override void UpdateUI(DebuffBoolType type, float value)
    {
        Sprite _sprite = debuffData.GetSprite(type);
        debuffUI.AddDebuff(type, _sprite, (int)value);
    }

    public override void RemoveIcon(DebuffStackType type)
    {
        debuffUI.RemoveDebuff(type);
    }

    public override void RemoveIcon(DebuffBoolType type)
    {
        debuffUI.RemoveDebuff(type);
    }

    public override void RemoveAll()
    {
        debuffUI.ClearAllDebuffs();
    }
}