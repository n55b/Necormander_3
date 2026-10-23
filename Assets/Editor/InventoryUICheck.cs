using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using Object = UnityEngine.Object;

/// <summary>실제 프리팹/데이터로 재실행하는 회귀 검사. 저장 데이터와 현재 씬은 수정하지 않는다.</summary>
public static class InventoryUICheck
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string PouchPath = "Assets/Prefabs/UI/PouchUI.prefab";
    private const string ExplainPath = "Assets/Prefabs/UI/SkillExplainUI.prefab";
    private const string PickerPath = "Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab";
    private const string RuntimeKey = "InventoryUICheck.Runtime";
    private const string RuntimeBatchKey = "InventoryUICheck.RuntimeBatch";
    private static readonly Vector2 WorldDropPoint = new Vector2(950, 80);
    private static System.Collections.IEnumerator _runtime;
    [Serializable] private class SceneBackup { public SceneSetup[] scenes; }

    [InitializeOnLoadMethod]
    private static void RegisterRuntimeCheck() => EditorApplication.playModeStateChanged += state =>
    {
        if (!SessionState.GetBool(RuntimeKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _runtime = RuntimeCheck();
            EditorApplication.update += TickRuntime;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(RuntimeKey, false);
            if (SessionState.GetBool(RuntimeBatchKey, false))
            {
                SessionState.SetBool(RuntimeBatchKey, false);
                EditorApplication.Exit(SessionState.GetBool(RuntimeKey + ".Failed", false) ? 1 : 0);
                return;
            }
            var backup = JsonUtility.FromJson<SceneBackup>(SessionState.GetString(RuntimeKey + ".Scenes", ""));
            if (backup != null) EditorSceneManager.RestoreSceneManagerSetup(backup.scenes);
        }
    };

    public static void RunRuntimeBatch()
    {
        if (!Application.isBatchMode) throw new Exception("배치 검증 전용입니다.");
        SessionState.SetBool(RuntimeBatchKey, true);
        SessionState.SetBool(RuntimeKey, true);
        SessionState.SetBool(RuntimeKey + ".Failed", false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Tools/UI/0928/Check runtime pickup and hold")]
    public static void StartRuntimeCheck()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("플레이를 끈 뒤 실행하세요.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Check(setup.All(s => !string.IsNullOrEmpty(s.path) && !SceneManager.GetSceneByPath(s.path).isDirty), "씬을 먼저 저장하세요. 검사는 씬/세이브를 수정하지 않습니다.");
        SessionState.SetString(RuntimeKey + ".Scenes", JsonUtility.ToJson(new SceneBackup { scenes = setup }));
        SessionState.SetBool(RuntimeKey, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void TickRuntime()
    {
        try { if (_runtime != null && _runtime.MoveNext()) return; }
        catch (Exception error) { SessionState.SetBool(RuntimeKey + ".Failed", true); Debug.LogException(error); }
        EditorApplication.update -= TickRuntime;
        _runtime = null;
        EditorApplication.ExitPlaymode();
    }

    private static System.Collections.IEnumerator RuntimeCheck()
    {
        // MCP 실행 중 에디터가 비활성이어도 실제 Game Update가 진행되게 한다.
        Application.runInBackground = true;
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        var fixture = new GameObject("Isolated runtime check"); fixture.SetActive(false);
        var gm = fixture.AddComponent<GameManager>(); GameManager.Instance = gm;
        var player = fixture.AddComponent<PlayerController>(); Field(gm, "playerController", player);
        var stat = fixture.AddComponent<CharacterStat>(); Field(player, "stat", stat);
        var health = fixture.GetComponent<CharacterHealth>(); Field(stat, "<Health>k__BackingField", health); Field(health, "_stat", stat); health.SetHP(50);
        var inventory = fixture.AddComponent<InventoryManager>(); InventoryManager.Instance = inventory;
        inventory.Slots.Add(new InventoryManager.CoreSlot()); inventory.Slots.Add(new InventoryManager.CoreSlot());
        var pouch = fixture.AddComponent<ItemPouch>(); ItemPouch.Instance = pouch;
        var popup = fixture.AddComponent<UIPopUpManager>(); Singleton<UIPopUpManager>(popup);
        var minions = AssetDatabase.FindAssets("t:MainMinionDataSO").Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g))).Take(2).ToArray();
        var canvas = new GameObject("Canvas", typeof(Canvas)).GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var picker = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PickerPath), canvas.transform).GetComponent<HandSlotSelectionUI>();
        var drop = GroundItem.Drop(minions[0], Vector3.zero);
        Check(drop.Interact(fixture), "F로 미니언 선택창");
        picker.Select(HandSlotSelectionItem.ActionKind.Candidate);
        Check(inventory.MainSummon == minions[0] && !drop.IsAvailable, "빈 슬롯 카드 클릭으로 장착/원본 소비");
        yield return null;
        drop = GroundItem.Drop(minions[1], Vector3.zero); drop.Interact(fixture);
        var pointer = new UnityEngine.EventSystems.PointerEventData(null) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
        var replace = ((GameObject)Read(picker, "replaceButton")).GetComponent<HandSlotSelectionItem>();
        // 배치 모드 첫 셰이더 컴파일 프레임은 1초를 넘을 수 있으므로 클릭 취소는 같은 프레임에 검증한다.
        replace.OnPointerDown(pointer);
        replace.OnPointerUp(pointer);
        Check(inventory.MainSummon == minions[0] && drop.IsAvailable, "짧은 누름은 교체 안 함");
        replace.OnPointerDown(pointer); float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < picker.ConfirmHoldSeconds + 0.3f) yield return null;
        Check(inventory.MainSummon == minions[1] && (drop == null || !drop.IsAvailable), $"1초 홀드로 교체 (Update 누적={Read(replace, "_elapsed")}, 활성={replace.isActiveAndEnabled})");
        Check(Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None).Count(d => d.IsAvailable && d.Minion == minions[0]) == 1, "교체한 기존 미니언 1개 드랍");
        var recycle = GroundItem.Drop(minions[0], Vector3.zero);
        recycle.OnHoldComplete(fixture); recycle.OnHoldComplete(fixture);
        Check(health.CurHP == 60f, "미니언 분해 +10 회복/중복 호출 방지");
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:ItemSO")[0]));
        var shop = new GameObject("Shop transaction", typeof(SpriteRenderer)).AddComponent<SellItem>();
        shop.item = new RewardCandidate { category = RewardCategory.Item, rawData = item, goldAmount = 100 };
        inventory.AddGold(500);
        Check(shop.Interact(fixture) && !shop.Interact(fixture) && inventory.GOLD == 400 && pouch.Get(0) == item, "상점 정확히 1회 결제/습득");
        yield return null;
        for (int i = 0; i < pouch.SlotCount; i++) pouch.RemoveAt(i);
        for (int i = 0; i < 10; i++) Check(pouch.TryAdd(AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/SOData/Items/GaleDashRecovery.asset")), "충전 아이템 10개");
        var rechargeObject = new GameObject("Runtime recharge check");
        var dodge = rechargeObject.AddComponent<MeleeDodgeController>();
        Field(dodge, "_player", player); Field(dodge, "rechargeTime", .3f);
        Field(dodge, "_lastRechargeTime", .3f); Field(dodge, "_currentCharges", 0); Field(dodge, "_rechargeTimer", .3f);
        float rechargeStarted = Time.time;
        while (Time.time - rechargeStarted < .24f) yield return null;
        Check(dodge.CurrentCharges == 1, "실제 Update에서 +50% 충전 속도로 0.3초 → 0.2초");
        for (int i = 0; i < 9; i++) Check(pouch.TryAdd(AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/SOData/Items/HermesContract.asset")), "계약서 9개");
        yield return null;
        Check(dodge.MaxCharges == 5 && dodge.CurrentCharges >= 4, "실제 Update에서 최대 횟수/새 충전 반영");
        Object.Destroy(rechargeObject);

        var pouchUI = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PouchPath), canvas.transform).GetComponent<PouchUI>();
        pouchUI.SetOpen(true);
        yield return null;
        var cell = pouchUI.GetComponentsInChildren<PouchSlotUI>().First(c => c.Item.name == "HermesContract");
        ExecuteEvents.Execute(cell.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(((TMP_Text)Read(pouchUI, "detailTitle")).text == "헤르메스와의 계약서류", "실제 UI 클릭으로 상세 선택");
        var sort = ((Button[])Read(pouchUI, "sortButtons"))[1];
        ExecuteEvents.Execute(sort.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check((int)Read(pouchUI, "_sort") == 1, "프리팹 정렬 버튼 영구 이벤트 연결");
        pouchUI.SetOpen(false); pouchUI.SetOpen(true); yield return null;
        Check(PouchUI.IsOpen, "실제 Play Mode 반복 토글");
        pouchUI.SetOpen(false);
        Debug.Log("[InventoryUICheck] STACK RUNTIME PASS — 실제 Update 충전/추가 횟수/UI 포인터 클릭/정렬 버튼/반복 토글.");
        Debug.Log("[InventoryUICheck] RUNTIME PASS — F 습득, 짧은 클릭 취소, 1초 홀드 교체, 기존 미니언 드랍, 분해 1회 회복, 결제 중복 방지.");
    }


    [MenuItem("Tools/UI/Inventory/Check stacked pouch and render")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("플레이를 끈 뒤 검사하세요.");
        InventoryUISetup.Validate();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var oldManager = GameManager.Instance;
        var oldInventory = InventoryManager.Instance;
        var oldPouch = ItemPouch.Instance;
        var oldUI = PouchUI.Instance;
        var oldExplain = SkillExplainUI.Instance;
        var oldPopup = UIPopUpManager.Instance;
        var otherCameras = Camera.allCameras.ToArray();
        var tempItems = new System.Collections.Generic.List<ItemSO>();
        ItemPouch pouch = null;
        try
        {
            foreach (var camera in otherCameras) camera.enabled = false;
            var host = new GameObject("Inventory check fixture"); host.SetActive(false);
            var gm = host.AddComponent<GameManager>(); GameManager.Instance = gm;
            var inventory = host.AddComponent<InventoryManager>(); InventoryManager.Instance = inventory; gm.inventoryManager = inventory;
            var player = host.AddComponent<PlayerController>(); Field(gm, "playerController", player);
            var stat = host.AddComponent<CharacterStat>(); Field(player, "stat", stat); Field(stat, "_isPlayer", true);
            var health = host.GetComponent<CharacterHealth>(); var status = host.GetComponent<CharacterStatus>();
            Field(stat, "<Health>k__BackingField", health); Field(stat, "<Status>k__BackingField", status);
            health.Init(stat, status);
            var data = host.AddComponent<DataManager>(); gm.dataManager = data;
            var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>("Assets/SOData/Registry/Growth Reward Registry.asset");
            Field(data, "growthRegistry", registry);
            pouch = host.AddComponent<ItemPouch>(); ItemPouch.Instance = pouch; Call(pouch, "OnEnable");
            var popup = host.AddComponent<UIPopUpManager>(); Singleton<UIPopUpManager>(popup);
            ItemSO Item(string id) => AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/SOData/Items/" + id + ".asset");
            void Clear() { for (int i = 0; i < pouch.SlotCount; i++) pouch.RemoveAt(i); }
            void Add(string id, int count) { for (int i = 0; i < count; i++) Check(pouch.TryAdd(Item(id)), "중첩 습득: " + id); }
            void Equal(float actual, float expected, string label) => Check(Mathf.Abs(actual - expected) < .001f, label + ": " + actual + " != " + expected);

            var stone = Item("Whetstone");
            Add("Whetstone", 10);
            Check(pouch.CountOf(stone) == 10 && !pouch.TryAdd(stone) && !RewardProcessor.CanOfferItem(stone), "최대 중첩/11번째 차단/보상 제외");
            Field(stat, "baseCritChance", 37f); Equal(stat.CRIT_OVERFLOW_DAMAGE, 0, "107% 절삭");
            Field(stat, "baseCritChance", 40f); Equal(stat.CRIT_OVERFLOW_DAMAGE, 10, "110% 변환 1회");
            Field(stat, "baseCritChance", 55f); Equal(stat.CRIT_OVERFLOW_DAMAGE, 20, "125% 변환");
            Clear(); Equal(stat.CRIT_OVERFLOW_DAMAGE, 0, "숫돌 없으면 변환 없음"); Field(stat, "baseCritChance", 0f);
            Add("CrackedWhetstone", 10); Equal(stat.CRIT_DAMAGE, 300, "치명타 피해 15 x10");
            Add("GaleDashRecovery", 10); Equal(stat.ApplyDashCooldown(3f), 2f, "충전 속도 +50%");
            Add("GaleDashLeap", 10); Equal(stat.DASH_DISTANCE_MULTIPLIER, 1.3f, "거리 +30%");
            var dodge = host.AddComponent<MeleeDodgeController>(); Call(dodge, "Awake");
            Add("HermesContract", 2); Call(dodge, "SyncItemBonuses"); Check(dodge.MaxCharges == 2, "계약서 2개 0회");
            Add("HermesContract", 1); Call(dodge, "SyncItemBonuses"); Check(dodge.MaxCharges == 3 && dodge.CurrentCharges == 3, "계약서 3개 +1회");
            Add("HermesContract", 6); Call(dodge, "SyncItemBonuses"); Check(dodge.MaxCharges == 5 && dodge.CurrentCharges == 5, "계약서 9개 +3회");
            Check(!pouch.TryAdd(Item("HermesContract")), "계약서 10번째 차단");
            Add("ClothArmor", 10); Equal(stat.Mods.Flat(StatType.Defense), 40, "방어력 +40");
            Add("LightBoots", 10); Equal(stat.Mods.Percent(StatType.MoveSpeed), .4f, "이동 속도 +40%");
            Add("WindFeather", 10); Equal(stat.EVASION, .2f, "회피율 +20%");
            Add("PortableShield", 10);
            var room1 = new GameObject("Room 1").AddComponent<RoomInstance>();
            var room2 = new GameObject("Room 2").AddComponent<RoomInstance>();
            var entry = typeof(ItemPouch).GetMethod("HandleRoomEntered", Flags);
            status.AddShield(7, float.PositiveInfinity);
            entry.Invoke(pouch, new object[] { room1 }); Equal(status.TotalShield, 67, "방 진입 보호막 +60");
            entry.Invoke(pouch, new object[] { room1 }); Equal(status.TotalShield, 67, "중복 진입 트리거 무시");
            status.ConsumeShield(10);
            entry.Invoke(pouch, new object[] { room2 }); Equal(status.TotalShield, 60, "다른 방에서 아이템 보호막 갱신");
            status.AddShield(9, float.PositiveInfinity);
            entry.Invoke(pouch, new object[] { room1 }); Equal(status.TotalShield, 69, "다른 출처 보호막 유지");

            foreach (var item in registry.items.Where(i => i != null)) if (!pouch.Contains(item)) Check(pouch.TryAdd(item), "9종 초과 습득");
            var unique = new System.Collections.Generic.List<ItemSO>(); pouch.GetUniqueItems(unique);
            Check(unique.Count > 9, "종류/칸 제한 없음");
            Check(!pouch.TryAdd(Item("LifeCharm")), "기존 아이템 1개 제한");
            var save = new SaveData(); pouch.SaveToData(save);
            pouch.LoadFromData(save); var restored = new SaveData(); pouch.SaveToData(restored);
            Check(save.pouchItemNames.SequenceEqual(restored.pouchItemNames), "중첩 수/획득순 저장 복원");
            pouch.LoadFromData(new SaveData { pouchSlotCount = 9, pouchItemNames = new System.Collections.Generic.List<string> { "Whetstone", "", "Whetstone", "LifeCharm" } });
            Check(pouch.CountOf(stone) == 2 && pouch.CountOf(Item("LifeCharm")) == 1, "기존 빈칸/중복 세이브 마이그레이션");
            pouch.LoadFromData(save); pouch.Refresh(); Equal(stat.Mods.Flat(StatType.Defense), 40, "로드/Refresh 중복 효과 없음");

            var ground = GroundItem.Drop(stone, Vector3.zero);
            Check(ground.HoldSeconds == 0 && !ground.InteractionPrompt.Contains("분해"), "아이템 길게 누르기 제거");
            ground.OnHoldComplete(host); Check(ground.IsAvailable, "아이템 분해 호출도 무효");
            Clear();
            int drops = Drops(scene);
            var reward = host.AddComponent<RewardManager>();
            reward.RequestClearReward(RoomType.Normal);
            Check(Drops(scene) == drops + 1, "일반방 아이템 정확히 1개 드랍");
            Check(inventory.GOLD == 200, "기존 일반방 골드 유지");
            foreach (var item in registry.items.Where(i => i != null)) pouch.TryAdd(item);
            Add("Whetstone", 8); Add("ClothArmor", 5); Add("LightBoots", 3);
            foreach (var world in scene.GetRootGameObjects().Where(g => g.GetComponent<GroundItem>() != null)) world.SetActive(false);

            var canvas = new GameObject("Preview Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            var cam = new GameObject("Preview Camera", typeof(Camera)).GetComponent<Camera>();
            cam.transform.position = new Vector3(0, 0, -10); cam.orthographic = true; cam.orthographicSize = 270;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.12f, .14f, .15f);
            canvas.worldCamera = cam; canvas.planeDistance = 1;
            var texture = new RenderTexture(960, 540, 24); cam.targetTexture = texture;
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PouchPath), canvas.transform).GetComponent<PouchUI>();
            Call(ui, "Awake"); ui.SetOpen(true);
            Check(PouchUI.IsOpen && Time.timeScale == 1f, "V 열기/시간 진행");
            var cells = ui.GetComponentsInChildren<PouchSlotUI>(true);
            Check(ui.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == LayerMask.NameToLayer("UI")), "새 uGUI 오브젝트는 UI 레이어 유지");
            Check(cells.Count(c => c.gameObject.activeSelf) == registry.items.Distinct().Count(), "종류별 셀 하나");
            Check(ui.GetComponentInChildren<ScrollRect>(true) != null && ui.transform.Find("DragGhost") == null, "스크롤/드래그 고스트 삭제");
            Check(typeof(PouchSlotUI).GetInterface(nameof(IBeginDragHandler)) == null, "버리기 드래그 제거");
            ui.SortByCount();
            Check(ui.GetComponentsInChildren<PouchSlotUI>().First().Item == stone, "개수순 내림차순");
            ui.SortByTier();
            Check(ui.GetComponentsInChildren<PouchSlotUI>().First().Item.tier == registry.items.Min(i => i.tier), "등급순");
            ui.SortByAcquired(); ui.Select(stone);
            Check(((TMP_Text)Read(ui, "stackText")).text.Contains("9 / 10"), "선택 상세 수량");
            Check(((TMP_Text)Read(ui, "totalEffectText")).text.Contains("+63%"), "선택 상세 총 효과");
            Check(((TMP_Text)Read(ui, "nextEffectText")).text.Contains("+70%"), "다음 중첩 미리보기");
            Render(cam, texture, "Logs/StackedPouch.png");
            // 목록이 화면을 넘어도 프리팹 셀/스크롤만 늘어나고 아이템 유실이 없다.
            for (int i = 0; i < 35; i++) { var item = ScriptableObject.CreateInstance<ItemSO>(); item.name = "ScrollCheck" + i; tempItems.Add(item); pouch.TryAdd(item); }
            Call(ui, "Refresh"); Canvas.ForceUpdateCanvases();
            var scroll = (ScrollRect)Read(ui, "itemScroll");
            Check(scroll.content.rect.height > scroll.viewport.rect.height, "많은 종류에서 스크롤 영역 확장");
            scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
            Check(scroll.content.anchoredPosition.y > 0, "끝 아이템까지 스크롤");
            for (int i = 0; i < pouch.SlotCount; i++) if (tempItems.Contains(pouch.Get(i))) pouch.RemoveAt(i);
            Call(ui, "Refresh");
            var explain = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ExplainPath), canvas.transform).GetComponent<SkillExplainUI>();
            Singleton<SkillExplainUI>(explain); Call(explain, "OnAwake"); explain.SetOpen(true);
            Check(explain.IsOpen && PouchUI.IsOpen, "C/V 독립 공존");
            explain.ToggleDetails(); Check(explain.IsExpanded && PouchUI.IsOpen, "C 확장/V 유지");
            Render(cam, texture, "Logs/StackedPouch-CV.png");
            explain.SetOpen(false); Check(PouchUI.IsOpen, "C만 닫기");
            ui.SetOpen(false); Check(!PouchUI.IsOpen, "V 토글 닫기");
            cam.targetTexture = null; texture.Release(); Object.DestroyImmediate(texture);
            Debug.Log("[InventoryUICheck] STACK PASS — 중첩/상한/스탯/대시/보호막/구세이브/보상/분해차단/정렬/상세/스크롤/CV 검사 통과.");
        }
        finally
        {
            if (pouch != null) Call(pouch, "OnDisable");
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous);
            foreach (var item in tempItems) Object.DestroyImmediate(item);
            foreach (var camera in otherCameras) if (camera != null) camera.enabled = true;
            GameManager.Instance = oldManager; InventoryManager.Instance = oldInventory; ItemPouch.Instance = oldPouch; PouchUI.Instance = oldUI;
            Singleton<SkillExplainUI>(oldExplain); Singleton<UIPopUpManager>(oldPopup);
        }
    }

    [MenuItem("Tools/UI/0928/Check scene wiring")]
    public static void CheckScenes()
    {
        var previous = SceneManager.GetActiveScene();
        foreach (var name in new[] { "BattleScene", "VillageScene", "BossTestScene", "EliteTestScene" })
        {
            string path = "Assets/Scenes/" + name + ".unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var pouch = roots.SelectMany(r => r.GetComponentsInChildren<PouchUI>(true)).ToArray();
                var info = roots.SelectMany(r => r.GetComponentsInChildren<SkillExplainUI>(true)).ToArray();
                var pickers = roots.SelectMany(r => r.GetComponentsInChildren<HandSlotSelectionUI>(true)).ToArray();
                Check(pouch.Length == 1 && info.Length == 1 && pickers.Length == 1, name + " C/V/미니언 창 단일 배치");
                var pouchRect = (RectTransform)pouch[0].transform;
                Check(pouchRect.anchorMin == Vector2.zero && pouchRect.anchorMax == Vector2.one, name + " V 루트 전체 화면");
                Check(pouch[0].gameObject.activeInHierarchy && info[0].gameObject.activeInHierarchy && pickers[0].gameObject.activeInHierarchy, name + " 싱글턴 초기화 가능한 루트");
                Check((GameObject)Read(info[0], "panelRoot") != info[0].gameObject, name + " C 루트와 표시 패널 분리");
                Check((GameObject)Read(pickers[0], "panel") != pickers[0].gameObject, name + " 미니언 루트와 표시 패널 분리");
                Check(roots.SelectMany(r => r.GetComponentsInChildren<CommonTooltipUI>(true)).Count() == 1, name + " 공용 툴팁 실제 배치");
                var maps = roots.SelectMany(r => r.GetComponentsInChildren<UIBasedMiniMap>(true)).ToArray();
                Check(maps.Length == (name == "VillageScene" ? 0 : 1), name + " 던전 미니맵 배치 수");
                foreach (var map in maps)
                {
                    var hudMap = (RectTransform)map.transform.Find("Image_MiniMap");
                    Check(hudMap.sizeDelta == new Vector2(120, 100) && hudMap.anchorMin == Vector2.one, name + " 고정 크기 미니맵 우측 상단 배치");
                }
                Debug.Log("[InventoryUICheck] SCENE PASS: " + name);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        SceneManager.SetActiveScene(previous);
    }

    [MenuItem("Tools/UI/0928/Check nearby drop and hold durations")]
    public static void CheckNearbyDrop()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode && MapGenerator.Instance == null, "플레이를 끈 뒤 독립 검사하세요.");
        var picker = AssetDatabase.LoadAssetAtPath<GameObject>(PickerPath).GetComponent<HandSlotSelectionUI>();
        Check(picker.HoldSeconds(HandSlotSelectionItem.ActionKind.Replace) == 1f, "교체는 1초");
        Check(picker.HoldSeconds(HandSlotSelectionItem.ActionKind.Skip) == 1f, "스킵도 1초");
        Check(picker.transform.Find("MinionSelection/Skip/Label").GetComponent<TextMeshProUGUI>().text.Contains("1초"), "스킵 문구도 1초");
        Check(picker.transform.Find("MinionSelection/Replace/Label").GetComponent<TextMeshProUGUI>().text.Contains("1초"), "교체 문구도 1초");
        float distance = 1.25f;
        Check(Mathf.Approximately(distance, 1.25f), "기본 드랍 거리 1.25");
        var origin = new Vector3(10000, 10000, 0);
        Check(!GroundItem.TryFindNearbyDropPoint(origin, origin + Vector3.right * 100, distance, out _), "바닥 준비 전에는 드랍 실패/원본 보존");
        var sources = new System.Collections.Generic.List<NavMeshBuildSource> {
            new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(10, 0.1f, 10),
                transform = Matrix4x4.TRS(new Vector3(0, -0.05f, 0), Quaternion.identity, Vector3.one), area = 0 }
        };
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), sources,
            new Bounds(Vector3.zero, new Vector3(20, 20, 20)), Vector3.zero, Quaternion.identity);
        Check(data != null, "검사용 NavMesh 생성");
        var instance = NavMesh.AddNavMeshData(data, origin, Quaternion.Euler(-90, 0, 0));
        GameObject wall = null;
        try
        {
            Check(GroundItem.TryFindNearbyDropPoint(origin, origin + Vector3.right * 100, distance, out var far), "먼 마우스 방향 드랍");
            Check(Vector2.Distance(far, origin + Vector3.right * distance) < 0.05f, "먼 마우스라도 플레이어 바로 옆 (2D 이동면 기준)");
            Check(GroundItem.TryFindNearbyDropPoint(origin, origin + Vector3.right * 0.2f, distance, out var close) && Vector3.Distance(far, close) < 0.01f, "마우스 거리와 무관한 고정 거리");
            Check(GroundItem.TryFindNearbyDropPoint(origin, origin + Vector3.left * 100, distance, out var left) && left.x < origin.x - 1f, "반대 방향");
            Check(GroundItem.TryFindNearbyDropPoint(origin, origin, distance, out var zero) && zero.y < origin.y, "방향 0일 때 아래쪽");
            Check(GroundItem.TryFindDropPoint(origin + Vector3.right * 10, 10, out var edge), "바닥 가장자리 찾기");
            var nearEdge = edge - Vector3.right * 0.4f;
            Check(GroundItem.TryFindNearbyDropPoint(nearEdge, origin + Vector3.right * 100, distance, out var shore)
                && Vector3.Distance(shore, edge) < 0.05f, "물/낭떠러지 방향은 가장 가까운 바닥 경계");
            wall = new GameObject("Nearby drop wall check"); wall.layer = LayerMask.NameToLayer("Wall");
            wall.transform.position = origin + Vector3.right * 0.8f;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 4f);
            Physics2D.SyncTransforms();
            Check(GroundItem.TryFindNearbyDropPoint(origin, origin + Vector3.right * 100, distance, out var blocked)
                && blocked.x < origin.x + 0.7f && Vector3.Distance(blocked, origin) <= distance,
                "벽 반대편이 아닌 플레이어 주변 안전한 땅");
            Check(!Physics2D.OverlapPoint(blocked, Layers.WallMask), "벽 내부에는 드랍하지 않음");
            Debug.Log("[InventoryUICheck] NEARBY PASS — 교체/스킵 1초, 고정 거리, 방향, 바닥 경계, 벽 우회 및 미준비 실패 검사.");
        }
        finally
        {
            if (wall != null) Object.DestroyImmediate(wall);
            instance.Remove(); Object.DestroyImmediate(data);
        }
    }

    private static int Drops(Scene scene) => scene.GetRootGameObjects().Count(g => g.GetComponent<GroundItem>() != null);
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static object Read(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);
    private static void Singleton<T>(T value) where T : Singleton<T> => typeof(Singleton<T>).GetProperty("Instance").SetValue(null, value);
    private static void Check(bool success, string message) { if (!success) throw new Exception("[InventoryUICheck] " + message); }
    private static void Render(Camera camera, RenderTexture texture, string path)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var label in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None)) label.ForceMeshUpdate();
        camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var output = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        output.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); output.Apply();
        System.IO.File.WriteAllBytes(path, output.EncodeToPNG());
        RenderTexture.active = previous; Object.DestroyImmediate(output);
    }
}
