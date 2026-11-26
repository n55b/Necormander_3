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

    [Header("우클릭 (가드 Lv1~4)")]
    [Tooltip("마을 NPC 교체 목록에 뜨는 우클릭들. 영구 선택이 이름으로 저장되므로 이름→SO 해석에도 쓰인다.")]
    public List<RightClickDataSO> rightClicks = new List<RightClickDataSO>();

    [Tooltip("아무것도 안 골랐을 때 플레이어가 기본으로 드는 우클릭(기본 Lv1 가드).\n" +
             "비워두면 rightClicks 에서 Lv1을 찾아 쓰고, 그것도 없으면 첫 유효 항목을 쓴다 — " +
             "'우클릭이 없는 플레이어'는 만들지 않는다.")]
    public RightClickDataSO defaultRightClick;

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

    /// <summary>
    /// 기본 우클릭. 인스펙터 지정 → Lv1 탐색 → 첫 유효 항목 순으로 떨어진다.
    /// 배선을 깜빡해도 플레이어가 우클릭 없이 게임을 시작하는 일은 없게 한다.
    /// </summary>
    public RightClickDataSO ResolveDefaultRightClick()
    {
        if (defaultRightClick != null && defaultRightClick.IsValid) return defaultRightClick;

        var parry = rightClicks.Find(r => r != null && r.IsValid && r.config.level == 1);
        if (parry != null) return parry;

        return rightClicks.Find(r => r != null && r.IsValid);
    }
}
