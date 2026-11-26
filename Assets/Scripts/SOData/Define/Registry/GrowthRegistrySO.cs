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
            ?? treasures?.Find(t => t != null && t.itemName == key);
    }

    public EquipmentSO ResolveSavedEquipment(string key)
        => string.IsNullOrEmpty(key) ? null : equipments?.Find(e => e != null && e.name == key);

    public ItemSO ResolveSavedItem(string key)
        => string.IsNullOrEmpty(key) ? null : items?.Find(i => i != null && i.name == key);
}
