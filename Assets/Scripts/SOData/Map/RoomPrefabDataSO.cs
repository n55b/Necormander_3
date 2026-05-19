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

    public GameObject GetRandomPrefab(RoomType type)
    {
        var entry = roomEntries.Find(e => e.roomType == type);
        if (entry != null && entry.prefabs.Count > 0)
        {
            return entry.prefabs[Random.Range(0, entry.prefabs.Count)];
        }
        return null;
    }
}
