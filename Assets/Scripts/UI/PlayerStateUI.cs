using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// GameManager에 의해 초기화되며, 플레이어의 실시간 상태(체력, 골드, 부활)를 표시합니다.
/// </summary>
public class PlayerStateUI : MonoBehaviour
{
    // === 내부 클래스: 부활 타이머 UI 항목 관리 ===
    private class ReviveIcon
    {
        public AllyManager.MinionInfo TargetInfo;
        public GameObject IconObject;
        public TextMeshProUGUI TimerText;
        public Image ArmyImage;

        public ReviveIcon(AllyManager.MinionInfo info, GameObject obj)
        {
            TargetInfo = info;
            IconObject = obj;
            TimerText = obj.GetComponentInChildren<TextMeshProUGUI>();
            ArmyImage = obj.GetComponentInChildren<Image>(); // 혹은 특정 이름으로 찾기
            ArmyImage.sprite = info.Data.minionIcon; // 아이콘 설정
        }
    }

    [Header("Panel Parent")]
    [SerializeField] private GameObject panelParent;

    [Header("HP Settings")]
    [SerializeField] private Image hpSprite;
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("Gold Settings")]
    [SerializeField] private TextMeshProUGUI goldText;

    [Header("Revive Settings")]
    [SerializeField] private GameObject reviveIconPrefab;
    [SerializeField] private Transform reviveContainer;

    [Header("Stamina Settings")]
    [SerializeField] private GameObject staminaUIPrefab;

    private CharacterHealth _playerHealth;
    private AllyManager _allyManager;
    private List<Image> _hpFillImages = new List<Image>();
    private List<ReviveIcon> _revivingIcons = new List<ReviveIcon>();

    /// <summary>
    /// GameManager에서 호출하여 초기 데이터를 연결합니다.
    /// </summary>
    public void Initialize(CharacterHealth playerHealth, AllyManager allyManager)
    {
        _playerHealth = playerHealth;
        _allyManager = allyManager;
        
        if (_playerHealth != null)
        {
            _playerHealth.UpdateHPBar += RefreshHP;
            SetupHearts();
            RefreshHP();
        }

        if (_allyManager != null)
        {
            _allyManager.OnAllyRespawnStart += AddReviveIcon;
            _allyManager.OnAllyRespawned += RemoveReviveIcon;
        }

        RefreshGold();

        // 스태미나 UI 초기화
        StaminaUI staminaUI = GetComponentInChildren<StaminaUI>();
        
        if (staminaUI == null)
        {
            if (staminaUIPrefab != null)
            {
                GameObject obj = Instantiate(staminaUIPrefab, panelParent.transform);
                staminaUI = obj.GetComponent<StaminaUI>();
                if (staminaUI == null) staminaUI = obj.AddComponent<StaminaUI>();
            }
            else
            {
                staminaUI = gameObject.AddComponent<StaminaUI>();
            }
        }
        
        if (staminaUI != null && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            staminaUI.Initialize(GameManager.Instance.PLAYERCONTROLLER.STAMINA);
        }

        Debug.Log("<color=green>[PlayerStateUI]</color> HUD Initialized.");
    }

    #region UI State Management
    public void PopUpStateUI()
    {
        ClearReviveIcons();
    }
    public void CloseStateUI()
    {
    }
    #endregion

    private void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.UpdateHPBar -= RefreshHP;
        if (_allyManager != null)
        {
            _allyManager.OnAllyRespawnStart -= AddReviveIcon;
            _allyManager.OnAllyRespawned -= RemoveReviveIcon;
        }
    }

    private void Update()
    {
        RefreshGold();
        UpdateReviveTimers();
    }
    
    #region HP
    private void SetupHearts()
    {
        RefreshHP();
    }

    public void RefreshHP()
    {
        if (hpSprite == null || hpText == null) return;

        int maxHP = (int)_playerHealth.MaxHP;
        int curHP = (int)_playerHealth.CurHP;

        hpSprite.fillAmount = (float)curHP / maxHP;
        hpText.text = $"{curHP} / {maxHP}";
    }
    #endregion

    #region Gold
    public void RefreshGold()
    {
        if (InventoryManager.Instance != null && goldText != null)
        {
            goldText.text = InventoryManager.Instance.GOLD.ToString();
        }
    }
    #endregion
    
    #region Revive
    private void AddReviveIcon(AllyManager.MinionInfo info)
    {
        // if (reviveIconPrefab == null) return;
        // GameObject iconObj = Instantiate(reviveIconPrefab, reviveContainer);
        // _revivingIcons.Add(new ReviveIcon(info, iconObj));

        // Debug.Log($"<color=yellow>[PlayerStateUI]</color> Added revive icon for {info.Data.name}. Total reviving: {_revivingIcons.Count}");
        
        // 아이콘 이미지 설정 (주석 처리)
        // if (info.Data.icon != null) {
        //     var icon = _revivingIcons.Find(r => r.TargetInfo == info);
        //     if(icon != null && icon.ArmyImage != null) icon.ArmyImage.sprite = info.Data.icon;
        // }
    }

    private void RemoveReviveIcon(AllyManager.MinionInfo info)
    {
        var icon = _revivingIcons.Find(r => r.TargetInfo == info);
        if (icon != null)
        {
            Destroy(icon.IconObject);
            _revivingIcons.Remove(icon);
        }
    }

    private void ClearReviveIcons()
    {
        foreach (var icon in _revivingIcons)
        {
            Destroy(icon.IconObject);
        }
        _revivingIcons.Clear();
    }

    private void UpdateReviveTimers()
    {
        foreach (var icon in _revivingIcons)
        {
            if (icon.TargetInfo != null && icon.TimerText != null)
            {
                icon.TimerText.text = icon.TargetInfo.RespawnTimer.ToString("F1");
            }
        }
    }
    #endregion
}
