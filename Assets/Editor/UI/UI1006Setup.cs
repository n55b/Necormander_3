using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>1006 외형 검수 전용(저장하지 않음). 레이아웃을 초기화하던 '적용' 메뉴 두 개는 끝난 일회성 이행이라 지웠다 —
/// 다시 돌리면 지운 단색 테두리(Frame1006)를 UI 스킨 위에 다시 덮었다.</summary>
public static class UI1006Setup
{
    private const string Explain = "Assets/Prefabs/UI/SkillExplainUI.prefab";
    private const string Pouch = "Assets/Prefabs/UI/PouchUI.prefab";
    private const string Picker = "Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab";
    private const string Hud = "Assets/Prefabs/UI/Player State/PlayerStateUI.prefab";
    private const string Map = "Assets/Prefabs/UI/MiniMap.prefab";

    private static RectTransform At(Transform parent, string name)
        => parent.Find(name) as RectTransform ?? throw new InvalidOperationException("기존 UI 누락: " + parent.name + "/" + name);

    [MenuItem("Tools/UI/1006/Validate layout")]
    public static void Validate()
    {
        InventoryUISetup.Validate();
        foreach (var path in new[] { Explain, Pouch, Picker, Hud, Map })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(root.GetComponentsInChildren<MonoBehaviour>(true).All(c => c != null), "Missing script: " + path);
            foreach (var frame in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Frame1006"))
                Check(frame.GetComponentsInChildren<UnityEngine.UI.Image>().All(i => i.sprite == null && !i.raycastTarget), "기본 도형/클릭 통과");
        }
        var info = AssetDatabase.LoadAssetAtPath<GameObject>(Explain);
        Check(At(info.transform, "LoadoutPanel/StatsPanel").sizeDelta == new Vector2(220, 480), "C 왼쪽 스탯 치수");
        Check(!At(info.transform, "LoadoutPanel/DetailsPanel").gameObject.activeSelf, "C 상세는 기본 접힘");
        Check(At(info.transform, "LoadoutPanel/StatsPanel/ExpandButton").GetComponent<UnityEngine.UI.Button>().onClick.GetPersistentEventCount() == 1, "화살표 배선");
        Check(new SerializedObject(info.GetComponent<SkillExplainUI>()).FindProperty("statRows").arraySize > 0, "스탯 배선");
        // V(주머니)는 9칸 고정판에서 스크롤 목록으로 바뀌어 옛 치수·칸 수 검사를 뺐다. 지금 구조는 InventoryUICheck 가 검사한다.
        var picker = AssetDatabase.LoadAssetAtPath<GameObject>(Picker);
        Check(At(picker.transform, "MinionSelection/Replace").sizeDelta == new Vector2(300, 60), "버튼 치수");
        Check(picker.GetComponent<MainMinionSelectionUI>().ConfirmHoldSeconds == 1f, "1초 홀드 보존");
        var map = AssetDatabase.LoadAssetAtPath<GameObject>(Map);
        Check(At(map.transform, "Image_MiniMap").sizeDelta == new Vector2(120, 100), "미니맵 치수");
        Debug.Log("[UI1006] VALIDATE PASS — authored placeholders, C/V/buttons/HUD/map dimensions and references.");
    }

