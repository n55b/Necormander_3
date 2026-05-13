using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// GameManager에 의해 초기화되며, 플레이어의 실시간 상태를 표시합니다.
/// 하트 아이콘을 최대 체력에 맞춰 동적으로 생성합니다.
/// </summary>
public class PlayerStateUI : MonoBehaviour
{
    [Header("HP Settings (Dynamic)")]
    [SerializeField] private GameObject heartPrefab;    // 하트 아이콘 프리팹 (Filled 이미지 포함)
    [SerializeField] private Transform heartContainer; // 하트가 생성될 부모 (Panel_HP)
    private const float HP_PER_HEART = 2.0f;           // 하트 하나당 체력 포인트

    [Header("Gold Settings")]
    [SerializeField] private TextMeshProUGUI goldText;

    private CharacterHealth _playerHealth;
    private List<Image> _hpFillImages = new List<Image>();

    public void Initialize(CharacterHealth playerHealth)
    {
        _playerHealth = playerHealth;
        
        if (_playerHealth != null)
        {
            // 1. 기존 하트 모두 제거
            foreach (Transform child in heartContainer) Destroy(child.gameObject);
            _hpFillImages.Clear();

            // 2. 최대 체력에 맞춰 하트 생성 (예: 6 HP -> 3 Hearts)
            int heartCount = Mathf.CeilToInt(_playerHealth.MaxHP / HP_PER_HEART);
            for (int i = 0; i < heartCount; i++)
            {
                GameObject heartObj = Instantiate(heartPrefab, heartContainer);
                // 프리팹에서 실제 Fill 기능을 하는 Image 컴포넌트 추출 (보통 자식이나 본인)
                Image fillImg = heartObj.GetComponentInChildren<Image>(); 
                if (fillImg != null) _hpFillImages.Add(fillImg);
            }

            // 3. 이벤트 구독
            _playerHealth.UpdateHPBar += RefreshHP;
            RefreshHP();

            // [보강] 최상위 부모부터 하위까지 레이아웃 강제 재구성
            StopAllCoroutines();
            StartCoroutine(RefreshLayoutRoutine());
        }

        RefreshGold();
        Debug.Log($"<color=green>[PlayerStateUI]</color> HUD Initialized. Hearts Created: {_hpFillImages.Count}");
    }

    private System.Collections.IEnumerator RefreshLayoutRoutine()
    {
        // UI 시스템이 소환된 하트들의 'Preferred Size'를 인식할 시간을 줌
        yield return new WaitForEndOfFrame();

        // 최상위(PlayerStateUI)부터 하위 레이아웃 그룹들을 순차적으로 재계산
        RectTransform rootRect = GetComponent<RectTransform>();
        if (rootRect != null)
        {
            // 캔버스 전체 업데이트 강제
            Canvas.ForceUpdateCanvases();
            
            // 모든 자식 레이아웃 리빌드
            LayoutGroup[] groups = GetComponentsInChildren<LayoutGroup>();
            // 깊은 곳(자식)부터 얕은 곳(부모) 순서로 갱신하기 위해 역순 순회
            for (int i = groups.Length - 1; i >= 0; i--)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(groups[i].GetComponent<RectTransform>());
            }
            
            // 마지막으로 본인 리빌드
            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
        }
    }

    private void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.UpdateHPBar -= RefreshHP;
    }

    private void Update()
    {
        RefreshGold();
    }

    public void RefreshHP()
    {
        if (_playerHealth == null) return;

        float currentHP = _playerHealth.CurHP;
        
        for (int i = 0; i < _hpFillImages.Count; i++)
        {
            // 각 하트의 체력 잔량 계산
            float heartFill = Mathf.Clamp(currentHP - (i * HP_PER_HEART), 0, HP_PER_HEART) / HP_PER_HEART;
            _hpFillImages[i].fillAmount = heartFill;
        }
    }

    public void RefreshGold()
    {
        if (InventoryManager.Instance != null && goldText != null)
        {
            goldText.text = InventoryManager.Instance.GOLD.ToString();
        }
    }
}
