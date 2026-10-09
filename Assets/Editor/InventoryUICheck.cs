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
            var backup = JsonUtility.FromJson<SceneBackup>(SessionState.GetString(RuntimeKey + ".Scenes", ""));
            if (backup != null) EditorSceneManager.RestoreSceneManagerSetup(backup.scenes);
        }
    };

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
        catch (Exception error) { Debug.LogException(error); }
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
        replace.OnPointerDown(pointer); float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 0.2f) yield return null;
        replace.OnPointerUp(pointer);
        Check(inventory.MainSummon == minions[0] && drop.IsAvailable, "짧은 누름은 교체 안 함");
        replace.OnPointerDown(pointer); start = Time.realtimeSinceStartup;
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
        Debug.Log("[InventoryUICheck] RUNTIME PASS — F 습득, 짧은 클릭 취소, 1초 홀드 교체, 기존 미니언 드랍, 분해 1회 회복, 결제 중복 방지.");
    }

    [MenuItem("Tools/UI/0928/Check flows and render previews")]
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
        var oldPouchUI = PouchUI.Instance;
        var oldExplain = SkillExplainUI.Instance;
        var oldTooltip = CommonTooltipUI.Instance;
        var oldPopup = UIPopUpManager.Instance;
        var oldEventSystem = EventSystem.current;
        float oldScale = Time.timeScale;
        NavMeshDataInstance navInstance = default;
        NavMeshData navData = null;
        var otherCameras = Camera.allCameras.Where(c => c.CompareTag("MainCamera")).ToArray();
        try
        {
            foreach (var other in otherCameras) other.enabled = false;
            var manager = new GameObject("Check managers");
            manager.SetActive(false);
            var gm = manager.AddComponent<GameManager>(); GameManager.Instance = gm;
            // StartScene에서도 실제 팝업 등록/정렬/닫기 경로를 검사한다.
            var popup = manager.AddComponent<UIPopUpManager>(); Singleton<UIPopUpManager>(popup);
            var inventory = manager.AddComponent<InventoryManager>(); InventoryManager.Instance = inventory;
            inventory.Slots.Add(new InventoryManager.CoreSlot()); inventory.Slots.Add(new InventoryManager.CoreSlot());
            var pouch = manager.AddComponent<ItemPouch>(); ItemPouch.Instance = pouch; Field(pouch, "slotCount", 9);
            var player = manager.AddComponent<PlayerController>(); Field(gm, "playerController", player);
            var stat = manager.AddComponent<CharacterStat>(); Field(player, "stat", stat);
            var health = manager.GetComponent<CharacterHealth>(); Field(stat, "<Health>k__BackingField", health); Field(health, "_stat", stat); health.SetHP(50f);
            var items = AssetDatabase.FindAssets("t:ItemSO").Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).ToArray();
            var minions = AssetDatabase.FindAssets("t:MainMinionDataSO").Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).ToArray();
            Check(items.Length > 0 && minions.Length >= 2, "검사용 실제 아이템/미니언");

            var canvas = new GameObject("Preview Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            var camera = new GameObject("Preview Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 270;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.16f, 0.17f, 0.18f);
            canvas.worldCamera = camera; canvas.planeDistance = 1;
            var texture = new RenderTexture(960, 540, 24); camera.targetTexture = texture;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            var events = new GameObject("Preview EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

            var pouchUI = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PouchPath), canvas.transform).GetComponent<PouchUI>();
            Call(pouchUI, "Awake");

            var explain = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ExplainPath), canvas.transform).GetComponent<SkillExplainUI>();
            Singleton<SkillExplainUI>(explain); Call(explain, "OnAwake");
            var slots = (PouchSlotUI[])Read(pouchUI, "slots");
            Time.timeScale = 1f;
            pouch.TryAdd(items[0]);
            pouchUI.SetOpen(true);
            Check(Time.timeScale == 1f, "V는 시간 정지 없음");
            pouchUI.BeginDrag(slots[0]); pouchUI.EndDrag(null, new Vector2(-10000, -10000), true);
            Check(pouch.Get(0) == items[0], "화면 밖/유효하지 않은 위치는 원본 보존");
            pouchUI.BeginDrag(slots[0]); pouchUI.SetOpen(false);
            Check(pouch.Get(0) == items[0] && Drops(scene) == 0, "놓기 전에 창 닫기: 드랍 취소");
            pouchUI.SetOpen(true);
            pouchUI.BeginDrag(slots[0]); pouchUI.EndDrag(slots[1]); Call(pouchUI, "Refresh");
            Check(pouch.Get(0) == null && pouch.Get(1) == items[0], "인벤토리 재정렬");
            Canvas.ForceUpdateCanvases();
            // 독립된 평면 NavMesh: 마우스 (800,270)는 월드 (320,0), 벽/물 방향 끝은 x≈420.
            var settings = NavMesh.GetSettingsByIndex(0);
            var sources = new System.Collections.Generic.List<NavMeshBuildSource> {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(200, 0.1f, 200),
                    transform = Matrix4x4.TRS(new Vector3(0, -0.05f, 0), Quaternion.identity, Vector3.one), area = 0 }
            };
            navData = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(220, 20, 220)), Vector3.zero, Quaternion.identity);
            Check(navData != null, "검사 바닥 NavMesh 생성");
            navInstance = NavMesh.AddNavMeshData(navData, new Vector3(320, 0, 0), Quaternion.Euler(-90, 0, 0));
            Check(GroundItem.TryFindDropPoint(new Vector3(320, 0, 0), 2, out var groundPoint) && Vector3.Distance(groundPoint, new Vector3(320, 0, 0)) < 0.1f, "안전한 바닥은 마우스 위치 그대로");
            Check(GroundItem.TryFindDropPoint(new Vector3(450, 0, 0), 100, out var edgePoint) && edgePoint.x < 420 && edgePoint.x > 415 && Mathf.Abs(edgePoint.y) < 0.1f, "바닥 밖은 가장 가까운 이동 가능 경계로 보정");
            manager.transform.position = new Vector3(320, 0, 0);
            var dropRay = camera.ScreenPointToRay(WorldDropPoint);
            Check(new Plane(Vector3.forward, manager.transform.position).Raycast(dropRay, out float dropDistance), "검사 마우스의 월드 평면 교차");
            Check(GroundItem.TryFindNearbyDropPoint(manager.transform.position, dropRay.GetPoint(dropDistance), 1.25f, out var nearbyPoint), "플레이어 주변 드랍 위치");
            int before = Drops(scene);
            pouchUI.BeginDrag(slots[1]); pouchUI.EndDrag(null, WorldDropPoint, true);
            Check(pouch.Get(1) == null && Drops(scene) == before + 1, "창을 닫기 전 즉시 드랍/원본 제거");
            var placed = scene.GetRootGameObjects().Select(g => g.GetComponent<GroundItem>()).First(g => g != null);
            Check(Vector3.Distance(placed.transform.position, nearbyPoint) < 0.1f && placed.GetComponent<SpriteRenderer>().sprite == GroundItem.ItemIcon(items[0]), "플레이어 주변 고정 거리/월드와 가방 아이콘 일치");
            before = Drops(scene);
            explain.SetOpen(true);
            Check(PouchUI.IsOpen && explain.IsOpen && !explain.IsExpanded && Time.timeScale == 1f && Drops(scene) == before, "V 위에 C 기본 접힘/공존/추가 드랍 없음");
            var statsLabel = (TextMeshProUGUI)Read(explain, "playerStatsText");
            Check(statsLabel != null && statsLabel.text.StartsWith("<line-height=19>체력<pos=112>50/") &&
                statsLabel.text.Contains($"물리 공격력<pos=112>{stat.ATK:0.##}\n"), "C 실제 스탯/열 정렬 연결");
            health.SetHP(40f); Call(explain, "RefreshPlayerStats");
            Check(statsLabel.text.Contains($"체력<pos=112>40/{stat.MAXHP:0}\n"), "열린 C 스탯 갱신");
            explain.SetOpen(false);
            var candidate = new RewardCandidate { category = RewardCategory.Item, rawData = items[0], goldAmount = 100 };
            before = Drops(scene);
            Check(RewardManager.TryGivePurchase(candidate, Vector3.zero) && pouch.Get(0) == items[0] && Drops(scene) == before, "빈자리 있으면 직접 습득");
            CheckCVFlows(pouchUI, explain, popup, events, camera, scene);
            while (!pouch.IsFull) pouch.TryAdd(items[0]);
            Check(RewardManager.TryGivePurchase(candidate, Vector3.zero) && Drops(scene) == before + 1, "가득 차면 무료 픽업 1개");
            candidate = new RewardCandidate { category = RewardCategory.Minion, rawData = minions[0] };
            Check(RewardManager.TryGivePurchase(candidate, Vector3.zero) && inventory.MainSummon == minions[0], "빈 미니언 슬롯 직접 장착");
            before = Drops(scene); candidate.rawData = minions[1];
            Check(RewardManager.TryGivePurchase(candidate, Vector3.zero) && inventory.MainSummon == minions[0] && Drops(scene) == before + 1, "미니언 슬롯 차면 기존 장착 유지/드랍");
            var sell = manager.AddComponent<SellItem>(); sell.item = new RewardCandidate { category = RewardCategory.Item, rawData = items[0], goldAmount = 1,
                displayData = new GrowthItemData { itemName = items[0].DisplayName, description = items[0].description } };
            var tooltip = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Hand Slot Selection/CommonTooltipUI.prefab"), canvas.transform).GetComponent<CommonTooltipUI>();
            Singleton<CommonTooltipUI>(tooltip); Call(tooltip, "OnAwake");
            Call(sell, "OnMouseOver");
            Check(((RectTransform)Read(tooltip, "tooltipPanel")).gameObject.activeSelf &&
                ((TextMeshProUGUI)Read(tooltip, "titleText")).text == items[0].DisplayName &&
                ((TextMeshProUGUI)Read(tooltip, "descriptionText")).text.Contains(items[0].description) &&
                ((TextMeshProUGUI)Read(tooltip, "footerText")).text.Contains("1G"), "진열품 호버 이름/효과/가격");
            Call(sell, "OnMouseExit");
            Check(!((RectTransform)Read(tooltip, "tooltipPanel")).gameObject.activeSelf, "진열품 호버 종료 시 툴팁 닫힘");
            before = Drops(scene);
            Check(!sell.Interact(manager) && inventory.GOLD == 0 && Drops(scene) == before, "잔액 부족 시 아이템/골드 변화 없음");

            var picker = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PickerPath), canvas.transform).GetComponent<HandSlotSelectionUI>();
            Call(picker, "OnAwake");
            var drop = GroundItem.Drop(minions[1], Vector3.zero);
            Check(drop.GetComponent<SpriteRenderer>().sprite == minions[1].minionIcon, "미니언 픽업은 스킬 아닌 미니언 아이콘");
            float hpBefore = health.CurHP;
            picker.Show(drop);
            var candidateCard = (SkillExplainSlotUI)Read(picker, "candidateCard");
            Check(((Image)Read(candidateCard, "iconImage")).sprite == minions[1].minionIcon &&
                ((TextMeshProUGUI)Read(candidateCard, "titleText")).text == minions[1].minionName, "습득 카드 미니언 그림/이름");
            foreach (var fill in picker.GetComponentsInChildren<Image>(true).Where(i => i.name == "HoldProgress"))
                Check(fill.sprite == null && fill.type == Image.Type.Simple && fill.rectTransform.anchorMin == Vector2.zero && fill.rectTransform.anchorMax.y == 1f, "홀드 게이지는 버튼 전체 직사각 영역");
            picker.Select(HandSlotSelectionItem.ActionKind.Skip);
            Check(drop.IsAvailable && health.CurHP == hpBefore && inventory.MainSummon == minions[0], "스킵 시 후보 유지/회복 없음");

            // 소유 데이터는 그대로 두고 화면 내용만 채워 미리보기를 렌더링한다.
            foreach (var world in scene.GetRootGameObjects().Where(g => g.GetComponent<GroundItem>() != null)) world.SetActive(false);
            picker.gameObject.SetActive(false);
            pouchUI.SetOpen(false);
            explain.SetOpen(true);
            ((SkillExplainSlotUI)Read(explain, "minionHeader")).SetData(minions[0].minionIcon, minions[0].minionName, "");
            var links = (SkillExplainSlotUI[])Read(explain, "linkedSkillSlots");
            links[0].SetData(minions[0].finisher.uiIcon, "기본 공격 마무리", minions[0].finisher.Describe());
            links[1].SetData(minions[0].dashModifier.uiIcon, "대쉬", minions[0].dashModifier.Describe());
            links[2].SetData(minions[0].minionSkill.icon, "Space · 미니언 스킬", minions[0].minionSkill.description);
            Render(camera, texture, "Temp/UI0928-C-Collapsed.png");
            var arrow = ((TextMeshProUGUI)Read(explain, "expandLabel")).GetComponentInParent<Button>();
            ClickArrow(arrow);
            Render(camera, texture, "Temp/UI0928-C.png");
            pouchUI.SetOpen(true);
            Render(camera, texture, "Temp/UI0928-CV-Expanded.png");
            ClickArrow(arrow);
            Render(camera, texture, "Temp/UI0928-CV-Collapsed.png");
            explain.SetOpen(false); pouchUI.SetOpen(true);
            pouchUI.BeginDrag(slots[0]);
            ((GameObject)Read(pouchUI, "dropOutline")).SetActive(true);
            ((Image)Read(pouchUI, "dragGhost")).rectTransform.anchoredPosition = new Vector2(325, -70);
            Render(camera, texture, "Temp/UI0928-V.png");
            pouchUI.SetOpen(false); picker.gameObject.SetActive(true); picker.Show(drop);
            foreach (var fill in picker.GetComponentsInChildren<Image>(true).Where(i => i.name == "HoldProgress"))
            {
                typeof(HandSlotSelectionItem).GetMethod("SetProgress", Flags).Invoke(fill.GetComponentInParent<HandSlotSelectionItem>(), new object[] { 0.5f });
                Check(fill.enabled && Mathf.Abs(fill.rectTransform.rect.width - (((RectTransform)fill.transform.parent).rect.width * 0.5f - 6f)) < 0.1f, "50% 홀드의 실제 채움 너비");
            }
            Render(camera, texture, "Temp/UI0928-Minion.png");
            picker.Hover(HandSlotSelectionItem.ActionKind.Compare, true);
            Render(camera, texture, "Temp/UI0928-Compare.png");
            camera.targetTexture = null; texture.Release(); Object.DestroyImmediate(texture);
            Debug.Log("[InventoryUICheck] PASS — C/V 양방향 공존/화살표/렌더·레이캐스트/ESC/Modal/System/드랍 취소, 기존 구매·미니언 검사 및 960x540 화면 7개 렌더.");
        }
        finally
        {
            if (navInstance.valid) navInstance.Remove();
            if (navData != null) Object.DestroyImmediate(navData);
            foreach (var other in otherCameras) if (other != null) other.enabled = true;
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous);
            GameManager.Instance = oldManager; InventoryManager.Instance = oldInventory; ItemPouch.Instance = oldPouch;
            PouchUI.Instance = oldPouchUI; Singleton<SkillExplainUI>(oldExplain); Singleton<CommonTooltipUI>(oldTooltip); Time.timeScale = oldScale;
            Singleton<UIPopUpManager>(oldPopup);
            if (oldEventSystem != null && oldEventSystem.isActiveAndEnabled) EventSystem.current = oldEventSystem;
        }
    }

    private static void CheckCVFlows(PouchUI pouchUI, SkillExplainUI explain, UIPopUpManager popup,
        EventSystem events, Camera camera, Scene scene)
    {
        var cRoot = (GameObject)Read(explain, "panelRoot");
        var vRoot = (GameObject)Read(pouchUI, "panelRoot");
        var stats = (RectTransform)Read(explain, "statsPanel");
        var details = (RectTransform)Read(explain, "detailsPanel");
        var label = (TextMeshProUGUI)Read(explain, "expandLabel");
        var effects = (RectTransform)Read(pouchUI, "setEffectsPanel");
        var panel = (RectTransform)Read(pouchUI, "panelRect");
        var slots = (PouchSlotUI[])Read(pouchUI, "slots");
        Check(stats != null && details != null && label != null && effects != null, "C/V 신규 직렬화 참조");
        var arrow = label.GetComponentInParent<Button>(includeInactive: true);
        Check(arrow != null && arrow.interactable && arrow.onClick.GetPersistentEventCount() == 1 &&
            arrow.onClick.GetPersistentTarget(0) == explain && arrow.onClick.GetPersistentMethodName(0) == nameof(SkillExplainUI.ToggleDetails) &&
            arrow.onClick.GetPersistentListenerState(0) != UnityEventCallState.Off, "실제 화살표의 영구 ToggleDetails 콜백");
        var rootRect = (RectTransform)cRoot.transform;
        Check(rootRect.anchorMin == Vector2.zero && rootRect.anchorMax == Vector2.one &&
            rootRect.offsetMin == Vector2.zero && rootRect.offsetMax == Vector2.zero, "C LoadoutPanel 전체 stretch");
        Check(stats.parent == rootRect && stats.sizeDelta == new Vector2(220, 480) && stats.anchoredPosition == new Vector2(-350, 0) &&
            details.parent == rootRect && details.sizeDelta == new Vector2(660, 480) && details.anchoredPosition == new Vector2(130, 0), "C 스탯/상세 패널 배치");
        Check(((TextMeshProUGUI)Read(explain, "playerStatsText")).transform.IsChildOf(stats.Find("PlayerStats")), "PlayerStats는 StatsPanel 아래");
        foreach (string name in new[] { "Equipment", "Guard", "Minion", "SpaceSkill", "Finisher", "Dash" })
            Check(details.Find(name) != null, "상세 패널 자식 " + name);
        Check(((SkillExplainSlotUI[])Read(explain, "equipmentSlots")).Any(s => s != null && s.transform == details.Find("Equipment")), "기존 equipmentSlots 참조 유지");
        var icons = (Image[])Read(explain, "summaryIcons");
        Check(icons != null && icons.Length == 5 && icons.All(i => i != null && i.transform.IsChildOf(stats)), "접힌 C의 요약 아이콘 5개");

        void State(bool c, bool v, bool expanded, string context, bool blocked = false)
        {
            Check(explain.IsOpen == c && PouchUI.IsOpen == v && explain.IsExpanded == expanded &&
                popup.IsOpen(cRoot) == c && popup.IsOpen(vRoot) == v &&
                cRoot.activeInHierarchy == c && vRoot.activeInHierarchy == v &&
                details.gameObject.activeSelf == expanded && label.text == (expanded ? "<" : ">"), context);
            Check(Time.timeScale == 1f && !GameManager.Instance.IsTimeStopped && popup.BlocksGameplayInput == blocked,
                context + ": 시간 정지 없음/입력 차단 레이어");
        }

        explain.SetOpen(false); pouchUI.SetOpen(false);
        explain.ToggleDetails(); State(false, false, false, "닫힌 C 상세 토글 무시");
        foreach (bool cFirst in new[] { false, true })
        {
            string order = cFirst ? "C→V" : "V→C";
            if (cFirst) { explain.Toggle(); pouchUI.SetOpen(true); }
            else { pouchUI.SetOpen(true); explain.Toggle(); }
            State(true, true, false, order + " 기본 접힘 공존");
            Check(explain.transform.parent == pouchUI.transform.parent &&
                explain.transform.GetSiblingIndex() > pouchUI.transform.GetSiblingIndex(), order + " C visualRoot 렌더 우선");
            Call(pouchUI, "Refresh");
            for (int toggle = 0; toggle < 4; toggle++)
            {
                bool expanded = toggle % 2 != 0;
                State(true, true, expanded, order + " 반복 토글 " + toggle);
                foreach (var slot in slots)
                {
                    var hit = RaycastUI(events, camera, ScreenPoint((RectTransform)slot.transform, camera));
                    Check(hit != null && (expanded ? hit.transform.IsChildOf(details) :
                        ExecuteEvents.GetEventHandler<IBeginDragHandler>(hit) == slot.gameObject),
                        order + (expanded ? " 확장 C 아래 V 입력 차단 " : " 접힌 C 옆 V 슬롯 입력 가능 ") + slot.Index +
                        $" hit={(hit != null ? hit.name : "null")}, position={ScreenPoint((RectTransform)slot.transform, camera)}");
                }
                var arrowHit = RaycastUI(events, camera, ScreenPoint((RectTransform)arrow.transform, camera));
                Check(arrowHit != null && ExecuteEvents.GetEventHandler<IPointerClickHandler>(arrowHit) == arrow.gameObject, "화살표 실제 클릭 영역");
                ClickArrow(arrow);
            }
            State(true, true, false, order + " 반복 토글 후 접힘");
            var slotHit = RaycastUI(events, camera, ScreenPoint((RectTransform)slots[0].transform, camera));
            ExecuteEvents.ExecuteHierarchy(slotHit, new PointerEventData(events), ExecuteEvents.beginDragHandler);
            Check(((Image)Read(pouchUI, "dragGhost")).gameObject.activeSelf, "접힌 C에서 실제 V 드래그 이벤트");
            pouchUI.EndDrag(null);
            explain.SetOpen(true); pouchUI.SetOpen(true);
            State(true, true, false, order + " 중복 열기");
            ClickArrow(arrow);
            pouchUI.SetOpen(false); State(true, false, true, order + " V만 닫아도 확장 C 유지");
            pouchUI.SetOpen(true); State(true, true, true, order + " V 재열기에도 확장 C 유지");
            explain.Toggle(); State(false, true, false, order + " C만 닫고 상세 초기화");
            explain.Toggle(); State(true, true, false, order + " C 재열기는 기본 접힘");
            ClickArrow(arrow);
            if (cFirst) { pouchUI.SetOpen(false); pouchUI.SetOpen(true); }
            Check(popup.CloseTopByEscape(), order + " 첫 ESC 처리");
            State(false, true, false, order + " 첫 ESC는 상단 C만 닫음");
            Check(popup.CloseTopByEscape(), order + " 두 번째 ESC 처리");
            State(false, false, false, order + " 두 번째 ESC는 남은 V 닫음");
            Check(!popup.CloseTopByEscape(), "중복 등록/남은 Overlay 없음");
        }

        pouchUI.SetOpen(true); Call(pouchUI, "Refresh");
        CheckDragCancelled(pouchUI, slots[0], scene, () => explain.SetOpen(true), "C 열기 중 V 드래그 취소");
        State(true, true, false, "드래그 중 C 열기도 공존");
        CheckDragCancelled(pouchUI, slots[0], scene, () => ClickArrow(arrow), "상세 펼치기 중 드래그 취소");
        State(true, true, true, "드래그 중 상세 펼치기");
        CheckDragCancelled(pouchUI, slots[0], scene, () => ClickArrow(arrow), "상세 접기 중 드래그 취소");
        State(true, true, false, "드래그 중 상세 접기");

        Canvas.ForceUpdateCanvases();
        var statsPoint = ScreenPoint(stats, camera);
        var detailsPoint = ScreenPoint(details, camera, new Vector2(.45f, 0));
        var effectsPoint = ScreenPoint(effects, camera);
        foreach (var point in new[] { statsPoint, detailsPoint, effectsPoint, WorldDropPoint })
            Check(camera.pixelRect.Contains(point) && !RectTransformUtility.RectangleContainsScreenPoint(panel, point, camera), "패널 제외 검사의 좌표는 화면 안/V 본체 밖");
        Check(!explain.ContainsScreenPoint(detailsPoint, camera) && IsOutside(pouchUI, detailsPoint), "접힌 상세/전체 stretch 루트는 드랍을 막지 않음");
        Check(!IsOutside(pouchUI, statsPoint), "보이는 C 스탯은 드랍 제외");
        CheckDragCancelled(pouchUI, slots[0], scene, () => pouchUI.EndDrag(null, statsPoint, true), "C 스탯에 놓기 취소");
        ClickArrow(arrow);
        Check(!IsOutside(pouchUI, detailsPoint) && IsOutside(pouchUI, WorldDropPoint), "펼친 C 상세만 드랍 제외");
        CheckDragCancelled(pouchUI, slots[0], scene, () => pouchUI.EndDrag(null, detailsPoint, true), "C 상세에 놓기 취소");
        cRoot.SetActive(false);
        try { Check(IsOutside(pouchUI, statsPoint) && IsOutside(pouchUI, detailsPoint), "상위 루트가 숨겨진 C 영역은 드랍 제외 해제"); }
        finally { cRoot.SetActive(true); }
        explain.SetOpen(false);
        Check(IsOutside(pouchUI, statsPoint) && IsOutside(pouchUI, detailsPoint), "닫힌 C 영역은 드랍 제외 해제");
        Check(!IsOutside(pouchUI, effectsPoint), "보이는 세트 효과는 드랍 제외");
        CheckDragCancelled(pouchUI, slots[0], scene, () => pouchUI.EndDrag(null, effectsPoint, true), "세트 효과에 놓기 취소");
        effects.gameObject.SetActive(false);
        try { Check(IsOutside(pouchUI, effectsPoint), "숨긴 세트 효과는 드랍 제외 해제"); }
        finally { effects.gameObject.SetActive(true); }

        explain.SetOpen(true); ClickArrow(arrow);
        var modal = new GameObject("Check modal"); modal.SetActive(false);
        CheckDragCancelled(pouchUI, slots[0], scene,
            () => Check(popup.Open(modal, UIPopUpManager.Layer.Modal), "Modal 열기"), "Modal이 V 드래그 취소");
        State(false, false, false, "Modal은 C/V 모두 닫고 상세 초기화", true);
        Check(!popup.CloseTopByEscape() && popup.IsOpen(modal), "Modal은 기본 ESC로 닫히지 않음");
        popup.Close(modal); Object.DestroyImmediate(modal);
        State(false, false, false, "Modal 닫은 뒤 Overlay 등록 없음");
        CheckDialoguePriority(pouchUI, explain, popup, events, camera, arrow);
        State(false, false, false, "대화 종료 후 정리");
    }

    private static void CheckDialoguePriority(PouchUI pouchUI, SkillExplainUI explain, UIPopUpManager popup,
        EventSystem events, Camera camera, Button arrow)
    {
        var oldDialogue = DialogueUI.Instance;
        var dialogue = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Dialogue/DialogueUI.prefab"), explain.transform.parent).GetComponent<DialogueUI>();
        try
        {
            Singleton<DialogueUI>(dialogue); Call(dialogue, "OnAwake");
            Field(dialogue, "charsPerSecond", 0f); Field(dialogue, "bodyEffect", null); Field(dialogue, "blockPlayerInput", false);
            dialogue.transform.SetAsFirstSibling();
            explain.SetOpen(true); ClickArrow(arrow); pouchUI.SetOpen(true);
            dialogue.Play((string)Read(dialogue, "debugDialogueId"));
            var panel = (GameObject)Read(dialogue, "panel");
            Check(dialogue.IsPlaying && popup.IsOpen(panel) && explain.IsExpanded && PouchUI.IsOpen &&
                dialogue.transform.GetSiblingIndex() > explain.transform.GetSiblingIndex(), "실제 Dialogue.Play의 System visualRoot가 C/V 위");
            var hit = RaycastUI(events, camera, ScreenPoint(((TextMeshProUGUI)Read(dialogue, "bodyText")).rectTransform, camera));
            Check(hit != null && hit.transform.IsChildOf(dialogue.transform), "중첩 Canvas 대화가 C/V보다 렌더/레이캐스트 우선");
            Check(!popup.CloseTopByEscape() && explain.IsExpanded && PouchUI.IsOpen, "System 위 ESC는 아래 C/V를 닫지 않음");
            explain.SetOpen(false); pouchUI.SetOpen(false);
            explain.SetOpen(true); pouchUI.SetOpen(true);
            Check(!explain.IsOpen && !PouchUI.IsOpen && popup.IsOpen(panel) && popup.BlocksGameplayInput &&
                Time.timeScale == 1f && !GameManager.Instance.IsTimeStopped, "System 중 Overlay 재열기 거절/시간 계속");
        }
        finally
        {
            dialogue.StopDialogue(); Object.DestroyImmediate(dialogue.gameObject); Singleton<DialogueUI>(oldDialogue);
        }
    }

    private static void CheckDragCancelled(PouchUI pouchUI, PouchSlotUI source, Scene scene, Action cancel, string context)
    {
        var item = ItemPouch.Instance.Get(source.Index);
        int before = Drops(scene);
        var ghost = (Image)Read(pouchUI, "dragGhost");
        var outline = (GameObject)Read(pouchUI, "dropOutline");
        var icon = (Image)Read(source, "iconImage");
        pouchUI.BeginDrag(source);
        Check(item != null && ghost.gameObject.activeSelf && icon.color.a < 1f, context + ": 실제 드래그 시작");
        outline.SetActive(true);
        cancel();
        Check(!ghost.gameObject.activeSelf && !outline.activeSelf && icon.color.a == 1f, context + ": 고스트/테두리/원본 표시 복원");
        pouchUI.EndDrag(null, WorldDropPoint, true);
        Check(ItemPouch.Instance.Get(source.Index) == item && Drops(scene) == before, context + ": 늦은 놓기에도 원본 보존/추가 드랍 없음");
    }

    private static void ClickArrow(Button arrow)
    {
        // EditMode에서도 프리팹의 RuntimeOnly 콜백을 검증한다. 복제 인스턴스만 잠시 변경한다.
        var state = arrow.onClick.GetPersistentListenerState(0);
        try { arrow.onClick.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime); arrow.onClick.Invoke(); }
        finally { arrow.onClick.SetPersistentListenerState(0, state); }
    }

    private static Vector2 ScreenPoint(RectTransform rect, Camera camera, Vector2 offset = default)
        => RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center + Vector2.Scale(rect.rect.size, offset)));

    private static bool IsOutside(PouchUI pouchUI, Vector2 point)
        => (bool)typeof(PouchUI).GetMethod("IsOutside", Flags).Invoke(pouchUI, new object[] { point });

    private static GameObject RaycastUI(EventSystem events, Camera camera, Vector2 point)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var hits = new System.Collections.Generic.List<RaycastResult>();
        // GraphicRaycaster는 ExecuteAlways가 아니므로 EditMode 픽스처에서는 등록 수명을 직접 감싼다.
        var raycasters = events.gameObject.scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<GraphicRaycaster>())
            .Where(raycaster => !RaycasterManager.GetRaycasters().Contains(raycaster)).ToArray();
        try
        {
            foreach (var raycaster in raycasters) Call(raycaster, "OnEnable");
            events.RaycastAll(new PointerEventData(events) { position = point }, hits);
        }
        finally { foreach (var raycaster in raycasters) Call(raycaster, "OnDisable"); }
        return hits.FirstOrDefault(h => h.gameObject != null && h.gameObject.scene == events.gameObject.scene).gameObject;
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
                Check(pouchRect.anchorMin == Vector2.zero && pouchRect.anchorMax == Vector2.one, name + " 드랍 테두리가 실제 씬에서도 전체 화면");
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
        var pouch = AssetDatabase.LoadAssetAtPath<GameObject>(PouchPath).GetComponent<PouchUI>();
        float distance = (float)Read(pouch, "dropDistance");
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
