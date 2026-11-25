using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PresentationRegressionCheck
{
    private const string Running = "PresentationRegressionCheck.Running";
    private static IEnumerator checks;
    private static double deadline;

    static PresentationRegressionCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Running, false)) return;
            checks = Verify();
            deadline = EditorApplication.timeSinceStartup + (SessionState.GetBool("Verification.ArtDirection",false) ? 300 : 120);
            EditorApplication.update += Tick;
        };
    }

    public static void Run()
    {
        if (!Application.isBatchMode || PlayerSettings.companyName != "NecromanderVerification")
            throw new InvalidOperationException("Use Tools/Invoke-UnityVerification.ps1 to isolate player data.");
        EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");
        SessionState.SetBool(Running, true);
        EditorApplication.EnterPlaymode();
    }

    internal static void UpdateCamera(CinemachineBrain brain)
    {
        var method = brain.UpdateMethod;
        try
        {
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.ManualUpdate();
        }
        finally { brain.UpdateMethod = method; }
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Presentation checks timed out.");
            if (checks.MoveNext()) return;
            Debug.Log("[PresentationRegressionCheck] PASS");
            Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Exit(1);
        }
    }

    private static void Exit(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.EraseBool(Running);
        EditorApplication.Exit(code);
    }

    private static IEnumerator CheckVisibility(OptionManager options)
    {
        float previousScale = Time.timeScale;
        Time.timeScale = 0f;
        try
        {
            var settings = options.settingsMenuPanel;
            var pause = options.pauseMenuPanel;
            float until = Time.realtimeSinceStartup + .4f;
            while (Time.realtimeSinceStartup < until) yield return null;
            options.SwitchToPausePanel();
            var input = settings.GetComponent<CanvasGroup>();
            Require(!UIOpenMotion.IsShown(settings) && settings.activeSelf && !input.blocksRaycasts && !input.interactable,
                "Closing settings releases input immediately during pause");
            yield return null;
            options.SwitchToSettingsPanel();
            Require(UIOpenMotion.IsShown(settings) && input.blocksRaycasts && !UIOpenMotion.IsShown(pause),
                "Reopening settings cancels its pending exit");
            until = Time.realtimeSinceStartup + .5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Require(settings.activeSelf && !pause.activeSelf && input.alpha > .99f, "Paused menu reversal settles on the requested panel");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Interactables/RoomRewardBox.prefab");
            var chest = Object.Instantiate(prefab);
            try
            {
                var sprite = chest.GetComponent<SpriteRenderer>();
                var collider = chest.GetComponent<Collider2D>();
                var origin = chest.transform.position;
                Require(sprite.color.a == 0f && collider.enabled, "Reward chest appears with its floor contact and collider ready");
                until = Time.realtimeSinceStartup + .35f;
                bool intermediate = false;
                while (Time.realtimeSinceStartup < until)
                {
                    intermediate |= sprite.color.a > 0f && sprite.color.a < 1f;
                    yield return null;
                }
                Require(intermediate && sprite.color.a > .99f && chest.transform.position == origin,
                    "Reward chest interpolates opacity at a fixed ground position");
                PopupSystem.ShowInteractionIcon(collider, .2f);
                var first = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                    .Single(r => r.name.StartsWith("Interaction F Icon"));
                while (first.color.a < .99f) yield return null;
                var point = first.transform.position;
                var second = new GameObject("Next interaction").AddComponent<BoxCollider2D>();
                try
                {
                    second.transform.position = origin + Vector3.right * 3f;
                    PopupSystem.ShowInteractionIcon(second, .2f);
                    Require(first.transform.position == point && first.color.a > .99f,
                        "Interaction prompt leaves its previous target in place");
                    PopupSystem.HideInteractionIcon();
                    while (first.gameObject.activeSelf) yield return null;
                    Require(!Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                        .Any(r => r.name.StartsWith("Interaction F Icon")), "Both interaction prompts finish fading");
                }
                finally { Object.Destroy(second.gameObject); }
            }
            finally { PopupSystem.ReleaseInteractionIcon(); Object.Destroy(chest); }
            Debug.Log("[Visibility] PASS paused option reversal, input, chest reveal and prompt crossfade.");
        }
        finally { Time.timeScale = previousScale; }
    }

    private static IEnumerator Verify()
    {
        Application.runInBackground = true;
        PlayModeWindow.SetCustomRenderingResolution(640,360,"Presentation reference");
        while (Screen.width != 640 || Screen.height != 360) yield return null;
        Require(PixelViewport.CalculateDisplayRect(1280, 720) == new RectInt(0, 0, 1280, 720), "1280×720 output");
        Require(PixelViewport.CalculateDisplayRect(1366, 768) == new RectInt(0, 0, 1366, 768), "1366×768 output");
        Require(PixelViewport.CalculateDisplayRect(1920, 1080) == new RectInt(0, 0, 1920, 1080), "1920×1080 output");
        Require(PixelViewport.CalculateDisplayRect(2560, 1440) == new RectInt(0, 0, 2560, 1440), "2560×1440 output");
        Require(PixelViewport.CalculateDisplayRect(1280, 960) == new RectInt(0, 120, 1280, 720), "4:3 letterbox");
        Require(PixelViewport.CalculateDisplayRect(512, 384) == new RectInt(0, 48, 512, 288), "small window fits entire frame");
        Require(PixelViewport.CalculateDisplayRect(0, 0) == default, "empty viewport before window initialization");
        foreach (var size in new[] { new Vector2Int(1280, 960), new Vector2Int(1366, 768) })
        {
            var display = PixelViewport.CalculateDisplayRect(size.x, size.y);
            var game = new Vector2(160, 270);
            var screen = PixelViewport.GameToScreenPoint(game, display);
            Require((PixelViewport.ScreenToGamePoint(screen, display) - game).sqrMagnitude < .0001f,
                $"Logical and display coordinates round trip at {size.x}×{size.y}");
        }
        while (UnityNote.SceneLoader.IsLoading) yield return null;

        foreach (string scene in new[] { "StartScene", "OptionScene", "VillageScene", "BattleScene" })
        {
            if (scene == "OptionScene") SceneManager.LoadScene(scene, LoadSceneMode.Additive);
            else if (scene != "StartScene")
            {
                if (scene == "BattleScene") SceneManager.sceneLoaded += SetBattleSeed;
                Require(UnityNote.SceneLoader.Load(scene), "Load " + scene);
            }
            yield return null;
            while (UnityNote.SceneLoader.IsLoading) yield return null;
            yield return null;
            yield return null;
            if (scene == "OptionScene")
            {
                Object.FindFirstObjectByType<OptionManager>().SwitchToSettingsPanel();
                yield return null;
            }

            Canvas.ForceUpdateCanvases();
            var camera = PixelArtUpscaleRig.RenderCamera;
            Require(camera != null && camera.targetTexture != null, scene + " frame camera");
            var frame = camera.targetTexture;
            Require(frame.width == 640 && frame.height == 360 && frame.filterMode == FilterMode.Point &&
                frame.antiAliasing == 1 && !camera.allowMSAA, scene + " point-sampled frame");

            if (scene == "VillageScene" || scene == "BattleScene")
            {
                var birds = scene == "VillageScene" ? AmbientBirdCheck.Village(camera) : AmbientBirdCheck.Dungeon();
                while (birds.MoveNext()) yield return birds.Current;
            }
            if (scene is "StartScene" or "OptionScene") VerifyButton(scene, camera);
            if (scene == "StartScene")
            {
                if (UiResolutionCheck.Enabled)
                {
                    var sizes = UiResolutionCheck.Verify("Title", GameObject.Find("Start Button").GetComponent<RectTransform>(), GameObject.Find("Start Button").GetComponent<Button>());
                    while (sizes.MoveNext()) yield return sizes.Current;
                }
                VerifyMenuFontRecovery();
                var logo = Object.FindObjectsByType<VectorUiImage>(FindObjectsSortMode.None)
                    .FirstOrDefault(image => image.sprite != null && image.sprite.name == "title_0");
                Require(logo != null && logo.UsesVector && logo.mainTexture == Texture2D.whiteTexture,
                    "the authored title logo renders as vector geometry");
            }
            if (scene == "OptionScene")
            {
                var visibility = CheckVisibility(Object.FindFirstObjectByType<OptionManager>());
                while (visibility.MoveNext()) yield return visibility.Current;
                VerifyVectorImageLayout();
                var dropdown = Object.FindFirstObjectByType<TMP_Dropdown>();
                dropdown.Show();
                float readyAt = Time.unscaledTime + dropdown.alphaFadeSpeed;
                do { yield return null; } while (Time.unscaledTime < readyAt);
                Canvas.ForceUpdateCanvases();
                Require(dropdown.IsExpanded, "Language dropdown opens");
                var popup = dropdown.template.parent.Find("Dropdown List");
                Require(popup != null, "Language dropdown creates a popup beside its template");
                var options = popup.GetComponentsInChildren<TextMeshProUGUI>();
                Require(options.Any(label => label.text == "English" && UsesPixelGlyphs(label)) &&
                    options.Any(label => label.text == "한국어" && UsesPixelGlyphs(label)),
                    "English and Korean dropdown items use pixel glyphs");
                var exit = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                    .First(button => button.name == "Button_Exit" && button.gameObject.scene.name == scene);
                Require(!Overlaps((RectTransform)popup, (RectTransform)exit.transform, camera),
                    "open language list clears the settings exit button");
                SaveFrame(frame, "OptionSceneDropdown");
                dropdown.Hide();
                while (dropdown.IsExpanded) yield return null;

                while (!LocalizationSettings.InitializationOperation.IsDone) yield return null;
                var locales = LocalizationSettings.AvailableLocales.Locales;
                var before = LocalizationSettings.SelectedLocale;
                int korean = locales.FindIndex(locale => locale.Identifier.Code == "ko");
                Require(korean >= 0, "Korean locale exists");
                if (before != locales[korean]) dropdown.value = korean;
                var koreanLabel = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None)
                    .First(label => label.name == "Text_OptionName" && label.gameObject.scene.name == scene);
                float localeDeadline = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup < localeDeadline &&
                    !koreanLabel.text.Any(c => c is >= '\uAC00' and <= '\uD7A3'))
                    yield return null;
                Require(koreanLabel.text.Any(c => c is >= '\uAC00' and <= '\uD7A3') && UsesPixelGlyphs(koreanLabel),
                    $"localized settings labels use the same pixel font ({koreanLabel.text} / expected {koreanLabel.GetComponent<LocalizeStringEvent>()?.StringReference.GetLocalizedString()})");
                yield return null;
                Canvas.ForceUpdateCanvases();
                var continueLabel = GameObject.Find("Load Button").GetComponentInChildren<TextMeshProUGUI>();
                continueLabel.ForceMeshUpdate();
                Require(continueLabel.textInfo.lineCount == 1 && !continueLabel.isTextOverflowing,
                    "the Korean Continue label fits its clickable button on a single line");
                Require(continueLabel.font.name == "Pixel Emphasis", "language changes keep the authored menu face");
                if (UiResolutionCheck.Enabled)
                {
                    var sizes = UiResolutionCheck.Verify("Options", dropdown.GetComponent<RectTransform>(), dropdown);
                    while (sizes.MoveNext()) yield return sizes.Current;
                }
                SaveFrame(frame, "OptionSceneKorean");
                LocalizationSettings.SelectedLocale = before;
            }

            var labels = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None)
                .Where(label => label.gameObject.scene.name == scene && label.isActiveAndEnabled &&
                    !string.IsNullOrWhiteSpace(label.text))
                .ToArray();
            Require(labels.Length > 0, scene + " active labels");
            foreach (var label in labels)
                VerifyPixelLabel(label);

            if (scene is "VillageScene" or "BattleScene")
            {
                var movement = VerifyTileAlignment(scene, camera);
                while (movement.MoveNext()) yield return movement.Current;
            }
            yield return null;
            SaveFrame(frame, scene);
            if (scene is "VillageScene" or "BattleScene")
            {
                var depthPair = CaptureDepthPair(frame, scene);
                while (depthPair.MoveNext()) yield return depthPair.Current;
            }
            if (scene == "VillageScene") PixelDensityAudit.Capture(camera, scene);
            if (scene == "OptionScene")
            {
                Object.FindFirstObjectByType<OptionManager>().OnBackButtonClicked();
                while (SceneManager.GetSceneByName("OptionScene").isLoaded) yield return null;
                Canvas.ForceUpdateCanvases();
                VerifyMenuFontRecovery();
            }
            if (scene == "BattleScene")
            {
                GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Battle);
                for (int i = 0; i < 4; i++) yield return null;
                var terrain = Object.FindFirstObjectByType<MiniMapTerrainGraphic>();
                Require(terrain != null && terrain.GetComponentInParent<RectMask2D>() != null &&
                    terrain.canvasRenderer != null && !terrain.raycastTarget,
                    "Combat minimap terrain shares the HUD clipping and leaves input available");
                while (MapGenerator.Instance != null && !MapGenerator.Instance.IsMapGenerationCompleted) yield return null;
                var corners = VerifyCornerHud(camera);
                while (corners.MoveNext()) yield return corners.Current;
                var contacts = Object.FindObjectsByType<TilemapRenderer>(FindObjectsSortMode.None)
                    .Where(r => r.name == "Shadow").ToArray();
                var scan = GroundShadowRenderCheck.InspectScene(camera, scene);
                while (scan.MoveNext()) yield return scan.Current;
                var scroll = TileScrollRenderCheck.Verify(camera, scene);
                while (scroll.MoveNext()) yield return scroll.Current;
                var added = Object.FindFirstObjectByType<WallContactShadows>();
                Require(added != null && added.ReceiverCount > 0, "Dungeon floor meets the exposed walls");
                Debug.Log($"[GroundShadowCheck] {added.ReceiverCount} dungeon terrain shadow receiver cells");
                Require(contacts.Length > 0 && contacts.All(r => r.sharedMaterial != null &&
                    r.sharedMaterial.shader.name == "Necromancer/Terrain Contact Shadow"),
                    "Dungeon wall-contact maps: " + string.Join(", ", Object.FindObjectsByType<TilemapRenderer>(FindObjectsSortMode.None)
                        .Select(r => $"{r.name}/{r.GetComponent<Tilemap>().GetUsedTilesCount()}/{r.sharedMaterial?.shader.name}")));
                if (UiResolutionCheck.Enabled)
                {
                    var sizes = UiResolutionCheck.Verify("Minimap", terrain.GetComponentInParent<RectMask2D>().rectTransform);
                    while (sizes.MoveNext()) yield return sizes.Current;
                }
                SaveFrame(frame, "BattleHud");
                if (UiResolutionCheck.Enabled)
                {
                    var player = GameManager.Instance.PLAYERCONTROLLER;
                    player.ChangeState(PlayerStates.Idle);
                    yield return null;
                    var mapUi = Object.FindFirstObjectByType<MapUIManager>();
                    mapUi.ToggleFullMap(true);
                    try
                    {
                        float opened = Time.unscaledTime + .35f;
                        while (Time.unscaledTime < opened) yield return null;
                        var room = Object.FindObjectsByType<RoundedPlate>(FindObjectsSortMode.None)
                            .Select(p => p.GetComponent<Button>()).FirstOrDefault(b => b != null && b.IsInteractable());
                        Require(mapUi.IsMapOpen && room != null, "Expanded map exposes a visited room");
                        var rect = (RectTransform)room.transform;
                        var bounds = rect.rect;
                        var events = new PointerEventData(EventSystem.current);
                        ExecuteEvents.Execute(room.gameObject, events, ExecuteEvents.pointerEnterHandler);
                        ExecuteEvents.Execute(room.gameObject, events, ExecuteEvents.pointerDownHandler);
                        float pressed = Time.unscaledTime + .16f;
                        while (Time.unscaledTime < pressed) yield return null;
                        var mesh = room.GetComponent<Image>().canvasRenderer.GetMesh();
                        Require(mesh != null && mesh.bounds.size.x < bounds.width && mesh.bounds.size.x > bounds.width * .75f &&
                            rect.rect == bounds, "Pressed map face insets inside its stable hit region");
                        SaveFrame(frame, "TabMapPressed");
                        ExecuteEvents.Execute(room.gameObject, events, ExecuteEvents.pointerUpHandler);
                        ExecuteEvents.Execute(room.gameObject, events, ExecuteEvents.pointerExitHandler);
                    }
                    finally { mapUi.CloseMapUI(); player.ChangeState(PlayerStates.Battle); }
                    yield return null;
                }
                PixelDensityAudit.Capture(camera, scene);
                var tutorial = Object.FindFirstObjectByType<TutorialQuestPanelController>(FindObjectsInactive.Include);
                Require(tutorial != null, "the tutorial quest panel exists in BattleScene");
                tutorial.ShowStage(TutorialQuestPanelController.TutorialStage.Combat);
                for (int i = 0; i < 4; i++) yield return null;
                var visibleKeys = new System.Collections.Generic.List<string>();
                foreach (var label in tutorial.GetComponentsInChildren<TextMeshProUGUI>())
                {
                    label.GetComponent<TMPTextEffectPlayer>()?.FinishReveal();
                    label.ForceMeshUpdate();
                    var info = label.textInfo;
                    for (int i = 0; i < info.characterCount; i++)
                    {
                        var ch = info.characterInfo[i];
                        if (!ch.isVisible || ch.elementType != TMP_TextElementType.Sprite) continue;
                        var glyph = (TMP_SpriteGlyph)ch.textElement.glyph;
                        Require(!VectorUiCatalog.TryGet(glyph.sprite, out _) &&
                            ch.material.mainTexture == glyph.sprite.texture &&
                            info.meshInfo[ch.materialReferenceIndex].colors32[ch.vertexIndex].a > 0,
                            "tutorial draws the visible glyph directly from its authored pixel sheet");
                        visibleKeys.Add(glyph.sprite.name);
                    }
                }
                Require(visibleKeys.OrderBy(key => key).SequenceEqual(new[] { "Keyboard_LShift", "Keyboard_Tab", "Mouse_Left" }),
                    "combat tutorial shows the three authored input icons");
                SaveFrame(frame, "TutorialPixelIcons");
                tutorial.Hide();
                if (SessionState.GetBool("Verification.CombatPolishRoom", false))
                {
                    SessionState.EraseBool("Verification.CombatPolishRoom");
                    var reward = ArtDirectionPlayModeCheck.VerifyClearedRoom(camera);
                    while (reward.MoveNext()) yield return reward.Current;
                }
            }
            if (scene == "VillageScene")
            {
                var scan = GroundShadowRenderCheck.InspectScene(camera, scene);
                while (scan.MoveNext()) yield return scan.Current;
                var scroll = TileScrollRenderCheck.Verify(camera, scene);
                while (scroll.MoveNext()) yield return scroll.Current;
                var depth = VerifyVillageDepth(camera);
                while (depth.MoveNext()) yield return depth.Current;
                var finalGear = ArtDirectionPlayModeCheck.VerifyFinalEquipment(camera, GameManager.Instance.PLAYERCONTROLLER);
                try { while (finalGear.MoveNext()) yield return finalGear.Current; }
                finally { (finalGear as IDisposable)?.Dispose(); }
                if (SessionState.GetBool("Verification.ArtDirection", false))
                {
                    var warning = CaptureEnemyWarning(camera);
                    try { while (warning.MoveNext()) yield return warning.Current; }
                    finally { (warning as IDisposable)?.Dispose(); }
                }
                var explain = SkillExplainUI.Instance;
                Require(explain != null, "Village skill information panel");
                explain.SetOpen(true);
                explain.ToggleDetails();
                Require(explain.IsExpanded, "expanded skill information");
                float reveal = Time.realtimeSinceStartup + .25f;
                while (Time.realtimeSinceStartup < reveal) yield return null;
                Canvas.ForceUpdateCanvases();
                foreach (var label in explain.GetComponentsInChildren<TextMeshProUGUI>()
                    .Where(label => !string.IsNullOrWhiteSpace(label.text)))
                    VerifyPixelLabel(label);
                if (UiResolutionCheck.Enabled)
                {
                    var sizes = UiResolutionCheck.Verify("PlayerInfo", explain.GetComponentsInChildren<RectTransform>().First(r=>r.name=="LoadoutPanel"));
                    while (sizes.MoveNext()) yield return sizes.Current;
                }
                SaveFrame(frame, "VillageSkillInfo");
                var scrollPanel = VerifyPanelScroll(camera, explain);
                while (scrollPanel.MoveNext()) yield return scrollPanel.Current;
                var edgeCheck = VerifyEdgeMist(camera);
                while (edgeCheck.MoveNext()) yield return edgeCheck.Current;
                explain.SetOpen(false);
                var pouch = PouchUI.Instance;
                if (pouch != null)
                {
                    pouch.SetOpen(true);
                    Require(PouchUI.IsOpen, "Village item pouch opens");
                    float pouchReady = Time.realtimeSinceStartup + .25f;
                    while (Time.realtimeSinceStartup < pouchReady) yield return null;
                    Canvas.ForceUpdateCanvases();
                    foreach (var label in pouch.GetComponentsInChildren<TextMeshProUGUI>()
                        .Where(label => !string.IsNullOrWhiteSpace(label.text)))
                        VerifyPixelLabel(label);
                    if (UiResolutionCheck.Enabled)
                    {
                        var sort = (Button)new SerializedObject(pouch).FindProperty("sortButtons").GetArrayElementAtIndex(0).objectReferenceValue;
                        var window = (GameObject)new SerializedObject(pouch).FindProperty("panelRoot").objectReferenceValue;
                        var sizes = UiResolutionCheck.Verify("Pouch", (RectTransform)window.transform, sort);
                        while (sizes.MoveNext()) yield return sizes.Current;
                    }
                    SaveFrame(frame, "VillagePouch");
                    var panel = new SerializedObject(pouch).FindProperty("panelRoot").objectReferenceValue as GameObject;
                    pouch.SetOpen(false);
                    while (panel != null && panel.activeInHierarchy) yield return null;
                }
                var atmosphere = VerifyWorldEffects(camera);
                while (atmosphere.MoveNext()) yield return atmosphere.Current;
                var hit = VerifyDamageText(camera);
                while (hit.MoveNext()) yield return hit.Current;
                var controls = PlayerControlPlayModeCheck.Run(camera);
                while (controls.MoveNext()) yield return controls.Current;
                var dodge = VerifyProjectileDodge(GameManager.Instance.PLAYERCONTROLLER);
                while (dodge.MoveNext()) yield return dodge.Current;
                var choices = VerifyCompanionChoices(camera);
                while (choices.MoveNext()) yield return choices.Current;
            }
        }
    }

    internal static IEnumerator VerifyProjectileDodge(PlayerController player)
    {
        var settings = CameraSettingsSO.Instance;
        var body = player.GetComponent<Collider2D>();
        var shot = new GameObject("Ranged dodge check");
        var shape = shot.AddComponent<CircleCollider2D>();
        shape.isTrigger = true;
        shape.radius = .06f;
        var projectile = shot.AddComponent<Projectile>();
        projectile.enabled = false;
        Vector3 center = body.bounds.center;
        void Sample(Vector3 position)
        {
            shot.transform.position = position;
            Physics2D.SyncTransforms();
            projectile.SendMessage("Update");
        }
        int accents = 0;
        void OnSound(FeedbackCue cue, AudioClip clip) { if (cue == FeedbackCue.ProjectileGraze && clip != null) accents++; }
        PolishFeedback.SoundPlayed += OnSound;
        try
        {
            float ready = Time.unscaledTime + settings.grazeCooldown + .1f;
            while (Time.unscaledTime < ready) yield return null;
            Vector3 beside = center + Vector3.up * (body.bounds.extents.y + shape.radius + settings.grazeMargin * .5f);
            shot.transform.position = beside + Vector3.left * 2;
            Physics2D.SyncTransforms();
            projectile.Init(center, 1, Layers.PlayerMask, null, 0, 15, false);
            Sample(shot.transform.position + Vector3.up * 2);
            Sample(beside + Vector3.right * 2 + Vector3.up * 2);
            Require(Mathf.Approximately(Time.timeScale, 1) && accents == 0, "a distant shot gives no dodge reward");
            Sample(beside + Vector3.right * 2);
            Sample(beside + Vector3.left * 2);
            Require(Mathf.Approximately(Time.timeScale, settings.grazeTimeScale), "fast projectile crossing the graze band triggers slow motion");
            float deadline = Time.unscaledTime + settings.grazeCooldown + .1f;
            while (Time.unscaledTime < deadline) yield return null;
            Require(Mathf.Approximately(Time.timeScale, 1), "graze releases the game clock");

            projectile.Init(center, 1, Layers.PlayerMask, null, 0, 15, false);
            Sample(center);
            Sample(center + Vector3.left * 2);
            Require(Mathf.Approximately(Time.timeScale, 1), "an ordinary overlap does not count as a dodge");
            float hp = player.Stat.Health.CurHP;
            Require(player.GetComponent<MeleeDodgeController>().TryDash(Vector2.right, 1), "player starts an invulnerable dash");
            projectile.Init(center, 1, Layers.PlayerMask, null, 0, 15, false);
            Sample(body.bounds.center);
            Require(Mathf.Approximately(Time.timeScale, settings.grazeTimeScale) && player.Stat.Health.CurHP == hp,
                "dashing through a live projectile rewards the dodge and preserves HP");
            Require(accents == 2, "two dodges play the dedicated cue once each");
            Debug.Log("[TravelFeedback] Fast graze, clock recovery, ordinary contact, invulnerable dash and two audio cues verified.");
        }
        finally { PolishFeedback.SoundPlayed -= OnSound; Object.Destroy(shot); }
    }

    internal static IEnumerator VerifyCornerHud(Camera camera)
    {
        var elements = Object.FindObjectsByType<HudMotionElement>(FindObjectsSortMode.None);
        var map = (RectTransform)elements.Single(e => e.name == "Image_MiniMap").transform;
        var icons = elements.Where(e => e.name is "Icon_Character" or "Icon_Map" or "Icon_Items")
            .Select(e => (RectTransform)e.transform).ToArray();
        Require(icons.Length == 3, "Three lower-right shortcuts are visible");
        Vector2 ScreenPoint(RectTransform rect, Vector2 local) => camera.WorldToScreenPoint(rect.TransformPoint(local));
        var brain = camera.GetComponent<CinemachineBrain>();
        bool following = brain.enabled;
        var start = camera.transform.position;
        float mapError = 0, iconX = 0, iconY = 0;
        try
        {
            brain.enabled = false;
            for (int frame = 0; frame < 180; frame++)
            {
                camera.transform.position = start + new Vector3(Mathf.Sin(frame * .051f) * 6, Mathf.Sin(frame * .069f) * 3);
                yield return null;
                var edge = ScreenPoint(map, map.rect.max);
                mapError = Mathf.Max(mapError, Vector2.Distance(edge, new Vector2(632, 352)));
                foreach (var icon in icons)
                {
                    var slot = (RectTransform)icon.parent;
                    Vector2 offset = ScreenPoint(icon, icon.rect.center) - ScreenPoint(slot, slot.rect.center);
                    iconX = Mathf.Max(iconX, Mathf.Abs(offset.x));
                    iconY = Mathf.Max(iconY, Mathf.Abs(offset.y));
                }
            }
        }
        finally { camera.transform.position = start; brain.enabled = following; }
        string measurement = $"HUD camera pan: minimap 8px inset error {mapError:F3}px, shortcut idle X {iconX:F3}px Y {iconY:F3}px";
        Require(mapError < .1f && iconX < .1f && iconY > .05f && iconY <= .82f, measurement);
        Debug.Log("[PresentationRegressionCheck] " + measurement);
    }

    internal static IEnumerator VerifyPanelScroll(Camera camera, SkillExplainUI panel, bool followPlayer = false)
    {
        var rows = new SerializedObject(panel).FindProperty("statRows");
        var points = new Vector3[4];
        var area = new RectInt(640, 360, 0, 0);
        int right = 0, top = 0;
        for (int n = 0; n < rows.arraySize; n++)
        {
            var label = rows.GetArrayElementAtIndex(n).FindPropertyRelative("value").objectReferenceValue as TMP_Text;
            if (label == null || !label.isActiveAndEnabled) continue;
            ((RectTransform)label.transform.parent).GetWorldCorners(points);
            foreach (var point in points)
            {
                var px = camera.WorldToScreenPoint(point);
                area.x = Mathf.Min(area.x, Mathf.FloorToInt(px.x));
                area.y = Mathf.Min(area.y, Mathf.FloorToInt(px.y));
                right = Mathf.Max(right, Mathf.CeilToInt(px.x));
                top = Mathf.Max(top, Mathf.CeilToInt(px.y));
            }
        }
        area.x = Mathf.Clamp(area.x, 0, 639);
        area.y = Mathf.Clamp(area.y, 0, 359);
        area.size = new Vector2Int(Mathf.Min(right, 640) - area.x, Mathf.Min(top, 360) - area.y);
        var brain = camera.GetComponent<CinemachineBrain>();
        Vector3 origin = camera.transform.position;
        bool following = brain.enabled;
        var texture = new Texture2D(area.width, area.height, TextureFormat.RGB24, false);
        bool[] reference = null;
        try
        {
            if (!followPlayer) brain.enabled = false;
            for (int frame = 0; frame < 10; frame++)
            {
                if (!followPlayer) camera.transform.position = origin + new Vector3(.139f, -.083f) * frame;
                foreach (var label in panel.GetComponentsInChildren<TextMeshProUGUI>()) label.SetVerticesDirty();
                yield return null; yield return null;
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = camera.targetTexture;
                    texture.ReadPixels(new Rect(area.x, area.y, area.width, area.height), 0, 0);
                    texture.Apply();
                }
                finally { RenderTexture.active = previous; }
                var mask = Array.ConvertAll(texture.GetPixels32(), c => c.g > c.r + 24 && c.b > c.r + 24 && c.g > 100);
                if (reference == null)
                {
                    Require(mask.Count(p => p) > 100, "Visible player info text in scroll probe");
                    reference = mask;
                }
                int changed = mask.Where((value, index) => value != reference[index]).Count();
                Debug.Log($"[PanelScroll] follow={followPlayer} frame {frame}, changed {changed}, ink {mask.Count(p => p)}");
                if (frame == 0 || changed > 0) File.WriteAllBytes(Path.Combine(ProjectVerification.Output, $"PanelScroll{frame}.png"), texture.EncodeToPNG());
                Require(changed == 0, $"Player info glyphs moved during camera scroll ({changed} pixels, frame {frame})");
            }
        }
        finally
        {
            Object.DestroyImmediate(texture);
            if (!followPlayer) camera.transform.position = origin;
            brain.enabled = following;
            if (following) UpdateCamera(brain);
        }
    }

    private static IEnumerator VerifyEdgeMist(Camera camera)
    {
        var graphic = Object.FindFirstObjectByType<FrameEdgeMist>();
        var settings = CameraSettingsSO.Instance;
        Require(graphic != null && graphic.canvas.worldCamera == camera &&
            !ShaderUtil.ShaderHasError(graphic.material.shader), "logical vignette is bound to the game camera");
        var backdrop = new GameObject("Edge measurement backdrop",typeof(RectTransform),typeof(Image));
        var rect = (RectTransform)backdrop.transform;
        rect.SetParent(graphic.transform.parent,false); rect.SetAsFirstSibling();
        rect.anchorMin = rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = rect.anchoredPosition = Vector2.zero;
        backdrop.GetComponent<Image>().raycastTarget = false;
        var texture = new Texture2D(640,360,TextureFormat.RGB24,false);
        Color32[] Read()
        {
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = camera.targetTexture;
                texture.ReadPixels(new Rect(0,0,640,360),0,0); texture.Apply();
                return texture.GetPixels32();
            }
            finally { RenderTexture.active = prev; }
        }
        int draws = 0;
        void Drawn(UnityEngine.Rendering.ScriptableRenderContext ctx,Camera rendered) { if (rendered == camera) draws++; }
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += Drawn;
        float opacity = settings.edgeMistOpacity, speed = settings.edgeMistSpeed;
        try
        {
            settings.edgeMistOpacity = 0;
            while (draws < 4) yield return null;
            var plain = Read();
            Require(plain[320+640*180].r > 245, "white vignette measurement backdrop");
            settings.edgeMistOpacity = opacity; settings.edgeMistSpeed = 0;
            draws = 0; while (draws < 4) yield return null;
            var a = Read();
            settings.edgeMistSpeed = 1;
            draws = 0; while (draws < 4) yield return null;
            var b = Read();
            int changed = 0;
            int inward = 0;
            var sides = new int[4];
            for (int y = 0; y < 360; y++)
            for (int x = 0; x < 640; x++)
            {
                int i = y*640+x;
                if (a[i].r != b[i].r) changed++;
                if (a[i].r == plain[i].r) continue;
                int inset = Math.Min(Math.Min(x,639-x), Math.Min(y,359-y));
                if (inset >= settings.edgeMistWidth * .55f) inward++;
                if (x < settings.edgeMistWidth) sides[0]++;
                else if (x >= 640-settings.edgeMistWidth) sides[1]++;
                else if (y < settings.edgeMistWidth) sides[2]++;
                else if (y >= 360-settings.edgeMistWidth) sides[3]++;
                else throw new InvalidOperationException("Edge mist reached the center");
            }
            Debug.Log($"[EdgeMist] edges {string.Join("/",sides)}, animated pixels {changed}, canvas {graphic.canvas.sortingOrder}, rect {graphic.rectTransform.rect}");
            Require(changed > 200 && sides.All(n => n > 200), "animated rectangular vignette on all four edges");
            Require(inward > 640, "soft falloff continues inward beyond the narrow noise border");
        }
        finally
        {
            settings.edgeMistOpacity = opacity; settings.edgeMistSpeed = speed;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= Drawn;
            Object.DestroyImmediate(backdrop); Object.DestroyImmediate(texture);
        }
        yield return null; yield return null;
        SaveFrame(camera.targetTexture,"EdgeMistPanel");
    }

    private static IEnumerator VerifyWorldEffects(Camera camera)
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var ground = player.transform.position;
        Require(camera.GetComponent<WorldAtmosphere>() != null, "Village world atmosphere attached");
        var shadows = GroundShadowRenderCheck.Verify(camera);
        while (shadows.MoveNext()) yield return shadows.Current;
        var swatches = Resources.Load<Texture2D>("WorldAtmosphere/necrohanderPalette");
        Require(swatches != null && swatches.width == 16 && swatches.height == 9 &&
            swatches.filterMode == FilterMode.Point && swatches.mipmapCount == 1 && swatches.isReadable, "artist palette");
        foreach (string path in new[] { "Assets/Resources/WorldAtmosphere/WorldSparks.shader",
            "Assets/Resources/WorldAtmosphere/GroundProjection.shader", "Assets/Resources/WorldAtmosphere/TerrainContactShadow.shader",
            "Assets/Resources/WorldAtmosphere/WorldAtmosphere.shader", "Assets/Shaders/PixelArt/TreeCanopyLit.shader" })
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), path);
        }
        var tree = Object.FindObjectsByType<TreeCanopyWind>(FindObjectsSortMode.None).FirstOrDefault();
        if (tree == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/Objects/Tree/Tree_New_0.prefab");
            tree = prefab.GetComponent<TreeCanopyWind>();
        }
        Require(tree != null && tree.GetComponent<Animator>() == null && tree.GetComponent<SpriteRenderer>().sprite != null &&
            tree.GetComponent<SpriteRenderer>().sharedMaterial.shader.name == "Necromancer/Tree Canopy Lit", "static lit tree pose");

        var hud = Object.FindFirstObjectByType<PlayerStateUI>();
        var fill = (Image)new SerializedObject(hud).FindProperty("hpSprite").objectReferenceValue;
        Require(fill.GetComponent<HealthLiquidSurface>() != null && fill.material.shader.name == "Necromancer/UI/Health Liquid" &&
            !ShaderUtil.ShaderHasError(fill.material.shader), "player health liquid material");
        var corners=new Vector3[4];
        fill.rectTransform.GetWorldCorners(corners);
        Vector2 lower = PixelViewport.CanvasToGamePoint(fill.canvas,corners[0]);
        Vector2 upper = PixelViewport.CanvasToGamePoint(fill.canvas,corners[2]);
        int width=Mathf.FloorToInt(upper.x)-Mathf.CeilToInt(lower.x);
        int height=Mathf.FloorToInt(upper.y)-Mathf.CeilToInt(lower.y);
        Require(width>12 && height>3, "visible liquid area");
        var samples = new Texture2D(width,height,TextureFormat.RGB24,false);
        Color32[] earlier=null;
        int changed=0;
        try
        {
            for(int sample=0;sample<12;sample++)
            {
                float end=Time.time+.112f;
                while(Time.time<end) yield return null;
                var previous=RenderTexture.active;
                try
                {
                    RenderTexture.active=camera.targetTexture;
                    samples.ReadPixels(new Rect(Mathf.CeilToInt(lower.x),Mathf.CeilToInt(lower.y),width,height),0,0);
                    samples.Apply();
                }
                finally { RenderTexture.active=previous; }
                var current=samples.GetPixels32();
                if(earlier!=null) for(int i=0;i<current.Length;i++) if(!current[i].Equals(earlier[i])) changed++;
                earlier=current;
                File.WriteAllBytes(Path.Combine(ProjectVerification.Output,"HealthLiquid"+sample+".png"),samples.EncodeToPNG());
            }
        }
        finally { Object.Destroy(samples); }
        Require(changed > width/6, $"animated health liquid changed {changed} pixels in the fill");
        WorldSparkField.Emit(WorldSparkField.Kind.EnemyHit,ground+Vector3.left,Vector2.left,14);
        WorldSparkField.Emit(WorldSparkField.Kind.Projectile,ground+Vector3.right,Vector2.right,6);
        yield return null;
        var batch = GameObject.Find("GPU combat sparks").GetComponent<MeshRenderer>();
        Require(batch.enabled && batch.sharedMaterial.shader.isSupported, "GPU spark batch is drawing");
        SaveFrame(camera.targetTexture,"VillageAtmosphereAndSparks");

        Require(Resources.Load<HandPoseCatalog>("PlayerHandPoses") != null, "split hand dependencies imported");
        var travel = VerifyFloorDeparture(camera, player);
        try { while (travel.MoveNext()) yield return travel.Current; }
        finally { (travel as IDisposable)?.Dispose(); }
    }

    internal static IEnumerator VerifyFloorDeparture(Camera camera, PlayerController player)
    {
        var pouch = ItemPouch.Instance;
        var borrowed = new System.Collections.Generic.List<int>();
        var known = new System.Collections.Generic.HashSet<Sprite>();
        for (int i = 0; i < pouch.SlotCount; i++)
            if (GroundItem.ItemIcon(pouch.Get(i)) is Sprite icon) known.Add(icon);
        foreach (var guid in AssetDatabase.FindAssets("t:ItemSO", new[] { "Assets/SOData" }))
        {
            if (known.Count >= 3) break;
            var item = AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (item == null || item.icon == null || known.Contains(item.icon)) continue;
            int slot = pouch.FirstEmptySlot();
            if (slot < 0) slot = pouch.SlotCount;
            if (pouch.TryAdd(item)) { borrowed.Add(slot); known.Add(item.icon); }
        }
        Require(known.Count >= 3, "three distinct carried sprites for the portal check");
        var carried = Enumerable.Range(0, pouch.SlotCount).Select(pouch.Get).ToArray();
        var inventory = InventoryManager.Instance;
        var skills = player.GetComponentInChildren<PlayerSkillController>();
        int activeSlot = skills.ActiveSlot;
        bool loanedSummon = inventory.GetSummonAt(activeSlot) == null;
        if (loanedSummon)
        {
            var ally = GameManager.Instance.dataManager.GET_GROWTH_REGISTRY().minionDatas.OfType<MainMinionDataSO>()
                .First(data => data.basicAnim != null && data.basicAnim.visual != null);
            Require(inventory.EquipMinion(activeSlot, ally), "equip a field companion for departure");
            yield return null;
        }
        var bodySprite = player.GetComponentInChildren<YSortableObject>().Renderer;
        var hudRects = Object.FindObjectsByType<HudMotionElement>(FindObjectsSortMode.None)
            .Where(group => group.transform.parent.GetComponentInParent<HudMotionElement>() == null)
            .Select(group => (RectTransform)group.transform).ToArray();
        Require(hudRects.Length > 0, "HUD elements participate in floor travel");
        var homes = hudRects.ToDictionary(rect => rect, rect => rect.anchoredPosition);
        FloorPortalGraphic portal = null;
        try
        {
            portal = FloorPortalGraphic.Capture(player);
            Require(portal != null && !bodySprite.forceRenderingOff, "departure begins on the existing world pose");
            Require(portal.material.GetFloat("_Coverage") == 0 && portal.transform.childCount == 0, "opening preserves the original scene");
            portal.SetProgress(.065f);
            float coverage = portal.material.GetFloat("_Coverage");
            Require(coverage > .04f && coverage < .14f && portal.transform.childCount == 0 &&
                !bodySprite.forceRenderingOff, "the soft background tint precedes the HUD and party handoff");
            float tintUntil = Time.unscaledTime + .24f;
            while (Time.unscaledTime < tintUntil) yield return null;
            portal.SetProgress(.08f);
            var hero = portal.transform.Find("Portal hero").GetComponent<RawImage>();
            var ally = portal.transform.Find("Portal summon")?.GetComponent<RawImage>();
            Require(ally != null, "the active field summon participates");
            foreach (var picture in new[] { hero, ally })
            {
                var pose = (RenderTexture)picture.texture;
                var prev = RenderTexture.active;
                var sample = new Texture2D(pose.width, pose.height, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = pose;
                    sample.ReadPixels(new Rect(0, 0, pose.width, pose.height), 0, 0);
                    int opaque = sample.GetPixels32().Count(c => c.a > 64);
                    File.WriteAllBytes(Path.Combine(ProjectVerification.Output, picture.name + ".png"), sample.EncodeToPNG());
                    Require(pose.filterMode == FilterMode.Point && opaque > 20,
                        $"{picture.name} pose {pose.width}x{pose.height} has {opaque} opaque pixels");
                }
                finally { RenderTexture.active = prev; Object.Destroy(sample); }
            }
            Vector2 expected = camera.WorldToScreenPoint(bodySprite.bounds.center);
            Require(Vector2.Distance(hero.rectTransform.anchoredPosition, expected) < 23, "departure starts at the hero");
            var images = portal.GetComponentsInChildren<Image>();
            Require(images.All(image => !image.enabled), "inventory starts inside the hero");
            portal.SetProgress(.11f);
            Require(!bodySprite.forceRenderingOff && hero.material.GetFloat("_Opacity") > .1f &&
                hero.material.GetFloat("_Opacity") < .9f, "world and captured poses crossfade");
            portal.SetProgress(.2135f);
            Require(images[0].color.a > .1f && images[0].color.a < .9f && images[images.Length-1].color.a == 0,
                "the leading item fades in before the tail");
            portal.SetProgress(.35f);
            Require(bodySprite.forceRenderingOff && !ShaderUtil.ShaderHasError(portal.material.shader), "floor shader and party handoff");
            Canvas.ForceUpdateCanvases();
            Require(hero.rectTransform.anchoredPosition.x < 320 && ally.rectTransform.anchoredPosition.x > 320,
                $"party flanks the center (hero {hero.rectTransform.anchoredPosition}, ally {ally.rectTransform.anchoredPosition})");
            foreach (var rect in hudRects)
                Require(Vector2.Distance(homes[rect], rect.anchoredPosition) > 10,
                    $"{rect.name} retreats from {homes[rect]} to {rect.anchoredPosition}");
            Require(images.Count(image => known.Contains(image.sprite) && image.enabled) >= 3, "carried items emerge during assembly");
            yield return null;
            Require(hudRects.All(rect => rect.GetComponent<CanvasGroup>().alpha == 0), "HUD remains hidden after its outward travel");
            SaveFrame(camera.targetTexture, "FloorPortalAssemble");
            portal.SetProgress(.456f);
            yield return null;
            SaveFrame(camera.targetTexture, "FloorPortal");
            foreach (var beat in new[] { .57f, .695f })
            {
                portal.SetProgress(beat);
                yield return null;
                var actor = beat < .6f ? hero : ally;
                VerifyGrains(camera, portal, actor, beat < .6f ? "Hero" : "Ally");
            }
            portal.SetProgress(.65f);
            Require(!hero.enabled && ally.enabled, "hero enters before the minion");
            yield return null;
            SaveFrame(camera.targetTexture, "FloorPortalSummon");
            portal.SetProgress(.81f);
            Require(!hero.enabled && !ally.enabled && images.All(image => image.enabled), "entire item train follows the party");
            Require(images.All(image => image.rectTransform.anchoredPosition ==
                (Vector2)Vector2Int.RoundToInt(image.rectTransform.anchoredPosition)), "train stays on the logical grid");
            yield return null;
            SaveFrame(camera.targetTexture, "FloorPortalItems");
            portal.SetProgress(.819f);
            Require(images[0].color.a > .1f && images[0].color.a < .9f, "lead item fades as it falls through the horizon");
            portal.SetProgress(.857f);
            Require(!images[0].enabled && images[images.Length-1].enabled, "train enters in order");
            portal.SetProgress(1);
            Require(images.All(image => !image.enabled), "all icons disappear");
            portal.SetProgress(0);
            Canvas.ForceUpdateCanvases();
            Require(hudRects.All(rect => Vector2.Distance(homes[rect], rect.anchoredPosition) < 4), "HUD returns to its original anchor");
            Object.Destroy(portal.gameObject);
            yield return null;
            Require(!bodySprite.forceRenderingOff, "world pose restored after travel");

            var door = new GameObject("Door departure check");
            var exit = new GameObject("Door destination check");
            try
            {
                door.transform.position = player.transform.position + Vector3.right;
                exit.transform.position = player.transform.position - Vector3.right;
                Vector2 destination = exit.transform.position + Vector3.right * 2.5f;
                var controller = door.AddComponent<DoorController>();
                controller.SetupTeleport(exit.AddComponent<DoorController>(), Vector2Int.right);
                controller.SetTriggerEnabled(true);
                controller.SendMessage("OnTriggerEnter2D", player.GetComponent<Collider2D>());
                controller.SetTriggerEnabled(false);
                Require(player.IsInputBlocked, "ordinary doorway starts its fade and blocks input");
                float until = Time.unscaledTime + Fader.FullScreenFader.FadeDuration(FadeSignal.방이동) * 2 + 3;
                do
                {
                    Require(Object.FindFirstObjectByType<PortalItemStream>() == null &&
                        Object.FindFirstObjectByType<FloorPortalGraphic>() == null, "ordinary room fade never creates floor effects");
                    yield return null;
                } while (player.IsInputBlocked && Time.unscaledTime < until);
                Require(!player.IsInputBlocked && Vector2.Distance(player.transform.position, destination) < .2f,
                    "ordinary doorway arrives and restores input");
                Require(carried.SequenceEqual(Enumerable.Range(0,pouch.SlotCount).Select(pouch.Get)), "travel preserves every inventory slot");
            }
            finally { Object.Destroy(door); Object.Destroy(exit); }
            Debug.Log("[TravelFeedback] Floor handoff, staggered item fades, ordinary door without items, input and inventory verified.");
        }
        finally
        {
            if (portal != null) Object.Destroy(portal.gameObject);
            if (loanedSummon)
            {
                inventory.EquipMinion(activeSlot, null, 0);
            }
            foreach (int slot in borrowed) pouch.RemoveAt(slot);
        }
        yield return null;
    }

    private static IEnumerator VerifyVillageDepth(Camera camera)
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var body = player.GetComponent<Rigidbody2D>();
        var sort = player.GetComponentInChildren<YSortableObject>();
        var barrier = Object.FindObjectsByType<YSortableObject>(FindObjectsSortMode.None)
            .Where(candidate => candidate.name.StartsWith("Barricade_Normal", StringComparison.Ordinal))
            .OrderBy(candidate => (candidate.GroundPosition - player.transform.position).sqrMagnitude)
            .FirstOrDefault();
        Require(sort != null && sort.Group != null && barrier != null && barrier.Renderer != null,
            "Village player and authored barricade are depth participants");

        var origin = player.transform.position;
        bool simulated = body.simulated;
        var contact = new GameObject("Test ground contact");
        var bounds = barrier.Renderer.bounds;
        var foot = barrier.GroundPosition;
        var brain = camera.GetComponent<CinemachineBrain>();
        bool brainEnabled = brain.enabled;
        try
        {
            body.simulated = false;
            // A controlled overlap shows the real frames without a collider moving the player out of view.
            player.transform.position = new Vector3(bounds.center.x, bounds.max.y - .23f, origin.z);
            contact.transform.position = foot + Vector3.up * .25f;
            sort.SetPivot(contact.transform);
            UpdateCamera(brain);
            for (int i = 0; i < 4; i++) yield return null;
            brain.enabled = false;
            yield return null;
            Require(sort.Group.sortingOrder < barrier.Renderer.sortingOrder, "Village barricade occludes the player from behind");
            SaveFrame(camera.targetTexture, "VillageDepthBehind");

            contact.transform.position = foot + Vector3.down * .25f;
            for (int i = 0; i < 4; i++) yield return null;
            Require(sort.Group.sortingOrder > barrier.Renderer.sortingOrder, "Village player and its hand draw in front as one unit");
            SaveFrame(camera.targetTexture, "VillageDepthInFront");
        }
        finally
        {
            sort.SetPivot(player.transform);
            player.transform.position = origin;
            body.simulated = simulated;
            Object.DestroyImmediate(contact);
            brain.enabled = brainEnabled;
            UpdateCamera(brain);
        }

        for (int i = 0; i < 3; i++) yield return null;
    }

    private static void VerifyVectorImageLayout()
    {
        var frame = AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/Sprites/UI/UI_Panel.png")
            .OfType<Sprite>().Single(sprite => sprite.name == "UI_Panel_Red_Big");
        var canvas = Object.FindObjectsByType<VectorUiImage>(FindObjectsSortMode.None)
            .First(image => image.gameObject.scene.name == "OptionScene" && image.sprite == frame).canvas;
        var fixture = new GameObject("Vector image layout fixture", typeof(RectTransform), typeof(VectorUiImage));
        fixture.transform.SetParent(canvas.transform, false);
        var image = fixture.GetComponent<VectorUiImage>();
        image.rectTransform.sizeDelta = new Vector2(120f, 64f);
        image.raycastTarget = false;
        image.sprite = frame;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = canvas.referencePixelsPerUnit / frame.pixelsPerUnit;
        Canvas.ForceUpdateCanvases();
        Require(image.UsesVector && image.mainTexture == Texture2D.whiteTexture &&
            image.canvasRenderer.GetMesh().bounds.size.x > 118f, "nine-sliced vector frame spans the authored width");
        float fullArea = NetMeshArea(image.canvasRenderer.GetMesh());
        Require(fullArea > 7400f, "vector panel includes the opaque center behind its labels");
        image.fillCenter = false;
        Canvas.ForceUpdateCanvases();
        Require(NetMeshArea(image.canvasRenderer.GetMesh()) < fullArea * .85f, "nine-slice center can remain transparent");

        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = 0;
        image.fillAmount = .25f;
        Canvas.ForceUpdateCanvases();
        Require(Mathf.Abs(image.canvasRenderer.GetMesh().bounds.size.x - 30f) < 1f,
            "vector horizontal gauge stays clipped to a quarter of its range");
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = 2;
        image.fillAmount = .125f;
        Canvas.ForceUpdateCanvases();
        float radialArea = NetMeshArea(image.canvasRenderer.GetMesh());
        Require(radialArea > 820f && radialArea < 1150f, "vector radial cooldown preserves its partial sector");

        var texture = new Texture2D(8, 8);
        var spriteAsset = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f));
        image.overrideSprite = spriteAsset;
        Canvas.ForceUpdateCanvases();
        Require(!image.UsesVector && image.mainTexture == texture, "sprite swaps can show authored illustrations");
        VerifyWorldGaugeReuse(frame);
        Object.DestroyImmediate(fixture);
        Object.DestroyImmediate(spriteAsset);
        Object.DestroyImmediate(texture);
    }

    private static void VerifyWorldGaugeReuse(Sprite frame)
    {
        var root = new GameObject("Shared world gauge check", typeof(RectTransform), typeof(Canvas));
        try
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var bars = new VectorUiImage[2];
            for (int i = 0; i < bars.Length; i++)
            {
                var bar = new GameObject("Gauge", typeof(RectTransform), typeof(VectorUiImage));
                bar.transform.SetParent(root.transform, false);
                bars[i] = bar.GetComponent<VectorUiImage>();
                bars[i].rectTransform.sizeDelta = new Vector2(100, 10);
                bars[i].sprite = frame;
                bars[i].type = Image.Type.Filled;
                bars[i].fillMethod = Image.FillMethod.Horizontal;
                bars[i].fillAmount = .5f;
            }
            Canvas.ForceUpdateCanvases();
            float original = NetMeshArea(bars[0].canvasRenderer.GetMesh());
            Require(original > 0 && Mathf.Approximately(original, NetMeshArea(bars[1].canvasRenderer.GetMesh())),
                "equal world health fills show the same silhouette");
            for (int i = 1; i <= 20; i++)
            {
                bars[1].fillAmount = i / 25f;
                Canvas.ForceUpdateCanvases();
            }
            Require(Mathf.Approximately(original, NetMeshArea(bars[0].canvasRenderer.GetMesh())) &&
                NetMeshArea(bars[1].canvasRenderer.GetMesh()) > original,
                "changing one world health fill preserves the other after mesh reuse");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static float NetMeshArea(Mesh mesh)
    {
        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        float area = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            area += Mathf.Abs(Vector3.Cross(b - a, c - a).z) * .5f;
        }
        return area;
    }

    private static void VerifyMenuFontRecovery()
    {
        var button = GameObject.Find("Start Button").GetComponent<Button>();
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        var face = label.font;
        Require(face != null && face.name == "Pixel Emphasis", "start menu has its baked font");

        var stale = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        Require(stale != null, "legacy font fixture");
        label.font = stale;
        label.fontSharedMaterial = stale.material;
        label.fontSize = 36f;
        label.enableAutoSizing = true;
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        Canvas.ForceUpdateCanvases();
        Require(label.font == face && label.fontSharedMaterial == face.material && Mathf.Abs(label.fontSize - 12f) < .01f,
            "menu restores its authored bitmap face, material and size after a stale override");
        VerifyPixelLabel(label);
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
    }

    private static bool Overlaps(RectTransform first, RectTransform second, Camera camera)
    {
        Rect ScreenBounds(RectTransform rect)
        {
            var min = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.min));
            var max = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.max));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        return ScreenBounds(first).Overlaps(ScreenBounds(second));
    }

    internal static void VerifyPixelLabel(TextMeshProUGUI label)
    {
        Require(UsesPixelGlyphs(label), $"{label.gameObject.scene.name} '{label.name}' uses pixel glyphs");
        float units = PixelViewport.CanvasPixelsPerUnit(label.canvas, label.rectTransform);
        float multiple = label.fontSize * label.font.faceInfo.scale * units / label.font.faceInfo.pointSize;
        Require(multiple >= .99f && IsWhole(multiple), $"{label.name} fits a native bitmap size or integer multiple ({multiple:F3}×)");
    }

    private static IEnumerator CaptureEnemyWarning(Camera camera)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<BaseHitBox>("Assets/Prefabs/Effect/Combat/Skills/TelegraphHitbox Prefab.prefab");
        Vector3 center = camera.ViewportToWorldPoint(new Vector3(.55f,.54f,-camera.transform.position.z));
        var warning = Object.Instantiate(prefab, center, Quaternion.Euler(0,0,-32));
        warning.transform.localScale = new Vector3(4.2f,1.6f,1);
        var edge = warning.GetComponentsInChildren<SpriteRenderer>().First(r=>!r.transform.IsChildOf(warning.fillingTransform));
        var props = new MaterialPropertyBlock();
        float originalStep = Time.captureDeltaTime;
        try
        {
            Time.captureDeltaTime = 1f/60;
            warning.Init(default,0,.5f,1f);
            for (int phase = 0; phase < 3; phase++)
            {
                float deadline = Time.realtimeSinceStartup + 5;
                do
                {
                    yield return null;
                    edge.GetPropertyBlock(props);
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Enemy preview countdown");
                } while (props.GetVector("_ThreatCue").y < (phase == 0 ? .1f : phase == 1 ? .55f : .93f));
                if (phase == 2) Require(props.GetVector("_ThreatCue").z > .5f, "Enemy outline pulses before contact");
                SaveFrame(camera.targetTexture, "EnemyWarning-" + phase);
            }
        }
        finally { Time.captureDeltaTime = originalStep; Object.Destroy(warning.gameObject); }
    }

    private static void SetBattleSeed(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "BattleScene") return;
        SceneManager.sceneLoaded -= SetBattleSeed;
        Object.FindFirstObjectByType<MapGenerator>().SetNextLayoutSeed(907);
    }

    private static void VerifyButton(string scene, Camera camera)
    {
        var button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .First(button => button.gameObject.scene.name == scene && button.IsInteractable());
        var rect = (RectTransform)button.transform;
        var point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        Require(hits.Any(hit => hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform)),
            scene + " button raycast at its visual position");
    }

    private static IEnumerator VerifyTileAlignment(string scene, Camera camera)
    {
        var ground = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None)
            .First(map => map.gameObject.scene.name == scene && map.name == "Ground");
        var player = GameManager.Instance.PLAYERCONTROLLER.transform;
        var origin = player.position;
        var initialCamera = camera.transform.position;
        var brain = camera.GetComponent<CinemachineBrain>();
        var settings = CameraSettingsSO.Instance;
        var authoredStep = settings.cameraMotion;
        bool sawFractional = false;
        try {
        for (int step = 0; step < 15; step++)
        {
            settings.cameraMotion = step < 8 ? PixelMotionStep.Continuous : PixelMotionStep.Whole;
            player.position = origin + new Vector3(.0913f * step, .0237f * step, 0);
            yield return null;
            UpdateCamera(brain);
            Vector2 cell = camera.WorldToScreenPoint(ground.CellToWorld(Vector3Int.zero));
            Vector2 adjacent = camera.WorldToScreenPoint(ground.CellToWorld(Vector3Int.right));
            if (step < 8) sawFractional |= !IsWhole(cell.x) || !IsWhole(cell.y);
            Require((step < 8 || IsWhole(cell.x) && IsWhole(cell.y)) && Mathf.Abs(adjacent.x - cell.x - 16f) < .01f,
                scene + " tile alignment during camera movement");
        }
        } finally { settings.cameraMotion = authoredStep; }
        Require(sawFractional, scene + " continuous camera preserves fractional positions");
        Require((initialCamera - camera.transform.position).sqrMagnitude > .002f, scene + " camera followed the player");
    }

    private static IEnumerator VerifyDamageText(Camera camera)
    {
        var target = GameObject.Find("Enemy Scarecrow");
        Require(target != null, "Village scarecrow");
        var anchor = target.GetComponentInChildren<FloatingTextSpawner>().FloatPoint;
        var player = GameManager.Instance.PLAYERCONTROLLER;
        player.transform.position = target.transform.position + Vector3.left * 2f;
        for (int frame = 0; frame < 12; frame++) yield return null;
        UpdateCamera(camera.GetComponent<CinemachineBrain>());

        var playerSort = player.GetComponentInChildren<YSortableObject>();
        var scarecrowSort = target.GetComponentInChildren<YSortableObject>();
        var stake = scarecrowSort.Renderer.bounds;
        Require(Mathf.Abs(scarecrowSort.GroundPosition.y - stake.min.y) < .05f,
            "scarecrow sorts at the bottom of its stake");
        var saved = player.transform.position;
        var rigid = player.GetComponent<Rigidbody2D>();
        bool simulated = rigid.simulated;
        try
        {
            rigid.simulated = false;
            foreach (float side in new[] { .08f, -.08f })
            {
                player.transform.position += new Vector3(stake.center.x - playerSort.GroundPosition.x,
                    scarecrowSort.GroundPosition.y + side - playerSort.GroundPosition.y, 0);
                yield return null;
                yield return null;
                Require((playerSort.Group.sortingOrder > scarecrowSort.Group.sortingOrder) == (side < 0),
                    "whole player crosses the scarecrow at its stake");
                SaveFrame(camera.targetTexture, side < 0 ? "ScarecrowFront" : "ScarecrowBehind");
            }
        }
        finally { player.transform.position = saved; rigid.simulated = simulated; }
        for (int i = 0; i < 4; i++) yield return null;
        var previous = Object.FindObjectsByType<TextFloating>(FindObjectsSortMode.None).ToHashSet();
        target.GetComponentInChildren<CharacterHealth>().TakeDamage(new DamageInfo(15, DamageType.Physical, player.gameObject));
        var actual = Object.FindObjectsByType<TextFloating>(FindObjectsSortMode.None).First(text => !previous.Contains(text));
        var normal = actual.GetComponent<TextMeshProUGUI>();
        var expected = camera.WorldToScreenPoint(anchor.position);
        var displayed = PixelViewport.CanvasToGamePoint(normal.canvas, actual.transform.position);
        Require(normal.enabled && !normal.raycastTarget && Mathf.Abs(displayed.x - expected.x) < 17f &&
            Mathf.Abs(displayed.y - expected.y) < 1.1f, "Damage number starts at the damage anchor");

        previous.Add(actual);
        object criticalBonus = new object();
        bool resolvedCritical = false;
        var health = target.GetComponentInChildren<CharacterHealth>();
        void OnDamage(int amount, DamageType type, string label, bool crit) => resolvedCritical |= crit;
        health.TakeDamageEvent += OnDamage;
        player.Stat.Mods.AddFlat(criticalBonus, StatType.CritChance, 101);
        try
        {
            health.TakeDamage(new DamageInfo(27, DamageType.Physical, player.gameObject));
        }
        finally
        {
            player.Stat.Mods.RemoveSource(criticalBonus);
            health.TakeDamageEvent -= OnDamage;
        }
        Require(resolvedCritical, "scarecrow resolved a critical strike");
        var critical = Object.FindObjectsByType<TextFloating>(FindObjectsSortMode.None)
            .Single(text => !previous.Contains(text)).GetComponent<TextMeshProUGUI>();
        var criticalPoint = PixelViewport.CanvasToGamePoint(critical.canvas, critical.transform.position);
        Require(Mathf.Abs(criticalPoint.x - expected.x) < 17f && Mathf.Abs(criticalPoint.y - expected.y) < 1.1f,
            "critical damage uses the struck target's anchor");
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var label in new[] { normal, critical })
            {
                var rect = label.rectTransform;
                var pixel = PixelViewport.CanvasToGamePoint(label.canvas, rect.position);
                var pixels = label.fontSize * PixelViewport.CanvasPixelsPerUnit(label.canvas, rect);
                Require(float.IsFinite(pixel.x) && float.IsFinite(pixel.y) && IsWhole(pixels) &&
                    rect.localRotation == Quaternion.identity && rect.localScale == Vector3.one && UsesPixelGlyphs(label),
                    $"Damage text {label.name} pixel={pixel:F3} size={pixels:F3} scale={rect.localScale} face={label.font.name}");
            }
            if (frame == 4) SaveFrame(camera.targetTexture, "Scarecrow");
        }
        var burstCheck = VerifyCombatTextBurst(camera, anchor);
        while (burstCheck.MoveNext()) yield return burstCheck.Current;
    }

    private static IEnumerator VerifyCombatTextBurst(Camera camera, Transform anchor)
    {
        var manager = FloatingTextManager.Instance;
        var field = manager.GetComponentInChildren<CombatTextBurstField>();
        var book = Resources.Load<CombatTextLibrary>("UI/CombatText");
        string[] styles = { "Hit", "Shield", "Miss", "Combo", "Critical", "Finisher" };
        foreach (var id in styles)
            Require(book != null && book.Find(id) is { } style && TextMotionLibrary.Instance.Find(style.glyphMotion) != null,
                id + " has a combat style and glyph motion");
        Require(field != null && !field.raycastTarget, "combat ornaments share a non-interactive mesh");
        manager.SetShowDamageNumbers(false);
        manager.SetShowDamageNumbers(true);
        manager.ShowDamage("128", Color.white, anchor.position, true, "Finisher");
        float strike = Time.time + .14f;
        while (Time.time < strike) yield return null;
        Canvas.ForceUpdateCanvases();
        var finisher = manager.GetComponentsInChildren<TextMeshProUGUI>().Single(label => label.text == "128");
        Require(finisher.materialForRendering.shader.name == "Necromancer/Combat Text Ink" &&
            !ShaderUtil.ShaderHasError(finisher.materialForRendering.shader) && finisher.materialForRendering.mainTexture == finisher.font.atlasTexture,
            "damage ink uses the native bitmap atlas");
        SaveFrame(camera.targetTexture, "CombatTextFinisher");
        manager.SetShowDamageNumbers(false);
        manager.SetShowDamageNumbers(true);
        for (int i = 0; i < 64; i++)
        {
            var offset = new Vector3((i % 8 - 3.5f) * .94f, (i / 8 - 3.5f) * .47f);
            manager.ShowDamage((5100 + i).ToString(), Color.white, anchor.position + offset, i % 6 == 4, styles[i % 6]);
        }
        int start = Time.frameCount;
        while (Time.frameCount < start + 2) yield return null;
        yield return null;
        Canvas.ForceUpdateCanvases();
        var labels = manager.GetComponentsInChildren<TextMeshProUGUI>().Where(label => int.TryParse(label.text, out var n) &&
            n >= 5100 && n < 5164).ToArray();
        Require(labels.Select(label => label.text).Distinct().Count() == 64 && labels.All(UsesPixelGlyphs),
            "all 64 distinct damage values use native bitmap glyphs");
        var mesh = field.canvasRenderer.GetMesh();
        Require(field.ActiveCount > 0 && field.ActiveCount <= CombatTextBurstField.Capacity &&
            mesh != null && mesh.vertexCount > 100, "bounded shared burst mesh is populated");
        var pixels = mesh.vertices.Select(v => PixelViewport.CanvasToGamePoint(field.canvas, field.rectTransform.TransformPoint(v))).ToArray();
        Require(pixels.All(p => float.IsFinite(p.x) && float.IsFinite(p.y)) && pixels.Min(p => p.x) < 20 &&
            pixels.Max(p => p.x) > 620 && pixels.Min(p => p.y) < 15 && pixels.Max(p => p.y) > 345, "pixel rays reach all four edges");
        SaveFrame(camera.targetTexture, "CombatTextBurst");
        Debug.Log($"[CombatText] PASS 64 distinct labels, {field.ActiveCount} active bursts, {mesh.vertexCount} vertices.");
        manager.SetShowDamageNumbers(false);
        Require(field.ActiveCount == 0 && labels.All(label => !label.gameObject.activeSelf), "damage toggle clears numbers and particles");
        manager.SetShowDamageNumbers(true);
        var skin = UISkinSettings.Active;
        bool motion = skin.motionEnabled;
        try
        {
            skin.motionEnabled = false;
            manager.ShowDamage("13", Color.white, anchor.position, false, "Finisher");
            Require(field.ActiveCount == 0 && manager.GetComponentsInChildren<TextMeshProUGUI>().Any(v => v.text == "13"),
                "reduced motion preserves the value without bursts");
        }
        finally { skin.motionEnabled = motion; }
        float until = Time.time + 1.6f;
        while (Time.time < until) yield return null;
        Require(field.ActiveCount == 0 && labels.All(label => !label.gameObject.activeSelf), "reused combat effects expire");
    }

    private static void VerifyGrains(Camera camera, FloorPortalGraphic portal, RawImage actor, string name)
    {
        var grains = portal.GetComponentsInChildren<RawImage>().Where(g => g.name == "Pose grains" && g.texture == actor.texture).ToArray();
        Require(grains.Length > 0, name + " uses its captured pose for grains");
        var frame = camera.targetTexture;
        var sample = new Texture2D(frame.width, frame.height, TextureFormat.RGB24, false);
        var before = RenderTexture.active;
        Color32[] Read()
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = frame;
            sample.ReadPixels(new Rect(0,0,frame.width,frame.height),0,0);
            return sample.GetPixels32();
        }
        try
        {
            var visible = Read();
            SaveFrame(frame, "FloorGrains" + name);
            foreach (var g in grains) g.enabled = false;
            var hidden = Read();
            var center = actor.rectTransform.anchoredPosition;
            var half = actor.rectTransform.sizeDelta * .5f;
            int outside = 0;
            for (int i = 0; i < visible.Length; i++)
            {
                var p = new Vector2(i % frame.width, i / frame.width);
                if (Mathf.Abs(p.x-center.x) <= half.x && Mathf.Abs(p.y-center.y) <= half.y) continue;
                if (Vector2.Distance(p, new Vector2(320,180)) > 90) continue;
                if (Mathf.Abs(visible[i].r-hidden[i].r)+Mathf.Abs(visible[i].g-hidden[i].g)+Mathf.Abs(visible[i].b-hidden[i].b)>30) outside++;
            }
            Require(outside >= 4, $"{name} detached grains outside silhouette near intake: {outside}");
            Debug.Log($"[FloorGrains] {name}: {outside} detached pixels outside actor bounds.");
        }
        finally
        {
            foreach (var g in grains) g.enabled = true;
            RenderTexture.active = before;
            Object.Destroy(sample);
        }
    }

    private static IEnumerator VerifyCompanionChoices(Camera camera)
    {
        var npc = Object.FindFirstObjectByType<MinionSelectNPC>();
        Require(npc != null && npc.Interact(GameManager.Instance.PLAYERCONTROLLER.gameObject), "village companion selector opens");
        var picker = Object.FindFirstObjectByType<RewardSelectionUI>();
        var before = LocalizationSettings.SelectedLocale;
        try
        {
            foreach (var language in new[] { "ko", "en" })
            {
                LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales.First(l => l.Identifier.Code == language);
                float until = Time.realtimeSinceStartup + .45f;
                while (Time.realtimeSinceStartup < until) yield return null;
                Canvas.ForceUpdateCanvases();
                var labels = picker.GetComponentsInChildren<TextMeshProUGUI>();
                var cards = picker.GetComponentsInChildren<RewardCard>();
                Require(cards.Length == 3 && labels.Any(l => l.text == (language == "ko" ? "소환수 선택" : "Choose a companion")),
                    language + " heading and three village choices");
                Require(labels.Any(l => l.text.Contains(language == "ko" ? "돌진 인형" : "Dash Doll")) &&
                    labels.Any(l => l.text.Contains(language == "ko" ? "회전 난타" : "Spinning Flurry")), "localized names and skill text");
                foreach (var label in labels) VerifyPixelLabel(label);
                foreach (var card in cards)
                {
                    var scroll = card.GetComponentInChildren<ScrollRect>();
                    var body = scroll.content.GetComponent<TextMeshProUGUI>();
                    body.ForceMeshUpdate();
                    Require(body.preferredHeight <= scroll.viewport.rect.height + 1,
                        $"{language} {card.name} description needs {body.preferredHeight} of {scroll.viewport.rect.height} pixels");
                    Require(!card.GetComponentsInChildren<TextMeshProUGUI>().First(v => v.name == "Name").isTextOverflowing,
                        "companion name fits card");
                }
                var skipCaption = picker.GetComponentInChildren<RewardSkipButton>().GetComponentInChildren<TextMeshProUGUI>();
                skipCaption.ForceMeshUpdate();
                Require(skipCaption.text == (language == "ko" ? "건너뛰기" : "Skip") && !skipCaption.isTextOverflowing &&
                    skipCaption.preferredWidth <= skipCaption.rectTransform.rect.width, language + " Skip caption fits before its icon");
                SaveFrame(camera.targetTexture, "CompanionChoice-" + language);
            }
            var skip = picker.GetComponentsInChildren<Button>().First(b => b.name == "Skip Button");
            if (UiResolutionCheck.Enabled)
            {
                var sizes = UiResolutionCheck.Verify("Companions", picker.GetComponent<RectTransform>(), skip);
                while (sizes.MoveNext()) yield return sizes.Current;
            }
            var focus = EventSystem.current.currentSelectedGameObject.GetComponent<Button>();
            Require(focus.FindSelectableOnRight() != null && focus.FindSelectableOnDown() == skip && skip.FindSelectableOnUp() != null,
                "card navigation stays inside the choice panel");
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(camera, skip.transform.position) };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            var plate = skip.GetComponent<RewardSkipSurface>();
            var caption = skip.GetComponentInChildren<TextMeshProUGUI>().rectTransform;
            var hitRect = plate.rectTransform.rect;
            Color32[] PlateColors()
            {
                Canvas.ForceUpdateCanvases();
                return plate.canvasRenderer.GetMesh().colors32;
            }
            var normalColors = PlateColors();
            ExecuteEvents.Execute(skip.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            for (float end = Time.realtimeSinceStartup + .2f; Time.realtimeSinceStartup < end;) yield return null;
            var hoverColors = PlateColors();
            Require(!normalColors.SequenceEqual(hoverColors), "Skip hover lights the engraved rim");
            ExecuteEvents.Execute(skip.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            skip.Select();
            for (float end = Time.realtimeSinceStartup + .2f; Time.realtimeSinceStartup < end;) yield return null;
            Require(hoverColors.SequenceEqual(PlateColors()), "keyboard focus lights the same rim as hover");
            SaveFrame(camera.targetTexture, "CompanionSkipFocus");
            ExecuteEvents.Execute(skip.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return null;
            Require(caption.anchoredPosition.y == -1 && hitRect == plate.rectTransform.rect, "press sinks one logical pixel inside the stationary hit area");
            SaveFrame(camera.targetTexture, "CompanionSkipPressed");
            ExecuteEvents.Execute(skip.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Require(caption.anchoredPosition.y == 0, "release restores the caption baseline");
            EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler) == skip.gameObject,
                "pointer hits Skip at its displayed position");
            Require(npc.Interact(GameManager.Instance.PLAYERCONTROLLER.gameObject), "skip permits reopening companion selection");
            skip.Select();
            ExecuteEvents.Execute(skip.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            for (float end = Time.realtimeSinceStartup + 1f; skip.gameObject.activeInHierarchy && Time.realtimeSinceStartup < end;) yield return null;
            Require(!skip.gameObject.activeInHierarchy && npc.Interact(GameManager.Instance.PLAYERCONTROLLER.gameObject),
                "keyboard Skip closes the picker and permits reopening");
            var cardButton = picker.GetComponentsInChildren<RewardCard>()[2].GetComponent<Button>();
            ExecuteEvents.Execute(cardButton.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Require(InventoryManager.Instance.MainSummon != null && InventoryManager.Instance.MainSummon.minionName == "전사", "submit equips the selected companion");
            var template = AssetDatabase.LoadAssetAtPath<RewardSelectionUI>("Assets/Prefabs/UI/Reward Selection/EquipmentUpgradeSelectionUI.prefab");
            var shop = Object.Instantiate(template, picker.transform.parent, false);
            try
            {
                var entry = new RewardCandidate { displayData = new GrowthItemData { itemName = "Upgrade",
                    description = string.Join("\n", Enumerable.Repeat("A longer upgrade description.", 12)) } };
                var offers = new System.Collections.Generic.List<RewardCandidate> { entry, entry };
                shop.ShowWithCallbacks(offers, _ => shop.Hide(), shop.Hide, "Choice_Upgrade");
                float settle = Time.realtimeSinceStartup + .3f;
                while (Time.realtimeSinceStartup < settle) yield return null;
                Canvas.ForceUpdateCanvases();
                var active = shop.GetComponentsInChildren<RewardCard>();
                var scroll = active[0].GetComponentInChildren<ScrollRect>();
                Require(active.Length == 2 && shop.GetComponentsInChildren<TextMeshProUGUI>().Any(l => l.text == "Choose an upgrade"),
                    "equipment upgrade inherits the shared two-card layout");
                scroll.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down * 5 });
                Require(scroll.verticalNormalizedPosition < .99f && scroll.viewport.GetComponent<RectMask2D>() != null,
                    "long upgrade descriptions scroll within the card");
                shop.Hide();
                offers.RemoveAt(1);
                shop.Show(offers);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var only = shop.GetComponentsInChildren<RewardCard>().Single();
                var canvas = only.GetComponentInParent<Canvas>();
                var point = PixelViewport.CanvasToGamePoint(canvas, only.transform.position);
                Require(Mathf.Abs(point.x-320) <= 1 && shop.GetComponentsInChildren<TextMeshProUGUI>().Any(l => l.text == "Choose a reward"),
                    "single regular reward is centered");
            }
            finally { shop.Hide(); Object.Destroy(shop.gameObject); }
            var inventory = InventoryManager.Instance;
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales.First(l => l.Identifier.Code == "ko");
            var mage = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>("Assets/SOData/Minion/Main/IceMage Minion.asset");
            var doll = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>("Assets/SOData/Minion/Main/DashDoll Minion.asset");
            int other = 1 - inventory.ActiveSummonSlot;
            Require(inventory.EquipMinion(other, mage), "second slot set for companion comparison");
            var swap = MainMinionSelectionUI.Instance;
            if (swap == null)
            {
                var asset = AssetDatabase.LoadAssetAtPath<MainMinionSelectionUI>("Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab");
                swap = Object.Instantiate(asset,picker.transform.parent,false);
            }
            Require(swap.ShowPending(doll), "replacement panel opens from a pending reward");
            float ready = Time.unscaledTime + .35f;
            while (Time.unscaledTime < ready) yield return null;
            SaveFrame(camera.targetTexture,"CompanionSwap");
            swap.ToggleTargetSlot();
            int slot = swap.TargetSlot;
            swap.Hover(MainMinionSelectionAction.ActionKind.Compare,true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var label in swap.GetComponentsInChildren<TextMeshProUGUI>())
            {
                VerifyPixelLabel(label);
                if (label.name != "Description" || !label.transform.parent.name.EndsWith("Details")) continue;
                Require(label.preferredHeight <= label.rectTransform.rect.height + 1,
                    $"{label.transform.parent.name} needs {label.preferredHeight} of {label.rectTransform.rect.height} pixels");
            }
            SaveFrame(camera.targetTexture,"CompanionComparison");
            swap.Select(MainMinionSelectionAction.ActionKind.Replace);
            Require(!swap.IsOpen && inventory.GetSummonAt(slot) == doll, "replacement equips the chosen slot");
        }
        finally { LocalizationSettings.SelectedLocale = before; }
    }

    private static bool UsesPixelGlyphs(TextMeshProUGUI label) =>
        label.font != null && label.TryGetComponent<PixelText>(out _) && !label.enableAutoSizing &&
        label.font.atlasPopulationMode == AtlasPopulationMode.Static &&
        label.font.atlasRenderMode == GlyphRenderMode.RASTER_HINTED &&
        label.font.atlasTextures.All(texture => texture.filterMode == FilterMode.Point && texture.mipmapCount == 1);

    private static bool IsWhole(float value) => Mathf.Abs(value - Mathf.Round(value)) < .01f;

    private static IEnumerator CaptureDepthPair(RenderTexture frame, string scene)
    {
        var preview = LightingPreview.Active;
        var property = typeof(LightingPreview).GetProperty(nameof(LightingPreview.Active));
        var comparison = preview != null ? JsonUtility.FromJson<LightingPreview>(JsonUtility.ToJson(preview)) :
            LightingPreview.From(CameraSettingsSO.Instance);
        float speed = Time.timeScale;
        try
        {
            Time.timeScale = 0;
            property.SetValue(null,comparison);
            foreach (bool enabled in new[] { false,true })
            {
                comparison.depthSuppression = enabled ? 0 : 1;
                yield return null;
                yield return null;
                Require((Shader.GetGlobalFloat("_WorldDepthStrength") > 0) == enabled, scene+" depth control");
                SaveFrame(frame,"Depth/"+scene+(enabled?"-with-depth":"-without-depth"));
            }
            comparison.cloudSuppression = 1;
            yield return null; yield return null;
            Require(Shader.GetGlobalVector("_CloudProjection").z == 0,scene+" cloud opacity can be isolated");
            SaveFrame(frame,"CloudLight/"+scene+"-clear");
            comparison.cloudSuppression = 0;
            yield return null; yield return null;
            Require(Shader.GetGlobalVector("_CloudProjection").z > 0,scene+" cloud shadow reaches the floor");
            SaveFrame(frame,"CloudLight/"+scene+"-cloud");
        }
        finally
        {
            property.SetValue(null,preview);
            Time.timeScale = speed;
        }
        if (scene == "VillageScene" && SessionState.GetBool("Verification.ArtDirection",false) && speed > 0)
        {
            var camera = PixelArtUpscaleRig.RenderCamera;
            var brain = camera.GetComponent<CinemachineBrain>();
            bool following = brain != null && brain.enabled;
            var position = camera.transform.position;
            try
            {
                if (following) brain.enabled = false;
                double start = Time.timeAsDouble;
                var initial = Shader.GetGlobalVector("_CloudProjection");
                for (int i = 0; i < 37; i++)
                {
                    while (Time.timeAsDouble < start + i/8.0) yield return null;
                    SaveFrame(frame,$"CloudLight/Drift/{i:D2}");
                }
                var drift = Shader.GetGlobalVector("_CloudProjection");
                Require(drift.w > 0 && Mathf.Abs(Mathf.DeltaAngle(initial.x*360,drift.x*360)) > 5 &&
                    Vector3.Distance(camera.transform.position,position) < .02f,
                    "clouds travel past a stationary camera at game speed");
            }
            finally { if (following) brain.enabled = true; }
        }
    }

    internal static void SaveFrame(RenderTexture frame, string name)
    {
        var image = ReadFrame(frame, TextureFormat.RGB24);
        try { SaveFrame(image, name); }
        finally { Object.Destroy(image); }
    }

    internal static void SaveFrame(Texture2D image, string name)
    {
        var path = Path.Combine(ProjectVerification.Output, name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, image.EncodeToPNG());
    }

    internal static Texture2D ReadFrame(RenderTexture frame, TextureFormat format = TextureFormat.RGBA32)
    {
        var previous = RenderTexture.active;
        var image = new Texture2D(frame.width, frame.height, format, false);
        try
        {
            RenderTexture.active = frame;
            image.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0);
            return image;
        }
        catch
        {
            Object.Destroy(image);
            throw;
        }
        finally { RenderTexture.active = previous; }
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("[PresentationRegressionCheck] " + label);
    }
}
