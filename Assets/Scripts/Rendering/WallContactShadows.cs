using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

// The red channel stores wall height in 1/32 cells. Green locates the visible foot inside its tile.
[DisallowMultipleComponent]
public sealed class WallContactShadows : MonoBehaviour
{
    private static readonly Vector3Int[] Neighbours =
        { Vector3Int.right, Vector3Int.up, Vector3Int.left, Vector3Int.down };
    private const int ChunkSize = 32;
    private readonly List<Mesh> meshes = new();
    private Transform surfaces;
    private Material material;
    private Texture2D relief;
    private TilemapRenderer paintedRenderer;
    private bool paintedWasVisible;
    public int ReceiverCount { get; private set; }

    internal static void BindScene(Tilemap ground)
    {
        if (ground == null || ground.GetComponent<WallContactShadows>() != null || ground.transform.parent == null) return;
        var grid = ground.transform.parent;
        var wall = grid.Find("Wall")?.GetComponent<Tilemap>();
        var painted = grid.Find("Shadow")?.GetComponent<Tilemap>();
        if (wall != null) Rebuild(ground, wall, painted);
    }

    internal static void Rebuild(Tilemap ground, Tilemap wall, Tilemap painted)
    {
        if (ground == null || wall == null || !Application.isPlaying) return;
        if (!ground.TryGetComponent<WallContactShadows>(out var field))
            field = ground.gameObject.AddComponent<WallContactShadows>();
        var build = field.Build(ground, wall, painted, false);
        while (build.MoveNext()) { }
    }

    internal static IEnumerator BuildForFloor(Tilemap ground, Tilemap wall, Tilemap painted)
    {
        if (ground == null || wall == null || !Application.isPlaying) yield break;
        if (!ground.TryGetComponent<WallContactShadows>(out var field))
            field = ground.gameObject.AddComponent<WallContactShadows>();
        yield return field.Build(ground, wall, painted, true);
    }

    private void Awake()
    {
        surfaces = new GameObject("Wall contact shadows").transform;
        surfaces.SetParent(transform, false);
        material = new Material(Resources.Load<Shader>("WorldAtmosphere/TerrainContactShadow"));
        material.SetFloat("_ProceduralEdges", 1);
    }

    private void ClearChunks()
    {
        foreach (var mesh in meshes) Destroy(mesh);
        meshes.Clear();
        for (int i = surfaces.childCount - 1; i >= 0; i--)
        {
            var chunk = surfaces.GetChild(i).gameObject;
            chunk.SetActive(false);
            Destroy(chunk);
        }
        ReceiverCount = 0;
    }

