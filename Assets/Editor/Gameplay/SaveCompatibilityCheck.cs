using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SaveCompatibilityCheck
{
    private const string ModeKey = "SaveCompatibilityCheck.Mode";
    private const int LegacyGold = 137;
    private const int GoldAdded = 9;
    private const float LegacyHealth = 68.25f;
    private const float UpdatedHealth = 55.5f;
    private static string FilePath => Path.Combine(Application.persistentDataPath, "save_data.json");

    // Layout used before v2 stored the active slot and the second summon's enhancement.
    private const string OlderV2 = @"{
        ""saveVersion"":2, ""currentFloor"":1, ""playerHP"":68.25, ""gold"":137,
        ""slots"":[{""equippedMinionName"":""MeleeDoll Minion"",""quantity"":1}],
        ""treasures"":[{""treasureSOAddress"":""Minion Stat Treasure"",""stackCount"":2}],
        ""equipment"":{""equipmentSOName"":""Serendipity"",""enhanceLevel"":3,
                       ""serendipityElapsed"":8.25,""moveBuffRemaining"":3.5},
        ""pouchSlotCount"":6,""pouchItemNames"":[""Whetstone"","""",""CrackedWhetstone"",""Whetstone""],
        ""augmentMaxHpBonus"":12.5, ""mainSummonEnhanceLevel"":1
    }";

    private static IEnumerator _checks;
    private static double _deadline;

    static SaveCompatibilityCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            int mode = SessionState.GetInt(ModeKey, 0);
            if (state != PlayModeStateChange.EnteredPlayMode || mode == 0) return;
            _checks = VerifyContinue(afterRestart: mode == 2);
            _deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += Tick;
        };
    }

    public static void VerifyFileStorage()
    {
        RequireIsolatedProject();
        string backup = FilePath + ".bak";
        Require(SaveSystem.DeleteSave(), "reset isolated files");
        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(FilePath, OlderV2);
            var save = SaveSystem.Load();
            Require(SaveSystem.SaveExists() && save?.gold == LegacyGold && save.secondSummonEnhanceLevel == 0
                && save.activeSummonSlot == 0, "older v2 fields and defaults");
            save.gold = 140;
            Require(SaveSystem.Save(save) && SaveSystem.Load()?.gold == 140
                && File.ReadAllText(backup) == OlderV2, "atomic replacement retains the previous v2 bytes");

            using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Require(!SaveSystem.Save(save), "a locked checkpoint rejects the write");
            Require(SaveSystem.Load()?.gold == 140, "a failed write preserves the current checkpoint");

            File.WriteAllText(FilePath, "{\"saveVersion\":2,\"currentFloor\":1,");
            Require(SaveSystem.SaveExists() && SaveSystem.Load()?.gold == LegacyGold, "truncated primary recovers from backup");
            File.WriteAllText(FilePath, "{\"saveVersion\":2,\"currentFloor\":1}");
            Require(SaveSystem.Load()?.gold == LegacyGold, "missing checkpoint HP is detected and uses backup");
            Require(SaveSystem.Save(save) && File.ReadAllText(backup) == OlderV2
                && SaveSystem.Load()?.gold == 140, "writing after recovery preserves the good backup");
            File.Delete(FilePath);
            Require(SaveSystem.SaveExists() && SaveSystem.Load()?.gold == LegacyGold, "missing primary recovers from backup");

            foreach (string unsupported in new[] {
                "{\"currentFloor\":1,\"gold\":3}",
                "{\"saveVersion\":1,\"currentFloor\":1}",
                "{\"saveVersion\":3,\"currentFloor\":1}" })
            {
                File.WriteAllText(FilePath, unsupported);
                Require(!SaveSystem.SaveExists() && SaveSystem.Load() == null && !SaveSystem.Save(save)
                    && File.ReadAllText(FilePath) == unsupported, "unsupported save is preserved and cannot be overwritten");
            }

            Require(SaveSystem.DeleteSave() && !File.Exists(FilePath) && !File.Exists(backup)
                && !SaveSystem.SaveExists(), "explicit reset clears both checkpoint files");
            File.WriteAllText(backup, "{\"saveVersion\":3,\"currentFloor\":1}");
            Require(!SaveSystem.Save(save) && File.Exists(backup) && !File.Exists(FilePath),
                "unsupported backup is preserved when it is the only copy");
            Require(Directory.GetFiles(Application.persistentDataPath, "save_data.json.*.tmp").Length == 0,
                "no temporary writes left behind");
        }
        finally
        {
            SaveSystem.DeleteSave();
        }
        Debug.Log("[SaveCompatibilityCheck] File storage PASS");
    }

    // DATA 새 콘텐츠로 있던 때의 세이브 키 content_<id>가 굽기 후 새 에셋으로 해석된다(아이템·장비·보물·소환수).
    public static void VerifyBakedVariantKeys()
    {
        RequireIsolatedProject();
        Require(SaveSystem.DeleteSave(), "reset isolated files");
        try
        {
            using var fixture = new ProjectDataBakeFixture();
            var growth = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>("Assets/SOData/Registry/Growth Reward Registry.asset");
            var shop = AssetDatabase.LoadAssetAtPath<ShopRegistrySO>("Assets/SOData/Registry/Shop Registry.asset");
            fixture.Keep(growth, shop);
            ScriptableObject[] templates = { growth.items.First(i => i != null), growth.equipments.First(e => e != null),
                growth.treasures.First(t => t != null), growth.minionDatas.OfType<MainMinionDataSO>().First() };
            string[] ids = templates.Select((t, n) => fixture.Variant(t, "QA 세이브 굽기 " + n)).ToArray();
            Object Resolve(SaveData save, int n) => n switch
            {
                0 => growth.ResolveSavedItem(save.pouchItemNames[0]),
                1 => growth.ResolveSavedEquipment(save.equipment.equipmentSOName),
                2 => growth.ResolveSavedTreasure(save.treasures[0].treasureSOAddress),
                _ => growth.ResolveSavedMinion(save.slots[0].equippedMinionName),
            };
            // 변형이 DATA에 있을 때 쓴 세이브: 실행 중 복제본의 이름이 content_<id>다.
            ProjectParameterBindings.WithPublished(() =>
            {
                var save = new SaveData { currentFloor = 1, playerHP = 10, pouchSlotCount = 1,
                    pouchItemNames = { "content_" + ids[0] }, equipment = new EquipmentSaveData { equipmentSOName = "content_" + ids[1] },
                    treasures = { new TreasureSaveData { treasureSOAddress = "content_" + ids[2], stackCount = 1 } },
                    slots = { new CoreSlotSaveData { equippedMinionName = "content_" + ids[3], quantity = 1 } } };
                for (int n = 0; n < ids.Length; n++)
                    Require(Resolve(save, n)?.name == "content_" + ids[n], "variant save key resolves before baking " + n);
                Require(SaveSystem.Save(save), "write the variant-era save");
            });
            ProjectDataBake.Execute(ids.Select(id => ProjectDataBakeFixture.Variants + "/" + id).ToArray(), requireCleanGit: false);
            var loaded = SaveSystem.Load();
            for (int n = 0; n < ids.Length; n++)
            {
                var asset = Resolve(loaded, n);
                Require(asset != null && asset.name == "QA 세이브 굽기 " + n && AssetDatabase.Contains(asset)
                    && asset.GetType() == templates[n].GetType(), "content_ save key resolves to the baked asset " + n);
            }
            Require(growth.ResolveSavedItem(templates[0].name) == templates[0] && growth.ResolveSavedMinion(templates[3].name) == templates[3],
                "original save keys still resolve to the originals");
        }
        finally
        {
            SaveSystem.DeleteSave();
        }
        Debug.Log("[SaveCompatibilityCheck] Baked variant keys PASS");
    }

    // DATA 구조 문서: 삭제 표시(원본·새 콘텐츠)는 목록에서 빠지고 세이브 해석만 남는다. 가입 끔·autoJoin:false·이름 변경과 복원 순서를 본다.
    public static void VerifyItemLifecycle()
    {
        RequireIsolatedProject();
        Require(SaveSystem.DeleteSave(), "reset isolated files");
        var growth = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>("Assets/SOData/Registry/Growth Reward Registry.asset");
        var shop = AssetDatabase.LoadAssetAtPath<ShopRegistrySO>("Assets/SOData/Registry/Shop Registry.asset");
        var items = growth.items.ToList();
        var pool = shop.itemPool.ToList();
        try
        {
            using var fixture = new ProjectDataBakeFixture();
            var picks = growth.items.Where(i => i != null && i != growth.startingItem && shop.itemPool.Contains(i)).Distinct().Take(3).ToArray();
            Require(picks.Length == 3, "three shop items to edit");
            ItemSO a = picks[0], b = picks[1], c = picks[2];
            string originalB = b.DisplayName;
            JObject Removed() => new JObject { ["removed"] = true };
            string docA = fixture.Write(ProjectDataBakeFixture.Document(a), "removed.json", Removed());
            string v = fixture.Variant(b, "QA 삭제한 새 아이템");
            fixture.Write(ProjectDataBakeFixture.Variants + "/" + v, "removed.json", Removed());
            string va = fixture.Variant(a, "QA 삭제한 원본의 새 아이템");
            fixture.Parameters(b, new JObject { ["itemName"] = "QA 새 이름" });
            fixture.Write(ProjectDataBakeFixture.Document(c), "MEMBERS/" + ProjectDataBakeFixture.Guid(shop) + "/itemPool.json", new JObject { ["included"] = false });
            string w = fixture.Variant(c, "QA 등장 꺼진 새 아이템", autoJoin: false);
            bool Has(System.Collections.Generic.IEnumerable<ItemSO> list, string id) => list.Any(i => i != null && i.name == "content_" + id);

            ProjectParameterBindings.ValidatePublished();
            Require(growth.items.SequenceEqual(items) && shop.itemPool.SequenceEqual(pool), "build validation applies structure only while checking");
            ProjectParameterBindings.WithPublished(() =>
            {
                Require(!growth.items.Contains(a) && !shop.itemPool.Contains(a) && growth.ResolveSavedItem(a.name) == a, "removed original leaves the lists and still resolves");
                Require(!Has(growth.items, v) && !Has(shop.itemPool, v) && growth.ResolveSavedItem("content_" + v)?.name == "content_" + v, "removed new content still resolves");
                Require(Has(growth.items, va) && Has(shop.itemPool, va) && growth.ResolveSavedItem("content_" + va) != null,
                    "new content of a removed original keeps the original's lists");
                Require(b.DisplayName == "QA 새 이름" && growth.ResolveSavedItem(b.name) == b, "a renamed original keeps its save key");
                Require(!shop.itemPool.Contains(c) && growth.items.Contains(c), "membership off leaves the shop only");
                Require(Has(growth.items, w) && !Has(shop.itemPool, w), "autoJoin:false joins only the save lists");
                // 씬마다 다시 오는 Bind: 가입 판정은 원래 목록으로 하므로 결과가 같다.
                var once = (growth.items.ToList(), shop.itemPool.ToList());
                typeof(ProjectParameterBindings).GetMethod("Bind", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { ProjectParameterBindings.Published(strict: true) });
                Require(growth.items.SequenceEqual(once.Item1) && shop.itemPool.SequenceEqual(once.Item2), "a second bind gives the same lists");
                var save = new SaveData { currentFloor = 1, playerHP = 10, pouchSlotCount = 3, pouchItemNames = { a.name, "content_" + v, b.name } };
                Require(SaveSystem.Save(save), "write a save holding removed items");
                Require(SaveSystem.Load()?.pouchItemNames.All(n => growth.ResolveSavedItem(n) != null) == true, "every saved item resolves after reload");
            });
            File.Delete(Path.Combine(ProjectContentStore.Root, docA, "removed.json"));
            ProjectParameterBindings.WithPublished(() =>
                Require(growth.items.IndexOf(a) == items.IndexOf(a) && shop.itemPool.IndexOf(a) == pool.IndexOf(a), "restored original returns to its original place"));
            Require(growth.items.SequenceEqual(items) && shop.itemPool.SequenceEqual(pool) && growth.ResolveSavedItem(a.name) == a && b.DisplayName == originalB,
                "registry lists and values restored after the check");
            fixture.Write(ProjectDataBakeFixture.Document(growth.startingItem), "removed.json", Removed());
            bool refused = false;
            try { ProjectParameterBindings.ValidatePublished(); }
            catch (InvalidDataException e) { refused = e.Message.Contains("시작 아이템"); }
            Require(refused, "removing the starting item is refused");
        }
        finally
        {
            SaveSystem.DeleteSave();
        }
        Debug.Log("[SaveCompatibilityCheck] Item lifecycle PASS");
    }

    public static void WriteAndReload()
    {
        RequireIsolatedProject();
        Require(SaveSystem.DeleteSave(), "reset isolated files");
        Directory.CreateDirectory(Application.persistentDataPath);
        File.WriteAllText(FilePath, OlderV2);
        StartPlayMode(1);
    }

    public static void ReadAfterRestart()
    {
        RequireIsolatedProject();
        StartPlayMode(2);
    }

    private static void StartPlayMode(int mode)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");
        SessionState.SetInt(ModeKey, mode);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        int exit;
        try
        {
            if (EditorApplication.timeSinceStartup > _deadline)
                throw new TimeoutException("Save verification timed out.");
            if (_checks.MoveNext()) return;
            Debug.Log("[SaveCompatibilityCheck] Play Mode PASS");
            exit = 0;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            exit = 1;
        }
        EditorApplication.update -= Tick;
        SessionState.EraseInt(ModeKey);
        EditorApplication.Exit(exit);
    }

    private static IEnumerator VerifyContinue(bool afterRestart)
    {
        Application.runInBackground = true;
        Debug.Log($"[SaveCompatibilityCheck] Checking Continue (after restart: {afterRestart}).");
        Require(SaveSystem.Load()?.currentFloor == (afterRestart ? 2 : 1), "checkpoint floor on disk before Continue");
        var menu = Object.FindFirstObjectByType<MainMenuManager>();
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var loaderField = typeof(MainMenuManager).GetField("sceneLoader", privateInstance);
        while (UnityNote.SceneLoader.IsLoading || menu == null || loaderField.GetValue(menu) == null)
        {
            yield return null;
            menu = Object.FindFirstObjectByType<MainMenuManager>();
        }
        var button = (Button)typeof(MainMenuManager).GetField("loadButton", privateInstance).GetValue(menu);
        Require(button != null && button.interactable, "real Continue button is enabled");

        SceneManager.sceneLoaded += ConfigureBattle;
        button.onClick.Invoke();
        Require(UnityNote.SceneLoader.IsLoading, "Continue starts changing scenes");
        while (UnityNote.SceneLoader.IsLoading || SceneManager.GetActiveScene().name != "BattleScene")
            yield return null;

        var manager = GameManager.Instance;
        int expectedFloor = manager.debugStartFloor > 0 ? manager.debugStartFloor : (afterRestart ? 2 : 1);
        Require(manager.IsPlayerReady && manager.currentFloor == expectedFloor, "BattleScene reached the configured start floor");
        VerifyGrowth(manager, afterRestart);

        if (afterRestart)
        {
            Require(SaveSystem.DeleteSave(), "clean up isolated checkpoint after restart");
            yield break;
        }

        var inventory = manager.inventoryManager;
        var registry = manager.dataManager.GET_GROWTH_REGISTRY();
        Require(inventory.EquipMinion(1, registry.ResolveSavedMinion("IceMage Minion"))
            && inventory.SetActiveSummonSlot(1) && inventory.EnhanceMainSummon(), "equip and enhance the second summon");
        inventory.AddGold(GoldAdded);
        Health(manager).SetHP(UpdatedHealth);

        Require(manager.SaveCurrentState() && SaveSystem.Load()?.currentFloor == manager.currentFloor,
            "same-floor checkpoint captures the live loadout");
        var portal = new GameObject("Save verification portal").AddComponent<FloorProceedPortal>();
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Require(!portal.Interact(manager.PLAYERCONTROLLER.gameObject) && portal.isActiveAndEnabled
                && !UnityNote.SceneLoader.IsLoading, "failed checkpoint keeps the floor portal available for retry");
        SceneManager.sceneLoaded += ConfigureBattle;
        Require(portal.Interact(manager.PLAYERCONTROLLER.gameObject), "floor portal accepts retry after the checkpoint unlocks");
        yield return null;
        while (UnityNote.SceneLoader.IsLoading) yield return null;
        var next = GameManager.Instance;
        Require(next != manager && next.IsPlayerReady && next.currentFloor == 2, "next floor checkpoint reloads BattleScene at floor two");
        VerifyGrowth(next, updated: true);
        Require(SaveSystem.Load()?.currentFloor == 2, "floor two checkpoint is available for the next Unity process");
    }

    private static void ConfigureBattle(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "BattleScene") return;
        SceneManager.sceneLoaded -= ConfigureBattle;
        var manager = Object.FindFirstObjectByType<GameManager>();
        manager.testMode_DisableAutoBattle = true;
        manager.playerStateUI = null; // HP is restored even when the scene has no bound HUD.
        Object.FindFirstObjectByType<MapGenerator>().SetNextLayoutSeed(907);
    }

    private static void VerifyGrowth(GameManager manager, bool updated)
    {
        var inventory = manager.inventoryManager;
        var registry = manager.dataManager.GET_GROWTH_REGISTRY();
        var fighter = registry.ResolveSavedMinion("Fighter Minion") as MainMinionDataSO;
        Require(fighter != null && inventory.GetSummonAt(0) == fighter && inventory.GetEnhanceLevelOf(fighter) == 1,
            "renamed first summon and its enhancement are restored");
        Require(inventory.ActiveSummonSlot == (updated ? 1 : 0), "active summon slot is restored");
        var mage = registry.ResolveSavedMinion("IceMage Minion") as MainMinionDataSO;
        Require(updated ? mage != null && inventory.GetSummonAt(1) == mage && inventory.GetEnhanceLevelOf(mage) == 1
            : inventory.GetSummonAt(1) == null, "second summon defaults in older saves and survives the new checkpoint");
        var equipment = manager.playerSkillInventoryManager.EquippedEquipment;
        Require(equipment?.baseData.name == "Serendipity" && equipment.enhanceLevel == 3, "equipment and upgrade stage are restored");
        var equipmentState = new SaveData();
        manager.playerSkillInventoryManager.SaveToData(equipmentState);
        Require(equipmentState.equipment.serendipityElapsed >= 8f && equipmentState.equipment.moveBuffRemaining > 0f,
            "equipment effect timers are restored");
        Require(inventory.GOLD == LegacyGold + (updated ? GoldAdded : 0)
            && Mathf.Approximately(inventory.AugmentMaxHpBonus, 12.5f)
            && Mathf.Approximately(Health(manager).CurHP, updated ? UpdatedHealth : LegacyHealth),
            "gold, max HP bonus, and current HP are restored");
        var treasure = registry.ResolveSavedTreasure("Minion Stat Treasure");
        Require(treasure != null && inventory.TreasureStacks.TryGetValue(treasure, out int stacks) && stacks == 2,
            "treasure stacks are restored");
        var pouch = manager.itemPouch;
        Require(pouch.Get(0)?.name == "Whetstone" && pouch.Get(1)?.name == "CrackedWhetstone"
            && pouch.Get(2)?.name == "Whetstone", "pouch keeps acquisition order and duplicate copies");
    }

    private static CharacterHealth Health(GameManager manager)
        => manager.PLAYERCONTROLLER.GetComponentInChildren<CharacterHealth>();

    private static void RequireIsolatedProject()
        => Require(Application.isBatchMode && PlayerSettings.companyName == "NecromanderVerification",
            "Use the isolated verification runner.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[SaveCompatibilityCheck] " + message);
    }
}
