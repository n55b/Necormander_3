using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>격리된 batchmode Play Mode에서 물가 착지·가림 렌더·표시/회복 회귀를 검사한다.</summary>
public static class PlaytestRegressionCheck
{
    private const string Key = "PlaytestRegressionCheck.Running";
    private static IEnumerator checks;
    private static int passed;
    private static double deadline;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("새 batchmode 에디터에서만 실행하세요.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    public static void RunThreats()
    {
        SessionState.SetBool(Key + ".Threats", true);
        Run();
    }
    public static void RunGuard()
    {
        SessionState.SetBool(Key + ".GuardOnly", true);
        Run();
    }
    [InitializeOnLoadMethod]
    private static void Resume() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (checks == null) { deadline = EditorApplication.timeSinceStartup + 120; checks = SessionState.GetBool(Key + ".Threats", false) ? CheckThreats() : SessionState.GetBool(Key + ".GuardOnly", false) ? CheckGuard() : Check(); }
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("검사 120초 초과");
            if (!checks.MoveNext()) Finish(0);
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }
    private static void Finish(int code)
    {
        Time.timeScale = 1;
        Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
        MapGenerator.Instance = null;
        SessionState.EraseBool(Key);
        SessionState.EraseBool(Key + ".GuardOnly");
        SessionState.EraseBool(Key + ".Threats");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
    private static IEnumerator Wait(float seconds)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end) yield return null;
    }
    private static IEnumerator CheckThreats()
    {
        var camera = new GameObject("Threat preview camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 180;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var ink = new Color(.72f, .11f, .13f, 1);
        var lane = PixelVectorShape.Create("Lane", PixelVectorShape.Kind.Rectangle, ink, 0, PixelVectorShape.Stroke.Boundary);
        lane.transform.localScale = new Vector3(80, 40, 1);
        var ellipse = PixelVectorShape.Create("Ellipse", PixelVectorShape.Kind.Ellipse, ink, 0, PixelVectorShape.Stroke.Boundary);
        ellipse.transform.position = Vector3.left * 100;
        ellipse.transform.localScale = new Vector3(80, 40, 1);
        var cone = BoneMasterTelegraphUtil.SpawnCone(null, Vector2.right * 100, Vector2.right, 40, 35, ink);
        foreach (int scale in new[] { 1, 2, 3 })
        {
            var target = RenderTexture.GetTemporary(640 * scale, 360 * scale, 24);
            camera.targetTexture = target;
            float previous = 0, peak = 0;
            foreach (float progress in new[] { 0f, .55f, .93f, 1f })
            {
                var cue = PixelVectorShape.ThreatCue(progress, 1);
                PixelVectorShape.SetThreatCue(lane, cue);
                PixelVectorShape.SetThreatCue(ellipse, cue);
                BoneMasterTelegraphUtil.UpdateCone(cone, Vector2.right * 100, Vector2.right, progress);
                camera.Render();
                var frame = Read(target);
                float edge = frame.GetPixel(320 * scale, 200 * scale - 1).g;
                int horizontal = 0, vertical = 0, oval = 0;
                for (int offset = 0; offset < 6 * scale; offset++)
                {
                    if (frame.GetPixel(360 * scale - 1 - offset, 180 * scale).r > .4f) horizontal++;
                    if (frame.GetPixel(320 * scale, 200 * scale - 1 - offset).r > .4f) vertical++;
                    if (frame.GetPixel(220 * scale, 200 * scale - 1 - offset).r > .4f) oval++;
                }
                Require(horizontal == scale && vertical == scale && oval == scale,
                    $"Threat width at {target.width}x{target.height}, long/short/ellipse = {horizontal}/{vertical}/{oval}");
                Require(frame.GetPixel(320 * scale, 200 * scale).r < .02f && frame.GetPixel(420 * scale, 223 * scale).r < .02f,
                    "Warning stays inside the indicated range");
                if (progress == .55f) Require(edge > previous, "Rim brightens as the countdown fills");
                if (progress == .93f) { Require(edge > previous + .04f, "One short pulse immediately before impact"); peak = edge; }
                if (progress == 1f) Require(edge < peak, "Pulse ends when the attack goes live");
                previous = edge;
                if (scale == 2 && progress == .93f) PresentationRegressionCheck.SaveFrame(frame,"ThreatPreview");
                Object.Destroy(frame);
            }
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<BaseHitBox>("Assets/Prefabs/Effect/Combat/Skills/Boss Execution Lane.prefab");
        var box = Object.Instantiate(prefab);
        box.Init(default, 0, .5f, 1f);
        var props = new MaterialPropertyBlock();
        box.GetComponentInChildren<SpriteRenderer>().GetPropertyBlock(props);
        Require(props.GetVector("_ThreatCue").x == 1 && box.GetComponentsInChildren<LineRenderer>().All(line => !line.enabled),
            "Execution lane uses the common countdown and one boundary");
        box.ForceActivate();
        box.GetComponentInChildren<SpriteRenderer>().GetPropertyBlock(props);
        Require(props.GetVector("_ThreatCue").y == 1 && props.GetVector("_ThreatCue").z == 0,
            "Animation event activation settles the warning immediately");
        Object.Destroy(box.gameObject);
        yield return null;
    }

    private static IEnumerator CheckGuard()
    {
        var managerHost = new GameObject("Inactive guard data"); managerHost.SetActive(false);
        var gm = managerHost.AddComponent<GameManager>(); GameManager.Instance = gm;
        var data = managerHost.AddComponent<DataManager>(); gm.dataManager = data;
        Set(data, "growthRegistry", AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(
            AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:GrowthRegistrySO")[0])));
        var root = new GameObject("Guard visual fixture", typeof(Rigidbody2D)); root.SetActive(false);
        var player = root.AddComponent<PlayerController>();
        var stat = root.AddComponent<CharacterStat>(); Set(stat, "setDoneActions", new UnityEngine.Events.UnityEvent());
        Set(player, "stat", stat);
        var body = new GameObject("Body", typeof(SpriteRenderer), typeof(Animator)); body.transform.SetParent(root.transform, false);
        var hand = new GameObject("Hand", typeof(SpriteRenderer)); hand.transform.SetParent(body.transform, false);
        var animator = body.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Player/Player_Animator.controller");
        Set(player, "BodyAnimator", animator);
        root.SetActive(true); player.enabled = false;
        var baseIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Player/Player_AnimClip_Idle.anim");
        foreach (string name in new[] { "Viper", "ViperSC", "ViperAS", "Serendipity", "Marionette" })
        {
            var weapon = AssetDatabase.LoadAssetAtPath<EquipmentSO>($"Assets/SOData/Equipment/Weapons/{name}.asset");
            animator.runtimeAnimatorController = weapon.handAnimationOverride; animator.Rebind(); animator.Update(0);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); weapon.handAnimationOverride.GetOverrides(pairs);
            var clip = pairs.Single(p => p.Key == baseIdle).Value;
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single(b => b.path == "Hand" && b.propertyName == "m_Sprite");
            var expected = (Sprite)AnimationUtility.GetObjectReferenceCurve(clip, binding)[0].value;
            Set(player, "moveInput", Vector2.zero);
        }
        root.SetActive(false); GameManager.Instance = null;
        Debug.Log($"[PlaytestGuard] PASS: {passed} weapon/line/locomotion checks plus existing guard damage, projectile, break/recovery regressions.");
        yield break;
    }
    private static IEnumerator Check()
    {
        while (UnityNote.SceneLoader.IsLoading) yield return null;
        CheckAssets();
        CheckCursorMaterials();
        CheckDash();
        CheckAuthoredWaterBanks();
        CheckWaves();

        var corpse = new GameObject("Missing death clip", typeof(SpriteRenderer));
        var renderer = corpse.GetComponent<SpriteRenderer>();
        var death = corpse.AddComponent<AstroNuts.Monsters.MonsterDeathHandler>();
        Set(death, "collidersToDisable", System.Array.Empty<Collider2D>());
        Set(death, "behavioursToDisable", System.Array.Empty<MonoBehaviour>());
        Set(death, "fallbackDelay", .3f);
        // The asset inventory above can take longer than the entire death animation on the first frame.
        float previousStep = Time.captureDeltaTime;
        Time.captureDeltaTime = 1f / 60f;
        yield return null;
        death.Die();
        bool faded = false;
        float deadline = Time.realtimeSinceStartup + 2f;
        while (corpse != null && Time.realtimeSinceStartup < deadline)
        {
            faded |= renderer.color.a > 0f && renderer.color.a < 1f;
            yield return null;
        }
        Time.captureDeltaTime = previousStep;
        Require(faded && corpse == null, $"Missing death animation fades before retirement (intermediate={faded}, retired={corpse == null})");

        // 게임/저장 초기화 없이 실제 Vignette 변경 API를 검사한다.
        var gmHost = new GameObject("Inactive GM"); gmHost.SetActive(false);
        var gm = gmHost.AddComponent<GameManager>();
        var volume = new GameObject("Test volume").AddComponent<Volume>();
        volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var vignette = volume.profile.Add<Vignette>(); vignette.color.value = Color.black;
        gm.globalvolume = volume;
        gm.ChangeVignetteColor(.06f, Color.red);
        gm.ChangeVignetteColor(.06f, Color.red);
        gm.StopAllCoroutines(); Time.timeScale = 0;
        var wait = Wait(.1f); while (wait.MoveNext()) yield return null;
        Call(gm, "LateUpdate");
        Require(vignette.color.value == Color.black, "연속 피격 + 정지 + 코루틴 취소 뒤 원래 색 복원");
        gm.ChangeVignetteColor(1, Color.red); Call(gm, "OnDisable");
        Require(vignette.color.value == Color.black, "비활성화 시 피격 색 정리");
        Time.timeScale = 1; volume.enabled = false;

        var camera = new GameObject("Test camera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 2;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var normalMaterial = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/Feedback/Dither Afterimage.mat"));
        normalMaterial.SetFloat("_Dither", 0);
        var playerSprite = SpriteRect("Visible body", new Vector2(2, 2), Color.blue, normalMaterial);
        var playerGroup = playerSprite.gameObject.AddComponent<SortingGroup>();
        var playerDepth = playerSprite.gameObject.AddComponent<YSortableObject>();
        Set(playerDepth, "sortingGroup", playerGroup);
        var blocker = SpriteRect("Torch pixels", new Vector2(1, 2), new Color(.3f, 0, 0), normalMaterial, true);
        blocker.transform.position = new Vector3(.5f, -.1f, 0); blocker.gameObject.layer = Layers.Wall;
        blocker.gameObject.AddComponent<YSortableObject>();
        playerSprite.gameObject.SetActive(false);
        var occlusion = playerSprite.gameObject.AddComponent<PlayerOcclusionMist>();
        playerSprite.gameObject.SetActive(true);
        wait = Wait(.35f); while (wait.MoveNext()) yield return null;
        Require(camera == PixelArtUpscaleRig.RenderCamera && camera.targetTexture != null,
            "가림 검사 카메라가 게임 프레임에 연결됨");
        var pixels = Read(camera.targetTexture);
        var uncovered = Average(pixels, camera, new Vector2(-.5f, 0));
        var covered = Average(pixels, camera, new Vector2(.5f, 0));
        var hole = Average(pixels, camera, new Vector2(.5f, .43f));
        System.IO.File.WriteAllBytes("Logs/Playtest-occlusion.png", pixels.EncodeToPNG());
        Debug.Log($"[PlaytestRegression] render uncovered={uncovered}, covered={covered}, hole={hole}");
        Require(uncovered.b > .9f && uncovered.r < .05f && uncovered.g < .05f, "가리지 않은 몸은 원래 색 그대로");
        Require(covered.b > .28f && covered.g < .18f, "밝은 테두리 없이 실제 플레이어의 파란색이 비침");
        Require(hole.b > .9f && hole.r < .05f, "가림막 투명 여백에서 원래 플레이어 색 보존");
        Require(occlusion.ActiveSurfaces == 1, "가리는 지형에 안개 한 개 적용");
        var mistBlock = new MaterialPropertyBlock();
        blocker.GetPropertyBlock(mistBlock);
        float initialCenter = mistBlock.GetVector("_OcclusionFogShape").x;
        var initialPosition = playerSprite.transform.position;
        playerSprite.transform.position += Vector3.right * .4f;
        Call(occlusion, "UpdateMist", .05f);
        blocker.GetPropertyBlock(mistBlock);
        float following = mistBlock.GetVector("_OcclusionFogShape").x;
        float wake = mistBlock.GetVector("_OcclusionFogWake").x;
        Require(initialCenter < wake && wake < following && following < initialCenter + .2f,
            "수관의 열린 자리와 잔류 안개가 서로 다른 속도로 플레이어를 뒤따름");
        float full = mistBlock.GetFloat("_OcclusionFogProgress");
        playerSprite.transform.position += Vector3.right * 5;
        Call(occlusion, "UpdateMist", .12f);
        blocker.GetPropertyBlock(mistBlock);
        float closing = mistBlock.GetFloat("_OcclusionFogProgress");
        Require(closing > 0 && closing < full && following == mistBlock.GetVector("_OcclusionFogShape").x
            && wake == mistBlock.GetVector("_OcclusionFogWake").x, "나무를 벗어나면 마지막 자리에 머물러 닫힘");
        playerSprite.transform.position = initialPosition;
        blocker.gameObject.SetActive(false);
        wait = Wait(.25f); while (wait.MoveNext()) yield return null;
        Require(occlusion.ActiveSurfaces == 0, "비활성 지형 안개 해제");
        blocker.gameObject.SetActive(true);
        wait = Wait(.25f); while (wait.MoveNext()) yield return null;
        Require(occlusion.ActiveSurfaces == 1, "재등록 지형 안개 복원");
        blocker.gameObject.layer = 0;
        wait = Wait(.65f); while (wait.MoveNext()) yield return null;
        Require(occlusion.ActiveSurfaces == 0, "가림 후보에서 빠진 지형 복원");
        blocker.gameObject.layer = Layers.Wall;
        blocker.transform.position = Vector3.right * 5;
        wait = Wait(.2f); while (wait.MoveNext()) yield return null;
        Require(occlusion.ActiveSurfaces == 0, "가림막에서 벗어나면 안개 정리");

        blocker.transform.localScale = new Vector3(1f, .22f, 1f);
        blocker.transform.position = new Vector3(.5f, -.72f, 0);
        var impact = new GameObject("Detached impact depth fixture").AddComponent<PooledHitEffect>();
        impact.FollowTarget(playerSprite.transform);
        wait = Wait(.12f); while (wait.MoveNext()) yield return null;
        pixels = Read(camera.targetTexture);
        var feet = Average(pixels, camera, new Vector2(.5f, -.72f));
        var torso = Average(pixels, camera, new Vector2(.5f, .4f));
        Require(feet.r > .15f && feet.b < .1f && torso.b > .9f && occlusion.ActiveSurfaces == 0,
            "뒤에 서면 낮은 사물이 발 쪽만 가리고 몸통은 그대로 보임");
        var impactGroup = impact.GetComponent<SortingGroup>();
        Require(impactGroup != null && impactGroup.sortingOrder > playerGroup.sortingOrder &&
            impactGroup.sortingOrder < blocker.sortingOrder, "분리된 타격 연출도 피해자 바로 위와 전경 사물 사이에 정렬됨");
        System.IO.File.WriteAllBytes("Logs/World-depth-behind.png", pixels.EncodeToPNG());
        Object.Destroy(pixels);

        // Move the contact before moving the art to verify the same rule for animated offsets.
        playerDepth.SetGroundPoint(new Vector3(0, -.8f, 0));
        wait = Wait(.12f); while (wait.MoveNext()) yield return null;
        pixels = Read(camera.targetTexture);
        feet = Average(pixels, camera, new Vector2(.5f, -.72f));
        Require(feet.b > .9f && feet.r < .1f, "발 기준점이 앞으로 지나가면 같은 그림도 사물 앞에 표시됨");
        Require(impactGroup.sortingOrder > playerGroup.sortingOrder,
            "피해자가 사물 앞으로 이동할 때 타격 연출의 깊이도 갱신됨");
        System.IO.File.WriteAllBytes("Logs/World-depth-in-front.png", pixels.EncodeToPNG());
        Object.Destroy(pixels);
        impact.gameObject.SetActive(false);
        playerSprite.gameObject.SetActive(false); blocker.gameObject.SetActive(false);

        // 실제 렌더 타깃에서 방향 표시, 등록 해제와 화면 내 진입을 검사한다.
        var indicator = new GameObject("Offscreen enemy", typeof(SpriteRenderer)).AddComponent<EntityDirectionIndicator>();
        indicator.transform.position = Vector3.right * 8;
        var arrowManager = new GameObject("Arrow manager").AddComponent<OffscreenEnemyArrowManager>();
        arrowManager.Initialize(); arrowManager.Initialize();
        wait = Wait(.15f); while (wait.MoveNext()) yield return null;
        var arrows = Get<List<SpriteRenderer>>(arrowManager, "_arrows");
        Require(arrows.Count == 1 && arrows[0].enabled && PixelVectorShape.IsApplied(arrows[0]), "화면 밖 적 벡터 화살표 표시 + 중복 초기화 방지");
        pixels = Read(camera.targetTexture);
        System.IO.File.WriteAllBytes("Logs/Playtest-arrow.png", pixels.EncodeToPNG());
        Require(pixels.GetPixels().Count(IsArrowFill) > 20, "실제 URP 출력에 화살표 색상 픽셀 존재");
        Object.Destroy(pixels);
        indicator.transform.position = Vector3.up * 8;
        wait = Wait(.1f); while (wait.MoveNext()) yield return null;
        pixels = Read(camera.targetTexture);
        var arrowCenter = (Vector2)camera.WorldToScreenPoint(arrows[0].transform.position);
        Require(ArrowRowWidth(pixels, arrowCenter, -5) > ArrowRowWidth(pixels, arrowCenter, 5), "화면 상단 화살표가 렌더 타깃에서 위를 가리킴");
        Object.Destroy(pixels);
        indicator.transform.position = Vector3.zero;
        wait = Wait(.1f); while (wait.MoveNext()) yield return null;
        Require(!arrows[0].enabled, "적이 화면 안에 들어오면 화살표 숨김");
        indicator.transform.position = Vector3.right * 8; indicator.gameObject.SetActive(false);
        wait = Wait(.1f); while (wait.MoveNext()) yield return null;
        Require(!arrows[0].enabled, "비활성/은신 대상 등록 해제");
        var shapes = CheckVectorShapes(camera);
        while (shapes.MoveNext()) yield return null;
        CheckPips(camera);
        Debug.Log($"[PlaytestRegression] PASS: {passed} checks; box landing/physics, wall, interrupted dash, waves, pause vignette, partial URP mask, arrows, late camera HUD, stage3 assets.");
    }

    private static bool IsArrowFill(Color pixel) => pixel.r > .4f && pixel.r > pixel.g + .14f;

    private static int ArrowRowWidth(Texture2D frame, Vector2 center, int offsetY)
    {
        int count = 0;
        for (int x = -10; x <= 10; x++)
            if (IsArrowFill(frame.GetPixel((int)center.x + x, (int)center.y + offsetY))) count++;
        return count;
    }

    private static IEnumerator CheckVectorShapes(Camera camera)
    {
        const int markerCount = 16;
        const int markerSize = 30;
        var markers = new SpriteRenderer[markerCount];
        var directions = new Vector2[markerCount];
        for (int i = 0; i < markerCount; i++)
        {
            float radians = i * Mathf.PI * 2f / markerCount;
            var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            directions[i] = direction;
            var marker = PixelVectorShape.Create("Locator test", PixelVectorShape.Kind.Locator, Color.white, 0,
                PixelVectorShape.Stroke.Indicator);
            marker.transform.position = WorldAtPixel(camera, new Vector2(48 + i % 8 * 76, 286 + i / 8 * 46));
            PixelVectorShape.SetScreenShape(marker, Vector2.one * markerSize, direction);
            markers[i] = marker;
        }

        float unitsPerPixel = PixelViewport.WorldUnitsPerPixel;
        var ring = BossTelegraph.SpawnRing(WorldAtPixel(camera, new Vector2(118, 145)), 60 * unitsPerPixel, Color.red);
        var cone = BoneMasterTelegraphUtil.SpawnCone(null, WorldAtPixel(camera, new Vector2(204, 145)), Vector2.right,
            40 * unitsPerPixel, 55f, Color.green);
        BoneMasterTelegraphUtil.UpdateCone(cone, cone.transform.position, Vector2.right, .5f);
        var lanePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effect/Combat/Skills/Telegraph Line Hitbox Prefab.prefab");
        var lane = BoneMasterTelegraphUtil.SpawnLane(null, WorldAtPixel(camera, new Vector2(290, 145)), Vector2.right,
            72 * unitsPerPixel, 20 * unitsPerPixel, Color.magenta, lanePrefab.GetComponent<BaseHitBox>(), 0f, 5f);
        var ellipsePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effect/Combat/Skills/Center Skill Hitbox Circle Prefab.prefab");
        var ellipse = BoneMasterTelegraphUtil.SpawnEllipse(null, WorldAtPixel(camera, new Vector2(447, 145)),
            34 * unitsPerPixel, 18 * unitsPerPixel, Color.cyan, ellipsePrefab.GetComponent<BaseHitBox>(), 0f, 5f);
        var gaugeHost = new GameObject("World-space gauge test", typeof(Canvas));
        gaugeHost.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        gaugeHost.transform.position = WorldAtPixel(camera, new Vector2(540, 145));
        var anchor = new GameObject("Gauge anchor", typeof(RectTransform)).GetComponent<RectTransform>();
        anchor.SetParent(gaugeHost.transform, false);
        anchor.sizeDelta = new Vector2(68, 26) * unitsPerPixel;
        gaugeHost.SetActive(false);
        var gauge = gaugeHost.AddComponent<CenterMeetFill>();
        Set(gauge, "gaugeAnchor", anchor);
        gaugeHost.SetActive(true);
        gauge.SetColor(Color.yellow);
        gauge.StartFill(.05f);

        var stroke = PixelVectorShape.Create("Stroke width test", PixelVectorShape.Kind.Rectangle, Color.white, 0,
            PixelVectorShape.Stroke.Boundary);
        stroke.transform.position = WorldAtPixel(camera, new Vector2(320, 66));
        stroke.transform.localScale = new Vector3(36 * unitsPerPixel, 16 * unitsPerPixel, 1f);

        var arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effect/Combat/Skills/Projectiles/Archer Projectile.prefab");
        var arrow = Object.Instantiate(arrowPrefab, WorldAtPixel(camera, new Vector2(220, 67)), Quaternion.Euler(0f, 0f, 32f));
        var bowCollider = arrow.GetComponent<BoxCollider2D>();
        var authoredCollider = arrowPrefab.GetComponent<BoxCollider2D>();
        Require(PixelVectorShape.IsApplied(arrow.GetComponent<SpriteRenderer>()) && bowCollider.size == authoredCollider.size &&
            bowCollider.offset == authoredCollider.offset, "궁수 화살의 회전 표시와 충돌 크기 분리");

        var wait = Wait(.15f); while (wait.MoveNext()) yield return null;
        var frame = Read(camera.targetTexture);
        int validDirections = 0;
        for (int i = 0; i < markerCount; i++)
        {
            var center = (Vector2)camera.WorldToScreenPoint(markers[i].transform.position);
            Vector2 tip = center + directions[i] * 8f;
            Vector2 back = center - directions[i] * 8f;
            if (frame.GetPixel((int)tip.x, (int)tip.y).r > .8f && frame.GetPixel((int)back.x, (int)back.y).r < .1f)
                validDirections++;
        }

        Require(validDirections == markerCount, "16개 각도에서 발밑 포인터가 연속 방향으로 표시됨");
        Require(frame.GetPixel(146, 145).r > .8f && frame.GetPixel(118, 145).r < .1f && frame.GetPixel(149, 145).r < .1f,
            "고리 안쪽과 위험 범위 밖이 투명함");
        Require(frame.GetPixel(217, 145).g > .15f && frame.GetPixel(232, 145).g < .1f && frame.GetPixel(194, 145).g < .1f,
            "부채꼴 진행 채움과 뒤쪽의 비판정 영역 분리");
        Require(frame.GetPixel(342, 145).r > .15f && frame.GetPixel(290, 155).r < .1f && frame.GetPixel(362, 145).r < .1f,
            "레인 너비와 끝점이 월드 판정 범위와 일치");
        Require(frame.GetPixel(462, 145).g > .15f && frame.GetPixel(447, 164).g < .1f && frame.GetPixel(482, 145).g < .1f,
            "타원 장축과 단축이 서로 독립된 월드 반경을 따름");
        Require(!gauge.IsFilling && frame.GetPixel(540, 145).r > .8f && frame.GetPixel(540, 145).g > .8f && frame.GetPixel(540, 145).b < .1f,
            "머리 위 벡터 게이지가 카운터 색으로 채워짐");
        Require(frame.GetPixel(208, 59).r > .45f, "회전한 궁수 화살의 벡터 화살촉이 실제 프레임에 표시됨");
        System.IO.File.WriteAllBytes("Logs/Playtest-vector-effects.png", frame.EncodeToPNG());
        var cameraSettings = new SerializedObject(CameraSettingsSO.Instance);
        var strokeWidth = cameraSettings.FindProperty(nameof(CameraSettingsSO.vectorTelegraphStrokePixels));
        int originalWidth = strokeWidth.intValue;
        Object.Destroy(frame);

        try
        {
            foreach (int width in new[] { 1, 2 })
            {
                strokeWidth.intValue = width;
                cameraSettings.ApplyModifiedPropertiesWithoutUndo();
                float deadline = Time.unscaledTime + 1f;
                while (stroke.sharedMaterial.GetVector("_StrokeWidths").y != width && Time.unscaledTime < deadline)
                    yield return null;
                wait = Wait(.1f); while (wait.MoveNext()) yield return null;
                frame = Read(camera.targetTexture);
                float boundary = frame.GetPixel(320, 74 - width).r;
                float interior = frame.GetPixel(320, 73 - width).r;
                float exterior = frame.GetPixel(320, 74).r;
                float materialWidth = stroke.sharedMaterial.GetVector("_StrokeWidths").y;
                Require(boundary > .8f && interior < .1f && exterior < .1f,
                    $"전조 셰이더가 {width} 논리 픽셀 외곽선을 판정 안쪽에 그림 (재질 {materialWidth}, 경계 {boundary}, 안쪽 {interior}, 바깥 {exterior})");
                Object.Destroy(frame);
            }
        }
        finally
        {
            strokeWidth.intValue = originalWidth;
            cameraSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (var marker in markers) marker.gameObject.SetActive(false);
        foreach (var effect in new[] { ring, cone, lane, ellipse, gaugeHost, stroke.gameObject, arrow }) effect.SetActive(false);
    }

    private static Vector3 WorldAtPixel(Camera camera, Vector2 point)
        => camera.ViewportToWorldPoint(new Vector3(point.x / PixelViewport.Width, point.y / PixelViewport.Height, -camera.transform.position.z));
    private static void CheckAssets()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/Feedback/Dither Afterimage.mat");
        Require(material.GetFloat("_Dither") == 0, "플레이어/돌진 공용 잔상 디더링 해제");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab");
        Require(source.GetComponentsInChildren<PlayerOcclusionMist>(true).Length == 1 &&
            Resources.Load<Shader>("WorldAtmosphere/WorldPixelSprite") != null, "플레이어 몸통에 안개 처리 연결");
        foreach (var pair in new[] { new[] { "Serendipity", "ViperSC" }, new[] { "Marionette", "ViperAS" } })
        {
            var target = AssetDatabase.LoadAssetAtPath<EquipmentSO>($"Assets/SOData/Equipment/Weapons/{pair[0]}.asset");
            var previous = AssetDatabase.LoadAssetAtPath<EquipmentSO>($"Assets/SOData/Equipment/Weapons/{pair[1]}.asset");
            Require(target.handAnimationOverride != null && target.handAnimationOverride == previous.handAnimationOverride, pair[0] + " 직전 무기 클립 유지");
        }
        Require(CameraSettingsSO.Instance.vectorShapeShader != null, "빌드용 벡터 셰이더 연결");
        foreach (var name in new[] { "Torchlight", "Candle_Long" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Map/Objects/{name}.prefab");
            var wave = prefab.GetComponentInChildren<LightScaleWave>(true);
            Require(wave != null && Get<Vector3>(wave, "baseScale") == Vector3.one * 1.2f &&
                Mathf.Approximately(Get<float>(wave, "speed"), 5f) && Mathf.Approximately(Get<float>(wave, "relativeAmplitude"), .02f),
                name + " 빛 흔들림 기존 값 유지");
        }
    }
    private static void CheckDash()
    {
        var host = new GameObject("Inactive map"); host.SetActive(false);
        var map = host.AddComponent<MapGenerator>(); MapGenerator.Instance = map;
        var grid = new GameObject("Dash grid", typeof(Grid)); grid.transform.localScale = Vector3.one * .5f;
        var ground = MakeTilemap(grid, "Ground"); var wall = MakeTilemap(grid, "Wall"); var water = MakeTilemap(grid, "Water");
        Set(map, "globalGroundTilemap", ground); Set(map, "globalWallTilemap", wall); Set(map, "globalUnsteppableTilemap", water);
        var tile = ScriptableObject.CreateInstance<Tile>(); tile.colliderType = Tile.ColliderType.None;
        for (int x = -10; x < 25; x++) for (int y = -12; y < 12; y++) ground.SetTile(new Vector3Int(x, y), tile);
        for (int x = 3; x < 7; x++) for (int y = -12; y < 12; y++)
        { ground.SetTile(new Vector3Int(x, y), null); water.SetTile(new Vector3Int(x, y), tile); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab");
        var authored = prefab.GetComponent<BoxCollider2D>();
        var size = authored.size; Vector2 start = new Vector2(0, .25f);
        Require(size == new Vector2(1, .2f), "실제 발 박스 크기 검증");
        var landing = SkillCombatUtil.GetSafeBoxDestination(start, Vector2.right, 4.5f, size);
        Require(landing.x > 4f && SkillCombatUtil.IsSafeBoxLanding(landing, size), "좁은 물 건너편 몸 전체 안전 착지");
        landing = SkillCombatUtil.GetSafeBoxDestination(start, Vector2.right, 3f, size);
        Require(landing.x < 1f && landing.x > .8f, "못 건너면 출발한 땅 끝에서 정지");
        Require(!SkillCombatUtil.IsSafeBoxLanding(new Vector2(3.6f, .25f), size), "발 중심만 땅이고 몸이 물에 걸친 착지 거부");
        Require(!map.HasGroundSupport(new Vector2(3.95f, .25f), size), "모서리까지 전체 발 검사");
        for (int y = -12; y < 12; y++) wall.SetTile(new Vector3Int(2, y), tile);
        landing = SkillCombatUtil.GetSafeBoxDestination(start, Vector2.right, 6, size);
        Require(landing.x < .5f, "물/땅이 뒤에 있어도 Wall 관통 금지");
        for (int i = -8; i <= 8; i++)
        {
            landing = SkillCombatUtil.GetSafeBoxDestination(start, new Vector2(1, i * .1f), 4.5f, size);
            Require(landing.x < .5f && SkillCombatUtil.IsSafeBoxLanding(landing, size), "대각선 물가/벽 안전 착지 " + i);
        }
        wall.ClearAllTiles();
        // 물리 Collider도 동일하게 검사한다. 실제 대시 API -> 물리 시뮬레이션 -> 일반 이동.
        var bank = new GameObject("Water collider"); bank.layer = Layers.Unsteppable;
        var obstacle = bank.AddComponent<BoxCollider2D>(); obstacle.size = new Vector2(2, 12); bank.transform.position = new Vector2(2.5f, 0);
        var playerHost = new GameObject("Dash physics", typeof(Rigidbody2D), typeof(BoxCollider2D));
        playerHost.layer = Layers.Player;
        var rb = playerHost.GetComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        var body = playerHost.GetComponent<BoxCollider2D>(); body.size = size; body.offset = authored.offset;
        var player = playerHost.AddComponent<PlayerController>(); player.enabled = false;
        foreach (var extra in playerHost.GetComponents<MonoBehaviour>()) extra.enabled = false;
        rb.position = start - body.offset; Physics2D.SyncTransforms();
        var target = player.GetSafeDashPosition(rb.position, Vector2.right, 4.5f);
        player.SetDashLayer(true); Physics2D.simulationMode = SimulationMode2D.Script;
        for (int i = 0; i < 25; i++) { player.AdvanceDash(target, 15); Physics2D.Simulate(.02f); }
        player.SetDashLayer(false);
        Require(Vector2.Distance(rb.position, target) < .005f, "실제 물리 대시가 안전 목적지까지 도착");
        Vector2 before = rb.position; rb.linearVelocity = Vector2.up; Physics2D.Simulate(.1f);
        Require(rb.position.y > before.y + .09f, "착지 후 일반 이동으로 빠져나옴");
        rb.linearVelocity = Vector2.zero; rb.position = start - body.offset; Physics2D.SyncTransforms();
        player.GetSafeDashPosition(rb.position, Vector2.right, 4.5f); player.SetDashLayer(true);
        rb.position = new Vector2(2.5f, .15f); Physics2D.SyncTransforms(); player.SetDashLayer(false);
        Require(rb.position.x < 1 && SkillCombatUtil.IsSafeBoxLanding(rb.position + body.offset, size), "물 위에서 강제 중단돼도 안전 지점 복귀");
        Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
        playerHost.SetActive(false); bank.SetActive(false);
        MapGenerator.Instance = null; Object.Destroy(playerHost); Object.Destroy(bank); Object.Destroy(grid); Object.Destroy(host);
    }
    private static void CheckAuthoredWaterBanks()
    {
        var host = new GameObject("Authored map check"); host.SetActive(false);
        var map = host.AddComponent<MapGenerator>(); MapGenerator.Instance = map;
        int rooms = 0, paths = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Map/Room Map Prefabs" }))
        {
            // Tilemap 네이티브 데이터는 프리팹 내용을 PreviewScene에 로드한 뒤 검사한다.
            var prefab = PrefabUtility.LoadPrefabContents(AssetDatabase.GUIDToAssetPath(guid));
            try
            {
            var tilemaps = prefab.GetComponentsInChildren<Tilemap>(true);
            var ground = tilemaps.FirstOrDefault(t => t.name == "Ground");
            var wall = tilemaps.FirstOrDefault(t => t.name == "Wall");
            var water = tilemaps.FirstOrDefault(t => t.name == "Unsteppable");
            if (ground == null || water == null) continue;
            Set(map, "globalGroundTilemap", ground); Set(map, "globalWallTilemap", wall); Set(map, "globalUnsteppableTilemap", water);
            int sampled = 0;
            foreach (var cell in ground.cellBounds.allPositionsWithin)
            {
                if (!ground.HasTile(cell)) continue;
                Vector2 from = ground.GetCellCenterWorld(cell);
                if (!map.HasGroundSupport(from, new Vector2(1.04f, .24f))) continue;
                bool bank = false;
                for (int y = -2; y <= 2 && !bank; y++) for (int x = -2; x <= 2 && !bank; x++)
                {
                    Vector3 point = ground.GetCellCenterWorld(cell + new Vector3Int(x, y));
                    bank = water.HasTile(water.WorldToCell(point)) && !ground.HasTile(ground.WorldToCell(point));
                }
                if (!bank) continue;
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 end = SkillCombatUtil.GetSafeBoxDestination(from, dir, 4.5f, new Vector2(1, .2f));
                    if (!map.HasGroundSupport(end, new Vector2(1.04f, .24f)))
                        throw new Exception($"Unsafe authored bank: {prefab.name}, {from}->{end}");
                    Vector2 swept = SkillCombatUtil.ClampBoxToWall(from, end - from, new Vector2(1, .2f));
                    if (Vector2.Distance(end, swept) > .001f) throw new Exception("Authored wall crossed: " + prefab.name);
                    paths++;
                }
                if (++sampled >= 24) break;
            }
            if (sampled > 0) { rooms++; Debug.Log($"[PlaytestRegression] authored banks: {prefab.name}, {sampled * 16} paths"); }
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        MapGenerator.Instance = null; Object.Destroy(host);
        Require(rooms > 0 && paths > 0, $"실제 방 {rooms}개의 물가 {paths}방향 착지/벽 검사");
    }
    private static void CheckWaves()
    {
        ActiveAugment.ClearPenalty();
        var host = new GameObject("Wave check"); host.SetActive(false);
        var roomEvent = host.AddComponent<NormalRoomEvent>();
        var cluster = ScriptableObject.CreateInstance<EnemyClusterSO>();
        var enemy = ScriptableObject.CreateInstance<EnemyMinionDataSO>();
        cluster.enemies.Add(new EnemyCount { enemyData = enemy, count = 3 });
        Call(roomEvent, "AppendWaveEnemies", cluster, 1, .5f);
        Call(roomEvent, "AppendWaveEnemies", cluster, 2, .5f);
        Require(Get<List<EnemyCount>>(roomEvent, "_toSpawn").Count == 4, "합친 웨이브는 각 웨이브별 반올림 총수 유지");
        Get<List<EnemyCount>>(roomEvent, "_toSpawn").Clear();
        ActiveAugment.Apply(new AugmentOffer { penalty = new AugmentPenalty { kind = AugmentPenaltyKind.EnemyCountEarlyWaves, value = 2, waveLimit = 1 } });
        Call(roomEvent, "AppendWaveEnemies", cluster, 1, 1f);
        Call(roomEvent, "AppendWaveEnemies", cluster, 2, 1f);
        Require(Get<List<EnemyCount>>(roomEvent, "_toSpawn").Count == 8, "첫 웨이브 한정 증강은 합쳐도 한 번만 적용");
        ActiveAugment.ClearPenalty();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/Room Map Prefabs/Tutorial/Tutorial_02.prefab");
        if (prefab != null) Require(!Get<bool>(prefab.GetComponent<NormalRoomEvent>(), "combineAllWaves"), "기존 방 기본값은 웨이브 분리");
        Object.Destroy(host); Object.Destroy(cluster); Object.Destroy(enemy);
    }
    private static void CheckPips(Camera camera)
    {
        var host = new GameObject("Inactive HUD player"); host.SetActive(false);
        var player = host.AddComponent<PlayerController>(); player.enabled = false; host.SetActive(true);
        foreach (var behaviour in host.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
        var canvas = new GameObject("HUD", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        PixelArtUpscaleRig.BindCanvas(canvas);
        var ui = new GameObject("Dash UI", typeof(RectTransform)).AddComponent<DashCooldownUI>(); ui.transform.SetParent(canvas.transform, false);
        var pips = new GameObject("Pips", typeof(RectTransform)).GetComponent<RectTransform>(); pips.SetParent(ui.transform, false);
        Set(ui, "worldPips", pips); ui.Initialize(player);
        host.transform.position = new Vector3(.3f, .6f, 0);
        // Update 이후 카메라가 바뀐 것과 같은 순서. Canvas 콜백에서 새 위치를 사용해야 한다.
        camera.transform.position = new Vector3(.17f, -.28f, -10); Canvas.ForceUpdateCanvases();
        Vector3 expected = camera.WorldToScreenPoint(host.transform.position + Get<Vector3>(ui, "worldOffset"));
        Vector2 actual = PixelViewport.CanvasToGamePoint(canvas, pips.position);
        Require(Vector2.Distance(actual, expected) < .1f, "Canvas 렌더 직전 카메라 좌표로 대시 UI 동기화");
        ui.enabled = false; Require(!pips.gameObject.activeSelf, "UI 비활성화 시 발밑 표시 정리");
    }
    private static SpriteRenderer SpriteRect(string name, Vector2 size, Color color, Material mat, bool hole = false)
    {
        int w = Mathf.RoundToInt(size.x * 32), h = Mathf.RoundToInt(size.y * 32);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var colors = Enumerable.Repeat(Color.white, w * h).ToArray();
        if (hole) for (int x = 10; x < 22; x++) for (int y = 44; y < 54; y++) colors[y * w + x] = Color.clear;
        tex.SetPixels(colors); tex.Apply();
        var sr = new GameObject(name).AddComponent<SpriteRenderer>(); sr.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.one * .5f, 32);
        sr.color = color; sr.sharedMaterial = mat; return sr;
    }
    private static Texture2D Read(RenderTexture rt)
    {
        var old = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
        RenderTexture.active = old; return image;
    }
    private static Color Average(Texture2D image, Camera camera, Vector2 point)
    {
        var p = camera.WorldToScreenPoint(point); Color color = Color.clear;
        for (int x = -3; x <= 3; x++) for (int y = -3; y <= 3; y++) color += image.GetPixel((int)p.x + x, (int)p.y + y);
        return color / 49f;
    }
    private static Tilemap MakeTilemap(GameObject grid, string name)
    {
        var go = new GameObject(name, typeof(Tilemap)); go.transform.SetParent(grid.transform, false); return go.GetComponent<Tilemap>();
    }
    private static void CheckCursorMaterials()
    {
        var cursor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<TargetCursorSO>("Assets/SOData/MouseCursor/TargetCursorSO.asset"));
        var a = new GameObject("Target A", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        var b = new GameObject("Target B", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        var shared = a.sharedMaterial;
        try
        {
            cursor.OnEffect(a.gameObject);
            bool highlighted = a.sharedMaterial != shared;
            cursor.OnEffect(b.gameObject);
            cursor.OutEffect();
            Require(highlighted && a.sharedMaterial == shared && b.sharedMaterial == shared,
                "두 타깃 사이 커서를 옮긴 뒤 원래 공유 머티리얼 복원");
        }
        finally { Object.Destroy(a.gameObject); Object.Destroy(b.gameObject); Object.Destroy(cursor); }
    }

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static void Require(bool success, string message)
    {
        if (!success) throw new Exception("[PlaytestRegression] FAIL: " + message);
        passed++; Debug.Log("[PlaytestRegression] OK: " + message);
    }
}
