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
using Object = UnityEngine.Object;

/// <summary>실제 프리팹/데이터로 재실행하는 회귀 검사. 저장 데이터와 현재 씬은 수정하지 않는다.</summary>
public static class InventoryUICheck
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string PouchPath = "Assets/Prefabs/UI/PouchUI.prefab";
    private const string ExplainPath = "Assets/Prefabs/UI/SkillExplainUI.prefab";
    private const string PickerPath = "Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab";
    private const string RuntimeKey = "InventoryUICheck.Runtime";
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
        Check(inventory.MainSummon == minions[1] && (drop == null || !drop.IsAvailable), "3초 홀드로 교체");
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
        Debug.Log("[InventoryUICheck] RUNTIME PASS — F 습득, 짧은 클릭 취소, 3초 홀드 교체, 기존 미니언 드랍, 분해 1회 회복, 결제 중복 방지.");
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
            int before = Drops(scene);
            pouchUI.BeginDrag(slots[1]); pouchUI.EndDrag(null, new Vector2(800, 270), true);
            Check(pouch.Get(1) == null && Drops(scene) == before + 1, "창을 닫기 전 즉시 드랍/원본 제거");
            var placed = scene.GetRootGameObjects().Select(g => g.GetComponent<GroundItem>()).First(g => g != null);
            Check(Vector3.Distance(placed.transform.position, groundPoint) < 0.1f && placed.GetComponent<SpriteRenderer>().sprite == GroundItem.ItemIcon(items[0]), "드랍 좌표 무작위 오프셋 없음/월드와 가방 아이콘 일치");
            before = Drops(scene);
            explain.SetOpen(true);
            Check(!PouchUI.IsOpen && explain.IsOpen && Time.timeScale == 1f && Drops(scene) == before, "C로 전환 시 추가 드랍 없음");
            explain.SetOpen(false);
            var candidate = new RewardCandidate { category = RewardCategory.Item, rawData = items[0], goldAmount = 100 };
            before = Drops(scene);
            Check(RewardManager.TryGivePurchase(candidate, Vector3.zero) && pouch.Get(0) == items[0] && Drops(scene) == before, "빈자리 있으면 직접 습득");
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
            explain.SetOpen(true);
            ((SkillExplainSlotUI)Read(explain, "minionHeader")).SetData(minions[0].minionIcon, minions[0].minionName, "");
            var links = (SkillExplainSlotUI[])Read(explain, "linkedSkillSlots");
            links[0].SetData(minions[0].finisher.uiIcon, "기본 공격 마무리", minions[0].finisher.Describe());
            links[1].SetData(minions[0].dashModifier.uiIcon, "대쉬", minions[0].dashModifier.Describe());
            links[2].SetData(minions[0].minionSkill.icon, "Space · 미니언 스킬", minions[0].minionSkill.description);
            Render(camera, texture, "Temp/UI0928-C.png");
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
            Debug.Log("[InventoryUICheck] PASS — 즉시 드랍/가장 가까운 바닥/취소/아이콘 통일/사각 홀드/구매/미니언 스킵/상점 호버 및 4개 화면 렌더.");
        }
        finally
        {
            if (navInstance.valid) navInstance.Remove();
            if (navData != null) Object.DestroyImmediate(navData);
            foreach (var other in otherCameras) if (other != null) other.enabled = true;
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous);
            GameManager.Instance = oldManager; InventoryManager.Instance = oldInventory; ItemPouch.Instance = oldPouch;
            PouchUI.Instance = oldPouchUI; Singleton<SkillExplainUI>(oldExplain); Singleton<CommonTooltipUI>(oldTooltip); Time.timeScale = oldScale;
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
                Check(pouchRect.anchorMin == Vector2.zero && pouchRect.anchorMax == Vector2.one, name + " 드랍 테두리가 실제 씬에서도 전체 화면");
                Check(pouch[0].gameObject.activeInHierarchy && info[0].gameObject.activeInHierarchy && pickers[0].gameObject.activeInHierarchy, name + " 싱글턴 초기화 가능한 루트");
                Check((GameObject)Read(info[0], "panelRoot") != info[0].gameObject, name + " C 루트와 표시 패널 분리");
                Check((GameObject)Read(pickers[0], "panel") != pickers[0].gameObject, name + " 미니언 루트와 표시 패널 분리");
                Check(roots.SelectMany(r => r.GetComponentsInChildren<CommonTooltipUI>(true)).Count() == 1, name + " 공용 툴팁 실제 배치");
                Debug.Log("[InventoryUICheck] SCENE PASS: " + name);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        SceneManager.SetActiveScene(previous);
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
