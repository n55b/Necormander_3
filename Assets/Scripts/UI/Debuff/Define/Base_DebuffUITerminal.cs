using System.Collections.Generic;
using UnityEngine;

public abstract class Base_DebuffUITerminal : MonoBehaviour
{
    public abstract void UpdateUI(DebuffStackType type, float value);
    public abstract void RemoveIcon(DebuffStackType type);
    public abstract void RemoveAll();
}
