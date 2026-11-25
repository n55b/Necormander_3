using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Village ground owns a flock for the visit. Dungeon rooms keep their flock's state on revisits.
[DisallowMultipleComponent]
public sealed class AmbientBirdFlock : MonoBehaviour
{
    private const string BirdPath = "Wildlife/DuskFinch";
    private const float FlushRadius = 3.65f;
    private const float AlertSeconds = .10f;
    private const float AccelerationSeconds = .18f;
    private const float FlightSeconds = 2.8f;
    private enum Pose { Idle, Peck, Alert, Takeoff, Fly }

    private sealed class Bird
    {
        public Transform foot;
        public SpriteRenderer picture;
        public Animator animator;
        public AirborneShadow shadow;
        public Vector2 floor, velocity;
        public float timer, airborneTime, speed;
        public Pose pose;
    }

    private static readonly int[] States = { Animator.StringToHash("Idle"), Animator.StringToHash("Peck"),
        Animator.StringToHash("Alert"), Animator.StringToHash("Takeoff"), Animator.StringToHash("Fly") };
    private readonly List<Bird> birds = new(6);
    private readonly Collider2D[] blocker = new Collider2D[1];
    private readonly float[] lengths = new float[5];
    private System.Random random;
    private RoomInstance room;
    private Tilemap ground, walls, blocked;
    private GameObject group;
    private bool placed, flushing, departed, newVillagePatch;
    private float nextPlacement;
    private Vector2 center, lastSearch, lastRoost;
    private int searches;

    internal static void Visit(RoomInstance room, int? layoutSeed)
    {
        if (room == null || !Application.isPlaying || room.groundTilemap == null) return;
        if (room.TryGetComponent<AmbientBirdFlock>(out var existing)) { existing.SetPresent(true); return; }
        if (room.roomType != RoomType.Normal && room.roomType != RoomType.Spawn && room.roomType != RoomType.Reward) return;
        var cell = Vector2Int.RoundToInt(room.transform.position);
        int seed = unchecked((layoutSeed ?? room.gameObject.scene.handle) ^ cell.x * 73856093 ^ cell.y * 19349663);
        var random = new System.Random(seed);
        if (random.NextDouble() >= .42) return;
        var flock = room.gameObject.AddComponent<AmbientBirdFlock>();
        flock.room = room;
        flock.random = random;
        flock.ground = room.groundTilemap;
        flock.walls = room.wallTilemap;
        flock.blocked = room.unsteppableTilemap;
    }

    internal void SetPresent(bool present)
    {
        if (departed) return;
        // A fleeing flock has already left this room when the player returns.
        if (!present && flushing) { Depart(); return; }
        if (group != null) group.SetActive(present);
        enabled = present;
    }

    private void Start()
    {
        if (room != null) return;
        ground = GetComponent<Tilemap>();
        if (ground == null) { enabled = false; return; }
        walls = ground.transform.parent.Find("Wall")?.GetComponent<Tilemap>();
        blocked = ground.transform.parent.Find("Unsteppable")?.GetComponent<Tilemap>();
        random = new System.Random(unchecked(Environment.TickCount ^ gameObject.scene.handle));
    }

    private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

    private bool CanStand(Vector2 point)
    {
        if (!ground.HasTile(ground.WorldToCell(point)) ||
            (walls != null && walls.HasTile(walls.WorldToCell(point))) ||
            (blocked != null && blocked.HasTile(blocked.WorldToCell(point))) ||
            (room != null && room.IsInDoorKeepOut(point, 3f, 2f, 1f))) return false;
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(Layers.WallMask | Layers.UnsteppableMask | Layers.ObjectMask | (1 << Layers.Interactable));
        return Physics2D.OverlapCircle(point, .16f, filter, blocker) == 0;
    }

    private static bool OutsideFrame(Camera camera, Vector2 point)
    {
        if (camera == null) return false;
        var view = camera.WorldToViewportPoint(point);
        return view.x < -.04f || view.x > 1.04f || view.y < -.05f || view.y > 1.05f;
    }

