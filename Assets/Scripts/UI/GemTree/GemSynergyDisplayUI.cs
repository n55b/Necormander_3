using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

/// <summary>
/// GemTreeUI의 한편에 현재 활성화된 시너지 목록과 단계를 표시하는 클래스입니다.
/// </summary>
public class GemSynergyDisplayUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject synergyItemPrefab; // 시너지 항목 하나를 나타내는 프리팹
    [SerializeField] private Transform contentParent;      // 항목들이 생성될 부모 (Vertical Layout Group 권장)

    private List<GameObject> _spawnedItems = new List<GameObject>();

    private void OnEnable()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnGemTreeUpdated += RefreshSynergyList;
        }
        RefreshSynergyList();
    }

    private void OnDisable()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnGemTreeUpdated -= RefreshSynergyList;
        }
    }

    /// <summary>
    /// 현재 인벤토리의 전역 시너지 상태를 가져와 UI를 갱신합니다.
    /// </summary>
    public void RefreshSynergyList()
    {
        ClearItems();

        if (InventoryManager.Instance == null) return;

        var globalStats = InventoryManager.Instance.GlobalGemStats;
        
        // 1. 시너지 그룹 순회 (2개 이상 연결된 것만)
        foreach (var kvp in globalStats.SynergyCounts.OrderByDescending(x => x.Value))
        {
            if (kvp.Value < 2) continue; // 2개 미만은 시너지 아님

            int level = GemSynergyLogic.GetLevel(kvp.Value);
            string synergyName = kvp.Key.ToString();
            string description = GetSynergyDescription(kvp.Key, level);
            
            CreateSynergyItem(synergyName, level, kvp.Value, description, GetSynergyColor(kvp.Key));
        }

        // 2. 유니크 효과 순회
        foreach (var unique in globalStats.UniqueEffects)
        {
            CreateSynergyItem("Unique", 0, 1, GetUniqueDescription(unique), Color.yellow);
        }
    }

    private void CreateSynergyItem(string name, int level, int count, string desc, Color color)
    {
        if (synergyItemPrefab == null) return;

        GameObject item = Instantiate(synergyItemPrefab, contentParent);
        _spawnedItems.Add(item);

        // 프리팹 내의 텍스트 컴포넌트들을 찾아 데이터를 채웁니다. (규칙 기반)
        // 1. Name: [Poison] LV.2 (3 Gems)
        // 2. Desc: Poison duration +5s
        var texts = item.GetComponentsInChildren<TextMeshProUGUI>();
        
        var nameText = texts.FirstOrDefault(t => t.name.Contains("Name"));
        if (nameText != null)
        {
            string lvStr = level > 0 ? $"LV.{level}" : "UNIQUE";
            nameText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>[{name}]</color> {lvStr} <size=70%>({count})</size>";
        }

        var descText = texts.FirstOrDefault(t => t.name.Contains("Desc"));
        if (descText != null)
        {
            descText.text = desc;
        }
    }

    private string GetSynergyDescription(GemSynergyGroup group, int level)
    {
        switch (group)
        {
            case GemSynergyGroup.Poison:
                if (level >= 4) return "Poison tick 40% faster, Extra stack +1, Duration +5s";
                if (level >= 3) return "Extra stack +1, Duration +5s";
                if (level >= 2) return "Duration +15s (extended)";
                return "Poison synergy active";
            case GemSynergyGroup.Chill:
                if (level >= 4) return "Freeze deals fixed damage, 25% refund, Slow +25%";
                if (level >= 3) return "25% stack refund on freeze, Slow +25%";
                if (level >= 2) return "Slow value increased to 1.25%";
                return "Chill synergy active";
            case GemSynergyGroup.BloodPop:
                if (level >= 3) return "Explosion radius 1.5x, Damage +10";
                if (level >= 2) return "Explosion damage +10";
                return "BloodPop synergy active";
            case GemSynergyGroup.Aging:
                if (level >= 3) return "Max stacks 40, Weakness +25%";
                if (level >= 2) return "Weakness value increased to 1.25%";
                return "Aging synergy active";
            case GemSynergyGroup.Corrosion:
                if (level >= 2) return "Throw damage +40%";
                if (level >= 1) return "Throw damage +25%";
                return "Corrosion synergy active";
            default:
                return "Synergy active";
        }
    }

    private string GetUniqueDescription(GemUniqueType type)
    {
        switch (type)
        {
            case GemUniqueType.LethalPoison: return "Doubles poison stacks on throw hit.";
            case GemUniqueType.LethalDose: return "Poison tick interval -50%.";
            case GemUniqueType.AchingBones: return "No stacks while frozen, starts at 10.";
            case GemUniqueType.SlowlyFreezingFlower: return "Max Chill stacks +10.";
            case GemUniqueType.ExplodingFlesh: return "Explosion spreads BloodPop stacks.";
            case GemUniqueType.NoCountryForOldMen: return "Aging max stacks 100, insta-kill.";
            default: return "Unique ability active";
        }
    }

    private Color GetSynergyColor(GemSynergyGroup group)
    {
        switch (group)
        {
            case GemSynergyGroup.Poison: return new Color(0.2f, 0.8f, 0.2f);
            case GemSynergyGroup.Chill: return new Color(0.3f, 0.6f, 1.0f);
            case GemSynergyGroup.Execution: return new Color(1.0f, 0.3f, 0.1f);
            case GemSynergyGroup.BloodPop: return new Color(1.0f, 0.0f, 1.0f);
            case GemSynergyGroup.Aging: return new Color(0.7f, 0.5f, 0.5f);
            case GemSynergyGroup.Corrosion: return new Color(1.0f, 0.8f, 0.0f);
            default: return Color.white;
        }
    }

    private void ClearItems()
    {
        foreach (var item in _spawnedItems) if (item != null) Destroy(item);
        _spawnedItems.Clear();
    }
}
