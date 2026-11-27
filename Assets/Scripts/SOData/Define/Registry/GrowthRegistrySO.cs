using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 성장 데이터 계보와 보석, 보물들을 보관하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "GrowthRegistry", menuName = "Necromancer/Registry/GrowthRegistry")]
public class GrowthRegistrySO : ScriptableObject
{
    [Header("소환수 데이터")]
    public List<MinionDataSO> minionDatas = new List<MinionDataSO>();

    [Header("중첩 보물")]
    public List<TreasureSO> treasures = new List<TreasureSO>();

    [Header("장비 패시브")]
    public List<EquipmentSO> equipments = new List<EquipmentSO>();
    [Tooltip("새 게임 및 삭제된 구 장비 세이브의 기본 무기.")]
    public EquipmentSO startingEquipment;

    [Header("아이템 (주머니. 장비와 별개 시스템)")]
    [Tooltip("세이브에 이름으로 저장되므로 로드 시 이름→SO 해석에 이 목록이 필요하다.")]
    public List<ItemSO> items = new List<ItemSO>();
    [Tooltip("기본 소지 아이템. 새 게임과 이전 세이브 로드 시 없을 때만 1개 지급한다.")]
    public ItemSO startingItem;

    public MinionDataSO ResolveSavedMinion(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return minionDatas?.Find(m => m != null && m.name == key)
            ?? minionDatas?.Find(m => m is MainMinionDataSO main && main != null && main.MatchesSaveKey(key));
    }

    public TreasureSO ResolveSavedTreasure(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return treasures?.Find(t => t != null && t.name == key)
            ?? treasures?.Find(t => t != null && t.itemName == key)
            ?? treasures?.Find(t => t != null && Former(t.formerAssetNames, key));
    }

    public EquipmentSO ResolveSavedEquipment(string key)
        => string.IsNullOrEmpty(key) ? null : equipments?.Find(e => e != null && e.name == key)
            ?? equipments?.Find(e => e != null && Former(e.formerAssetNames, key));

    // 삭제 표시된 콘텐츠: 목록과 보상에서는 빠지고 세이브 해석만 남는다. DATA가 실행 중에만 채운다(ProjectStructure.Rebuild).
    [System.NonSerialized] internal readonly List<ScriptableObject> retired = new List<ScriptableObject>();

    public ItemSO ResolveSavedItem(string key)
        => string.IsNullOrEmpty(key) ? null : items?.Find(i => i != null && i.name == key)
            ?? items?.Find(i => i != null && Former(i.formerAssetNames, key))
            ?? retired.Find(r => r is ItemSO i && i != null && i.name == key) as ItemSO
            ?? retired.Find(r => r is ItemSO i && i != null && Former(i.formerAssetNames, key)) as ItemSO;

    // 현재 이름이 먼저다. 옛 이름(예: DATA 굽기 전 키 content_<id>)은 일치하는 현재 이름이 없을 때만 본다.
    private static bool Former(string[] names, string key) => names != null && System.Array.IndexOf(names, key) >= 0;
}
