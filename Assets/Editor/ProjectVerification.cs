using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ProjectVerification
{
    private const string WindowsProfile = "Assets/Settings/Build Profiles/New Windows Profile.asset";
    internal const string Output = "Logs/Verification";

    private static readonly HashSet<string> SerializedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".unity", ".prefab", ".asset", ".controller", ".overrideController", ".anim", ".playable"
    };

    private static readonly Regex ExternalReference = new(
        @"\{fileID: (-?\d+), guid: ([a-f0-9]{32}), type: \d+\}", RegexOptions.Compiled);
    private static readonly Regex EventMethod = new(
        @"^\s*(?:m_MethodName|functionName): ([^\r\n]+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ManagedType = new(
        @"^\s*type: \{class: .+, ns: .*, asm: .+\}", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex Lookup = new(
        @"\b(?:Resources\.Load\w*|Addressables\.\w+|PlayerPrefs\.\w+|Application\.persistentDataPath)\b",
        RegexOptions.Compiled);

    [Serializable]
    private sealed class Inventory
    {
        public string editorVersion;
        public string windowsBuildProfile;
        public string[] windowsDefines;
        public string[] enabledScenes;
        public string[] files;
        public string[] objectReferences;
        public string[] missingGuids;
        public string[] eventMethods;
        public string[] managedTypes;
        public string[] runtimeLookups;
    }

    public static void CaptureAssets()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(WindowsProfile);
        if (profile == null) throw new FileNotFoundException(WindowsProfile);
        foreach (var scene in EditorBuildSettings.scenes)
            if (!File.Exists(scene.path))
                throw new FileNotFoundException("A scene in Build Settings no longer exists.", scene.path);

        var references = new SortedSet<string>(StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var methods = new SortedSet<string>(StringComparer.Ordinal);
        var managed = new SortedSet<string>(StringComparer.Ordinal);
        var lookups = new SortedSet<string>(StringComparer.Ordinal);
        var paths = AssetDatabase.GetAllAssetPaths()
            .Where(IsGameAsset)
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string path in paths)
        {
            string extension = Path.GetExtension(path);
            if (extension == ".cs")
            {
                foreach (string line in File.ReadLines(path))
                    if (Lookup.IsMatch(line) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                        lookups.Add(path + " | " + line.Trim());
                continue;
            }
            if (!SerializedExtensions.Contains(extension)) continue;
            using var reader = File.OpenText(path);
            if (reader.ReadLine() != "%YAML 1.1") continue;
            string yaml = reader.ReadToEnd();
            foreach (Match match in ExternalReference.Matches(yaml))
            {
                string guid = match.Groups[2].Value;
                if (guid.StartsWith("0000000000000000", StringComparison.Ordinal)) continue;
                if (!resolved.TryGetValue(guid, out string target))
                {
                    target = AssetDatabase.GUIDToAssetPath(guid);
                    resolved.Add(guid, target);
                }
                references.Add(path + " | " + guid + " | " + match.Groups[1].Value + " | " + target);
                if (string.IsNullOrEmpty(target)) missing.Add(path + " | " + guid);
            }
            foreach (Match match in EventMethod.Matches(yaml))
                methods.Add(path + " | " + match.Groups[1].Value.Trim());
            foreach (Match match in ManagedType.Matches(yaml))
                managed.Add(path + " | " + match.Value.Trim());
        }

        foreach (string path in AssetDatabase.FindAssets("t:AnimationClip").Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(IsGameAsset))
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                foreach (var callback in AnimationUtility.GetAnimationEvents(clip))
                    methods.Add(FormattableString.Invariant($"{path} | {clip.name} | {callback.time:R} | {callback.functionName}"));

        Directory.CreateDirectory(Output);
        var inventory = new Inventory
        {
            editorVersion = Application.unityVersion,
            windowsBuildProfile = WindowsProfile,
            windowsDefines = profile.scriptingDefines,
            enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            files = paths.Select(path => AssetDatabase.AssetPathToGUID(path) + " | " + path).ToArray(),
            objectReferences = references.ToArray(),
            missingGuids = missing.ToArray(),
            eventMethods = methods.ToArray(),
            managedTypes = managed.ToArray(),
            runtimeLookups = lookups.ToArray()
        };
        File.WriteAllText(Path.Combine(Output, "asset-inventory.json"), JsonUtility.ToJson(inventory, true));
        Debug.Log($"[ProjectVerification] {paths.Length} assets, {references.Count} object references, {missing.Count} unresolved GUIDs.");

        string[] executableExtensions = { ".cs", ".dll", ".asmdef", ".asmref", ".shader", ".shadergraph", ".compute" };
        if (paths.Any(path => path.Contains("/Legacy/", StringComparison.OrdinalIgnoreCase) &&
                executableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An executable asset is inside an Assets/…/Legacy folder.");
    }

    public static void CheckWorldAssets()
    {
        var trap = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Traps/TrapArrow.prefab");
        var renderer = trap.GetComponentInChildren<SpriteRenderer>(true);
        var controller = trap.GetComponentInChildren<Animator>(true).runtimeAnimatorController;
        if (renderer.sprite == null) throw new InvalidDataException("Arrow trap has no resting sprite.");
        foreach (var state in new[] { "On", "Off" })
        {
            var clip = controller.animationClips.Single(c => c.name == "ArrowTrap_Clip_" + state);
            var curve = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single(b => b.propertyName == "m_Sprite");
            var frames = AnimationUtility.GetObjectReferenceCurve(clip, curve);
            if (frames.Length != 3 || frames.Any(f => !(f.value is Sprite)) || frames.Select(f => f.value).Distinct().Count() != 3)
                throw new InvalidDataException("Arrow trap has missing frames in " + clip.name);
        }
        RoomPrefabCheck.Run();
        RoomTilemapCheck.Run();
        MapColliderCheck.Run();
        PortalColliderSmokeTest.Run();
        DebugStartFloorCheck.Run();
        WorldSortingCheck.Run();
        MiniMapCheck.Run();
        HudLayoutChecks.Validate();
    }

    public static void BuildWindows()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(WindowsProfile);
        if (profile == null) throw new FileNotFoundException(WindowsProfile);
        var result = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
        {
            buildProfile = profile,
            locationPathName = Path.GetFullPath("Builds/Verification/Necromander.exe")
        });
        if (result == null || result.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("The Windows profile build failed.");
        Debug.Log($"[ProjectVerification] Windows build passed ({result.summary.totalSize} bytes).");
    }

    private static bool IsGameAsset(string path) => path.StartsWith("Assets/", StringComparison.Ordinal) &&
        !path.StartsWith("Assets/BuyAssets/", StringComparison.Ordinal) &&
        !path.StartsWith("Assets/Plugins/", StringComparison.Ordinal);
}
