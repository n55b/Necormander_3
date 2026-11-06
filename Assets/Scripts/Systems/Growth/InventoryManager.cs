using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 슬롯과 보물 인벤토리를 관리하는 핵심 매니저입니다.
/// </summary>
public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;

    /// <summary>
    /// 소환수 슬롯 하나. 소환수를 최대 2개 장착하고 그중 1개가 활성 소환수다.
    /// </summary>
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

    /// <summary>소환수 슬롯 인덱스. 세이브 호환을 위해 0번은 옛 메인 소환수 칸 그대로다.</summary>
    public const int SLOT_MAIN = 0;
    public const int SLOT_SECOND = 1;
    public const int SLOT_COUNT = 2;

    [Header("슬롯 시스템 (소환수 2칸)")]
    public List<CoreSlot> Slots = new List<CoreSlot>(SLOT_COUNT);

    [Tooltip("기본 공격 추가타와 우클릭 스킬을 담당하는 활성 소환수 슬롯.")]
    [SerializeField, Range(0, SLOT_COUNT - 1)] private int activeSummonSlot = SLOT_MAIN;
    public int ActiveSummonSlot => activeSummonSlot;

    /// <summary>활성 소환수. 평타/스킬/강화/진화의 기존 경로는 전부 이 값을 읽는다.</summary>
    public MainMinionDataSO MainSummon => GetSummonAt(activeSummonSlot);

    public MainMinionDataSO GetSummonAt(int index) => GetSummon(index) as MainMinionDataSO;

    public int EquippedSummonCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < SLOT_COUNT; i++) if (GetSummon(i) != null) count++;
            return count;
        }
    }

    /// <summary>활성 슬롯을 바꾼다. 빈 슬롯은 활성으로 둘 수 없다.</summary>
    public bool SetActiveSummonSlot(int slot)
    {
        if (GetSummon(slot) == null) return false;
        if (activeSummonSlot == slot) return true;
        activeSummonSlot = slot;
        OnMinionUpdated?.Invoke();
        return true;
    }

    private void NormalizeActiveSlot()
    {
        activeSummonSlot = Mathf.Clamp(activeSummonSlot, 0, SLOT_COUNT - 1);
        if (GetSummon(activeSummonSlot) != null) return;
        for (int i = 0; i < SLOT_COUNT; i++)
            if (GetSummon(i) != null) { activeSummonSlot = i; return; }
    }

    // ── 같은 소환수(진화 계열) 판정 ─────────────────────────────────
    /// <summary>진화 계열의 시작 형태. 레지스트리에 등록된 evolutions 연결을 거슬러 올라간다.</summary>
    private static MainMinionDataSO FamilyRoot(MainMinionDataSO minion)
    {
        var registry = GameManager.Instance != null && GameManager.Instance.dataManager != null
            ? GameManager.Instance.dataManager.GET_GROWTH_REGISTRY() : null;
        if (registry == null || registry.minionDatas == null) return minion;
        var current = minion;
        for (int depth = 0; depth < 8 && current != null; depth++)
        {
            MainMinionDataSO parent = null;
            foreach (var entry in registry.minionDatas)
                if (entry is MainMinionDataSO candidate && candidate != current && candidate.evolutions != null
                    && System.Array.IndexOf(candidate.evolutions, current) >= 0) { parent = candidate; break; }
            if (parent == null) break;
            current = parent;
        }
        return current;
    }

    private static bool EvolvesInto(MainMinionDataSO from, MainMinionDataSO to, int depth = 0)
    {
        if (from == null || from.evolutions == null || depth > 8) return false;
        foreach (var next in from.evolutions)
            if (next == to || EvolvesInto(next, to, depth + 1)) return true;
        return false;
    }

    /// <summary>같은 소환수인가. 전사·기사·용사·무사·장수처럼 진화 계열이 같으면 같은 소환수다.</summary>
    public static bool IsSameFamily(MinionDataSO a, MinionDataSO b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        if (!(a is MainMinionDataSO ma) || !(b is MainMinionDataSO mb)) return false;
        return EvolvesInto(ma, mb) || EvolvesInto(mb, ma) || FamilyRoot(ma) == FamilyRoot(mb);
    }

    /// <summary>같은 계열 소환수가 장착된 슬롯. 없으면 -1. ignoreSlot 은 비교에서 뺀다.</summary>
    public int FindFamilySlot(MinionDataSO minion, int ignoreSlot = -1)
    {
        for (int i = 0; i < SLOT_COUNT; i++)
            if (i != ignoreSlot && IsSameFamily(GetSummon(i), minion)) return i;
        return -1;
    }

    public bool HasSameFamilyEquipped(MinionDataSO minion) => FindFamilySlot(minion) >= 0;

    public int FindEmptySummonSlot()
    {
        for (int i = 0; i < SLOT_COUNT; i++)
            if (i < Slots.Count && !Slots[i].IsShattered && Slots[i].EquippedMinion == null) return i;
        return -1;
    }

    // ── 소환수 강화(보상방) ─────────────────────────────────────────
    // 런 단위 진행도라 런 세이브(SaveData)에 같이 들어간다 — 죽으면 세이브와 함께 0 으로 돌아간다.
    // 강화는 슬롯의 소환수에 붙는다. 다른 소환수로 바뀌면 0 부터 다시 시작한다(EquipMinion 참조).
    [Header("소환수 강화")]
    [SerializeField] private int[] summonEnhanceLevels = new int[SLOT_COUNT];

    /// <summary>활성 소환수의 강화 단계(0 ~ MinionEnhance.MAX_LEVEL).</summary>
    public int MainSummonEnhanceLevel => summonEnhanceLevels[activeSummonSlot];

    /// <summary>이 소환수가 장착된 슬롯의 강화 단계. 장착 중이 아니면 0.</summary>
    public int GetEnhanceLevelOf(MainMinionDataSO minion)
    {
        for (int i = 0; i < SLOT_COUNT; i++)
            if (minion != null && GetSummon(i) == minion) return summonEnhanceLevels[i];
        return 0;
    }

    /// <summary>보상방에서 강화를 더 받을 수 있는가.</summary>
    public bool CanEnhanceMainSummon
        => MainSummon != null && MainSummonEnhanceLevel < MainSummon.MaxEnhanceLevel;

    /// <summary>활성 소환수를 한 단계 강화한다. 성공하면 true.</summary>
    public bool EnhanceMainSummon()
    {
        if (!CanEnhanceMainSummon) return false;
        summonEnhanceLevels[activeSummonSlot]++;
        PolishFeedback.AtPlayer(FeedbackCue.Upgrade, PolishFeedback.MinionTier());
        Debug.Log($"<color=cyan>[InventoryManager]</color> 소환수 강화: {MainSummon.minionName} +{MainSummonEnhanceLevel}/{MainSummon.MaxEnhanceLevel}");
        OnMinionUpdated?.Invoke();
        return true;
    }

    /// <summary>강화 단계를 0 으로 되돌린다. 마을에서 시작 소환수를 새로 고를 때 쓴다.</summary>
    public void ResetMainSummonEnhance()
    {
        if (MainSummonEnhanceLevel == 0) return;
        summonEnhanceLevels[activeSummonSlot] = 0;
        OnMinionUpdated?.Invoke();
    }

    // ── 메인 소환수 진화(보상방) ─────────────────────────────────────
    // 진화 = 메인 슬롯의 MainMinionDataSO 를 그 소환수의 evolutions 중 하나로 갈아끼우는 것.
    // 평타/대쉬/스킬은 전부 OnMinionUpdated → PlayerSkillController.SyncWithInventory 로 새 데이터를 읽는다.
    [Header("메인 소환수 진화")]
    [Tooltip("진화에 필요한 강화 단계(모든 소환수 공통). 소환수별로 다르게 하려면\n" +
             "MainMinionDataSO.evolveRequiredLevelOverride 를 쓴다. 0 이면 강화 없이 바로 진화할 수 있다.\n" +
             "소환수의 최대 강화 단계보다 크게 넣어도 최대 단계로 잘린다(진화가 막히지 않게).")]
    [SerializeField, Min(0)] private int defaultEvolveRequiredLevel = 0; // [26/10/22] 강화 없이 첫 보상방에서 바로 진화

    [Tooltip("진화하면 강화 단계를 0 으로 되돌린다(진화형은 자기 강화표로 다시 쌓는다).\n" +
             "끄면 단계를 이어받는다(진화형의 최대 단계를 넘으면 잘린다).")]
    [SerializeField] private bool resetEnhanceOnEvolve = true;

    /// <summary>이 소환수가 진화하려면 필요한 강화 단계. 개별 오버라이드가 없으면 공통값.</summary>
    public int GetEvolveRequiredLevel(MainMinionDataSO minion)
    {
        if (minion == null) return int.MaxValue;
        int req = minion.evolveRequiredLevelOverride >= 0 ? minion.evolveRequiredLevelOverride : defaultEvolveRequiredLevel;
        return Mathf.Min(req, minion.MaxEnhanceLevel);
    }

    /// <summary>지금 메인 소환수가 진화할 수 있는가(진화형이 연결돼 있고 요구 강화 단계를 채웠는가).</summary>
    public bool CanEvolveMainSummon
        => MainSummon != null && MainSummon.CanEvolve && MainSummonEnhanceLevel >= GetEvolveRequiredLevel(MainSummon);

    /// <summary>메인 소환수를 target 으로 진화시킨다. target 은 현재 소환수의 evolutions 중 하나여야 한다.</summary>
    public bool EvolveMainSummon(MainMinionDataSO target)
    {
        var from = MainSummon;
        if (!CanEvolveMainSummon || target == null || target == from) return false;
        if (System.Array.IndexOf(from.evolutions, target) < 0) return false;

        // EquipMinion 을 쓰지 않는다 — 거기선 소환수가 바뀌면 강화를 무조건 0 으로 돌린다.
        Slots[activeSummonSlot].EquippedMinion = target;
        PolishFeedback.AtPlayer(FeedbackCue.Evolve, target.feedbackTier, target.minionIcon);
        summonEnhanceLevels[activeSummonSlot] = resetEnhanceOnEvolve ? 0 : Mathf.Min(MainSummonEnhanceLevel, target.MaxEnhanceLevel);

        Debug.Log($"<color=cyan>[InventoryManager]</color> 메인 소환수 진화: {from.minionName} → {target.minionName} (강화 {MainSummonEnhanceLevel})");
        OnMinionUpdated?.Invoke();
        return true;
    }

    private MinionDataSO GetSummon(int index)
        => (index >= 0 && index < Slots.Count && !Slots[index].IsShattered) ? Slots[index].EquippedMinion : null;

    // ======================================================
    // [에러 방지를 위한 병렬 리스트 디버그 설정]
    [Header("디버그 시작 아이템")]
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

        // 세이브에서 복원한 소환수(마을에서 고른 것, 진화형)가 있으면 디버그 소환수를 끼워 넣지 않는다.
        if (EquippedSummonCount > 0) return;

        for (int i = 0; i < debugStartingMinions.Count; i++)
        {
            if (debugStartingMinions[i] == null) continue;
            int qty = (i < debugStartingMinionQuantities.Count) ? debugStartingMinionQuantities[i] : 1;

            // [개선] Registry 에셋 등록 상태와 무관하게 인스펙터에 직접 지정된 소환수를 장착합니다.
            // 같은 계열이나 칸이 가득 찬 경우 EquipMinion 이 거절한다.
            EquipMinion(debugStartingMinions[i], qty);
        }

        // [수정] 유저 요청에 의해 미니언이 없을 때 기본 전사 1마리를 추가하는 로직을 제거(주석 처리)합니다.
        // if (!Slots.Exists(s => s.EquippedMinion != null)) AddMinionOrIncreaseQuantity(CommandData.SkeletonWarrior);

        // [추가] 디버그용 시작 미니언 적재가 끝나면 갱신 이벤트를 격발하여 동기화시킵니다.
        OnMinionUpdated?.Invoke();
    }

    #region Gold System
    [Header("골드 획득 텍스트")]
    [Tooltip("GainGold 로 골드를 얻었을 때 플레이어 앞에 띄울 텍스트 형식. {0} 자리에 획득량이 들어간다.")]
    [SerializeField] private string goldGainTextFormat = "+{0}G";
    [Tooltip("골드 획득 텍스트 색. 증강 보상 텍스트와 맞추려면 흰색.")]
    [SerializeField] private Color goldGainTextColor = Color.white;

    /// <summary>골드만 더한다(텍스트 없음). 환불·세이브 복원처럼 '획득'이 아닌 경로용.</summary>
    public void AddGold(int amount)
    {
        gold += amount;
        if (amount > 0 && PolishFeedback.Player != null)
            PolishFeedback.PlaySound(FeedbackCue.GoldPickup, PolishFeedback.Player.transform.position);
    }

    /// <summary>골드를 '획득'한다. 더한 뒤 증강 방 보상처럼 플레이어 앞에 "+NG" 텍스트를 띄운다.</summary>
    public void GainGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        ShowGoldGainText(amount);
    }

    /// <summary>증강 보상 텍스트와 같은 공용 경로(FloatingTextManager.ShowOnPlayer)로 띄운다.
    /// 증강 방 골드 보상이 기본 골드와 합산한 금액을 한 번만 띄울 때도 쓴다(ActiveAugment).</summary>
    public void ShowGoldGainText(int amount)
        => FloatingTextManager.ShowOnPlayer(string.Format(goldGainTextFormat, amount), goldGainTextColor);

