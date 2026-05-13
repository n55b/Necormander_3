using UnityEngine;

public abstract class PrizeDataSO : ScriptableObject
{
    [SerializeField] public int gold;

    public abstract void BuyItem();
}
