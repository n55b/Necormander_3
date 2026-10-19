using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit, non-destructive setup. Never runs automatically on import.</summary>
public sealed class ViperHandVisualSetup : EditorWindow
{
    private const string SourcePath = "Assets/Resources/Sprites/Player/Weapon_Viper.aseprite";
    private const string EquipmentPath = "Assets/SOData/Equipment/1015/Viper.asset";
    private const string PrefabPath = "Assets/Prefabs/Player Melee.prefab";
    private const string ControllerPath = "Assets/Animations/Player/Player_Animator.controller";
    private const string OutputFolder = "Assets/Animations/Player/ViperHandGenerated";
    private const string OverridePath = OutputFolder + "/ViperHand.overrideController";

    [Serializable]
    private sealed class SetupRecord
    {
        public string sourceDependencyHash;
        public float pixelsPerUnit;
        public Vector2 pivotOffsetPixels;
    }

    // Zero keeps the importer's PPU; zero offset keeps its original pivot.
    [SerializeField] private float pixelsPerUnit = 32f;
    [SerializeField] private Vector2 pivotOffsetPixels = new Vector2(8f, 105f);
    [SerializeField] private bool alignmentReviewed;

    [MenuItem("Tools/Necromander/Set Up Viper Hand Visuals")]
    private static void Open() => GetWindow<ViperHandVisualSetup>("Viper Hand Setup");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Creates six Body clip copies with Viper Hand sprites. Body animation, hit events, parry and death behavior are retained. Existing generated assets are never overwritten.", MessageType.Info);
        pixelsPerUnit = EditorGUILayout.FloatField("Output PPU (0 = imported)", pixelsPerUnit);
        pivotOffsetPixels = EditorGUILayout.Vector2Field("Pivot offset in pixels", pivotOffsetPixels);
        EditorGUILayout.HelpBox("Defaults 32 PPU / (+8,+105) are a pixel-preview calibration, not Unity-validated. Viper currently imports at 100 PPU with a canvas-bottom pivot; existing hands use 32 PPU. Offset is added to each imported sprite's pivot in pixel coordinates (positive Y = up). Nonzero settings create sprite copies referencing the imported texture; source art/importer remain unchanged. Inspect in Unity before accepting placement.", MessageType.Warning);
        alignmentReviewed = EditorGUILayout.Toggle("Alignment reviewed for this setup", alignmentReviewed);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Inspect Imported Viper Assets")) Inspect();
            if (GUILayout.Button("Generate and Wire Viper"))
            {
                try
                {
                    if (!alignmentReviewed) throw new InvalidOperationException("Review imported sprite alignment and enter deliberate settings first. Setup has not changed anything.");
                    Generate(pixelsPerUnit, pivotOffsetPixels);
                }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }

