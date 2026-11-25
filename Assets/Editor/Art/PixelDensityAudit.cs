using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// The imported sprite rectangle, the entire transform chain and the camera determine a visible texel.
public static class PixelDensityAudit
{
    [Serializable] private sealed class Source { public string source, asset; public float ppu; }
    [Serializable] private sealed class Manifest { public Source[] entries; }
    [Serializable] private sealed class Art
    {
        public string source, asset, guid, reachability, importer, sampling, priority, reproduce;
        public float recordedPPU, importedPPU;
        public string[] clips, scaleKeys;
    }
    [Serializable] private sealed class Instance
    {
        public string owner, hierarchy, role, sprite, guid, source, material, assessment;
        public string scaleChain, layout;
        public long localID;
        public Vector2 pixelsPerTexel;
        public float ppu;
        public bool active;
    }
    [Serializable] private sealed class Report
    {
        public string unity;
        public float referenceLens;
        public List<Art> sources = new();
        public List<Instance> instances = new();
    }
    private const string ManifestPath = "ARTIST/manifest.json";
    private static string Output => Path.Combine(ProjectVerification.Output, "pixel-density.json");

    public static void Run()
    {
        VerifyControls();
        var sources = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)).entries;
        var map = sources.GroupBy(s => s.asset).ToDictionary(g => g.Key, g => g.First());
        var build = new HashSet<string>(AssetDatabase.GetDependencies(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray()));
        var resources = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") &&
            p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
        var runtime = new HashSet<string>(AssetDatabase.GetDependencies(resources));
        var authored = new HashSet<string>(AssetDatabase.GetDependencies(Paths("t:Prefab").ToArray()));
        var report = new Report { unity = Application.unityVersion, referenceLens = CameraSettingsSO.Instance.orthographicSize };
        var clips = new Dictionary<string, HashSet<string>>();
        var scaleKeys = new Dictionary<string,HashSet<string>>();
        foreach (var path in Paths("t:AnimationClip"))
        {
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (binding.propertyName != "m_Sprite") continue;
                    foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                    {
                        if (!(key.value is Sprite sprite)) continue;
                        string asset = AssetDatabase.GetAssetPath(sprite);
                        if (!clips.TryGetValue(asset, out var uses)) clips.Add(asset, uses = new HashSet<string>());
                        if (!uses.Add(path + " | " + clip.name + " | " + binding.path)) continue;
                        if (!scaleKeys.TryGetValue(asset,out var scales)) scaleKeys.Add(asset,scales=new HashSet<string>());
                        foreach (var curve in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (!curve.propertyName.StartsWith("m_LocalScale.") || !(binding.path == curve.path || curve.path == "" || binding.path.StartsWith(curve.path+"/"))) continue;
                            var keys = AnimationUtility.GetEditorCurve(clip,curve).keys;
                            scales.Add(clip.name+" "+curve.path+" "+curve.propertyName+" "+string.Join(",",keys.Select(k=>$"{k.time:G4}s={k.value:G4}")));
                        }
                    }
                }
        }
        foreach (var item in sources)
        {
            var importer = AssetImporter.GetAtPath(item.asset);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(item.asset);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(item.asset);
            var textureImporter = importer as TextureImporter;
            var audit = new Art { source = item.source, asset = item.asset, guid = AssetDatabase.AssetPathToGUID(item.asset),
                reachability = build.Contains(item.asset) ? "build dependency" : runtime.Contains(item.asset) ? "runtime lookup or dependency" :
                    authored.Contains(item.asset) ? "prefab authoring dependency" : item.source.StartsWith("THIRD_PARTY/") ? "vendor library, no serialized use" :
                    item.source.StartsWith("LEGACY/") ? "archive candidate" : "unconfirmed",
                importer = importer != null ? importer.GetType().Name : "missing", recordedPPU = item.ppu,
                importedPPU = sprite != null ? sprite.pixelsPerUnit : 0,
                sampling = tex != null ? tex.filterMode + ", mips=" + tex.mipmapCount : "subasset",
                scaleKeys = scaleKeys.TryGetValue(item.asset,out var keys) ? keys.OrderBy(k=>k).ToArray() : Array.Empty<string>(),
                clips = clips.TryGetValue(item.asset, out var uses) ? uses.OrderBy(x => x).ToArray() : Array.Empty<string>() };
            if (textureImporter != null) audit.sampling += ", compression=" + textureImporter.textureCompression;
            report.sources.Add(audit);
        }
        foreach (string path in Paths("t:Prefab"))
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root != null) Inspect(root, path, report, map);
        }
        foreach (string path in Paths("t:Scene"))
        {
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try { foreach (var root in scene.GetRootGameObjects()) Inspect(root, path, report, map); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        var placements=report.instances.Where(i=>!string.IsNullOrEmpty(i.guid)).ToLookup(i=>i.guid);
        foreach(var art in report.sources)
        {
            var uses=placements[art.guid];
            var found=uses.FirstOrDefault(i=>i.active && i.role!="UI layout" && (i.assessment=="inspect scale" || i.assessment=="anisotropic"));
            art.priority=found!=null ? "review world density" : art.reachability=="unconfirmed" ? "trace use" : "routine";
            var sample=found ?? uses.FirstOrDefault();
            art.reproduce=sample!=null ? sample.owner+" | "+sample.hierarchy : art.clips.FirstOrDefault() ?? art.asset;
        }
        Directory.CreateDirectory(ProjectVerification.Output);
        PixelMotionBoards.Write(ProjectVerification.Output);
        File.WriteAllText(Output, JsonUtility.ToJson(report, true));
        Debug.Log($"[PixelDensityAudit] {report.sources.Count} sources, {report.instances.Count} placements, {Output}");
    }

    private static IEnumerable<string> Paths(string filter) =>
        AssetDatabase.FindAssets(filter, new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p);

    public static void Capture(Camera camera, string name)
    {
        var report = new Report { unity = Application.unityVersion, referenceLens = camera.orthographicSize };
        var map = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)).entries.GroupBy(e => e.asset).ToDictionary(g => g.Key, g => g.First());
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects()) Inspect(root, scene.path, report, map);
        Directory.CreateDirectory(ProjectVerification.Output);
        File.WriteAllText(Path.Combine(ProjectVerification.Output, name + "-density.json"), JsonUtility.ToJson(report, true));
    }

    private static void Inspect(GameObject root, string owner, Report report, Dictionary<string, Source> sources)
    {
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            Add(root, sr.transform, sr.sprite, sr.sharedMaterial, "world", owner, report, sources, sr.enabled);
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            var sprite = img.overrideSprite;
            if (sprite == null) continue;
            var canvas = img.GetComponentInParent<Canvas>(true);
            float pixels = 1;
            if (canvas != null && canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                pixels = PixelViewport.Height / (2f * report.referenceLens);
            else if (canvas != null)
            {
                var rootCanvas=canvas.rootCanvas;
                float rootHeight=((RectTransform)rootCanvas.transform).rect.height * rootCanvas.transform.lossyScale.y;
                if (rootHeight>0) pixels=PixelViewport.Height/rootHeight;
            }
            Vector2 layout = Vector2.Scale(img.rectTransform.rect.size, (Vector2)img.transform.lossyScale) * pixels;
            var ratio = new Vector2(Mathf.Abs(layout.x)/sprite.rect.width,Mathf.Abs(layout.y)/sprite.rect.height);
            if (img.preserveAspect && (img.type==Image.Type.Simple || img.type==Image.Type.Filled))
                ratio=Vector2.one*Mathf.Min(ratio.x,ratio.y);
            string usage = img is VectorUiImage vector && vector.UsesVector ? "vector silhouette" : img.type.ToString();
            string verdict = canvas == null ? "layout needs owning canvas" : usage == "vector silhouette" ? "vector silhouette" :
                img.type == Image.Type.Sliced || img.type == Image.Type.Tiled ? "border and center use separate sampling" : null;
            Add(root,img.transform,sprite,img.material,"UI layout",owner,report,sources,img.enabled,ratio,verdict,usage);
        }
        foreach (var tiles in root.GetComponentsInChildren<Tilemap>(true))
        {
            var bounds=tiles.cellBounds;
            if (bounds.size.x<=0 || bounds.size.y<=0 || bounds.size.z<=0) continue;
            var last=bounds.max-Vector3Int.one;
            int count=tiles.GetTilesRangeCount(bounds.min,last);
            if(count==0) continue;
            var positions=new Vector3Int[count];
            count=tiles.GetTilesRangeNonAlloc(bounds.min,last,positions,new TileBase[count]);
            var seen=new HashSet<(Sprite,Matrix4x4)>();
            for(int i=0;i<count;i++)
            {
                var cell=positions[i];
                var sprite=tiles.GetSprite(cell);
                var matrix=tiles.GetTransformMatrix(cell);
                if(sprite==null || !seen.Add((sprite,matrix))) continue;
                var world=tiles.transform.localToWorldMatrix*tiles.orientationMatrix*matrix;
                var ratio=new Vector2(world.MultiplyVector(Vector3.right).magnitude,world.MultiplyVector(Vector3.up).magnitude) *
                    (PixelViewport.Height/(2f*report.referenceLens*sprite.pixelsPerUnit));
                var renderer=tiles.GetComponent<TilemapRenderer>();
                string purpose=tiles.name=="Unsteppable" && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(sprite)) ? "procedural collision marker" :
                    !Application.isPlaying && (owner.Contains("/BuyAssets/") || owner.StartsWith("Assets/Resources/Sprites/Tile/")) ? "artist tile palette" :
                    !Application.isPlaying && owner.EndsWith(".prefab") && ratio.x>1.02f ? "source room, compare stamped runtime grid" : null;
                Add(root, tiles.transform, sprite, renderer!=null ? renderer.sharedMaterial : null, "tile cell",owner,report,sources,
                    tiles.enabled && renderer!=null && renderer.enabled,ratio,purpose,cell.ToString());
            }
        }
    }
    private static void Add(GameObject root, Transform transform, Sprite sprite, Material material, string role,
        string owner, Report report, Dictionary<string, Source> sources, bool enabled, Vector2? measured=null, string verdict=null, string layout="")
    {
        if (sprite == null) return;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long id);
        string path = AssetDatabase.GetAssetPath(sprite);
        float ppu = sprite.pixelsPerUnit;
        float factor = PixelViewport.Height / (2f * report.referenceLens * ppu);
        Vector2 density = new Vector2(((Vector2)transform.TransformVector(Vector3.right)).magnitude,
            ((Vector2)transform.TransformVector(Vector3.up)).magnitude) * factor;
        if(measured.HasValue) density=measured.Value;
        string assessment = verdict ?? Assess(density);
        if(role=="world" && (AnimationUtility.CalculateTransformPath(transform,root.transform).Contains("MiniMapIcon") || sprite.name.StartsWith("Icon_map_",StringComparison.OrdinalIgnoreCase) || transform.name.StartsWith("Icon_map_",StringComparison.OrdinalIgnoreCase))) assessment="minimap marker source";
        if(role=="world" && path.StartsWith("Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/")) assessment="built-in shape, inspect visual owner";
        var chain=new List<string>();
        bool active = enabled;
        for (Transform ancestor = transform; ancestor != null; ancestor = ancestor.parent)
        {
            active &= ancestor.gameObject.activeSelf;
            if(ancestor.localScale!=Vector3.one) chain.Add(ancestor.name+" "+ancestor.localScale.ToString("G4"));
            if (ancestor == root.transform) break;
        }
        report.instances.Add(new Instance { owner = owner, hierarchy = root.name + "/" + AnimationUtility.CalculateTransformPath(transform, root.transform),
            scaleChain=string.Join(" ← ",chain), layout=layout, role = role, sprite = sprite.name, guid = guid, localID = id, ppu = ppu, pixelsPerTexel = density,
            source = sources.TryGetValue(path, out var item) ? item.source : path,
            material = material != null ? material.name : "", active = active, assessment = assessment });
    }

    private static string Assess(Vector2 density)
    {
        if (Mathf.Abs(density.x-density.y)>.02f) return "anisotropic";
        return Mathf.Abs(density.x-1)>.02f ? "inspect scale" : "native";
    }

    // Thickness is measured from the transparent edge. Painted interior details are deliberately excluded.
    internal static int BorderRun(Color32[] pixels,int width,int start,int step)
    {
        int at=start, count=0;
        while(at>=0 && at<pixels.Length && count<width)
        {
            Color32 c=pixels[at];
            if(c.a<128 || c.r>50 || c.g>50 || c.b>50) break;
            count++; at+=step;
        }
        return count;
    }
    private static void VerifyControls()
    {
        var dark = new Color32(20,20,20,255);
        var body = new Color32(160,130,120,255);
        if (Assess(new Vector2(.5f,.5f))!="inspect scale" || Assess(new Vector2(1,2))!="anisotropic" ||
            BorderRun(new[]{dark,body,body},3,0,1)!=1 || BorderRun(new[]{dark,dark,body},3,0,1)!=2)
            throw new InvalidOperationException("Pixel density or outline control failed.");
    }

    public static void Show()
    {
        Run();
        EditorUtility.RevealInFinder(Path.GetFullPath(Output));
    }
}
