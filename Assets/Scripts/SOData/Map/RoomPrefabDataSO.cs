using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class RoomPrefabEntry
{
    public RoomType roomType;
    public List<GameObject> prefabs;
}

[CreateAssetMenu(fileName = "RoomPrefabData", menuName = "Map/RoomPrefabData")]
public class RoomPrefabDataSO : ScriptableObject
{
    public List<RoomPrefabEntry> roomEntries;

    public GameObject GetRandomPrefab(RoomType type, string diagnosticContext = null)
    {
        var candidates = GetValidPrefabs(type, diagnosticContext);
        return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
    }

    /// <summary>
    /// 파괴되었거나 비어 있는 참조를 제외한 후보 사본. 원본 등록 목록은 진단/수정을 위해 보존한다.
    /// 전용 후보가 모두 무효인 경우에도 Augment → Normal, EnhanceShop → Shop 폴백을 적용한다.
    /// </summary>
    public List<GameObject> GetValidPrefabs(RoomType type, string diagnosticContext = null)
    {
        var candidates = new List<GameObject>();
        CollectValidPrefabs(type, candidates, diagnosticContext);
        if (candidates.Count == 0)
        {
            if (type == RoomType.Augment) CollectValidPrefabs(RoomType.Normal, candidates, diagnosticContext);
            else if (type == RoomType.EnhanceShop) CollectValidPrefabs(RoomType.Shop, candidates, diagnosticContext);
        }

        if (candidates.Count == 0)
            Debug.LogWarning($"[MapPrefab] data='{DiagnosticName}' requested={type}: 유효한 등록 후보가 없습니다. {diagnosticContext}", this);
        return candidates;
    }

    private void CollectValidPrefabs(RoomType type, List<GameObject> candidates, string diagnosticContext)
    {
        if (roomEntries == null) return;
        int entryIndex = roomEntries.FindIndex(e => e != null && e.roomType == type);
        if (entryIndex < 0) return;
        var prefabs = roomEntries[entryIndex].prefabs;
        if (prefabs == null) return;

        for (int i = 0; i < prefabs.Count; i++)
        {
            var prefab = prefabs[i];
            // Unity의 == null은 관리 참조가 남아 있는 Destroy된 오브젝트도 잡는다.
            if (prefab != null)
            {
                candidates.Add(prefab);
                continue;
            }

            string state = ReferenceEquals(prefab, null) ? "Null" : "Missing/Destroyed";
            Debug.LogWarning($"[MapPrefab] data='{DiagnosticName}' roomEntries[{entryIndex}] type={type} " +
                $"prefabs[{i}] state={state}: 후보에서 제외합니다(등록 목록은 유지). {diagnosticContext}", this);
        }
    }

    private string DiagnosticName
    {
        get
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path)) return path;
#endif
            return name;
        }
    }
}