    [MenuItem("Tools/Necromander/Inspect Viper Hand Assets")]
    public static void Inspect()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(SourcePath);
        foreach (var obj in assets)
        {
            if (obj is Sprite s)
                Debug.Log($"[Viper] Sprite {s.name}: rect={s.rect}, pivot={s.pivot}, PPU={s.pixelsPerUnit}", s);
            if (obj is AnimationClip clip)
                Debug.Log($"[Viper] Clip {clip.name}: length={clip.length}, sprite paths=" + string.Join(", ", AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(b => b.path + ":" + b.propertyName)), clip);
        }
        Debug.Log("[Viper] Inspection only. Check the source's merged layer output, frame names, scale and pivot before setup.");
    }

    /// <summary>Explicit batch entry point: -executeMethod ViperHandVisualSetup.SetupBatch.
    /// Uses the reviewed pixel-preview calibration; never invoked automatically.</summary>
    public static void SetupBatch()
    {
        Generate(32f, new Vector2(8f, 105f));
        ValidateGenerated();
    }

    [MenuItem("Tools/Necromander/Validate Generated Viper Hand Visuals")]
    public static void ValidateGenerated()
    {
        var recordAsset = RequireAsset<TextAsset>(OutputFolder + "/ViperSetupRecord.json");
        var record = JsonUtility.FromJson<SetupRecord>(recordAsset.text);
        if (record == null || record.sourceDependencyHash != AssetDatabase.GetAssetDependencyHash(SourcePath).ToString())
            throw new InvalidOperationException("Viper source/importer changed after generation. Derived sprite rectangles may be stale; inspect and deliberately regenerate, do not reuse blindly.");
        var controller = RequireAsset<AnimatorOverrideController>(OverridePath);
        if (controller.runtimeAnimatorController != RequireAsset<RuntimeAnimatorController>(ControllerPath))
            throw new InvalidOperationException("Generated override uses a different base controller.");
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        controller.GetOverrides(overrides);
        var expectedNames = new[] { "Idle", "Run", "Dashf", "Attack_Light1", "Attack_Light2", "Attack_Medium" };
        var sourceObjects = AssetDatabase.LoadAllAssetsAtPath(SourcePath);
        var originals = new Dictionary<int, Sprite>();
        var aligned = new Dictionary<int, Sprite>();
        string derivedPath = OutputFolder + "/ViperAlignedSprites.asset";
        var derived = AssetDatabase.LoadAllAssetsAtPath(derivedPath).OfType<Sprite>().ToArray();
        bool usesDerived = record.pixelsPerUnit != 0 || record.pivotOffsetPixels != Vector2.zero;
        // Unity may give the main sprite the asset filename as its display name.
        // Frame zero is identified by its main-asset role, never by that name.
        var mainSprite = usesDerived ? AssetDatabase.LoadMainAssetAtPath(derivedPath) as Sprite : null;
        if (usesDerived && (derived.Length != 19 || derived.Distinct().Count() != 19 ||
            mainSprite == null || !AssetDatabase.IsMainAsset(mainSprite) || !derived.Contains(mainSprite)))
            throw new InvalidOperationException("Expected one main frame-zero sprite and eighteen distinct subasset sprites.");
        var resolved = new HashSet<Sprite>();
        for (int i = 0; i < 19; i++)
        {
            originals[i] = sourceObjects.OfType<Sprite>().Single(s => s.name == "Frame_" + i);
            aligned[i] = usesDerived
                ? (i == 0 ? mainSprite : derived.Single(s => s != mainSprite && AssetDatabase.IsSubAsset(s) && s.name == "Viper_Frame_" + i))
                : originals[i];
            if (!resolved.Add(aligned[i]))
                throw new InvalidOperationException("Multiple Viper frame indices resolved to the same sprite.");
            var a = originals[i]; var b = aligned[i];
            float expectedPpu = record.pixelsPerUnit > 0 ? record.pixelsPerUnit : a.pixelsPerUnit;
            if (b.texture != a.texture || b.rect != a.rect || Mathf.Abs(b.pixelsPerUnit - expectedPpu) > 0.001f ||
                Vector2.Distance(b.pivot, a.pivot + record.pivotOffsetPixels) > 0.001f)
                throw new InvalidOperationException("Generated sprite calibration differs from recorded settings: " + b.name);
        }
        int changed = 0;
        foreach (var pair in overrides)
        {
            if (pair.Value == null || pair.Value == pair.Key) continue;
            changed++;
            int slot = Array.IndexOf(expectedNames, pair.Key.name.Replace("Player_AnimClip_", ""));
            if (slot < 0 || AssetDatabase.GetAssetPath(pair.Key) != "Assets/Animations/Player/Player_AnimClip_" + expectedNames[slot] + ".anim")
                throw new InvalidOperationException("Unexpected override slot: " + pair.Key.name);
            ValidateClipCopy(pair.Key, pair.Value);
            ObjectReferenceKeyframe[] expected;
            if (slot < 3) expected = new[] { new ObjectReferenceKeyframe { time = 0, value = aligned[0] },
                new ObjectReferenceKeyframe { time = LastSample(pair.Key), value = aligned[0] } };
            else
            {
                int attack = slot - 3;
                var ranges = new[] { new[] { 1, 6, 3 }, new[] { 7, 11, 9 }, new[] { 12, 18, 14 } };
                var tag = sourceObjects.OfType<AnimationClip>().Single(c => c.name == "Attack_" + (attack + 1));
                var keys = ReadTag(tag, originals, ranges[attack][0], ranges[attack][1]);
                expected = RetargetAttack(pair.Key, tag, keys, originals, aligned, ranges[attack][2]);
            }
            var actual = AnimationUtility.GetObjectReferenceCurve(pair.Value, RequireHandBinding(pair.Value));
            if (actual.Length != expected.Length || actual.Where((k, i) => Mathf.Abs(k.time - expected[i].time) > 0.0001f || k.value != expected[i].value).Any())
                throw new InvalidOperationException(pair.Value.name + " hand sprite mapping/timing differs from the intended setup.");
        }
        if (changed != 6) throw new InvalidOperationException("Expected exactly six override clips, found " + changed);
        var equipment = RequireAsset<EquipmentSO>(EquipmentPath);
        if (equipment.handAnimationOverride != controller) throw new InvalidOperationException("Viper binding is missing.");
        var body = RequireAsset<GameObject>(PrefabPath).transform.Find("Body");
        if (body == null || body.GetComponent<PlayerEquipmentVisualController>() == null)
            throw new InvalidOperationException("Prefab runtime binding is missing.");
        Debug.Log("[Viper] Structural validation passed: six clips, preserved original body curves/events/duration, equipment and prefab references. Visual placement and gameplay still require Play Mode.");
    }

    private static void ValidateClipCopy(AnimationClip original, AnimationClip copy)
    {
        if (Mathf.Abs(original.length - copy.length) > 0.0001f || original.frameRate != copy.frameRate)
            throw new InvalidOperationException(copy.name + " changed original clip duration/frame rate.");
        if (JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(original)) !=
            JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(copy)))
            throw new InvalidOperationException(copy.name + " changed clip settings.");
        var originalEvents = AnimationUtility.GetAnimationEvents(original);
        var copiedEvents = AnimationUtility.GetAnimationEvents(copy);
        if (originalEvents.Length != copiedEvents.Length) throw new InvalidOperationException(copy.name + " changed event count.");
        for (int i = 0; i < originalEvents.Length; i++)
        {
            var a = originalEvents[i]; var b = copiedEvents[i];
            if (a.time != b.time || a.functionName != b.functionName || a.stringParameter != b.stringParameter ||
                a.floatParameter != b.floatParameter || a.intParameter != b.intParameter ||
                a.objectReferenceParameter != b.objectReferenceParameter || a.messageOptions != b.messageOptions)
                throw new InvalidOperationException(copy.name + " changed an original animation event.");
        }
        var floatBindings = AnimationUtility.GetCurveBindings(original);
        if (floatBindings.Length != AnimationUtility.GetCurveBindings(copy).Length)
            throw new InvalidOperationException(copy.name + " changed float bindings.");
        foreach (var binding in floatBindings)
        {
            var a = AnimationUtility.GetEditorCurve(original, binding); var b = AnimationUtility.GetEditorCurve(copy, binding);
            if (b == null || a.preWrapMode != b.preWrapMode || a.postWrapMode != b.postWrapMode || !a.keys.SequenceEqual(b.keys))
                throw new InvalidOperationException(copy.name + " changed a float curve.");
        }
        var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(original);
        if (objectBindings.Length != AnimationUtility.GetObjectReferenceCurveBindings(copy).Length)
            throw new InvalidOperationException(copy.name + " changed object bindings.");
        foreach (var binding in objectBindings)
        {
            if (binding.path == "Hand" && binding.type == typeof(SpriteRenderer) && binding.propertyName == "m_Sprite") continue;
            var a = AnimationUtility.GetObjectReferenceCurve(original, binding); var b = AnimationUtility.GetObjectReferenceCurve(copy, binding);
            if (b == null || a.Length != b.Length || a.Where((k, i) => k.time != b[i].time || k.value != b[i].value).Any())
                throw new InvalidOperationException(copy.name + " changed a non-hand object curve.");
        }
    }

    /// <summary>Called deliberately from the window or a reviewed Editor validation script.</summary>
    public static void Generate(float outputPixelsPerUnit, Vector2 pivotOffset)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before setup.");
        if (!Finite(outputPixelsPerUnit) || outputPixelsPerUnit < 0 || !Finite(pivotOffset.x) || !Finite(pivotOffset.y))
            throw new ArgumentException("PPU must be zero or positive; all alignment values must be finite.");
        var equipment = RequireAsset<EquipmentSO>(EquipmentPath);
        var controller = RequireAsset<RuntimeAnimatorController>(ControllerPath);
        var prefab = RequireAsset<GameObject>(PrefabPath);
        var prefabBody = prefab.transform.Find("Body");
        if (prefabBody == null || prefabBody.Find("Hand") == null) throw new InvalidOperationException("Expected Body/Hand hierarchy is missing; nothing changed.");
        var animator = prefabBody.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController != controller)
            throw new InvalidOperationException("Prefab Body controller differs from the expected base. Preserve that customization and review manually.");

        // A repeated invocation is a true no-op, including any user edits to generated clips.
        // Do not silently regenerate assets or replace a user's equipment override.
        if (AssetDatabase.IsValidFolder(OutputFolder))
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
            if (existing != null && equipment.handAnimationOverride == existing &&
                prefabBody.GetComponent<PlayerEquipmentVisualController>() != null)
            {
                ValidateGenerated();
                Debug.LogWarning("[Viper] Already wired. Existing assets, alignment and user edits were left untouched. Validate them; regeneration is deliberately not automatic.", existing);
                return;
            }
            throw new InvalidOperationException("Generated output folder already exists but setup is not fully wired. Nothing overwritten. Inspect/recover the prior output before retrying.");
        }
        if (equipment.handAnimationOverride != null)
            throw new InvalidOperationException("Viper already has an override assigned. Nothing replaced; review that binding first.");

        var imported = AssetDatabase.LoadAllAssetsAtPath(SourcePath);
        var sprites = new Dictionary<int, Sprite>();
        for (int i = 0; i < 19; i++)
        {
            var matches = imported.OfType<Sprite>().Where(s => s.name == "Frame_" + i).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"Expected exactly one imported Frame_{i}, found {matches.Length}. Inspect importer output; do not guess subasset IDs or merge layers automatically.");
            if (matches[0].packed) throw new InvalidOperationException("Packed SpriteAtlas input is not supported; inspect unpacked imported Viper sprites before setup.");
            sprites.Add(i, matches[0]);
        }
        var importedClips = new AnimationClip[3];
        for (int i = 0; i < 3; i++)
        {
            var matches = imported.OfType<AnimationClip>().Where(c => c.name == "Attack_" + (i + 1)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one generated Attack_" + (i + 1) + " clip. Inspect imported subassets before setup.");
            importedClips[i] = matches[0];
        }

        var names = new[] { "Idle", "Run", "Dashf", "Attack_Light1", "Attack_Light2", "Attack_Medium" };
        var bases = names.Select(n => RequireAsset<AnimationClip>("Assets/Animations/Player/Player_AnimClip_" + n + ".anim")).ToArray();
        foreach (var clip in bases)
        {
            if (!controller.animationClips.Contains(clip)) throw new InvalidOperationException("Base controller no longer uses " + clip.name);
            RequireHandBinding(clip);
        }
        // Validate source tag curves against the known source frame ranges before creating any output.
        var ranges = new[] { new[] { 1, 6, 3 }, new[] { 7, 11, 9 }, new[] { 12, 18, 14 } };
        var sourceCurves = new ObjectReferenceKeyframe[3][];
        for (int i = 0; i < 3; i++) sourceCurves[i] = ReadTag(importedClips[i], sprites, ranges[i][0], ranges[i][1]);
        for (int i = 3; i < 6; i++)
        {
            EventTime(bases[i], "OnAttackHitFrame");
            EventTime(bases[i], "CanChangeAnimState");
        }

        // Validate retiming before creating any assets, using original sprite references.
        for (int i = 0; i < 3; i++)
            RetargetAttack(bases[i + 3], importedClips[i], sourceCurves[i], sprites, sprites, ranges[i][2]);

        // All input validation is complete. Creation is isolated; no existing output is overwritten.
        AssetDatabase.CreateFolder("Assets/Animations/Player", "ViperHandGenerated");
        GameObject contents = null;
        try
        {
            var mapped = CreateAlignedSprites(sprites, outputPixelsPerUnit, pivotOffset);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            for (int i = 0; i < bases.Length; i++)
            {
                var copy = Instantiate(bases[i]);
                copy.name = "Viper_" + names[i];
                var binding = RequireHandBinding(copy);
                ObjectReferenceKeyframe[] keys;
                if (i < 3)
                {
                    keys = new[] { new ObjectReferenceKeyframe { time = 0, value = mapped[0] },
                        new ObjectReferenceKeyframe { time = LastSample(bases[i]), value = mapped[0] } };
                }
                else
                {
                    int attack = i - 3;
                    keys = RetargetAttack(bases[i], importedClips[attack], sourceCurves[attack], sprites, mapped, ranges[attack][2]);
                }
                AnimationUtility.SetObjectReferenceCurve(copy, binding, keys);
                // Reapply the original explicit settings/events after setting curves; no gameplay retiming.
                AnimationUtility.SetAnimationClipSettings(copy, AnimationUtility.GetAnimationClipSettings(bases[i]));
                AnimationUtility.SetAnimationEvents(copy, AnimationUtility.GetAnimationEvents(bases[i]));
                ValidateClipCopy(bases[i], copy);
                AssetDatabase.CreateAsset(copy, OutputFolder + "/" + copy.name + ".anim");
                overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(bases[i], copy));
            }
            var replacement = new AnimatorOverrideController(controller) { name = "ViperHand" };
            replacement.ApplyOverrides(overrides);
            AssetDatabase.CreateAsset(replacement, OverridePath);
            string recordPath = OutputFolder + "/ViperSetupRecord.json";
            File.WriteAllText(recordPath, JsonUtility.ToJson(new SetupRecord
            {
                sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(SourcePath).ToString(),
                pixelsPerUnit = outputPixelsPerUnit,
                pivotOffsetPixels = pivotOffset
            }, true));
            AssetDatabase.ImportAsset(recordPath);
            AssetDatabase.SaveAssets();
            foreach (var sprite in mapped.Values)
                if (!EditorUtility.IsPersistent(sprite) || sprite.texture == null || !EditorUtility.IsPersistent(sprite.texture))
                    throw new InvalidOperationException("Generated sprite or texture is not persistent; wiring stopped.");
            foreach (var pair in overrides) ValidateClipCopy(pair.Key, pair.Value);

            contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            var body = contents.transform.Find("Body");
            if (body.GetComponent<PlayerEquipmentVisualController>() == null)
                body.gameObject.AddComponent<PlayerEquipmentVisualController>();
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Prefab save failed. Generated files remain for inspection; equipment was not changed.");
            Undo.RecordObject(equipment, "Assign Viper hand animations");
            equipment.handAnimationOverride = replacement;
            EditorUtility.SetDirty(equipment);
            AssetDatabase.SaveAssetIfDirty(equipment);
            ValidateGenerated();
            Debug.Log("[Viper] Generated and wired. Source/importer untouched. Validate alignment, contact poses, parry/death, switching away, loading and respawning in Play Mode. Re-running setup will not overwrite this output.", replacement);
        }
        catch
        {
            Debug.LogError("[Viper] Setup stopped. Created output is retained for inspection; do not delete user assets or blindly retry. Check prefab/equipment wiring and resolve the reported failure.");
            throw;
        }
        finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
    }

    private static Dictionary<int, Sprite> CreateAlignedSprites(Dictionary<int, Sprite> sources, float ppu, Vector2 offset)
    {
        if (ppu == 0 && offset == Vector2.zero) return sources;
        var result = new Dictionary<int, Sprite>();
        string path = OutputFolder + "/ViperAlignedSprites.asset";
        for (int frame = 0; frame < 19; frame++)
        {
            var source = sources[frame];
            Vector2 pivot = source.pivot + offset;
            pivot = new Vector2(pivot.x / source.rect.width, pivot.y / source.rect.height);
            var sprite = Sprite.Create(source.texture, source.rect, pivot, ppu > 0 ? ppu : source.pixelsPerUnit,
                0, SpriteMeshType.FullRect, source.border, false);
            sprite.name = "Viper_Frame_" + frame;
            if (frame == 0) AssetDatabase.CreateAsset(sprite, path);
            else AssetDatabase.AddObjectToAsset(sprite, path);
            result.Add(frame, sprite);
        }
        return result;
    }

    private static ObjectReferenceKeyframe[] ReadTag(AnimationClip clip, Dictionary<int, Sprite> sprites, int first, int last)
    {
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .Where(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite").ToArray();
        if (bindings.Length != 1) throw new InvalidOperationException(clip.name + " must have one merged SpriteRenderer track. Current importer output needs review.");
        var keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
        var expected = Enumerable.Range(first, last - first + 1).Select(i => sprites[i]).ToArray();
        if (keys.Length != expected.Length && keys.Length != expected.Length + 1)
            throw new InvalidOperationException(clip.name + " frame count differs from inspected tag (only a final hold duplicate is permitted).");
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (!Finite(key.time) || key.time < 0 || (i == 0 && Mathf.Abs(key.time) > 0.00001f) ||
                (i > 0 && key.time <= keys[i - 1].time) || key.value != expected[Mathf.Min(i, expected.Length - 1)])
                throw new InvalidOperationException(clip.name + " has unexpected frame order, missing/null sprites, or invalid key timing.");
        }
        if (!Finite(clip.length) || clip.length <= 0 || keys[keys.Length - 1].time > clip.length + 0.001f)
            throw new InvalidOperationException(clip.name + " has invalid frame timing.");
        return keys;
    }

    private static ObjectReferenceKeyframe[] RetargetAttack(AnimationClip target, AnimationClip source,
        ObjectReferenceKeyframe[] sourceKeys, Dictionary<int, Sprite> original, Dictionary<int, Sprite> mapped, int contactFrame)
    {
        float contactSource = sourceKeys.First(k => k.value == original[contactFrame]).time;
        float contactTarget = EventTime(target, "OnAttackHitFrame");
        float endTarget = Mathf.Min(EventTime(target, "CanChangeAnimState"), LastSample(target));
        if (contactSource <= 0 || contactSource >= source.length || contactTarget <= 0 || contactTarget >= endTarget || endTarget > target.length + 0.001f)
            throw new InvalidOperationException("Invalid contact/recovery timing in " + target.name);
        var result = new List<ObjectReferenceKeyframe>();
        foreach (var key in sourceKeys)
        {
            float time = key.time <= contactSource
                ? key.time / contactSource * contactTarget
                : contactTarget + (key.time - contactSource) / (source.length - contactSource) * (endTarget - contactTarget);
            int frame = original.First(p => p.Value == key.value).Key;
            result.Add(new ObjectReferenceKeyframe { time = time, value = mapped[frame] });
        }
        // Explicit final hold maintains the original clip length even if Hand formerly supplied its last key.
        result.Add(new ObjectReferenceKeyframe { time = LastSample(target), value = result[result.Count - 1].value });
        return result.GroupBy(k => k.time).Select(g => g.Last()).OrderBy(k => k.time).ToArray();
    }

    private static float LastSample(AnimationClip clip) => Mathf.Max(0, clip.length - 1f / clip.frameRate);

    private static EditorCurveBinding RequireHandBinding(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .Where(b => b.path == "Hand" && b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite").ToArray();
        if (bindings.Length != 1) throw new InvalidOperationException("Expected one Hand sprite binding in " + clip.name);
        return bindings[0];
    }
    private static float EventTime(AnimationClip clip, string name)
    {
        var matches = AnimationUtility.GetAnimationEvents(clip).Where(e => e.functionName == name).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected one " + name + " event in " + clip.name);
        return matches[0].time;
    }
    private static T RequireAsset<T>(string path) where T : UnityEngine.Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new InvalidOperationException("Missing " + typeof(T).Name + ": " + path);
        return asset;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
