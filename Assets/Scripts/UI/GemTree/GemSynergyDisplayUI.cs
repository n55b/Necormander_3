using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

/// <summary>
/// GemTreeUI에 의해 관리되며, 현재 활성화된 시너지 목록과 단계를 표시하는 클래스입니다.
/// </summary>
public class GemSynergyDisplayUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject synergyItemPrefab; // 시너지 항목 하나를 나타내는 프리팹
    [SerializeField] private Transform contentParent;      // 항목들이 생성될 부모

    private List<GameObject> _spawnedItems = new List<GameObject>();

    /// <summary>
    /// GemTreeUI에서 호출하여 UI를 갱신합니다.
    /// </summary>
    public void RefreshSynergyList()
    {
        ClearItems();

        if (InventoryManager.Instance == null) return;

        var globalStats = InventoryManager.Instance.GlobalGemStats;
        
        foreach (GemSynergyGroup group in System.Enum.GetValues(typeof(GemSynergyGroup)))
        {
            if (group == GemSynergyGroup.Base) continue;

            // 1. 해당 그룹의 시너지 단계들 표시 (누적된 모든 단계 노출)
            int count = globalStats.SynergyCounts.TryGetValue(group, out int c) ? c : 0;
            int maxLevel = GemSynergyLogic.GetLevel(count);

            if (maxLevel >= 1)
            {
                // 달성한 모든 레벨을 개별 항목으로 생성
                for (int lv = 1; lv <= maxLevel; lv++)
                {
                    string synergyName = group.ToString();
                    string description = GetSingleLevelDescription(group, lv);
                    CreateSynergyItem(synergyName, lv, count, description, GetSynergyColor(group));
                }
            }

            // 2. 해당 그룹에 속한 유니크 효과들 표시
            foreach (var unique in globalStats.UniqueEffects)
            {
                if (GetSynergyGroupOfUnique(unique) == group)
                {
                    CreateSynergyItem("Unique", 0, 1, GetUniqueDescription(unique), Color.yellow);
                }
            }
        }

        // [보강] 텍스트 크기 계산 및 레이아웃 갱신을 위해 코루틴 실행
        StopAllCoroutines();
        StartCoroutine(RefreshLayoutRoutine());
    }

    private System.Collections.IEnumerator RefreshLayoutRoutine()
    {
        // 1. UI 시스템이 텍스트 변경을 인지할 시간 부여
        yield return new WaitForEndOfFrame();

        if (contentParent is RectTransform rect)
        {
            // 2. 캔버스 강제 업데이트 및 레이아웃 재계산
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }
    }

    private void CreateSynergyItem(string name, int level, int count, string desc, Color color)
    {
        if (synergyItemPrefab == null) return;

        GameObject item = Instantiate(synergyItemPrefab, contentParent);
        _spawnedItems.Add(item);

        var texts = item.GetComponentsInChildren<TextMeshProUGUI>();
        
        var nameText = texts.FirstOrDefault(t => t.name.Contains("Name"));
        if (nameText != null)
        {
            string lvStr = level > 0 ? $"LV.{level}" : "UNIQUE";
            nameText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>[{name}]</color> {lvStr} <size=70%>({count})</size>";
            nameText.ForceMeshUpdate(); // 즉시 크기 계산 강제
        }

        var descText = texts.FirstOrDefault(t => t.name.Contains("Desc"));
        if (descText != null)
        {
            descText.text = desc;
            descText.ForceMeshUpdate(); // 즉시 크기 계산 강제
        }
    }

    private string GetSingleLevelDescription(GemSynergyGroup group, int level)
    {
        switch (group)
        {
            case GemSynergyGroup.Poison:
                if (level == 1) return "Poison synergy activated.";
                if (level == 2) return "Poison duration extended to 15s.";
                if (level == 3) return "Basic attacks apply +1 extra Poison stack.";
                if (level == 4) return "Poison damage interval reduced by 40%.";
                break;
            case GemSynergyGroup.Priest_Chill:
                if (level == 1) return "Chill synergy activated.";
                if (level == 2) return "Slow value per stack increased to 1.25%.";
                if (level == 3) return "Refund 5 Chill stacks upon freezing.";
                if (level == 4) return "Freeze deals fixed damage based on max stacks.";
                break;
            case GemSynergyGroup.BloodPop:
                if (level == 1) return "BloodPop synergy activated.";
                if (level == 2) return "Explosion damage increased by 10.";
                if (level == 3) return "Explosion radius increased by 1.5x.";
                break;
            case GemSynergyGroup.Priest_Aging:
                if (level == 1) return "Aging synergy activated.";
                if (level == 2) return "Weakness value per stack increased to 1.25%.";
                if (level == 3) return "Maximum Aging stacks increased to 40.";
                break;
            case GemSynergyGroup.Priest_Corrosion:
                if (level == 1) return "Throw damage increased by 25%.";
                if (level == 2) return "Throw damage increased by 40% (Total).";
                break;
        }
        return "New power unlocked.";
    }

    private GemSynergyGroup GetSynergyGroupOfUnique(GemUniqueType type)
    {
        switch (type)
        {
            case GemUniqueType.LethalPoison:
            case GemUniqueType.LethalDose: return GemSynergyGroup.Poison;
            case GemUniqueType.AchingBones:
            case GemUniqueType.SlowlyFreezingFlower: return GemSynergyGroup.Priest_Chill;
            case GemUniqueType.ExplodingFlesh: return GemSynergyGroup.BloodPop;
            case GemUniqueType.NoCountryForOldMen: return GemSynergyGroup.Priest_Aging;
            default: return GemSynergyGroup.Base;
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
            case GemSynergyGroup.Priest_Chill: return new Color(0.3f, 0.6f, 1.0f);
            case GemSynergyGroup.Execution: return new Color(1.0f, 0.3f, 0.1f);
            case GemSynergyGroup.BloodPop: return new Color(1.0f, 0.0f, 1.0f);
            case GemSynergyGroup.Priest_Aging: return new Color(0.7f, 0.5f, 0.5f);
            case GemSynergyGroup.Priest_Corrosion: return new Color(1.0f, 0.8f, 0.0f);
            default: return Color.white;
        }
    }

    private void ClearItems()
    {
        foreach (var item in _spawnedItems) if (item != null) Destroy(item);
        _spawnedItems.Clear();
    }
}
