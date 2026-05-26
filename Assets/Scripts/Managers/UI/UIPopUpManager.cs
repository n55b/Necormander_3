using UnityEngine;

public class UIPopUpManager : MonoBehaviour
{
    public static UIPopUpManager Instance;

    private bool _isPopUpActive = false;
    [SerializeField] private bool _isOnBattle = false;
    private bool _isInitialized = false; // 중복 초기화 방지용 변수
    private GameObject _currentPopUp;
    [SerializeField] private PlayerStateUI _playerStateUI;

    public bool IsPopUpActive => _isPopUpActive;
    public bool IsOnBattle => _isOnBattle;

    private void Awake()
    {
        Instance = this;
    }
  
    private void Update()
    {
        if (_isInitialized) return;
        if (GameManager.Instance != null && GameManager.Instance.IsPlayerReady)
        {
            Initialize();
        }
    }

    // 중복으로 띄울 수 없는 팝업 띄울 때 사용해야 하는 메서드
    public void PopUpUI(GameObject popUpObj)
    {
        if (_isOnBattle || _isPopUpActive) return; // 전투 중에는 팝업 띄우지 않음

        _currentPopUp = popUpObj;
        _isPopUpActive = true;
    }

    // 팝업 닫힐 때 호출해야 하는 메서드
    public void ClosePopUpUI()
    {
        if (_currentPopUp != null)
        {
            _currentPopUp.SetActive(false);
            _currentPopUp = null;
        }
        _isPopUpActive = false;
    }

    // 강제로 팝업 띄워야 할 때 사용해야 하는 메서드
    public void ForcePopUpUI(GameObject popUpObj)
    {
        if (_currentPopUp != null)
        {
            _currentPopUp.SetActive(false);
        }
        _currentPopUp = popUpObj;
        _isPopUpActive = true;
    }

    /// Battle이나 Idle 상태 일 때 팝업 하는 것과 안 하는 것 관리
    private void SetPopUpState(bool isOnBattle)
    {
        _isOnBattle = isOnBattle;

        if(isOnBattle)
        {
            if (_currentPopUp != null)
            {
                _currentPopUp.SetActive(false);
            }
            _playerStateUI.CloseStateUI();
        }
        else
        {
            _playerStateUI.PopUpStateUI();
        }
    }

    /// <summary>
    /// 초기화 메서드. 이벤트 구독 등
    /// </summary>
    private void Initialize()
    {
        _isInitialized = true;

        GameManager.Instance.PLAYERCONTROLLER.OnEnterIdle += () => SetPopUpState(false);
        GameManager.Instance.PLAYERCONTROLLER.OnEnterBattle += () => SetPopUpState(true);
    }
    private void OnDestroy()
    {
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            GameManager.Instance.PLAYERCONTROLLER.OnEnterIdle -= () => SetPopUpState(false);
            GameManager.Instance.PLAYERCONTROLLER.OnEnterBattle -= () => SetPopUpState(true);
        }
    }
}