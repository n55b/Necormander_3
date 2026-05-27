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
        }
    }

    [Header("HP Settings")]
    [SerializeField] private GameObject heartPrefab;
    [SerializeField] private Transform heartContainer;
    private const float HP_PER_HEART = 2.0f;

    [Header("Gold Settings")]
    [SerializeField] private TextMeshProUGUI goldText;

    [Header("Revive Settings")]
    [SerializeField] private GameObject reviveIconPrefab;
    [SerializeField] private Transform reviveContainer;

    [Header("PanelHaveArmy")]
    [SerializeField] private Panel_HaveArmy panelHaveArmy;

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

        if(InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnMinionUpdated += panelHaveArmy.Update_HaveArmy;
            panelHaveArmy.Update_HaveArmy(); // 초기 상태 반영
        }

        RefreshGold();
        Debug.Log("<color=green>[PlayerStateUI]</color> HUD Initialized.");
    }

    #region UI State Management
    public void PopUpStateUI()
    {
        panelHaveArmy.Update_HaveArmy();
        ClearReviveIcons();
    }
    public void CloseStateUI()
    {
        panelHaveArmy.CloseUI();
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
        foreach (Transform child in heartContainer) Destroy(child.gameObject);
        _hpFillImages.Clear();

        if (_playerHealth == null || _playerHealth.MaxHP <= 0) return;

        int heartCount = Mathf.CeilToInt(_playerHealth.MaxHP / HP_PER_HEART);
        for (int i = 0; i < heartCount; i++)
        {
            GameObject heartObj = Instantiate(heartPrefab, heartContainer);
            
            // [수정] 자식 오브젝트 중 "HP_FillImage"라는 이름을 가진 이미지를 찾습니다.
            Image fillImg = null;
            Transform fillTransform = heartObj.transform.Find("HP_FillImage");
            if (fillTransform != null) fillImg = fillTransform.GetComponent<Image>();
            else fillImg = heartObj.GetComponentInChildren<Image>(); // 차선책

            if (fillImg != null) _hpFillImages.Add(fillImg);
        }

        // 레이아웃 갱신
        if (heartContainer is RectTransform rect)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            if (rect.parent is RectTransform parentRect) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        }
    }

    public void RefreshHP()
    {
        if (_playerHealth == null) return;
        
        float currentHP = _playerHealth.CurHP;
        // Debug.Log($"<color=red>[PlayerUI]</color> RefreshHP Called. Current HP: {currentHP} / {_playerHealth.MaxHP}");

        for (int i = 0; i < _hpFillImages.Count; i++)
        {
            float heartValue = currentHP - (i * HP_PER_HEART);
            float heartFill = Mathf.Clamp(heartValue, 0, HP_PER_HEART) / HP_PER_HEART;
            _hpFillImages[i].fillAmount = heartFill;
        }
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
        if (reviveIconPrefab == null) return;
        GameObject iconObj = Instantiate(reviveIconPrefab, reviveContainer);
        _revivingIcons.Add(new ReviveIcon(info, iconObj));
        
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
