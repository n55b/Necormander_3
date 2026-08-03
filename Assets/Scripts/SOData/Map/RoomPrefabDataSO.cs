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
        var entry = GetEntry(type);
        if (entry != null && entry.prefabs.Count > 0)
        {
            return entry.prefabs[Random.Range(0, entry.prefabs.Count)];
        }
        return null;
    }

    /// <summary>
    /// 그 타입의 프리팹 목록. Augment(증강 선택) 방은 전용 프리팹이 등록돼 있으면 그걸 쓰고,
    /// 없으면 일반 방 프리팹을 그대로 쓴다 — 증강 방은 지형이 일반 전투 방과 같고
    /// 미니맵 표시만 다르기 때문에, 새 방을 만들기 전까지 이 폴백으로 굴러간다.
    /// </summary>
    public RoomPrefabEntry GetEntry(RoomType type)
    {
        var entry = roomEntries.Find(e => e.roomType == type);
        if ((entry == null || entry.prefabs == null || entry.prefabs.Count == 0) && type == RoomType.Augment)
            entry = roomEntries.Find(e => e.roomType == RoomType.Normal);
        return entry;
    }
}