    private IEnumerator Build(Tilemap ground, Tilemap wall, Tilemap painted, bool staged)
    {
        const double sliceSeconds = .002;
        double deadline = Time.realtimeSinceStartupAsDouble + sliceSeconds;
        ClearChunks();
        if (relief != null)
        {
            material.SetTexture("_Relief", null);
            Destroy(relief);
            relief = null;
        }
        RestorePainted();
        if (!material.shader.isSupported) yield break;
        var profile = Resources.Load<TerrainReliefProfile>("WorldAtmosphere/TerrainRelief");
        var bounds = wall.cellBounds;
        if (profile == null || bounds.size.x == 0 || bounds.size.y == 0) yield break;
        const int reach = 3;
        int width = bounds.size.x + reach * 2, height = bounds.size.y + reach * 2;
        var first = bounds.min - new Vector3Int(reach, reach, 0);
        int Index(Vector3Int cell) => (cell.y-first.y)*width + cell.x-first.x;
        var data = new Color32[width * height];
        var receivers = new List<Vector3Int>();
        var occupied = new List<(Vector3Int cell, bool foot)>();
        int w = bounds.size.x;
        int rows = Mathf.Clamp(2048 / w, 1, bounds.size.y);
        var tiles = System.Buffers.ArrayPool<TileBase>.Shared.Rent(w * rows);
        try
        {
            for (int z = bounds.zMin; z < bounds.zMax; z++)
            for (int y = bounds.yMin; y < bounds.yMax; y += rows)
            {
                int h = Mathf.Min(rows, bounds.yMax - y);
                wall.GetTilesBlockNonAlloc(new BoundsInt(bounds.xMin, y, z, w, h, 1), tiles);
                for (int i = 0; i < w * h; i++)
                {
                    if (staged && (i & 127) == 0 && Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        yield return null;
                        deadline = Time.realtimeSinceStartupAsDouble + sliceSeconds;
                    }
                    var tile = tiles[i];
                    if (tile == null || tile is WorkshopTerrainTile { Kind: WorkshopTerrainTile.Surface.Shore }) continue;
                    var cell = new Vector3Int(bounds.xMin + i % w, y + i / w, z);
                    bool foot = tile == profile.cliffFoot;
                    float elevation = profile.wallHeight;
                    bool front = foot || tile == profile.cliffFace;
                    if (front)
                    {
                        // Repeated front-face rows are the authored vertical drop, measured up from the foot.
                        for (int row = 1; row <= 5 && wall.GetTile(cell + Vector3Int.up * row) == profile.cliffFace; row++)
                            elevation += 1;
                    }
                    occupied.Add((cell, foot));
                    // Front faces receive their own shading even when surrounded by other cliff tiles.
                    if (front) receivers.Add(cell);
                    data[Index(cell)] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(elevation * 32), 1, 255),
                        foot ? (byte)Mathf.RoundToInt(profile.footInset * 255) : (byte)0, front ? (byte)1 : (byte)0, front ? (byte)255 : (byte)0);
                }
            }
        }
        finally { System.Buffers.ArrayPool<TileBase>.Shared.Return(tiles, clearArray: true); }
        if (occupied.Count == 0) yield break;
        for (int i = 0; i < occupied.Count; ++i)
        {
            if (staged && (i & 63) == 0 && Time.realtimeSinceStartupAsDouble >= deadline)
            {
                yield return null;
                deadline = Time.realtimeSinceStartupAsDouble + sliceSeconds;
            }
            var (cell, boundary) = occupied[i];
            foreach (var step in Neighbours) boundary |= data[Index(cell + step)].r == 0;
            if (!boundary) continue;
            for (int y = -reach; y <= reach; y++)
            for (int x = -reach; x <= reach; x++)
            {
                var next = cell + new Vector3Int(x,y,0);
                int index = Index(next);
                var sample = data[index];
                // Use the scratch elevation grid for membership so overlapping wall edges add each cell once.
                if (sample.b != 0 || (sample.r != 0 && sample.g == 0)) continue;
                sample.b = 1;
                data[index] = sample;
                receivers.Add(next);
            }
        }
        relief = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            { name = "Terrain elevations", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        material.SetTexture("_Relief", relief);
        material.SetVector("_ReliefSize", new Vector4(width, height, 1f / width, 1f / height));
        var axisX = wall.CellToWorld(Vector3Int.right) - wall.CellToWorld(Vector3Int.zero);
        var axisY = wall.CellToWorld(Vector3Int.up) - wall.CellToWorld(Vector3Int.zero);
        float det = axisX.x * axisY.y - axisX.y * axisY.x;
        if (Mathf.Abs(det) < .00001f) yield break;
        material.SetVector("_ReliefBasis", new Vector4(axisY.y, -axisY.x, -axisX.y, axisX.x) * (axisY.magnitude / det));
        var chunks = new Dictionary<Vector2Int, List<Vector3Int>>();
        int visited = 0;
        foreach (var cell in receivers)
        {
            if (staged && (++visited & 63) == 0 && Time.realtimeSinceStartupAsDouble >= deadline)
            {
                yield return null;
                deadline = Time.realtimeSinceStartupAsDouble + sliceSeconds;
            }
            int index = Index(cell);
            var sample = data[index];
            sample.b = 0;
            data[index] = sample;
            if (sample.a == 0 && !ground.HasTile(ground.WorldToCell(wall.GetCellCenterWorld(cell))) &&
                (painted == null || !painted.HasTile(painted.WorldToCell(wall.GetCellCenterWorld(cell))))) continue;
            var key = new Vector2Int((cell.x - first.x) / ChunkSize, (cell.y - first.y) / ChunkSize);
            if (!chunks.TryGetValue(key, out var cellsInChunk))
                chunks.Add(key, cellsInChunk = new List<Vector3Int>());
            cellsInChunk.Add(cell);
            ReceiverCount++;
        }
        relief.SetPixels32(data);
        relief.Apply(false, true);

        // Separate renderer bounds let Unity cull remote receivers before the lighting vertex pass.
        var points = new List<Vector3>();
        var cells = new List<Vector2>();
        var indices = new List<int>();
        foreach (var chunk in chunks.Values)
        {
            if (staged && Time.realtimeSinceStartupAsDouble >= deadline)
            {
                yield return null;
                deadline = Time.realtimeSinceStartupAsDouble + sliceSeconds;
            }
            points.Clear(); cells.Clear(); indices.Clear();
            foreach (var cell in chunk)
            {
                if (staged && (points.Count & 255) == 0 && Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    yield return null;
                    deadline = Time.realtimeSinceStartupAsDouble + sliceSeconds;
                }
                int start = points.Count;
                Vertex(cell);
                Vertex(cell + Vector3Int.right);
                Vertex(cell + Vector3Int.right + Vector3Int.up);
                Vertex(cell + Vector3Int.up);
                indices.Add(start); indices.Add(start+1); indices.Add(start+2);
                indices.Add(start); indices.Add(start+2); indices.Add(start+3);
            }
            var mesh = new Mesh { name = "Exposed wall edges" };
            meshes.Add(mesh);
            mesh.SetVertices(points); mesh.SetUVs(0, cells); mesh.SetTriangles(indices, 0);
            mesh.UploadMeshData(true);
            var surface = new GameObject("Shadow receivers", typeof(MeshFilter), typeof(MeshRenderer));
            surface.transform.SetParent(surfaces, false);
            surface.layer = gameObject.layer;
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingLayerName = "Shadow";
            renderer.sortingOrder = 1;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        void Vertex(Vector3Int corner)
        {
            points.Add(transform.InverseTransformPoint(wall.CellToWorld(corner)));
            cells.Add(new Vector2(corner.x-first.x, corner.y-first.y));
        }
        if (ReceiverCount > 0 && painted != null && painted.TryGetComponent(out paintedRenderer))
        {
            paintedWasVisible = paintedRenderer.enabled;
            paintedRenderer.enabled = !isActiveAndEnabled && paintedWasVisible;
        }
    }

    private void RestorePainted()
    {
        if (paintedRenderer != null) paintedRenderer.enabled = paintedWasVisible;
        paintedRenderer = null;
    }

    private void OnEnable()
    {
        if (surfaces != null) surfaces.gameObject.SetActive(true);
        if (paintedRenderer != null) paintedRenderer.enabled = false;
    }

    private void OnDisable()
    {
        if (surfaces != null) surfaces.gameObject.SetActive(false);
        if (paintedRenderer != null) paintedRenderer.enabled = paintedWasVisible;
    }

    private void OnDestroy()
    {
        RestorePainted();
        if (relief != null) Destroy(relief);
        foreach (var mesh in meshes) Destroy(mesh);
        if (surfaces != null) Destroy(surfaces.gameObject);
        Destroy(material);
    }
}
