using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>임시 씬에서 조준/유도탄/Wall 대쉬 회귀 검사. 씬과 에셋은 저장하지 않는다.</summary>
public static class CombatMovementCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector2 Offset = new Vector2(2000f, 2000f);

    [MenuItem("Tools/Combat/Verify Aim Homing And Dash")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        // 새 배치 에디터는 이름 없는 씬으로 시작해서 Additive 생성이 금지된다. 기존 씬을 읽기만 한다.
        if (Application.isBatchMode)
            EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Single);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previous = MapGenerator.Instance;
        Tile tile = null;
        try
        {
            CheckAim();
            CheckHoming(scene);
            var host = Make(scene, "Inactive Generator");
            host.SetActive(false);
            var map = host.AddComponent<MapGenerator>();
            MapGenerator.Instance = map;
            var grid = Make(scene, "Grid", typeof(Grid));
            grid.transform.position = Offset;
            grid.transform.localScale = new Vector3(.5f, .5f, 1f);
            var ground = MakeTilemap(grid, "Ground");
            var wall = MakeTilemap(grid, "Wall");
            var water = MakeTilemap(grid, "Unsteppable");
            Set(map, "globalGroundTilemap", ground);
            Set(map, "globalWallTilemap", wall);
            Set(map, "globalUnsteppableTilemap", water);
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.colliderType = Tile.ColliderType.None; // Wall에 콜라이더가 없어도 통과 불가
            for (int y = -6; y < 12; y++)
            for (int x = -6; x < 22; x++)
                ground.SetTile(new Vector3Int(x, y, 0), tile);

            Check(ground.WorldToCell(Offset + new Vector2(2.25f, .25f)) == new Vector3Int(4, 0, 0),
                "실제 0.5 월드유닛 Grid 좌표 변환");
            Vector2 start = Offset + new Vector2(.25f, .25f);
            wall.SetTile(new Vector3Int(4, 0, 0), tile);
            float distance = map.GetDistanceBeforeWall(start, Vector2.right, 5f, .1f);
            Check(distance > 1.6f && distance < 1.66f, "콜라이더 없는 Wall 앞에서 몸통 폭만큼 정지");
            Vector2 landing = SkillCombatUtil.GetSafeDestination(start, Vector2.right, 5f, .1f);
            Check(landing.x < Offset.x + 1.91f, "벽 뒤 Ground로 착지 불가");
            distance = map.GetDistanceBeforeWall(Offset + new Vector2(.25f, .55f), Vector2.right, 5f, .1f);
            Check(distance < 1.66f, "중심선이 빗나가는 Wall 모서리도 몸통으로 검사");
            Vector2 contact = Offset + new Vector2(1.95f, .25f);
            Check(map.GetDistanceBeforeWall(contact, Vector2.right, 2f, .1f) == 0f, "시작 겹침 0은 이동 불가");
            Check(map.GetDistanceBeforeWall(contact, Vector2.left, 2f, .1f) == 2f, "벽에서 멀어지는 대쉬 허용");
            Check(map.GetDistanceBeforeWall(contact, Vector2.up, 2f, .1f) == 2f, "벽과 나란한 대쉬 허용");

            // 다양한 대각선/셀 중심 보정도 반환된 최종 경로가 Wall을 가로질러서는 안 된다.
            for (int i = -12; i <= 12; i++)
            {
                Vector2 direction = new Vector2(1f, i * .08f).normalized;
                landing = SkillCombatUtil.GetSafeDestination(start, direction, 5f, .12f);
                Vector2 travel = landing - start;
                Check((SkillCombatUtil.ClampToWall(start, travel, travel.magnitude, .12f) - landing).sqrMagnitude < .000001f,
                    "대각선/착지 보정 뒤 최종 경로 재검사 " + i);
            }

            wall.ClearAllTiles();
            for (int x = 4; x <= 6; x++)
            {
                ground.SetTile(new Vector3Int(x, 0, 0), null);
                water.SetTile(new Vector3Int(x, 0, 0), tile);
            }
            Check(map.HasUnsteppableBetween(start, Vector2.right, 3f), "물 구간은 대쉬 연장 대상으로 유지");
            landing = SkillCombatUtil.GetSafeDestination(start, Vector2.right, 4.5f, .1f);
            Check(landing.x > Offset.x + 4.7f, "Unsteppable만 건너서 Ground 착지 허용");
            ground.SetTile(new Vector3Int(4, 0, 0), tile);
            Check(map.TryGetGroundLandingPoint(Offset + new Vector2(2.25f, .25f), .1f, out _),
                "Ground 아래 물은 착지 허용");

            CheckDashStep(scene, map, wall, tile, start);
            CheckWallCollider(scene, start);
            Debug.Log("[CombatMovementCheck] PASS: aim lock 0.5s, bounded homing/no reacquire, Wall/corners/snap/water, fixed-step velocity limits.");
        }
        finally
        {
            MapGenerator.Instance = previous;
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previousScene);
            if (tile != null) Object.DestroyImmediate(tile);
        }
    }

    private static void CheckAim()
    {
        foreach (var type in new[] { typeof(BoneMasterAIPatternSO), typeof(BoneMasterPhase2AIPatternSO), typeof(EliteChargerAIPatternSO) })
        {
            var pattern = (BossAIPatternSO)ScriptableObject.CreateInstance(type);
            try
            {
                Check(pattern.aimLockLeadTime == .5f, type.Name + " 기본 조준 고정 0.5초");
                Check(pattern.CanTrackAim(.49f, 1f) && !pattern.CanTrackAim(.5f, 1f), "잠금 경계");
                Check(!pattern.CanTrackAim(0f, .3f) && !pattern.CanTrackAim(0f, .5f), "짧은 예고 처음부터 고정");
                for (int fps = 30; fps <= 120; fps *= 2)
                for (int i = 0; i <= fps; i++)
                    if (i / (float)fps >= .5f) Check(!pattern.CanTrackAim(i / (float)fps, 1f), "프레임률과 무관한 잠금");
            }
            finally { Object.DestroyImmediate(pattern); }
        }
    }

    private static void CheckHoming(Scene scene)
    {
        var projectile = Make(scene, "Fireball").AddComponent<TrackingFireball>();
        var target = Make(scene, "Target").transform;
        Set(projectile, "_direction", Vector2.right, typeof(Projectile));
        Set(projectile, "_targetTransform", target);
        target.position = new Vector2(10f, 10f);
        Call(projectile, "Steer", .1f);
        var direction = (Vector2)Get(projectile, "_direction", typeof(Projectile));
        Check(Vector2.Angle(Vector2.right, direction) > 5.9f && Vector2.Angle(Vector2.right, direction) < 6.1f,
            "유도탄 최대 선회 60도/초");
        target.position = new Vector2(0f, 10f);
        Call(projectile, "Steer", .1f);
        Check(Get(projectile, "_targetTransform") == null, "옆으로 피하면 유도 해제");
        target.position = new Vector2(10f, 0f);
        Call(projectile, "Steer", 1f);
        Check((Vector2)Get(projectile, "_direction", typeof(Projectile)) == direction, "앞으로 돌아와도 재추적/U턴 없음");
        Set(projectile, "_targetTransform", target);
        target.position = Vector2.left * 10f;
        Call(projectile, "Steer", .1f);
        Check(Get(projectile, "_targetTransform") == null, "뒤로 지나친 대상 추적 불가");
    }

    private static void CheckDashStep(Scene scene, MapGenerator map, Tilemap wall, Tile tile, Vector2 start)
    {
        var playerObject = Make(scene, "Dash Player", typeof(Rigidbody2D));
        var rb = playerObject.GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.position = start;
        var player = playerObject.AddComponent<PlayerController>();
        player.enabled = false; // 입력/저장/UI는 실행하지 않고 실제 공용 물리 스텝만 호출
        Set(player, "_rb", rb);
        Vector2 destination = start + new Vector2(.17f, .23f);
        // 다른 열린 씬의 물리를 진행시키지 않는다. 실제 AdvanceDash가 내보낸 속도를 한 틱 적분한다.
        for (int i = 0; i < 4; i++)
        {
            player.AdvanceDash(destination, 15f);
            rb.position += rb.linearVelocity * Time.fixedDeltaTime;
        }
        Check(Vector2.Distance(rb.position, destination) < .001f, "한 틱보다 짧은 대각선 착지점도 정확히 도달");
        Check(rb.linearVelocity.sqrMagnitude < .000001f, "착지 후 초과 속도 없음");

        rb.position = start;
        wall.SetTile(new Vector3Int(4, 0, 0), tile);
        destination = start + Vector2.right * 5f; // 잘못된 목적지가 넘어와도 스텝 자체가 벽을 차단
        for (int i = 0; i < 30; i++)
        {
            player.AdvanceDash(destination, 15f);
            rb.position += rb.linearVelocity * Time.fixedDeltaTime;
            Check(rb.position.x < Offset.x + 1.72f, "실제 물리 틱에서 Wall 통과 불가");
        }
        Check(rb.position.x > Offset.x + 1.65f && rb.linearVelocity.sqrMagnitude < .000001f,
            "벽 앞 정지 후 종료 가능");
        wall.ClearAllTiles();
    }

    private static void CheckWallCollider(Scene scene, Vector2 start)
    {
        var go = Make(scene, "Thin Wall Collider", typeof(BoxCollider2D));
        go.layer = LayerMask.NameToLayer("Wall");
        go.transform.position = Offset + new Vector2(2.037f, .25f);
        go.GetComponent<BoxCollider2D>().size = new Vector2(.005f, 2f);
        Physics2D.SyncTransforms();
        var end = SkillCombatUtil.ClampToWall(start, Vector2.right, 5f, .1f);
        Check(end.x > Offset.x + 1.9f && end.x < Offset.x + 1.94f, "0.005 폭의 순수 물리 Wall도 통과 불가");
        Vector2 contact = Offset + new Vector2(2f, .25f);
        Check(SkillCombatUtil.ClampToWall(contact, Vector2.right, 2f, .1f) == contact, "물리 Wall 시작 겹침은 관통 불가");
        Check(Vector2.Distance(SkillCombatUtil.ClampToWall(contact, Vector2.left, 2f, .1f), contact) > 1.99f,
            "물리 Wall에서 멀어지는 이동은 허용");
    }

    private static GameObject Make(Scene scene, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    private static Tilemap MakeTilemap(GameObject grid, string name)
    {
        var go = new GameObject(name, typeof(Tilemap));
        go.transform.SetParent(grid.transform, false);
        return go.GetComponent<Tilemap>();
    }

    private static void Set(object target, string name, object value, Type type = null)
        => (type ?? target.GetType()).GetField(name, Private).SetValue(target, value);
    private static object Get(object target, string name, Type type = null)
        => (type ?? target.GetType()).GetField(name, Private).GetValue(target);
    private static void Call(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[CombatMovementCheck] FAIL: " + message);
    }
}
