using System.Collections.Generic;
using UnityEngine;

public abstract class Base_DebuffUITerminal : MonoBehaviour
{
    public abstract void UpdateUI(DebuffStackType type, float value);
    public abstract void UpdateUI(DebuffBoolType type, float value); // [추가]
    public abstract void RemoveIcon(DebuffStackType type);
    public abstract void RemoveIcon(DebuffBoolType type); // [추가]
    public abstract void RemoveAll();
}
