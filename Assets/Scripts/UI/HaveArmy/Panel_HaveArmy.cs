using System.Collections.Generic;
using UnityEngine;

public class Panel_HaveArmy : MonoBehaviour
{
    [SerializeField] List<BG_HaveArmy> haveArmies;

    private void OnDisable()
    {
        UnSubscribeFromEvents();
    }
    private void UnSubscribeFromEvents()
    {
        InventoryManager.Instance.OnMinionUpdated -= Update_HaveArmy;
    }

    public void CloseUI()
    {
        foreach(var army in haveArmies)
        {
            army.gameObject.SetActive(false);
        }
    }

    public void Update_HaveArmy()
    {
        int i = 0;
        CloseUI();

        if(InventoryManager.Instance == null) return;

        foreach(var army in InventoryManager.Instance?.Slots)
        {
            if(army.EquippedMinion != null)
            {
                haveArmies[i].Init(army.EquippedMinion.minionIcon, army.Quantity);
                haveArmies[i].gameObject.SetActive(true);
                i++;
            }
        }
    }
}