#if UNITY_EDITOR
    /// <summary>인스펙터에서 형식 문자열을 잘못 넣으면(예: {1}) 런타임 FormatException 대신 여기서 경고하고 되돌린다.</summary>
    private void OnValidate()
    {
        const string fallback = "+{0}G";
        try
        {
            string.Format(goldGainTextFormat ?? string.Empty, 0);
            if (string.IsNullOrEmpty(goldGainTextFormat) || !goldGainTextFormat.Contains("{0"))
                Debug.LogWarning($"{name}: goldGainTextFormat 에 {{0}} 이 없어 획득량이 표시되지 않습니다.", this);
        }
        catch (System.FormatException)
        {
            Debug.LogWarning($"{name}: goldGainTextFormat '{goldGainTextFormat}' 형식이 잘못돼 '{fallback}' 로 되돌립니다.", this);
            goldGainTextFormat = fallback;
        }
    }
#endif
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
    /// 소환수를 빈 슬롯에 장착한다. 같은 계열을 이미 장착했거나 빈 칸이 없으면 false —
    /// 칸이 가득 찼을 때는 교체 UI(MainMinionSelectionUI)가 슬롯을 지정해 아래 오버로드를 부른다.
    /// </summary>
    public bool EquipMinion(MinionDataSO minion, int amount = 1)
    {
        if (minion == null || HasSameFamilyEquipped(minion)) return false;
        int slot = FindEmptySummonSlot();
        return slot >= 0 && EquipMinion(slot, minion, amount);
    }

    /// <summary>
    /// 마을 NPC 의 시작 소환수 선택. 같은 계열이 있으면 그 칸, 빈 칸이 있으면 빈 칸, 가득 차면 활성 칸을 바꾸고
    /// 그 칸의 강화를 0 부터 다시 시작한다.
    /// </summary>
    public bool EquipStartingSummon(MinionDataSO minion)
    {
        if (minion == null) return false;
        int slot = FindFamilySlot(minion);
        if (slot < 0) slot = FindEmptySummonSlot();
        if (slot < 0) slot = activeSummonSlot;
        if (!EquipMinion(slot, minion)) return false;
        summonEnhanceLevels[slot] = 0;
        activeSummonSlot = slot;
        OnMinionUpdated?.Invoke();
        return true;
    }

    public bool EquipMinion(int slotIndex, MinionDataSO minion, int amount = 1)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count || Slots[slotIndex].IsShattered) return false;

        // 소환수가 아니면(적 데이터가 흘러들어오면) 거른다. 다른 칸에 같은 계열이 있으면 중복 장착이다.
        if (minion != null && !(minion is MainMinionDataSO)) return false;
        if (minion != null && FindFamilySlot(minion, slotIndex) >= 0) return false;

        // 다른 소환수로 갈아끼우면 강화는 새 소환수에 따라가지 않는다.
        if (slotIndex < SLOT_COUNT && Slots[slotIndex].EquippedMinion != minion) summonEnhanceLevels[slotIndex] = 0;

        Slots[slotIndex].EquippedMinion = minion;
        Slots[slotIndex].Quantity = amount;
        NormalizeActiveSlot();

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

    public bool HasMinionInSlots(MinionDataSO minion) => HasSameFamilyEquipped(minion);
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
        data.mainSummonEnhanceLevel = summonEnhanceLevels[SLOT_MAIN];
        data.secondSummonEnhanceLevel = summonEnhanceLevels[SLOT_SECOND];
        data.activeSummonSlot = activeSummonSlot;

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
                coreSlot.EquippedMinion = registry.minionDatas.Find(m => m != null && m.name == slotData.equippedMinionName)
                        // [26/10/22] 에셋 이름을 바꾼 소환수는 예전 이름으로 저장된 세이브도 찾아 준다.
                        ?? registry.minionDatas.Find(m => m is MainMinionDataSO mm && mm.MatchesSaveKey(slotData.equippedMinionName));
                if (coreSlot.EquippedMinion == null)
                    Debug.LogWarning($"<color=orange>[InventoryManager]</color> 세이브의 미니언 '{slotData.equippedMinionName}' 을 GrowthRegistry 에서 찾지 못했습니다. 슬롯을 비웁니다.");
            }


            coreSlot.Quantity = slotData.quantity;
            Slots.Add(coreSlot);
        }
        // 소환수 2칸 보장.
        while (Slots.Count < SLOT_COUNT)
        {
            Slots.Add(new CoreSlot());
        }
        if (Slots.Count > SLOT_COUNT) Slots.RemoveRange(SLOT_COUNT, Slots.Count - SLOT_COUNT);

        // 세이브는 슬롯 순서대로 이름만 복원할 뿐 검증하지 않는다. 장착 경로를 우회하므로 여기서 거른다.
        for (int i = 0; i < Slots.Count; i++)
        {
            var m = Slots[i].EquippedMinion;
            if (m == null) continue;
            if (!(m is MainMinionDataSO) || (i == SLOT_SECOND && IsSameFamily(Slots[SLOT_MAIN].EquippedMinion, m)))
            {
                Debug.LogWarning($"<color=orange>[InventoryManager]</color> 세이브의 '{m.name}' 을 슬롯 {i} 에 장착할 수 없습니다. 비웁니다.");
                Slots[i].EquippedMinion = null;
            }
        }

        // 강화 단계 복원. 필드가 없던 옛 세이브는 2번째 칸 0, 활성 슬롯 0 으로 읽힌다.
        activeSummonSlot = data.activeSummonSlot;
        NormalizeActiveSlot();
        var first = GetSummonAt(SLOT_MAIN);
        var second = GetSummonAt(SLOT_SECOND);
        summonEnhanceLevels[SLOT_MAIN] = first != null ? Mathf.Clamp(data.mainSummonEnhanceLevel, 0, first.MaxEnhanceLevel) : 0;
        summonEnhanceLevels[SLOT_SECOND] = second != null ? Mathf.Clamp(data.secondSummonEnhanceLevel, 0, second.MaxEnhanceLevel) : 0;

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
