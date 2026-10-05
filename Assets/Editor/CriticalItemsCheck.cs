using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>10월 치명타 아이템 등록과 격리된 전투 경로 검사. 씬/사용자 세이브는 저장하지 않는다.</summary>
public static class CriticalItemsCheck
{
    private const string Folder = "Assets/SOData/Items/";
    private const string GrowthPath = "Assets/SOData/Registry/Growth Reward Registry.asset";
    private const string ShopPath = "Assets/SOData/Registry/Shop Registry.asset";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    public static void SetupAndRun() { CreateMissingItems(); Run(); }

    [MenuItem("Tools/Items/Create Missing October Critical Items")]
    public static void CreateMissingItems()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("플레이를 끈 뒤 실행하세요.");
        var growth = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(GrowthPath);
        var shop = AssetDatabase.LoadAssetAtPath<ShopRegistrySO>(ShopPath);
        if (growth == null || shop == null) throw new Exception("아이템 레지스트리가 없습니다.");

        ItemStatEffect Stat(StatType type, float value) => new ItemStatEffect { statType = type, value = value };
        void Add(string id, string title, string description, ItemTier tier, params ItemEffect[] effects)
        {
            string path = Folder + id + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
            // 이후 기획자가 조절한 값/아이콘을 다시 덮어쓰지 않는다.
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemSO>();
                item.itemName = title;
                item.description = description;
                item.tier = tier;
                item.effects.AddRange(effects);
                AssetDatabase.CreateAsset(item, path);
            }
            if (!growth.items.Contains(item)) { growth.items.Add(item); EditorUtility.SetDirty(growth); }
            if (!shop.itemPool.Contains(item)) { shop.itemPool.Add(item); EditorUtility.SetDirty(shop); }
        }

        Add("Whetstone", "숫돌", "치명타 확률이 10%p 증가합니다.", ItemTier.Tier4, Stat(StatType.CritChance, 10f));
        Add("CrackedWhetstone", "갈라진 숫돌", "치명타 피해가 15%p 증가합니다.", ItemTier.Tier4, Stat(StatType.CritDamage, 15f));
        Add("BalanceWeight", "균형의 추", "치명타가 아닌 공격이 명중할 때마다 치명타 확률이 5%p 증가합니다. 치명타 명중 시 초기화됩니다.",
            ItemTier.Tier4, new ItemCritPityEffect());
        Add("LuckyCoin", "럭키 코인", "치명타 명중 시 대상마다 5골드를 획득합니다.", ItemTier.Tier4, new ItemCritGoldEffect());
        Add("VitalStrike", "급소타격", "치명타 확률이 25%p 증가합니다.", ItemTier.Tier3, Stat(StatType.CritChance, 25f));
        Add("DeadlyCounter", "치명적인 반격", "가드 성공 후 10초 동안 치명타 확률과 치명타 피해가 각각 15%p 증가합니다. 재성공 시 지속시간이 갱신됩니다.",
            ItemTier.Tier3, new ItemGuardCritEffect());
        Add("SeventeenToOne", "17대 1", "치명타 확률이 15%p 증가합니다. 반경 2유닛 안에 적이 3명 이상이면 치명타 피해가 25%p 증가합니다.",
            ItemTier.Tier3, Stat(StatType.CritChance, 15f), new ItemNearbyCritEffect());
        AssetDatabase.SaveAssetIfDirty(growth);
        AssetDatabase.SaveAssetIfDirty(shop);
    }

    [MenuItem("Tools/Items/Verify Critical Items")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("플레이를 끈 뒤 실행하세요.");
        _checks = 0;
        var scene = EditorSceneManager.NewPreviewScene();
        var oldManager = GameManager.Instance;
        var oldInventory = InventoryManager.Instance;
        var oldPouch = ItemPouch.Instance;
        var oldRandom = UnityEngine.Random.state;
        var oldEnemies = CharacterStatus.ActiveEnemies;
        CharacterStatus.ActiveEnemies = new System.Collections.Generic.List<CharacterStatus>();
        ItemPouch pouch = null;
        try
        {
            GameObject Make(string name)
            {
                var go = new GameObject(name);
                go.SetActive(false);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }
            CharacterStat Stats(GameObject go, bool player = false)
            {
                var stat = go.AddComponent<CharacterStat>();
                Set(stat, "_isPlayer", player);
                var health = go.GetComponent<CharacterHealth>();
                var status = go.GetComponent<CharacterStatus>();
                typeof(CharacterStat).GetProperty("Health").SetValue(stat, health);
                typeof(CharacterStat).GetProperty("Status").SetValue(stat, status);
                Set(stat, "baseMaxHP", 10000f);
                health.Init(stat, status);
                return stat;
            }

            var host = Make("Critical items player");
            var playerStat = Stats(host, true);
            var player = host.AddComponent<PlayerController>();
            Set(player, "stat", playerStat);
            var guard = host.AddComponent<PlayerParryController>();
            Set(guard, "_activeSelf", playerStat.Health);
            Set(guard, "_guard", 100f);
            var manager = host.AddComponent<GameManager>();
            Set(manager, "playerController", player);
            var data = host.AddComponent<DataManager>();
            var growth = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(GrowthPath);
            var shop = AssetDatabase.LoadAssetAtPath<ShopRegistrySO>(ShopPath);
            Set(data, "growthRegistry", growth);
            manager.dataManager = data;
            GameManager.Instance = manager;
            InventoryManager.Instance = host.AddComponent<InventoryManager>();
            pouch = host.AddComponent<ItemPouch>();
            ItemPouch.Instance = pouch;
            Set(pouch, "slotCount", 9);
            Call(pouch, "OnEnable");

            var enemy = Stats(Make("Critical items target"));
            var caster = Make("Minion skill caster (no stats)");
            bool lastCritical = false;
            enemy.Health.TakeDamageEvent += (_, __, ___, critical) => lastCritical = critical;
            void Hit(DamageCategory category = DamageCategory.Skill, GameObject attacker = null, DamageType type = DamageType.Physical, float amount = 1f)
                => enemy.Health.GetDamage(new DamageInfo(amount, type, attacker != null ? attacker : host, category: category));
            ItemSO Item(string id)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemSO>(Folder + id + ".asset");
                Check(item != null && growth.items.Contains(item) && shop.itemPool.Contains(item), "에셋/보상/상점 등록: " + id);
                return item;
            }
            void Clear()
            {
                for (int i = 0; i < pouch.SlotCount; i++) pouch.RemoveAt(i);
                Set(playerStat, "baseCritChance", 0f);
            }

            var stone = Item("Whetstone");
            var cracked = Item("CrackedWhetstone");
            var vital = Item("VitalStrike");
            Check(stone.tier == ItemTier.Tier4 && vital.tier == ItemTier.Tier3, "저티어4 / 중티어3");
            pouch.TryAdd(stone); pouch.TryAdd(cracked); pouch.TryAdd(vital);
            Equal(playerStat.CRIT_CHANCE, 35f, "확률 +10/+25%p");
            Equal(playerStat.CRIT_DAMAGE, 165f, "피해 +15%p");
            pouch.Refresh(); pouch.Refresh();
            Equal(playerStat.CRIT_CHANCE, 35f, "Refresh 멱등");
            Clear();
            Equal(playerStat.CRIT_CHANCE, 0f, "제거 시 상시 효과 회수");

            var pity = Item("BalanceWeight");
            var coin = Item("LuckyCoin");
            pouch.TryAdd(pity); pouch.TryAdd(coin);
            Hit();
            Check(!lastCritical, "첫 0% 명중은 비치명타");
            Equal(playerStat.CRIT_CHANCE, 5f, "대상별 비치명타 누적");
            pouch.Swap(0, 5);
            Equal(playerStat.CRIT_CHANCE, 5f, "주머니 이동 시 누적 유지");
            float goldBefore = InventoryManager.Instance.GOLD;
            Set(playerStat, "baseCritChance", 100f);
            foreach (var category in new[] { DamageCategory.BasicAttack, DamageCategory.Skill, DamageCategory.DashAttack, DamageCategory.Parry })
                Hit(category);
            Hit(DamageCategory.Skill, caster); // 미니언의 기존 플레이어 스탯 폴백
            Check(lastCritical, "미니언 시전자도 실제 치명타");
            Equal(InventoryManager.Instance.GOLD, goldBefore + 25f, "기존 치명타 범위, 대상별 +5G");
            Set(playerStat, "baseCritChance", 0f);
            Equal(playerStat.CRIT_CHANCE, 0f, "치명타에서 누적 초기화");
            Hit(type: DamageType.Fixed);
            Hit(DamageCategory.Debuff, type: DamageType.Physical);
            Hit(amount: 0f);
            enemy.Health.Invincible = true; Hit(); enemy.Health.Invincible = false;
            Set(enemy, "baseEvasion", 1f); Hit(DamageCategory.BasicAttack); Set(enemy, "baseEvasion", 0f);
            Set(playerStat, "baseCritChance", 100f);
            enemy.Health.CalculateGuardDamage(new DamageInfo(10f, attacker: host, category: DamageCategory.Skill));
            Set(playerStat, "baseCritChance", 0f);
            Equal(playerStat.CRIT_CHANCE, 0f, "고정/디버프/0피해/무적/회피/가드 계산은 누적 안 함");
            Equal(InventoryManager.Instance.GOLD, goldBefore + 25f, "가드 계산은 골드를 주지 않음");
            Hit(); pouch.RemoveAt(5); pouch.TryAdd(pity);
            Equal(playerStat.CRIT_CHANCE, 0f, "버린 뒤 다시 먹으면 누적 초기화");
            Clear();

            pouch.TryAdd(coin); pouch.TryAdd(coin);
            Set(playerStat, "baseCritChance", 100f);
            goldBefore = InventoryManager.Instance.GOLD;
            Hit(); Hit(); Hit();
            Equal(InventoryManager.Instance.GOLD, goldBefore + 30f, "중복 아이템 / 다중 명중 합산");
            var shield = enemy.Status;
            shield.AddShield(100f, 5f);
            float hpBeforeShieldHit = enemy.Health.CurHP;
            Hit();
            Equal(enemy.Health.CurHP, hpBeforeShieldHit, "보호막이 피해 흡수");
            Equal(InventoryManager.Instance.GOLD, goldBefore + 40f, "보호막 명중도 대상별 한 번만 발동");
            Clear();

            var counter = Item("DeadlyCounter");
            pouch.TryAdd(counter);
            Call(guard, "Block", new DamageInfo(0f));
            Equal(playerStat.CRIT_CHANCE, 15f, "실제 가드 성공 이벤트 연결");
            Equal(playerStat.CRIT_DAMAGE, 165f, "가드 피해 버프");
            Call(guard, "Block", new DamageInfo(0f));
            Equal(playerStat.CRIT_CHANCE, 15f, "가드 재성공은 중첩하지 않음");
            var timestamps = (float[])Get(pouch, "_lastGuardAt");
            timestamps[0] = Time.time - 11f; Call(pouch, "Update");
            Equal(playerStat.CRIT_CHANCE, 0f, "10초 버프 만료");
            Call(guard, "Block", new DamageInfo(0f));
            pouch.RemoveAt(0);
            Equal(playerStat.CRIT_DAMAGE, 150f, "버프 도중 버리면 즉시 해제");

            var nearby = Item("SeventeenToOne");
            pouch.TryAdd(nearby);
            var targets = new CharacterStat[3];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = Stats(Make("Nearby target " + i));
                targets[i].transform.position = new Vector3(1.9f, 0f, 0f);
                targets[i].gameObject.SetActive(true);
                CharacterStatus.ActiveEnemies.Add(targets[i].Status);
            }
            Call(pouch, "Update");
            Equal(playerStat.CRIT_CHANCE, 15f, "17대1 상시 확률");
            Equal(playerStat.CRIT_DAMAGE, 175f, "2유닛 내 3명 조건");
            targets[2].transform.position = new Vector3(2.1f, 0f, 0f); Call(pouch, "Update");
            Equal(playerStat.CRIT_DAMAGE, 150f, "영역 이탈 즉시 해제");
            targets[2].transform.position = Vector3.zero; targets[2].Health.SetHP(0f); Call(pouch, "Update");
            Equal(playerStat.CRIT_DAMAGE, 150f, "죽은 적은 수에 미포함");
            targets[2].Health.ResetHP(); Call(pouch, "Update");
            Equal(playerStat.CRIT_DAMAGE, 175f, "재진입 조건 재적용");

            var save = new SaveData(); pouch.SaveToData(save); Clear(); pouch.LoadFromData(save);
            Check(pouch.Get(0) == nearby, "기존 이름 기반 세이브 로드");
            pouch.RemoveAt(0);
            pouch.TryAdd(counter); Call(guard, "Block", new DamageInfo(0f));
            Call(pouch, "OnDisable");
            Equal(playerStat.CRIT_CHANCE, 0f, "비활성화 시 조건부 버프 회수");
            Call(guard, "Block", new DamageInfo(0f));
            Equal(playerStat.CRIT_CHANCE, 0f, "비활성화 후 이벤트 해제");
            Call(pouch, "OnEnable");
            Equal(playerStat.CRIT_CHANCE, 0f, "재활성화 시 오래된 버프 없음");
            Call(guard, "Block", new DamageInfo(0f));
            Equal(playerStat.CRIT_CHANCE, 15f, "재활성화 후 이벤트 1회 연결");
            playerStat.Health.SetHP(0f); Call(pouch, "Update");
            Equal(playerStat.CRIT_CHANCE, 0f, "사망 시 임시 버프 정리");
            Debug.Log($"[CriticalItemsCheck] PASS: {_checks} assertions; 7 assets, registries, real hit/crit pipeline, guard, proximity, removal, save/load, lifecycle.");
        }
        finally
        {
            if (pouch != null) Call(pouch, "OnDisable");
            EditorSceneManager.ClosePreviewScene(scene);
            CharacterStatus.ActiveEnemies = oldEnemies;
            GameManager.Instance = oldManager;
            InventoryManager.Instance = oldInventory;
            ItemPouch.Instance = oldPouch;
            UnityEngine.Random.state = oldRandom;
        }
    }

    private static object Get(object obj, string name) => obj.GetType().GetField(name, Private).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    private static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    private static void Equal(float actual, float expected, string label) => Check(Mathf.Abs(actual - expected) < .001f, $"{label}: {actual} / expected {expected}");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("[CriticalItemsCheck] FAIL: " + message);
        _checks++;
    }
}
