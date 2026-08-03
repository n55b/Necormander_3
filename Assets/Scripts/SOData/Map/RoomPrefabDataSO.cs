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
    /// 그 타입의 프리팹 목록. 전용 프리팹이 등록돼 있으면 그걸 쓰고, 비어 있으면 지형이 같은
    /// 사촌 타입으로 떨어진다 — 새 방 프리팹을 만들기 전에도 맵 생성이 절대 실패하지 않게.
    ///   Augment(증강 선택)   → Normal (지형이 일반 전투 방과 같고 미니맵 표시만 다름)
    ///   EnhanceShop(강화 상점) → Shop  (지형이 상점과 같고 NPC 만 다름)
    /// </summary>
    public RoomPrefabEntry GetEntry(RoomType type)
    {
        var entry = roomEntries.Find(e => e.roomType == type);
        if (entry != null && entry.prefabs != null && entry.prefabs.Count > 0) return entry;

        if (type == RoomType.Augment) return roomEntries.Find(e => e.roomType == RoomType.Normal);
        if (type == RoomType.EnhanceShop) return roomEntries.Find(e => e.roomType == RoomType.Shop);
        return entry;
    }
}
