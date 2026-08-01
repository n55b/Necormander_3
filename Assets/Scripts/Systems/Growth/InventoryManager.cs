using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 슬롯과 보물 인벤토리를 관리하는 핵심 매니저입니다.
/// </summary>
public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;

    [System.Serializable]
    public class CoreSlot
    {
        public bool IsShattered; 
        public MinionDataSO EquippedMinion; 
        public int Quantity;                // 미니언 마리수
        
        public bool IsEmpty => !IsShattered && EquippedMinion == null;

        public MinionDataSO GetCurrentMinionData() => EquippedMinion;

        public GrowthItemData GetCurrentItemData()
        {
            if (EquippedMinion != null)
            {
                // [수정] rewardItemData가 비어있어도(미할당) 미니언 자체 정보(minionName/minionIcon)로 대체하여
                // 이미 장착된 미니언이 '비어있음'으로 잘못 표시되는 버그를 방지합니다.
                var baseData = EquippedMinion.rewardItemData;
                string finalName = (baseData != null && !string.IsNullOrEmpty(baseData.itemName)) ? baseData.itemName : EquippedMinion.minionName;
                Sprite finalIcon = (baseData != null && baseData.icon != null) ? baseData.icon : EquippedMinion.minionIcon;

                return new GrowthItemData
                {
                    itemName = finalName,
                    description = baseData != null ? baseData.description : null,
                    icon = finalIcon,
                    rarity = baseData != null ? baseData.rarity : default,
                    localizedItemName = baseData != null ? baseData.localizedItemName : null,
                    localizedDescription = baseData != null ? baseData.localizedDescription : null
                };
            }
            return null;
        }
    }

    [Header("자원 관리")]
    [SerializeField] private int gold = 0;
    public int GOLD => gold;

    [Tooltip("증강 방 보상으로 누적된 최대 체력. 런 영구이며 세이브에 같이 들어간다.")]
    [SerializeField] private float augmentMaxHpBonus = 0f;
    /// <summary>증강 보상으로 얻은 최대 체력 합계. CharacterStat.MAXHP 가 매 프레임 읽어 간다 —
    /// 보물 보너스와 같은 방식이라 층을 넘어가 플레이어가 새로 생겨도 재적용이 필요 없다.</summary>
    public float AugmentMaxHpBonus => augmentMaxHpBonus;
    public void AddAugmentMaxHp(float amount) => augmentMaxHpBonus += amount;

    /// <summary>소환수 슬롯 인덱스. 메인 1 + 서브 1 고정.</summary>
    public const int SLOT_MAIN = 0;
    public const int SLOT_SUB = 1;
    public const int SLOT_COUNT = 2;

    [Header("슬롯 시스템 (메인 1 + 서브 1 고정)")]
    public List<CoreSlot> Slots = new List<CoreSlot>(SLOT_COUNT);

    /// <summary>역할에 대응하는 슬롯 인덱스. 소환수가 아니면(적 데이터 등) -1.</summary>
    public static int SlotIndexOf(MinionDataSO minion) => minion switch
    {
        SubMinionDataSO => SLOT_SUB,
        MainMinionDataSO => SLOT_MAIN,
        _ => -1,
    };

    public MainMinionDataSO MainSummon => GetSummon(SLOT_MAIN) as MainMinionDataSO;
    public SubMinionDataSO SubSummon => GetSummon(SLOT_SUB) as SubMinionDataSO;

    private MinionDataSO GetSummon(int index)
        => (index >= 0 && index < Slots.Count && !Slots[index].IsShattered) ? Slots[index].EquippedMinion : null;

    // ======================================================
    // [에러 방지를 위한 병렬 리스트 디버그 설정]
    [Header("Debug Settings (Starting Items)")]
    [SerializeField] private bool useDebugStartingInventory = true;

    [Space(10)]
    [Tooltip("시작 시 지급할 미니언 리스트")]
    [SerializeField] private List<MinionDataSO> debugStartingMinions = new List<MinionDataSO>();
    [Tooltip("위 미니언들의 수량 (순서대로 매칭)")]
    [SerializeField] private List<int> debugStartingMinionQuantities = new List<int>();

    // ======================================================

    public System.Action OnMinionUpdated;

    [Header("보물 인벤토리 (중첩)")]
    public Dictionary<TreasureSO, int> TreasureStacks = new Dictionary<TreasureSO, int>();

    public void Initialize(bool hasSave)
    {
        Instance = this;
        while (Slots.Count < SLOT_COUNT)
        {
            Slots.Add(new CoreSlot());
        }

        Debug.Log("<color=cyan>[InventoryManager]</color> Initialized.");

        if (useDebugStartingInventory && !hasSave)
        {
            Debug_InitializeInventory();
        }
    }

    private void Debug_InitializeInventory()
    {
        Debug.Log($"<color=cyan>[InventoryManager]</color> Debug_InitializeInventory Started. Starting Minions Count: {debugStartingMinions.Count}");

        // 1. 미니언 먼저 생성 (미소유 직업만 추가)
        for (int i = 0; i < debugStartingMinions.Count; i++)
        {
            if (debugStartingMinions[i] == null) continue;
            if (HasMinion(debugStartingMinions[i].minionType)) continue;

            int qty = (i < debugStartingMinionQuantities.Count) ? debugStartingMinionQuantities[i] : 1;

            // [개선] Registry 에셋 등록 상태와 무관하게 인스펙터에 직접 지정된 미니언 데이터를 100% 꽂아 장착합니다.
            // 슬롯은 역할당 1칸이므로 빈 칸을 찾지 않고 역할 슬롯에 바로 넣는다.
            EquipMinion(debugStartingMinions[i], qty);
        }

        // [수정] 유저 요청에 의해 미니언이 없을 때 기본 전사 1마리를 추가하는 로직을 제거(주석 처리)합니다.
        // if (!Slots.Exists(s => s.EquippedMinion != null)) AddMinionOrIncreaseQuantity(CommandData.SkeletonWarrior);

        // [추가] 디버그용 시작 미니언 적재가 끝나면 갱신 이벤트를 격발하여 동기화시킵니다.
        OnMinionUpdated?.Invoke();
    }

    #region Gold System
    public void AddGold(int amount) { gold += amount; }
    public bool SpendGold(int amount)
    {
        if (gold >= amount) { gold -= amount; return true; }
        return false;
    }
    #endregion

    #region Slot Management
    public bool AddMinionOrIncreaseQuantity(CommandData job, int amount = 1)
    {
        var existingSlot = Slots.Find(s => !s.IsShattered && s.EquippedMinion != null && s.EquippedMinion.minionType == job);
        if (existingSlot != null) { existingSlot.Quantity += amount; OnMinionUpdated?.Invoke(); return true; }

        var registry = GameManager.Instance.dataManager.GET_GROWTH_REGISTRY();
        if (registry == null) return false;

        MinionDataSO targetMinion = registry.minionDatas.Find(m => m.minionType == job);
        if (targetMinion == null) return false;

        return EquipMinion(minion: targetMinion, amount: amount);
    }

    /// <summary>
    /// 소환수를 자기 역할의 슬롯에 장착한다. 슬롯은 역할당 1칸이므로 기존 것을 덮어쓴다.
    /// </summary>
    public bool EquipMinion(MinionDataSO minion, int amount = 1)
    {
        if (minion == null) return false;
        return EquipMinion(SlotIndexOf(minion), minion, amount);
    }

    public bool EquipMinion(int slotIndex, MinionDataSO minion, int amount = 1)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count || Slots[slotIndex].IsShattered) return false;

        // 역할과 슬롯이 어긋나면 역할 쪽 슬롯으로 돌려보낸다 (서브 카드가 메인 슬롯에 앉는 것을 방지).
        // 소환수가 아니면(적 데이터가 흘러들어오면) SlotIndexOf 가 -1 이라 여기서 걸러진다.
        if (minion != null && SlotIndexOf(minion) != slotIndex)
        {
            slotIndex = SlotIndexOf(minion);
            if (slotIndex < 0 || slotIndex >= Slots.Count || Slots[slotIndex].IsShattered) return false;
        }

        Slots[slotIndex].EquippedMinion = minion;
        Slots[slotIndex].Quantity = amount;

        OnMinionUpdated?.Invoke();
        return true;
    }

    public void ShatterSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Slots.Count)
        {
            Slots[slotIndex].IsShattered = true;
            Slots[slotIndex].EquippedMinion = null;
        }
    }
    #endregion

    public bool HasMinionInSlots(MinionDataSO minion) => Slots.Exists(s => s.EquippedMinion == minion);
    public bool HasJobInSlots(CommandData job) => Slots.Exists(s => s.EquippedMinion != null && s.EquippedMinion.minionType == job);

    public void AddTreasure(TreasureSO treasure)
    {
        if (TreasureStacks.ContainsKey(treasure)) TreasureStacks[treasure]++;
        else TreasureStacks[treasure] = 1;
    }

    public float GetTreasureBonus(TreasureEffectType type)
    {
        float totalBonus = 0f;
        foreach (var kvp in TreasureStacks)
        {
            if (kvp.Key.effectType == type) totalBonus += kvp.Key.valuePerStack * kvp.Value;
        }
        return totalBonus;
    }

    #region Save / Load Serialization
    public void SaveToData(SaveData data)
    {
        data.gold = gold;
        data.augmentMaxHpBonus = augmentMaxHpBonus;

        // Slots 저장
        data.slots.Clear();
        foreach (var slot in Slots)
        {
            var slotData = new CoreSlotSaveData();
            slotData.isShattered = slot.IsShattered;
            slotData.equippedMinionName = slot.EquippedMinion != null ? slot.EquippedMinion.name : "";
            slotData.evolutionIndex = 0;
            slotData.quantity = slot.Quantity;
            data.slots.Add(slotData);
        }

        // Treasures 저장
        data.treasures.Clear();
        foreach (var kvp in TreasureStacks)
        {
            if (kvp.Key == null) continue;
            var treasureData = new TreasureSaveData();
            treasureData.treasureSOAddress = kvp.Key.name;
            treasureData.stackCount = kvp.Value;
            data.treasures.Add(treasureData);
        }

    }

    public void LoadFromData(SaveData data)
    {
        gold = data.gold;
        augmentMaxHpBonus = data.augmentMaxHpBonus; // 증강 도입 전 세이브는 0 → 보정 없음

        var registry = GameManager.Instance.dataManager.GET_GROWTH_REGISTRY();
        if (registry == null)
        {
            Debug.LogError("[InventoryManager] GrowthRegistrySO is missing during LoadFromData!");
            return;
        }

        // Slots 로드
        Slots.Clear();
        for (int i = 0; i < data.slots.Count; i++)
        {
            var slotData = data.slots[i];
            var coreSlot = new CoreSlot();
            coreSlot.IsShattered = slotData.isShattered;

            if (!string.IsNullOrEmpty(slotData.equippedMinionName))
            {
                // 에셋 이름으로 정확히 복원한다 (직업으로 찾으면 A/B/C 배리언트가 붕괴됨).
                coreSlot.EquippedMinion = registry.minionDatas.Find(m => m.name == slotData.equippedMinionName);
                if (coreSlot.EquippedMinion == null)
                    Debug.LogWarning($"<color=orange>[InventoryManager]</color> 세이브의 미니언 '{slotData.equippedMinionName}' 을 GrowthRegistry 에서 찾지 못했습니다. 슬롯을 비웁니다.");
            }


            coreSlot.Quantity = slotData.quantity;
            Slots.Add(coreSlot);
        }
        // 메인/서브 2칸 보장. (예전엔 Initialize 가 3칸, 여기가 10칸으로 패딩이 어긋나 있었고,
        //  UI 가 안 보여주는 슬롯에 미니언이 들어가는 조용한 버그가 있었다.)
        while (Slots.Count < SLOT_COUNT)
        {
            Slots.Add(new CoreSlot());
        }
        if (Slots.Count > SLOT_COUNT) Slots.RemoveRange(SLOT_COUNT, Slots.Count - SLOT_COUNT);

        // 세이브는 슬롯 순서대로 이름만 복원할 뿐 역할을 검증하지 않는다. 슬롯이 자유 배치였던
        // 구버전 세이브면 메인이 0/1번에 둘 다 들어앉은 채로 살아남을 수 있고, 그러면 SubSummon 이
        // 메인을 가리켜 서브 패시브가 전부 조용히 0이 된다 (SubSummonPassiveController 는 null 을 0f 로 흘린다).
        // 장착 경로는 EquipMinion 이 막아주지만 이 경로는 그걸 우회하므로 여기서 한 번 더 거른다.
        for (int i = 0; i < Slots.Count; i++)
        {
            var m = Slots[i].EquippedMinion;
            if (m != null && SlotIndexOf(m) != i)
            {
                Debug.LogWarning($"<color=orange>[InventoryManager]</color> 세이브의 '{m.name}' 이 역할과 안 맞는 슬롯 {i} 에 있습니다. 비웁니다.");
                Slots[i].EquippedMinion = null;
            }
        }


        // Treasures 로드
        TreasureStacks.Clear();
        foreach (var treasureData in data.treasures)
        {
            var treasure = registry.treasures.Find(t => t.name == treasureData.treasureSOAddress || t.itemName == treasureData.treasureSOAddress);
            if (treasure != null)
            {
                TreasureStacks[treasure] = treasureData.stackCount;
            }
        }

        // 세이브 데이터를 복원한 후 디버그 아이템들을 추가 주입 (중복 항목은 제외)
        if (useDebugStartingInventory)
        {
            Debug_InitializeInventory();
        }

        OnMinionUpdated?.Invoke();
    }

    private bool HasMinion(CommandData jobType)
    {
        return Slots.Exists(s => s.EquippedMinion != null && s.EquippedMinion.minionType == jobType);
    }
    #endregion
}
