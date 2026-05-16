using UnityEngine;

public class PlayerDebuffTerminal : Base_DebuffUITerminal
{
    public override void UpdateUI(DebuffStackType type, float value) { }
    public override void UpdateUI(DebuffBoolType type, float value) { }
    public override void RemoveIcon(DebuffStackType type) { }
    public override void RemoveIcon(DebuffBoolType type) { }
    public override void RemoveAll() { }
}
