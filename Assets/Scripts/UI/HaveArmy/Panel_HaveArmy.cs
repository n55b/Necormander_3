using System.Collections.Generic;
using UnityEngine;

public class Panel_HaveArmy : MonoBehaviour
{
    [SerializeField] List<BG_HaveArmy> haveArmies;

    private void OnEnable()
    {
        Initialize();
    }

    private void OnDisable()
    {
        UnSubscribeFromEvents();
    }

    public void Initialize()
    {
        Update_HaveArmy();

        InventoryManager.Instance.OnMinionUpdated += Update_HaveArmy;
        var playerController = GameManager.Instance?.PLAYERCONTROLLER;
        if (playerController != null)
        {
            playerController.OnEnterBattle += CloseUI;
            playerController.OnEnterIdle += Update_HaveArmy;
        }
    }

    private void UnSubscribeFromEvents()
    {
        InventoryManager.Instance.OnMinionUpdated -= Update_HaveArmy;
        var playerController = GameManager.Instance?.PLAYERCONTROLLER;
        if (playerController != null)
        {
            playerController.OnEnterBattle -= CloseUI;
            playerController.OnEnterIdle -= Update_HaveArmy;
        }
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

        foreach(var army in InventoryManager.Instance.Slots)
        {
            if(army.EquippedLineage != null)
            {
                haveArmies[i].Init(army.EquippedLineage.baseForm.minionIcon, army.Quantity);
                haveArmies[i].gameObject.SetActive(true);
                i++;
            }
        }
    }
}
