#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

// Optional editing feedback ("손맛"): short flashes and pops while painting, erasing, placing, moving, deleting and undoing.
// On by default (J turns it off); every effect ends within about 0.2 seconds and nothing runs while it is off.
internal sealed partial class WorkshopPreview
{
    private const string JuiceKey = "Workshop.Juice";
    internal static bool Juice
    {
        get => PlayerPrefs.GetInt(JuiceKey, 1) == 1;
        set { PlayerPrefs.SetInt(JuiceKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    private const int MaxBurst = 600, RippleAbove = 25;
    private static readonly Color PaintFlash = new(1f, 1f, 1f, .5f);
    private static readonly Color ErasePuff = new(1f, 1f, 1f, .65f);
    private static readonly Color UndoFlash = new(.57f, .92f, .79f, .55f);
    private static readonly Color PopSpark = new(1f, .97f, .88f, .9f);
    private static readonly Color PopPuff = new(1f, 1f, 1f, .28f);
    private const int MaxPieceEffects = 60;

    private struct Effect
    {
        public Transform node, piece;
        public float start, life, power;
        public bool squash;
        public Vector3 from, to, pieceScale, origin, drift;
        public Color color;
        public SpriteRenderer[] fade;
        public Color[] tint;
    }

    // What a placed object looked like just before the room was redrawn, so a removed one can still pop away.
    internal struct Ghost
    {
        public Sprite sprite;
        public SpriteDrawMode drawMode;
        public Vector2 size;
        public Vector3 position, scale;
        public Quaternion rotation;
        public Color color;
        public int sortingLayer, order;
        public bool flipX, flipY;
    }
    internal sealed class PieceSnapshot
    {
        internal readonly Dictionary<string, (Bounds bounds, Ghost[] art)> shown = new();
    }
    private readonly List<Effect> effects = new();
    private readonly Stack<Transform> effectPool = new();
    private bool listening;
    internal Vector3 FeedbackOrigin;
    internal int ActiveEffects => effects.Count;

    internal void ListenForEdits()
    {
        if (listening) return;
        Tilemap.tilemapTileChanged += OnTilesChanged;
        listening = true;
    }

    private void StopFeedback()
    {
        if (listening) Tilemap.tilemapTileChanged -= OnTilesChanged;
        listening = false;
        foreach (var e in effects)
        {
            if (e.piece != null) e.piece.localScale = e.pieceScale;
            if (e.fade != null && e.node != null) Object.Destroy(e.node.gameObject);
        }
        effects.Clear(); effectPool.Clear();
    }

    private void OnTilesChanged(Tilemap map, Tilemap.SyncTile[] tiles)
    {
        if (!disposed) MarkCoverageDirty(map, tiles);
        if (!Juice || disposed || tiles == null || tiles.Length == 0 || System.Array.IndexOf(maps, map) < 0) return;
        IEnumerable<Tilemap.SyncTile> batch = tiles;
        var origin = map.WorldToCell(FeedbackOrigin);
        if (tiles.Length > MaxBurst) batch = tiles.OrderBy(t => (t.position - origin).sqrMagnitude).Take(MaxBurst);
        // Big batches (fill, rectangle, stamp) ripple outward from the cursor; one brush dab (up to 5x5) flashes at once.
        float reach = 0;
        bool ripple = tiles.Length > RippleAbove;
        if (ripple) foreach (var t in batch) reach = Mathf.Max(reach, Vector3Int.Distance(t.position, origin));
        foreach (var t in batch)
        {
            var (a, b) = CellCornersOf(map, t.position);
            var center = (a + b) * .5f; var size = b - a;
            float delay = ripple && reach > 0 ? .08f * Vector3Int.Distance(t.position, origin) / reach : 0;
            if (t.tile == null) Spawn(center, size * .55f, size * 1.25f, ErasePuff, .16f, delay);
            else Spawn(center, size, size, PaintFlash, .14f, delay);
        }
    }

    // A placed enemy or prop hops once: taller, then back to its exact scale.
    internal void Bounce(string key) => Wobble(key, false, 1, .16f);

    // A moved object lands: a short squash, then back to its exact scale. Smaller for arrow-key nudges.
    internal void Land(string key, float power = 1) => Wobble(key, true, power, .14f);

    private void Wobble(string key, bool squash, float power, float life)
    {
        if (!Juice || disposed || !pieces.TryGetValue(key, out var piece) || piece == null) return;
        // A new wobble replaces one still playing, keeping the true scale so repeats never drift.
        var scale = piece.localScale;
        int playing = effects.FindIndex(e => e.piece == piece);
        if (playing >= 0) { scale = effects[playing].pieceScale; effects.RemoveAt(playing); }
        effects.Add(new Effect { piece = piece, pieceScale = scale, start = Time.unscaledTime, life = life, squash = squash, power = power });
    }

    // Taken just before a rebuild; costs nothing while the effect is off.
    internal PieceSnapshot SnapshotPieces()
    {
        if (!Juice || disposed) return null;
        var shot = new PieceSnapshot();
        foreach (var p in pieces)
        {
            if (p.Value == null || !p.Value.gameObject.activeInHierarchy || !artwork.TryGetValue(p.Key, out var art)) continue;
            shot.shown[p.Key] = (PieceBounds(p.Key), art.Where(r => r != null && r.sprite != null && Visible(r)).Select(r => new Ghost
            {
                sprite = r.sprite, drawMode = r.drawMode, size = r.size, position = r.transform.position, scale = r.transform.lossyScale,
                rotation = r.transform.rotation, color = r.color, sortingLayer = r.sortingLayerID, order = r.sortingOrder,
                flipX = r.flipX, flipY = r.flipY
            }).ToArray());
        }
        return shot;
    }

    // After a rebuild: objects that disappeared pop away, objects that appeared hop in.
    internal void ReactToRebuild(PieceSnapshot before)
    {
        if (before == null || !Juice || disposed) return;
        int popped = 0, hopped = 0;
        foreach (var old in before.shown)
            if ((!pieces.TryGetValue(old.Key, out var now) || now == null || !now.gameObject.activeInHierarchy) && popped++ < MaxPieceEffects)
                Pop(old.Value.bounds, old.Value.art);
        foreach (var p in pieces)
            if (p.Value != null && p.Value.gameObject.activeInHierarchy && !before.shown.ContainsKey(p.Key) && hopped++ < MaxPieceEffects)
                Bounce(p.Key);
    }

    // "뿅": the old drawing swells a little, then shrinks and fades while a few sparks fly out.
    private void Pop(Bounds bounds, Ghost[] art)
    {
        var center = new Vector3(bounds.center.x, bounds.center.y, 0);
        var renderers = new List<SpriteRenderer>();
        if (art.Length > 0)
        {
            var root = new GameObject("Pop ghost") { layer = Layer }.transform;
            root.position = center;
            root.SetParent(stage.transform, true);
            foreach (var g in art)
            {
                var go = new GameObject("Ghost art") { layer = Layer };
                go.transform.SetPositionAndRotation(g.position, g.rotation);
                go.transform.localScale = g.scale;
                go.transform.SetParent(root, true);
                var r = go.AddComponent<SpriteRenderer>();
                // The old drawing's materials die with it, so the ghost uses this drawing's material for the same sheet.
                r.sprite = g.sprite; r.sharedMaterial = SpriteMaterial(g.sprite.texture); r.color = g.color;
                r.drawMode = g.drawMode; if (g.drawMode != SpriteDrawMode.Simple) r.size = g.size;
                r.sortingLayerID = g.sortingLayer; r.sortingOrder = g.order; r.flipX = g.flipX; r.flipY = g.flipY;
                renderers.Add(r);
            }
            effects.Add(new Effect { node = root, pieceScale = root.localScale, start = Time.unscaledTime, life = .2f,
                fade = renderers.ToArray(), tint = renderers.Select(r => r.color).ToArray() });
        }
        var size = new Vector3(Mathf.Max(bounds.size.x, .1f), Mathf.Max(bounds.size.y, .1f), 1);
        Spawn(center, size * .4f, size * 1.1f, PopPuff, .16f, .06f);
        float reach = Mathf.Max(size.x, size.y) * .75f, spark = Mathf.Min(size.x, size.y) * .14f;
        float turn = Random.value * 60f;
        for (int i = 0; i < 6; i++)
        {
            float angle = (turn + i * 60f + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
            var drift = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * reach * Random.Range(.7f, 1f);
            Spawn(center, Vector3.one * spark, Vector3.one * spark * .3f, PopSpark, .22f, .03f, drift);
        }
    }

    // After undo or redo: tiles and objects that changed light up briefly so the change is easy to spot.
    internal void FlashDifference(WorkshopRoom before, WorkshopRoom after)
    {
        if (!Juice || before == null || after == null) return;
        var old = before.cells.ToDictionary(c => (c.layer, c.at), c => (c.tile, c.style));
        var now = after.cells.ToDictionary(c => (c.layer, c.at), c => (c.tile, c.style));
        int shown = 0;
        foreach (var key in old.Keys.Union(now.Keys))
        {
            old.TryGetValue(key, out var x); now.TryGetValue(key, out var y);
            if (x == y || shown++ >= MaxBurst) continue;
            var map = maps[Mathf.Clamp(key.layer, 0, maps.Length - 1)] ?? Ground;
            var (a, b) = CellCornersOf(map, (Vector3Int)key.at);
            Spawn((a + b) * .5f, b - a, b - a, UndoFlash, .2f, 0);
        }
        var oldPieces = before.pieces.ToDictionary(p => p.id);
        foreach (var p in after.pieces)
        {
            if (oldPieces.TryGetValue(p.id, out var q) && q.local == p.local && q.removed == p.removed && q.catalog == p.catalog) continue;
            if (p.removed || !pieces.ContainsKey(p.id)) continue;
            var bounds = PieceBounds(p.id);
            Spawn(bounds.center, bounds.size, bounds.size, UndoFlash, .2f, 0);
            Bounce(p.id);
        }
    }

    private void Spawn(Vector3 center, Vector3 from, Vector3 to, Color color, float life, float delay, Vector3 drift = default)
    {
        var node = effectPool.Count > 0 ? effectPool.Pop() : AddMarker(stage.transform, Color.white, 1);
        node.name = "Edit feedback";
        node.gameObject.SetActive(false);
        node.position = new Vector3(center.x, center.y, node.position.z);
        node.GetComponent<SpriteRenderer>().sortingOrder = 14960;
        effects.Add(new Effect { node = node, from = from, to = to, color = color, start = Time.unscaledTime + delay, life = life,
            origin = node.position, drift = drift });
    }

    // Called every frame by the window; cheap when nothing is playing.
    internal void TickFeedback()
    {
        if (effects.Count == 0) return;
        float now = Time.unscaledTime;
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            var e = effects[i];
            float p = (now - e.start) / e.life;
            if (e.piece != null)
            {
                if (p >= 1 || !Juice) { e.piece.localScale = e.pieceScale; effects.RemoveAt(i); continue; }
                float hop = Mathf.Sin(Mathf.PI * Mathf.Clamp01(p)) * e.power;
                var shape = e.squash ? new Vector3(1 + .14f * hop, 1 - .16f * hop, 1) : new Vector3(1 - .08f * hop, 1 + .2f * hop, 1);
                e.piece.localScale = Vector3.Scale(e.pieceScale, shape);
                continue;
            }
            if (e.fade != null)
            {
                if (p >= 1 || e.node == null) { if (e.node != null) Object.Destroy(e.node.gameObject); effects.RemoveAt(i); continue; }
                // Swell to 118% in the first third, then shrink to nothing while fading out.
                float t = Mathf.Clamp01(p), late = Mathf.Clamp01((t - .3f) / .7f);
                float grow = t < .3f ? 1 + .18f * (t / .3f) : 1.18f * (1 - late);
                e.node.localScale = Vector3.Scale(e.pieceScale, new Vector3(grow * (1 + .1f * late), grow * (1 - .1f * late), 1));
                for (int k = 0; k < e.fade.Length; k++)
                    if (e.fade[k] != null) { var tint = e.tint[k]; tint.a *= 1 - late * late; e.fade[k].color = tint; }
                continue;
            }
            if (e.node == null) { effects.RemoveAt(i); continue; }
            if (p >= 1)
            {
                e.node.gameObject.SetActive(false); effectPool.Push(e.node); effects.RemoveAt(i); continue;
            }
            if (p < 0) continue;
            e.node.gameObject.SetActive(true);
            if (e.drift != Vector3.zero) e.node.position = e.origin + e.drift * (1 - (1 - p) * (1 - p));
            var size = Vector3.Lerp(e.from, e.to, p);
            e.node.localScale = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), 1);
            var c = e.color; c.a *= 1 - p;
            e.node.GetComponent<SpriteRenderer>().color = c;
        }
    }

    private static (Vector3 low, Vector3 high) CellCornersOf(Tilemap map, Vector3Int cell)
    {
        var a = map.CellToWorld(cell);
        var b = map.CellToWorld(cell + new Vector3Int(1, 1, 0));
        return (Vector3.Min(a, b), Vector3.Max(a, b));
    }
}
#endif
