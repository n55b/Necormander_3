using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private PlayerController playerController;
    [SerializeField] public DataManager dataManager;
    [SerializeField] public MouseManager mouseManager;

    public PlayerController PLAYERCONTROLLER {get {return playerController;}}

    private void Awake()
    {
        Instance = this;
        playerController = GameObject.FindWithTag("Player").GetComponent<PlayerController>();
    }
}
