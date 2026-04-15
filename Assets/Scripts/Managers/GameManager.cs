using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private PlayerController playerController;
    [SerializeField] public DataManager dataManager;

    public PlayerController PLAYERCONTROLLER {get {return playerController;}}

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        playerController = GameObject.FindWithTag("Player").GetComponent<PlayerController>();
    }
}
