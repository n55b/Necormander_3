#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Settings;
using AstroNuts.Localization;
using AstroNuts.UI;
using Object = UnityEngine.Object;

public static class GameplayFeedbackChecks
{
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static int _checks;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run checks in a separate batch Editor; do not replace an open scene.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _checks = 0;
        CheckChargeWalls();
        CheckPickupAndFocus();
        CheckKeywords();
        CheckInformationPanelBounds();
        CheckRoomRegistration();
        MinionSelectNPCSetup.VerifySpriteTests();
        Debug.Log($"[GameplayFeedbackChecks] PASS: {_checks} assertions + minion animation/evolution/NPC checks");
    }

    private static void CheckChargeWalls()
    {
        var bodyGo = new GameObject("Charge body");
        var wallGo = new GameObject("Wall");
        try
        {
            var body = bodyGo.AddComponent<CircleCollider2D>();
            body.radius = 1f;
            body.offset = Vector2.up;
            bodyGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            wallGo.layer = LayerMask.NameToLayer("Wall");
            var wall = wallGo.AddComponent<BoxCollider2D>();
            wall.size = new Vector2(0.2f, 20f);
            wallGo.transform.position = new Vector3(-1.1f, 1f, 0f);
            Physics2D.SyncTransforms();
            Assert(Physics2D.CircleCast(Vector2.zero, 1.5f, Vector2.right, 0.5f, Layers.WallMask).distance == 0f,
                "old feet/damage-radius probe reproduces zero-distance overlap");
            Assert(!Obstacle(body, Vector2.right, 0.5f), "charge can leave a rear wall");
            Assert(!Obstacle(body, Vector2.up, 0.5f), "charge can travel along a side wall");
            Assert(Obstacle(body, Vector2.left, 0.5f), "charge cannot enter a touching wall");
            wallGo.transform.position = new Vector3(1.6f, 1f, 0f);
            Physics2D.SyncTransforms();
            Assert(Obstacle(body, Vector2.right, 1f), "wall ahead stops the charge");
            Assert(!Obstacle(body, Vector2.left, 1f), "wall behind is not hit");
            wall.size = new Vector2(20f, 0.2f);
            wallGo.transform.position = new Vector3(0f, -0.1f, 0f);
            Physics2D.SyncTransforms();
            Assert(!Obstacle(body, Vector2.up, 1f), "body offset is respected at bottom wall");
            Assert(Obstacle(body, Vector2.down, 1f), "bottom wall still blocks movement into it");
        }
        finally { Object.DestroyImmediate(bodyGo); Object.DestroyImmediate(wallGo); }
    }

    private static bool Obstacle(Collider2D body, Vector2 direction, float distance)
    {
        object[] args = { body, direction, distance, (LayerMask)Layers.WallMask, default(RaycastHit2D) };
        return (bool)typeof(EliteChargerAIPatternSO).GetMethod("TryGetChargeObstacle", HiddenStatic).Invoke(null, args);
    }

    private static void CheckPickupAndFocus()
    {
        var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Interactables/EliteRewardBox.prefab");
        var box = (GameObject)PrefabUtility.InstantiatePrefab(boxPrefab);
        var pickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/GroundMinion.prefab");
        var pickup = (GameObject)PrefabUtility.InstantiatePrefab(pickupPrefab);
        try
        {
            Physics2D.SyncTransforms();
            var colliders = box.GetComponents<Collider2D>();
            Assert(colliders.Length == 2, "chest reproduction contains physical and interaction colliders");
            PopupSystem.ShowInteractionIcon(colliders[0], 0.25f);
            var icon = (SpriteRenderer)typeof(PopupSystem).GetField("_interactionIcon", HiddenStatic).GetValue(null);
            Vector3 first = icon.transform.position;
            for (int i = 0; i < 20; i++)
            {
                PopupSystem.ShowInteractionIcon(colliders[i % 2], 0.25f);
                Assert(Vector3.Distance(first, icon.transform.position) < 0.0001f, "F anchor stable with changing collider order");
            }
            var registry = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>("Assets/SOData/Registry/Growth Reward Registry.asset");
            var ground = pickup.GetComponent<GroundItem>();
            Assert(pickup.GetComponentsInChildren<SpriteRenderer>().Length == 1, "pickup has only one rendered icon");
            Assert(pickup.transform.Find("Icon").GetComponent<YSortableObject>() != null, "pickup retains Y sorting");
            foreach (var minion in registry.minionDatas.OfType<MainMinionDataSO>())
            {
                ground.SetMinion(minion);
                var sprite = pickup.GetComponentInChildren<SpriteRenderer>();
                Assert(Mathf.Abs(sprite.bounds.center.x - pickup.transform.position.x) < 0.001f &&
                    Mathf.Abs(sprite.bounds.min.y - pickup.transform.position.y) < 0.001f, minion.name + " pickup sprite aligns to its root");
                ground.OnFocused(null);
                var label = pickup.GetComponentInChildren<Canvas>();
                Assert(label != null && label.transform.position.y > sprite.bounds.max.y, minion.name + " label above visible sprite");
            }
        }
        finally
        {
            Object.DestroyImmediate(box); Object.DestroyImmediate(pickup);
            var icon = (SpriteRenderer)typeof(PopupSystem).GetField("_interactionIcon", HiddenStatic).GetValue(null);
            if (icon != null) Object.DestroyImmediate(icon.gameObject);
        }
    }

    private static void CheckKeywords()
    {
        LocalizationSettings.InitializationOperation.WaitForCompletion();
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("ko");
        var dictionary = AssetDatabase.LoadAssetAtPath<KeywordDictionary>("Assets/Data/Localization/KeywordDictionary.asset");
        string text = KeywordHighlighter.ApplyTags("충격파 격파 취약 <link=\"existing\">취약</link>", dictionary);
        Assert(text.StartsWith("충격파 "), "keyword does not match part of shockwave word");
        Assert(!text.Contains("<sprite"), "missing keyword sprite cannot render a question mark");
        Assert(text.Contains("vulnerable_stack") && text.Contains("break_stack"), "real keywords still get hover links: " + text);
        Assert(text.Contains("<link=\"existing\">취약</link>"), "existing markup is preserved");
        Assert(KeywordHighlighter.ApplyTags(text, dictionary) == text, "highlighting is idempotent");
        var ui = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/SkillExplainUI.prefab");
        foreach (var slot in ui.GetComponentsInChildren<SkillExplainSlotUI>(true))
        {
            var description = new SerializedObject(slot).FindProperty("descriptionText").objectReferenceValue as TMPro.TMP_Text;
            if (description == null) continue;
            var hover = description.GetComponent<KeywordTooltipController>();
            Assert(description.raycastTarget && hover != null &&
                new SerializedObject(hover).FindProperty("dictionary").objectReferenceValue == dictionary, "authored keyword hover wiring");
        }
    }

    private static void CheckInformationPanelBounds()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/SkillExplainUI.prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            var ui = instance.GetComponentInChildren<SkillExplainUI>(true);
            var settings = new SerializedObject(ui);
            var panel = (GameObject)settings.FindProperty("panelRoot").objectReferenceValue;
            var stats = (RectTransform)settings.FindProperty("statsPanel").objectReferenceValue;
            var details = (RectTransform)settings.FindProperty("detailsPanel").objectReferenceValue;
            instance.SetActive(true);
            panel.SetActive(true);
            details.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            var open = typeof(SkillExplainUI).GetField("_isOpen", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector2 statsPoint = RectTransformUtility.WorldToScreenPoint(null, stats.TransformPoint(stats.rect.center));
            Assert(!ui.ContainsScreenPoint(statsPoint, null), "closed C panel does not block attacks");
            open.SetValue(ui, true);
            Assert(ui.ContainsScreenPoint(statsPoint, null), "C panel consumes clicks inside its stats");
            Assert(!ui.ContainsScreenPoint(new Vector2(-10000f, -10000f), null), "open C panel does not block world clicks");
            details.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Vector2 detailPoint = RectTransformUtility.WorldToScreenPoint(null, details.TransformPoint(details.rect.center));
            Assert(ui.ContainsScreenPoint(detailPoint, null), "expanded C panel consumes clicks inside details");
            details.gameObject.SetActive(false);
            Assert(!ui.ContainsScreenPoint(detailPoint, null), "collapsed details no longer intercept attacks");
        }
        finally { Object.DestroyImmediate(instance); }
    }

    private static void CheckRoomRegistration()
    {
        var rooms = AssetDatabase.LoadAssetAtPath<RoomPrefabDataSO>("Assets/SOData/Map/RoomPrefabData.asset");
        var candidates = rooms.GetValidPrefabs(RoomType.Normal);
        for (int i = 1; i <= 13; i++)
        {
            var room = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Map/Room Map Prefabs/Normal/Test/Room_Normal {i}.prefab");
            Assert(candidates.Count(p => p == room) == 1, "normal room " + i + " registered exactly once");
            Assert(room.GetComponent<RoomInstance>().roomType == RoomType.Normal && room.GetComponent<NormalRoomEvent>() != null,
                "normal room " + i + " combat event/type");
            foreach (var transform in room.GetComponentsInChildren<Transform>(true))
                Assert(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "room " + i + " missing scripts");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[GameplayFeedbackChecks] FAIL: " + message);
        _checks++;
    }
}
#endif