    public static void ValidateBatch()
    {
        try
        {
            // 배치 시작의 이름 없는 씬은 Additive 검사 씬을 열 수 없다.
            if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Single);
            Validate(); CheckDynamic(); MiniMapCheck.Run();
            InventoryUICheck.CheckScenes(); InventoryUICheck.CheckNearbyDrop(); InventoryUICheck.Run();
            Preview(); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/UI/1006/Check dynamic HUD and text")]
    public static void CheckDynamic()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 검사하세요.");
        var scene = EditorSceneManager.NewPreviewScene();
        var canvasGo = new GameObject("HUD test", typeof(RectTransform), typeof(Canvas));
        var playerGo = new GameObject("HUD test player", typeof(PlayerController), typeof(MeleeDodgeController));
        var cameraGo = new GameObject("HUD test camera", typeof(Camera));
        foreach (var go in new[] { canvasGo, playerGo, cameraGo }) SceneManager.MoveGameObjectToScene(go, scene);
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var camera = cameraGo.GetComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Hud), canvas.transform);
            var dash = hud.GetComponentInChildren<DashCooldownUI>(true);
            var player = playerGo.GetComponent<PlayerController>();
            var dodge = playerGo.GetComponent<MeleeDodgeController>();
            dash.Initialize(player);
            typeof(DashCooldownUI).GetField("_worldCamera", flags).SetValue(dash, camera);
            var fills = (UnityEngine.UI.Image[])typeof(DashCooldownUI).GetField("worldPipFills", flags).GetValue(dash);
            var pips = (RectTransform)typeof(DashCooldownUI).GetField("worldPips", flags).GetValue(dash);
            foreach (int maximum in new[] { 2, 4, 1 })
            {
                typeof(MeleeDodgeController).GetField("maxCharges", flags).SetValue(dodge, maximum);
                typeof(MeleeDodgeController).GetField("_currentCharges", flags).SetValue(dodge, 1);
                typeof(MeleeDodgeController).GetField("_rechargeTimer", flags).SetValue(dodge, dodge.RechargeTime * .5f);
                typeof(DashCooldownUI).GetMethod("UpdateWorldPips", flags).Invoke(dash, null);
                for (int i = 0; i < fills.Length; i++)
                {
                    Check(fills[i].transform.parent.parent.gameObject.activeSelf == (i < maximum), "대시 최대 횟수 칸 표시");
                    float expected = i == 0 ? 1 : i == 1 ? dodge.RechargeProgress : 0;
                    Check(Mathf.Abs(fills[i].rectTransform.anchorMax.x - expected) < .001f, "대시 사용/충전 표시");
                }
            }
            var worldOffset = (Vector3)typeof(DashCooldownUI).GetField("worldOffset", flags).GetValue(dash);
            foreach (float scale in new[] { 1f, 2f })
            {
                canvas.scaleFactor = scale;
                Canvas.ForceUpdateCanvases();
                Check(((RectTransform)pips.parent).rect.width > 0f, "발밑 좌표 검사 부모 너비");
                foreach (var position in new[] { Vector3.zero, new Vector3(1, 1, 0), new Vector3(-2, -1, 0) })
                {
                    playerGo.transform.position = position;
                    typeof(DashCooldownUI).GetMethod("UpdateWorldPips", flags).Invoke(dash, null);
                    Vector2 expected = camera.WorldToScreenPoint(position + worldOffset);
                    Vector2 actual = RectTransformUtility.WorldToScreenPoint(null, pips.position);
                    Check(Vector2.Distance(expected, actual) < .1f,
                        $"발밑 위치 일치: scale={scale}, expected={expected}, actual={actual}");
                }
            }
            Vector2 before = pips.anchoredPosition;
            playerGo.transform.position = new Vector3(1, 1, 0);
            typeof(DashCooldownUI).GetMethod("UpdateWorldPips", flags).Invoke(dash, null);
            Check(Vector2.Distance(before, pips.anchoredPosition) > 1, "발밑 표시가 플레이어를 따라감");
            Object.DestroyImmediate(playerGo);
            typeof(DashCooldownUI).GetMethod("UpdateWorldPips", flags).Invoke(dash, null);
            Check(!pips.gameObject.activeSelf, "플레이어 파괴 후 표시 해제");
            foreach (var name in new[] { "hpSprite", "swapCooldownFill" })
            {
                var fill = (UnityEngine.UI.Image)new SerializedObject(hud.GetComponent<PlayerStateUI>()).FindProperty(name).objectReferenceValue;
                foreach (float fraction in new[] { 0f, .5f, 1f })
                {
                    typeof(PlayerStateUI).GetMethod("SetFill", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { fill, fraction });
                    Check(Mathf.Abs(fill.rectTransform.anchorMax.x - fraction) < .001f, "도형 게이지 0/50/100% 너비");
                }
            }
            var info = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Explain), canvas.transform);
            At(info.transform, "LoadoutPanel").gameObject.SetActive(true);
            At(info.transform, "LoadoutPanel/DetailsPanel").gameObject.SetActive(true);
            foreach (var equipment in AssetDatabase.FindAssets("t:EquipmentSO").Select(g => AssetDatabase.LoadAssetAtPath<EquipmentSO>(AssetDatabase.GUIDToAssetPath(g))))
                CheckText(info, "Equipment", equipment.description.ko + "\n\n강화 +0/2", equipment.name);
            foreach (var main in AssetDatabase.FindAssets("t:MainMinionDataSO").Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g))))
            {
                CheckText(info, "Finisher", main.finisher.Describe(), main.name);
                CheckText(info, "Dash", main.dashModifier.Describe(), main.name);
                if (main.minionSkill != null) CheckText(info, "SpaceSkill", main.minionSkill.description, main.name);
            }
            Debug.Log("[UI1006] DYNAMIC PASS — 대시 횟수/충전/추적/파괴, 도형 게이지 너비, 실제 데이터 설명 검사.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void CheckText(GameObject root, string slotName, string value, string source)
    {
        // 소환수 칸은 DetailsPanel/MinionPanel 아래로 옮겨졌다. 깊이와 무관하게 이름으로 찾는다.
        var slot = root.GetComponentsInChildren<SkillExplainSlotUI>(true).First(s => s.name == slotName);
        var label = (TextMeshProUGUI)new SerializedObject(slot).FindProperty("descriptionText").objectReferenceValue;
        label.enableAutoSizing = false; label.fontSize = 10;
        var preferred = label.GetPreferredValues(value, label.rectTransform.rect.width, Mathf.Infinity);
        Check(preferred.y <= label.rectTransform.rect.height + 1,
            $"TEXT OVERFLOW: {slotName}/{source} needs {preferred.y:0}, available {label.rectTransform.rect.height:0}");
    }

    [MenuItem("Tools/UI/1006/Render previews")]
    public static void Preview()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var cameraGo = new GameObject("UI1006 preview camera", typeof(Camera));
        var canvasGo = new GameObject("UI1006 preview canvas", typeof(RectTransform), typeof(Canvas));
        SceneManager.MoveGameObjectToScene(cameraGo, scene); SceneManager.MoveGameObjectToScene(canvasGo, scene);
        var camera = cameraGo.GetComponent<Camera>(); camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .16f);
        camera.orthographic = true;
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        var target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
        var oldTarget = RenderTexture.active;
        try
        {
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            // EditMode에서는 CanvasScaler.Update가 자동 실행되지 않는다.
            typeof(UnityEngine.UI.CanvasScaler).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(scaler, null);
            foreach (string path in new[] { Explain, Pouch, Picker, Hud, Map })
            {
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), canvas.transform);
                if (path == Explain)
                {
                    At(go.transform, "LoadoutPanel").gameObject.SetActive(true);
                    var slots = go.GetComponentsInChildren<SkillExplainSlotUI>(true);
                    var main = AssetDatabase.FindAssets("t:MainMinionDataSO").Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g))).First(m => m.minionSkill != null);
                    foreach (var slot in slots)
                    {
                        switch (slot.name)
                        {
                            case "Equipment": slot.SetData(null, "시작형 건틀릿 - MK0", "기본 공격 3연타 · 1 / 1 / 1.5\n보통 공격 속도 / 경직 0.2초\n기본 공격의 마지막 타격은 미니언과 연계합니다."); break;
                            case "Minion": slot.SetData(main.minionIcon, main.minionName, ""); break;
                            case "SpaceSkill": slot.SetData(main.minionSkill.icon, "Space · " + main.minionSkill.skillName, main.minionSkill.description); break;
                            case "Finisher": slot.SetData(main.finisher.uiIcon, "기본 공격 마무리", main.finisher.Describe()); break;
                            case "Dash": slot.SetData(main.dashModifier.uiIcon, "대쉬 공격", main.dashModifier.Describe()); break;
                        }
                    }
                }
                if (path == Pouch)
                {
                    At(go.transform, "PouchWindow").gameObject.SetActive(true);
                    var items = AssetDatabase.FindAssets("t:ItemSO").Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
                    var slots = go.GetComponentsInChildren<PouchSlotUI>(true);
                    for (int i = 0; i < slots.Length; i++) slots[i].Bind(go.GetComponent<PouchUI>(), i < 4 ? items[i % items.Length] : null, 1);
                }
                if (path == Picker)
                {
                    At(go.transform, "MinionSelection").gameObject.SetActive(true);
                    var minions = AssetDatabase.FindAssets("t:MainMinionDataSO").Take(2).Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
                    var pickerData = new SerializedObject(go.GetComponent<MainMinionSelectionUI>());
                    ((SkillExplainSlotUI)pickerData.FindProperty("currentCard").objectReferenceValue).SetData(minions[0].minionIcon, minions[0].minionName, "현재 장착");
                    ((SkillExplainSlotUI)pickerData.FindProperty("candidateCard").objectReferenceValue).SetData(minions[1].minionIcon, minions[1].minionName, "교체 후보");
                    foreach (var name in new[] { "Replace", "Skip" })
                    {
                        var fill = At(go.transform, "MinionSelection/" + name + "/HoldProgress").GetComponent<UnityEngine.UI.Image>();
                        fill.enabled = true; fill.rectTransform.anchorMax = new Vector2(.5f, 1);
                    }
                }
                if (path == Hud) At(go.transform, "WorldDashPips").gameObject.SetActive(true);
                CapturePreview(camera, target, go.name.Replace("(Clone)", ""));
                if (path == Explain)
                {
                    var bag = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Pouch), canvas.transform);
                    At(bag.transform, "PouchWindow").gameObject.SetActive(true);
                    foreach (var slot in bag.GetComponentsInChildren<PouchSlotUI>(true)) slot.Bind(bag.GetComponent<PouchUI>(), null, 0);
                    go.transform.SetAsLastSibling();
                    CapturePreview(camera, target, "C-V");
                    At(go.transform, "LoadoutPanel/DetailsPanel").gameObject.SetActive(true);
                    At(go.transform, "LoadoutPanel/StatsPanel/ExpandButton/Label").GetComponent<TextMeshProUGUI>().text = "<";
                    CapturePreview(camera, target, "C-expanded-V");
                    Object.DestroyImmediate(bag);
                    CapturePreview(camera, target, "SkillExplainUI-expanded");
                }
                Object.DestroyImmediate(go);
            }
            Debug.Log("[UI1006] PREVIEW PASS — Logs/UI1006/*.png");
        }
        finally
        {
            RenderTexture.active = oldTarget; camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CapturePreview(Camera camera, RenderTexture target, string name)
    {
        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
        System.IO.Directory.CreateDirectory("Logs/UI1006");
        System.IO.File.WriteAllBytes("Logs/UI1006/" + name + ".png", pixels.EncodeToPNG());
        Object.DestroyImmediate(pixels);
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("[UI1006] " + message); }
}
