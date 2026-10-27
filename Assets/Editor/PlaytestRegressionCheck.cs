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
            if (checks == null) { deadline = EditorApplication.timeSinceStartup + 120; checks = SessionState.GetBool(Key + ".GuardOnly", false) ? CheckGuard() : Check(); }
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
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
    private static IEnumerator Wait(float seconds)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end) yield return null;
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
        var guard = root.AddComponent<PlayerParryController>();
        var body = new GameObject("Body", typeof(SpriteRenderer), typeof(Animator)); body.transform.SetParent(root.transform, false);
        var hand = new GameObject("Hand", typeof(SpriteRenderer)); hand.transform.SetParent(body.transform, false);
        var animator = body.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Player/Player_Animator.controller");
        Set(player, "BodyAnimator", animator);
        root.SetActive(true); player.enabled = false;
        var baseIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Player/Player_AnimClip_Idle.anim");
        foreach (string name in new[] { "Viper", "ViperSC", "ViperAS", "Serendipity", "Marionette" })
        {
            var weapon = AssetDatabase.LoadAssetAtPath<EquipmentSO>($"Assets/SOData/Equipment/1015/{name}.asset");
            animator.runtimeAnimatorController = weapon.handAnimationOverride; animator.Rebind(); animator.Update(0);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); weapon.handAnimationOverride.GetOverrides(pairs);
            var clip = pairs.Single(p => p.Key == baseIdle).Value;
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single(b => b.path == "Hand" && b.propertyName == "m_Sprite");
            var expected = (Sprite)AnimationUtility.GetObjectReferenceCurve(clip, binding)[0].value;
            Set(player, "moveInput", Vector2.zero);
            guard.TryStartParry(); animator.Update(0);
            Require(guard.IsParrying && player.canChangeState, name + " 가드 중 이동 모션 잠금 없음");
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") && hand.GetComponent<SpriteRenderer>().sprite == expected,
                name + " 가드 시 장착 무기 외형 유지");
            var line = Get<GameObject>(guard, "_telegraph");
            Require(line != null && line.GetComponent<LineRenderer>().positionCount > 1, name + " 가드 선 표시 유지");
            Set(player, "moveInput", Vector2.right); Call(player, "UpdateLocomotionAnimation"); animator.Update(0);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), name + " 가드 중 걷기 가능");
            guard.StopGuard(); animator.Update(0);
            Require(!guard.IsParrying && animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), name + " 가드 종료가 걷기 모션을 덮지 않음");
        }
        root.SetActive(false); GameManager.Instance = null;
        GuardControlsSetup.VerifyProjectiles();
        Debug.Log($"[PlaytestGuard] PASS: {passed} weapon/line/locomotion checks plus existing guard damage, projectile, break/recovery regressions.");
        yield break;
    }
    private static IEnumerator Check()
    {
        CheckAssets();
        CheckDash();
        CheckAuthoredWaterBanks();
        CheckWaves();

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
        var rt = new RenderTexture(256, 256, 24); rt.Create(); camera.targetTexture = rt;
        var normalMaterial = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/Feedback/Dither Afterimage.mat"));
        normalMaterial.SetFloat("_Dither", 0);
        var playerSprite = SpriteRect("Visible body", new Vector2(2, 2), Color.blue, normalMaterial);
        var silhouette = SpriteRect("Silhouette", new Vector2(2, 2), Color.white,
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/Feedback/Player Dither Silhouette.mat"));
        silhouette.transform.SetParent(playerSprite.transform, false); silhouette.sortingOrder = 9000;
        var blocker = SpriteRect("Torch pixels", new Vector2(1, 2), new Color(.3f, 0, 0), normalMaterial, true);
        blocker.transform.position = new Vector3(.5f, -.1f, 0); blocker.gameObject.layer = Layers.Wall;
        blocker.gameObject.AddComponent<YSortableObject>();
        playerSprite.gameObject.SetActive(false);
        var occlusion = playerSprite.gameObject.AddComponent<PlayerOcclusionSilhouette>();
        Set(occlusion, "silhouetteRenderer", silhouette);
        playerSprite.gameObject.SetActive(true);
        wait = Wait(.35f); while (wait.MoveNext()) yield return null;
        var pixels = Read(rt);
        var uncovered = Average(pixels, camera, new Vector2(-.5f, 0));
        var covered = Average(pixels, camera, new Vector2(.5f, 0));
        var hole = Average(pixels, camera, new Vector2(.5f, .43f));
        System.IO.File.WriteAllBytes("Logs/Playtest-occlusion.png", pixels.EncodeToPNG());
        Debug.Log($"[PlaytestRegression] render uncovered={uncovered}, covered={covered}, hole={hole}");
        Require(uncovered.b > .9f && uncovered.r < .05f && uncovered.g < .05f, "가리지 않은 몸은 원래 색 그대로");
        Require(covered.g > .2f && Mathf.Abs(covered.g - covered.b) < .08f, "가려진 픽셀에 회색 실루엣 표시");
        Require(hole.b > .9f && hole.r < .05f, "가림막의 투명 여백에는 실루엣 없음");
        blocker.transform.position = Vector3.right * 5;
        wait = Wait(.2f); while (wait.MoveNext()) yield return null;
        Require(!silhouette.enabled, "가림막에서 벗어나면 실루엣 정리");
        playerSprite.gameObject.SetActive(false); blocker.gameObject.SetActive(false);

        // 실제 PNG를 실제 렌더러/카메라로 표시, 등록 해제와 화면 안 진입도 검사.
        var indicator = new GameObject("Offscreen enemy", typeof(SpriteRenderer)).AddComponent<EntityDirectionIndicator>();
        indicator.transform.position = Vector3.right * 8;
        var arrowManager = new GameObject("Arrow manager").AddComponent<OffscreenEnemyArrowManager>();
        arrowManager.Initialize(); arrowManager.Initialize();
        wait = Wait(.15f); while (wait.MoveNext()) yield return null;
        var arrows = Get<List<SpriteRenderer>>(arrowManager, "_arrows");
        Require(arrows.Count == 1 && arrows[0].enabled && arrows[0].sprite == ArrowIndicatorSprites.ArrowSide, "화면 밖 적 화살표 표시 + 중복 초기화 방지");
        pixels = Read(rt);
        System.IO.File.WriteAllBytes("Logs/Playtest-arrow.png", pixels.EncodeToPNG());
        Require(pixels.GetPixels().Count(c => c.r > .2f || c.g > .2f || c.b > .2f) > 20, "실제 URP 출력에 화살표 픽셀 존재");
        indicator.transform.position = Vector3.zero;
        wait = Wait(.1f); while (wait.MoveNext()) yield return null;
        Require(!arrows[0].enabled, "적이 화면 안에 들어오면 화살표 숨김");
        indicator.transform.position = Vector3.right * 8; indicator.gameObject.SetActive(false);
        wait = Wait(.1f); while (wait.MoveNext()) yield return null;
        Require(!arrows[0].enabled, "비활성/은신 대상 등록 해제");
        CheckPips(camera);
        Debug.Log($"[PlaytestRegression] PASS: {passed} checks; box landing/physics, wall, interrupted dash, waves, pause vignette, partial URP mask, arrows, late camera HUD, stage3 assets.");
    }
    private static void CheckAssets()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/Feedback/Dither Afterimage.mat");
        Require(material.GetFloat("_Dither") == 0, "플레이어/돌진 공용 잔상 디더링 해제");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab");
        foreach (var effect in source.GetComponentsInChildren<PlayerOcclusionSilhouette>(true))
        {
            Color color = Get<Color>(effect, "silhouetteColor");
            Require(Mathf.Abs(color.r - color.g) < .05f, "플레이어 프리팹 회색 실루엣 저장");
        }
        foreach (var pair in new[] { new[] { "Serendipity", "ViperSC" }, new[] { "Marionette", "ViperAS" } })
        {
            var target = AssetDatabase.LoadAssetAtPath<EquipmentSO>($"Assets/SOData/Equipment/1015/{pair[0]}.asset");
            var previous = AssetDatabase.LoadAssetAtPath<EquipmentSO>($"Assets/SOData/Equipment/1015/{pair[1]}.asset");
            Require(target.handAnimationOverride != null && target.handAnimationOverride == previous.handAnimationOverride, pair[0] + " 직전 무기 클립 유지");
        }
        Require(ArrowIndicatorSprites.ArrowSide != null && ArrowIndicatorSprites.AllyLocator != null, "PNG 슬라이스 로딩");
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
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/Room Map Prefabs/TestTutorial/Tutorial_02.prefab");
        if (prefab != null) Require(!Get<bool>(prefab.GetComponent<NormalRoomEvent>(), "combineAllWaves"), "기존 방 기본값은 웨이브 분리");
        Object.Destroy(host); Object.Destroy(cluster); Object.Destroy(enemy);
    }
    private static void CheckPips(Camera camera)
    {
        var host = new GameObject("Inactive HUD player"); host.SetActive(false);
        var player = host.AddComponent<PlayerController>(); player.enabled = false; host.SetActive(true);
        foreach (var behaviour in host.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
        var canvas = new GameObject("HUD", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var ui = new GameObject("Dash UI", typeof(RectTransform)).AddComponent<DashCooldownUI>(); ui.transform.SetParent(canvas.transform, false);
        var pips = new GameObject("Pips", typeof(RectTransform)).GetComponent<RectTransform>(); pips.SetParent(ui.transform, false);
        Set(ui, "worldPips", pips); ui.Initialize(player);
        host.transform.position = new Vector3(.3f, .6f, 0);
        // Update 이후 카메라가 바뀐 것과 같은 순서. Canvas 콜백에서 새 위치를 사용해야 한다.
        camera.transform.position = new Vector3(.17f, -.28f, -10); Canvas.ForceUpdateCanvases();
        Vector3 expected = camera.WorldToScreenPoint(host.transform.position + Get<Vector3>(ui, "worldOffset"));
        Vector2 actual = RectTransformUtility.WorldToScreenPoint(null, pips.position);
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