    private void TryPlace(Vector2 player)
    {
        // A fully blocked room should never keep querying colliders. In the village
        // try again only after the player reaches another patch of terrain.
        if (newVillagePatch && (player - lastRoost).sqrMagnitude < 64f) { nextPlacement = Time.time + 1f; return; }
        if (searches > 0 && (player - lastSearch).sqrMagnitude < 1f) { nextPlacement = Time.time + 1f; return; }
        if (room != null && searches >= 3) { departed = true; enabled = false; return; }
        searches++;
        lastSearch = player;
        nextPlacement = Time.time + 2f;
        Vector2 anchor = room != null ? (Vector2)room.transform.position + room.centerOffset : player;
        var camera = PixelArtUpscaleRig.RenderCamera;
        Vector2 ahead = (player - lastRoost).normalized;
        var points = new List<Vector2>(6);
        int wanted = random.Next(3, 7);
        for (int attempt = 0; attempt < 24 && points.Count < 3; ++attempt)
        {
            points.Clear();
            float angle = Range(0, Mathf.PI * 2);
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float radius = Range(room == null ? FlushRadius + .9f : 2.8f, room == null ? FlushRadius + 2.6f : 5.1f);
            if (newVillagePatch)
            {
                if (camera == null) return;
                direction = (ahead + direction * .85f).normalized;
                radius = camera.orthographicSize * Range(1.15f, 2.15f);
            }
            center = anchor + direction * radius;
            if (!CanStand(center)) continue;
            for (int i = 0; i < 22 && points.Count < wanted; i++)
            {
                Vector2 point = Snap(center + new Vector2(Range(-.85f,.85f), Range(-.55f,.55f)));
                if (newVillagePatch && !OutsideFrame(camera, point)) continue;
                if ((point - player).sqrMagnitude < (FlushRadius + .35f) * (FlushRadius + .35f) || !CanStand(point)) continue;
                bool spaced = true;
                foreach (var other in points) if ((other - point).sqrMagnitude < .40f * .40f) { spaced = false; break; }
                if (spaced) points.Add(point);
            }
        }
        if (points.Count < 3) return;
        var prefab = Resources.Load<GameObject>(BirdPath);
        var controller = prefab != null ? prefab.GetComponent<Animator>()?.runtimeAnimatorController : null;
        if (controller == null) { Debug.LogError("Bird animation prefab missing: " + BirdPath, this); enabled = false; return; }
        foreach (var clip in controller.animationClips)
            if (Enum.TryParse(clip.name, out Pose pose) && (uint)pose < lengths.Length) lengths[(int)pose] = clip.length;
        group = new GameObject("Dusk finch flock");
        group.transform.SetParent(transform, false);
        // Room anchors and village grids can carry authored transforms. Bird art uses world units.
        group.transform.localScale = new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1);
        foreach (var point in points)
        {
            var foot = new GameObject("Finch ground contact").transform;
            foot.SetParent(group.transform, false);
            foot.position = point;
            var visual = Instantiate(prefab, foot);
            var picture = visual.GetComponent<SpriteRenderer>();
            var sort = visual.AddComponent<YSortableObject>();
            sort.SetPivot(foot);
            sort.CastsGroundShadow = false;
            var bird = new Bird { foot = foot, picture = picture, animator = visual.GetComponent<Animator>(),
                shadow = visual.AddComponent<AirborneShadow>(), floor = point, speed = Range(7f, 8.6f) };
            picture.flipX = random.Next(2) == 0;
            bird.shadow.SetFloor(point);
            birds.Add(bird);
            Play(bird, Pose.Idle, Range(0, 1));
        }
        placed = true;
    }

    private void Play(Bird bird, Pose pose, float phase = 0)
    {
        bird.pose = pose;
        bird.animator.speed = pose switch
        {
            Pose.Alert => Mathf.Max(1f, lengths[(int)pose] / AlertSeconds),
            Pose.Takeoff => 1.45f,
            Pose.Fly => 1.18f,
            _ => 1f
        };
        bird.animator.Play(States[(int)pose], 0, phase);
        bird.timer = pose == Pose.Idle ? Range(.55f, 2.3f) : lengths[(int)pose] / bird.animator.speed;
    }

    private void Update()
    {
        var player = PolishFeedback.Player;
        if (player == null || Time.deltaTime <= 0 || player.IsInputBlocked || PlayerController.IsGameplayPaused) return;
        Vector2 feet = player.transform.position;
        if (!placed) { if (Time.time >= nextPlacement) TryPlace(feet); return; }
        if (!flushing)
            foreach (var bird in birds)
                if ((bird.floor - feet).sqrMagnitude <= FlushRadius * FlushRadius) { Flush(feet); break; }
        float dt = Time.deltaTime;
        bool allGone = true;
        foreach (var bird in birds)
        {
            bird.timer -= dt;
            if (!flushing)
            {
                if (bird.timer <= 0) Play(bird, bird.pose == Pose.Idle ? Pose.Peck : Pose.Idle);
                continue;
            }
            if (bird.pose == Pose.Alert && bird.timer <= 0) Play(bird, Pose.Takeoff);
            if (bird.pose == Pose.Takeoff && bird.timer <= 0) Play(bird, Pose.Fly);
            if (bird.pose == Pose.Alert) { allGone = false; continue; }
            bird.airborneTime += dt;
            float t = bird.airborneTime;
            bird.floor += bird.velocity * (bird.speed * Mathf.SmoothStep(.36f, 1f, t / AccelerationSeconds) * dt);
            var floor = Snap(bird.floor);
            bird.foot.position = floor;
            float height = 1.55f * (1f - Mathf.Exp(-4f * t)) + t * .8f;
            bird.picture.transform.position = Snap(bird.floor + Vector2.up * height);
            bird.shadow.SetFloor(floor);
            Color color = bird.picture.color;
            color.a = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(FlightSeconds - .45f, FlightSeconds, t));
            bird.picture.color = color;
            if (t < FlightSeconds) allGone = false;
        }
        if (flushing && allGone) Depart();
    }

    private void Flush(Vector2 player)
    {
        flushing = true;
        Vector2 away = (center - player).normalized;
        float nearest = float.PositiveInfinity;
        foreach (var bird in birds) nearest = Mathf.Min(nearest, Vector2.Distance(player, bird.floor));
        foreach (var bird in birds)
        {
            bird.velocity = (away + (bird.floor - center) * .6f + new Vector2(Range(-.22f,.22f),Range(-.22f,.22f))).normalized;
            bird.picture.flipX = bird.velocity.x < 0;
            Play(bird, Pose.Alert);
            bird.timer += Mathf.Min(.065f, (Vector2.Distance(player, bird.floor) - nearest) * .04f);
        }
    }

    private static Vector2 Snap(Vector2 point)
    {
        int ppu = Mathf.Max(1, CameraSettingsSO.Instance != null ? CameraSettingsSO.Instance.assetsPPU : 32);
        return new Vector2(Mathf.Round(point.x * ppu), Mathf.Round(point.y * ppu)) / ppu;
    }

    private void Depart()
    {
        if (group != null) Destroy(group);
        birds.Clear();
        if (room != null) { departed = true; enabled = false; return; }
        lastRoost = center;
        newVillagePatch = true;
        searches = 0;
        flushing = placed = false;
        nextPlacement = Time.time + 12f;
    }
}
